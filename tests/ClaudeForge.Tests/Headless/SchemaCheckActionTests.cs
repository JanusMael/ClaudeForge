using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Avalonia.Headless;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// The <em>Check for schema updates</em> action behind the About dialog's button: what it
/// reports, and that the nav badges move without the tree being rebuilt.
/// </summary>
/// <remarks>
/// ⭐ <b>The live re-badge is the whole reason <see cref="NavigationNodeViewModel.Badge"/> is
/// observable rather than <c>init</c>.</b> A test that only checked the returned summary
/// would stay green if the badges never moved, and the badge is the part a user sees.
/// </remarks>
[TestClass]
public sealed class SchemaCheckActionTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private string _sandbox = string.Empty;

    private static string Doc(string marker) =>
        $$"""
          {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
              "servedFor": { "type": "string", "description": "{{marker}}" }
            }
          }
          """;

    /// <summary>Refuses every request.</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network unavailable");
    }

    /// <summary>Down while the window loads, up by the time the user presses the button.</summary>
    private sealed class ComesUpHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) <= 1)
            {
                throw new HttpRequestException("Simulated network unavailable");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Doc(request.RequestUri!.ToString()), Encoding.UTF8, "application/json"),
            });
        }
    }

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "cfcheck_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        string ccDir = Path.Combine(_sandbox, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(Path.Combine(ccDir, "settings.json"), "{}");

        string dtDir = Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!;
        Directory.CreateDirectory(dtDir);
        File.WriteAllText(PlatformPaths.DesktopConfigPath, "{}");
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
            /* best effort — the file-system indexer may hold transient locks */
        }
    }

    private static async Task<MainWindowViewModel> LoadedAsync(HttpMessageHandler handler)
    {
        MainWindowViewModel vm = new(new SchemaRegistry(new HttpClient(handler)), new NullDialogService());
        await vm.LoadAllWorkspacesAsync();
        return vm;
    }

    private static NavigationNodeViewModel Header(MainWindowViewModel vm, string nodeId)
    {
        NavigationNodeViewModel? header = vm.NavigationTree
            .FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));

        Assert.IsNotNull(header, $"Premise: the '{nodeId}' section header must be in the tree.");
        return header;
    }

    /// <summary>
    /// ⭐ A check that reaches upstream moves Claude Code's badge on the node already in the
    /// tree — and leaves Claude Desktop's alone, because it was never checked.
    /// </summary>
    [TestMethod]
    public async Task ASuccessfulCheck_MovesTheBadgeInPlace_AndLeavesTheUncheckedSectionAlone()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = await LoadedAsync(new ComesUpHandler());

            NavigationNodeViewModel cc = Header(vm, MainWindowViewModel.NavIdClaudeCode);
            NavigationNodeViewModel dt = Header(vm, MainWindowViewModel.NavIdClaudeDesktop);

            Assert.AreEqual(Strings.SchemaBadgeBundled, cc.Badge,
                "Premise: the window loaded while the network was down, so the badge starts on "
                + "the bundled copy. Without that the assertion below could pass unchanged.");
            string desktopBefore = dt.Badge!;

            string summary = await vm.CheckForSchemaUpdatesAsync(CancellationToken.None);

            // The SAME node objects, not a rebuilt tree.
            Assert.AreSame(cc, Header(vm, MainWindowViewModel.NavIdClaudeCode),
                "The tree was rebuilt. The badge is observable precisely so it need not be.");

            Assert.IsNotNull(cc.Badge);
            StringAssert.StartsWith(cc.Badge, "fetched", StringComparison.Ordinal,
                "Claude Code still shows its load-time badge after a successful check.");

            Assert.AreEqual(desktopBefore, dt.Badge,
                "Claude Desktop has no upstream, so a check cannot have moved it.");

            StringAssert.Contains(summary, "Reload", StringComparison.OrdinalIgnoreCase,
                "An Updated result must tell the user the editors do not yet reflect the new copy.");

            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task ACheckWithNoNetwork_ReportsUnavailable()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = await LoadedAsync(new OfflineHandler());

            string summary = await vm.CheckForSchemaUpdatesAsync(CancellationToken.None);

            Assert.AreEqual(Strings.SchemaCheckUnavailable, summary);
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    // ── The summariser's severity order ──────────────────────────────────

    private static SchemaRefreshResult Result(string id, SchemaRefreshStatus status) =>
        new(new ProductDescriptor(id, id, "https://example.invalid/s.json", id + ".json",
                ArchiveFolder: id),
            status, null, null);

    [TestMethod]
    public void FailedOutranksEverything()
    {
        string line = MainWindowViewModel.SummariseSchemaCheck(
        [
            Result("a", SchemaRefreshStatus.Updated),
            Result("b", SchemaRefreshStatus.Failed),
        ]);

        StringAssert.Contains(line, "b", StringComparison.Ordinal);
        Assert.AreNotEqual(
            string.Format(CultureInfo.CurrentCulture, Strings.SchemaCheckUpdatedFmt, "a"),
            line,
            "A run where something failed must not be reported as a plain update.");
    }

    [TestMethod]
    public void UpdatedOutranksUnavailableAndUpToDate()
    {
        string line = MainWindowViewModel.SummariseSchemaCheck(
        [
            Result("a", SchemaRefreshStatus.Unchanged),
            Result("b", SchemaRefreshStatus.Updated),
            Result("c", SchemaRefreshStatus.Unavailable),
        ]);

        StringAssert.Contains(line, "b", StringComparison.Ordinal,
            "The actionable outcome is the one that changed, and it names which product.");
    }

    [TestMethod]
    public void EverythingUnchanged_IsUpToDate()
    {
        string line = MainWindowViewModel.SummariseSchemaCheck(
        [
            Result("a", SchemaRefreshStatus.Unchanged),
            Result("b", SchemaRefreshStatus.Unchanged),
        ]);

        Assert.AreEqual(Strings.SchemaCheckUpToDate, line);
    }

    // ── Test doubles ────────────────────────────────────────────────────

    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null)
            => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null)
            => Task.FromResult<string?>(null);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
            => Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel")
            => Task.FromResult<bool?>(false);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(false);
    }
}
