using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// End-to-end guard on the archive the Export command actually writes.
/// <para>
/// <b>Written because nothing covered this at all.</b> Before this class no test referenced
/// <c>MainWindowViewModel.ExportAsync</c>, <c>ZipArchiveWriter.SerialiseExportManifest</c> or
/// <c>ExportManifest</c> — so which products an export claimed to cover, and whether that
/// claim matched the folders it actually contained, was unguarded in a format written to
/// users' disks. <c>ExportManifestTests</c> covers the DTO; this covers the GUI leg that
/// fills it in.
/// </para>
/// <para>
/// <b>Two products open, deliberately.</b> Three separate Phase 4 canaries established that
/// the suite exercises one product at a time — transposing the two products passed all 2,798
/// tests, and making every shell lifecycle loop cover only the first open section passed all
/// 2,814. An export assertion with one section open would prove nothing about the loop.
/// </para>
/// <para>
/// <b>Dispatch shape matters.</b> These tests return a value from the dispatched lambda so it
/// binds <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, ct)</c>. The common
/// <c>return Session.Dispatch(async () =&gt; { ... }, ct)</c> form yields
/// <c>Task&lt;Task&gt;</c>, whose inner task is never awaited, and every assertion inside is
/// silently unobserved — such a test cannot fail. Both tests below were canaried with a
/// deliberate failure.
/// </para>
/// </summary>
[TestClass]
public sealed class ExportArchiveTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        File.WriteAllText(CcSettingsPath, """{ "cleanupPeriodDays": 90 }""");

        // Desktop must load too: LoadAllWorkspacesAsync is transactional across both
        // products, so a missing Desktop config rolls the swap back and leaves only one
        // section open — which would quietly defeat the point of these tests.
        Directory.CreateDirectory(Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!);
        File.WriteAllText(PlatformPaths.DesktopConfigPath, """{ "preferences": { "theme": "dark" } }""");
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
            }
        }
        catch
        {
            /* best effort — file-system indexer may hold transient locks */
        }
    }

    [TestMethod]
    public async Task Export_ManifestNamesEveryOpenProduct_AndNothingElse()
    {
        string manifestJson = await ExportAndRead(entryPath: "manifest.json");

        JsonNode manifest = JsonNode.Parse(manifestJson)!;

        Assert.AreEqual("export", (string?)manifest["kind"],
            "The Restore list filters on kind to keep exports out of the restorable list.");
        Assert.AreEqual(2, (int?)manifest["schemaVersion"],
            "Schema v2 is the Clients list. A v1 archive here would mean the write side "
            + "regressed to the two booleans.");

        string[] clients = manifest["clients"]!.AsArray().Select(n => (string)n!).ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                SchemaRegistry.ClaudeCodeProduct.ArchiveFolder,
                SchemaRegistry.ClaudeDesktopProduct.ArchiveFolder,
            },
            clients,
            "The manifest must name BOTH open products, in navigation order. One entry means "
            + "the manifest is built from something narrower than the open-section list.");

        Assert.IsNull(manifest["includesClaudeCode"],
            "The v1 booleans must not be written any more — a second, silently stale "
            + "statement of which products the archive covers.");
        Assert.IsNull(manifest["includesClaudeDesktop"]);
    }

    [TestMethod]
    public async Task Export_EveryFolderTheManifestNames_ActuallyExistsInTheArchive()
    {
        // The invariant that makes the manifest usable: a reader takes `clients` as the list
        // of folders to look in. Both sides now derive from ProductDescriptor.ArchiveFolder,
        // and this is the assertion that they still agree once the archive is on disk.
        string destination = Path.Combine(_sandbox, "export.zip");
        await RunExport(destination);

        using ZipArchive archive = ZipFile.OpenRead(destination);
        string[] entries = archive.Entries.Select(e => e.FullName).ToArray();

        using Stream manifestStream = archive.GetEntry("manifest.json")!.Open();
        JsonNode manifest = JsonNode.Parse(manifestStream)!;
        string[] clients = manifest["clients"]!.AsArray().Select(n => (string)n!).ToArray();

        Assert.AreEqual(2, clients.Length, "Precondition: both products were open.");

        foreach (string client in clients)
        {
            Assert.IsTrue(
                entries.Any(e => e.StartsWith(client + "/", StringComparison.Ordinal)),
                $"The manifest names \"{client}\" but no archive entry sits under "
                + $"\"{client}/\". Entries were:\n  {string.Join("\n  ", entries)}");
        }

        // The exact persisted paths, not just their prefixes. Users have archives with these
        // names; deriving the folder segment from ArchiveFolder must not have moved them.
        CollectionAssert.Contains(entries, "ClaudeCode/.claude/settings.json");
        CollectionAssert.Contains(entries, "ClaudeDesktop/claude_desktop_config.json");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static string CcSettingsPath =>
        Path.Combine(PlatformPaths.ClaudeHome, "settings.json");

    private async Task<string> ExportAndRead(string entryPath)
    {
        string destination = Path.Combine(_sandbox, "export.zip");
        await RunExport(destination);

        using ZipArchive archive = ZipFile.OpenRead(destination);
        ZipArchiveEntry? entry = archive.GetEntry(entryPath);
        Assert.IsNotNull(entry, $"Export archive has no \"{entryPath}\" entry.");
        using StreamReader reader = new(entry!.Open());
        return await reader.ReadToEndAsync();
    }

    private static async Task RunExport(string destination)
    {
        // Returning a value from the lambda is what makes the assertions inside observable —
        // see the class remarks.
        bool ran = await Session.Dispatch(
            async () =>
            {
                MainWindowViewModel vm = new(
                    new SchemaRegistry(new HttpClient()), new ExportingDialogService(destination));
                await vm.LoadAllWorkspacesAsync();

                Assert.AreEqual(2, vm.Sections.Count(s => s.Client is not null),
                    "Precondition: both product sections must be open, or a one-product "
                    + "export would satisfy the assertions by default.");

                await vm.ExportCommand.ExecuteAsync(null);
                return true;
            },
            CancellationToken.None);

        Assert.IsTrue(ran);
        Assert.IsTrue(File.Exists(destination),
            "The export command must have written the archive. If it did not, it bailed out "
            + "early — most likely the dialog double returned no destination, or no section "
            + "was open.");
    }

    /// <summary>
    /// Supplies a destination from the save-file picker. The shared <c>NullDialogService</c>
    /// copies in this folder return <see langword="null"/>, which makes
    /// <c>ExportAsync</c> return before writing anything — a test using one would find no
    /// archive and pass only if it asserted nothing.
    /// </summary>
    private sealed class ExportingDialogService(string destination) : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(destination);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null) =>
            Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel") => Task.FromResult<bool?>(true);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(true);
    }
}
