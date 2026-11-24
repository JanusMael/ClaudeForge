using System.IO.Compression;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Testing;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// <see cref="BackupEngine"/> and <see cref="RestoreEngine"/> driven by
/// <see cref="NeutralTestProduct"/>, a product that shares no name with Claude.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>These were <c>BackupEngineTests</c> on Claude Code's descriptor</b>, switched by
/// <c>plans/00008</c> step 5 because nothing they assert is about Claude: section paths, skip
/// rules, the credential file, listing, retention, restore, cancellation and the sanitized
/// contract are all read from the descriptor. On Claude's descriptor they could not tell an engine
/// that reads the descriptor from one that hardcodes <c>claude-dir</c> or <c>projects</c>; here,
/// every name the engine has to find is one only the descriptor knows.
/// </para>
/// <para>
/// What stays in <c>BackupEngineTests</c> is about Claude: its skip list, its per-project and
/// worktree data, Claude Desktop's files, schema bundling and restore validation against Claude's
/// schemas. Those move with that data in later steps.
/// </para>
/// <para>
/// ⓘ Since step 8 the engine holds no environment and discovers projects only through a product's
/// <see cref="ProductProjectLayout"/>, which this product leaves out; its project half is
/// <c>NeutralProjectBackupTests</c>.
/// </para>
/// </remarks>
public sealed class NeutralProductBackupTests : IDisposable
{
    private readonly string _root;
    private readonly ProductDescriptor _product;
    private readonly BackupEngine _engine;

    public NeutralProductBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "npb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        BackupEngine.InvalidateListCache();
        PlatformPaths.TestUserProfileOverride = _root;

        NeutralTestProduct.Seed(_root);
        _product = NeutralTestProduct.For(_root);
        _engine = new BackupEngine(restorableProducts: [_product]);
    }

    public void Dispose()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }

        GC.SuppressFinalize(this);
    }

    private string Home => NeutralTestProduct.HomePath(_root);

    private static string Entry(string section, string relative = "") =>
        relative.Length == 0
            ? $"{NeutralTestProduct.ArchiveFolder}/{section}"
            : $"{NeutralTestProduct.ArchiveFolder}/{section}/{relative}";

    private Task<BackupResult> CreateAsync(
        string dest, BackupMode mode = BackupMode.SettingsOnly, bool includeCredentials = false,
        int keepLast = 0, CancellationToken? ct = null) =>
        _engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = mode,
            Products = [_product],
            IncludeCredentials = includeCredentials,
            KeepLast = keepLast,
        }, progress: null, ct ?? TestContext.Current.CancellationToken);

    // ── What an archive holds ─────────────────────────────────────────────

    [Fact]
    public async Task SettingsOnly_ArchivesBothSections_AndLeavesOutTheFullOnlySubdir()
    {
        string dest = Path.Combine(_root, "settings.zip");

        BackupResult result = await CreateAsync(dest);

        Assert.True(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.Contains(Entry(NeutralTestProduct.ConfigSection), entries);
        Assert.Contains(Entry(NeutralTestProduct.HomeSection, "settings.json"), entries);
        MessageAssert.DoesNotContain(Entry(NeutralTestProduct.HomeSection, NeutralTestProduct.FullOnlySubdir + "/session.jsonl"),
            entries, "A settings-only backup must honour the descriptor's full-only rule.");
    }

    [Fact]
    public async Task Full_ArchivesTheFullOnlySubdir()
    {
        string dest = Path.Combine(_root, "full.zip");

        BackupResult result = await CreateAsync(dest, BackupMode.Full);

        Assert.True(result.Succeeded, result.Message);
        MessageAssert.Contains(Entry(NeutralTestProduct.HomeSection, NeutralTestProduct.FullOnlySubdir + "/session.jsonl"),
            ListEntries(dest), "A full backup must keep a subdirectory the descriptor marks full-only.");
    }

    [Theory]
    [InlineData(BackupMode.SettingsOnly)]
    [InlineData(BackupMode.Full)]
    public async Task ASkippedSubdir_IsNeverArchived(BackupMode mode)
    {
        string dest = Path.Combine(_root, $"skip-{mode}.zip");

        BackupResult result = await CreateAsync(dest, mode);

        Assert.True(result.Succeeded, result.Message);
        MessageAssert.DoesNotContain(Entry(NeutralTestProduct.HomeSection, NeutralTestProduct.SkippedSubdir + "/work.tmp"),
            ListEntries(dest), "A subdirectory the descriptor skips must stay out of every mode.");
    }

    [Fact]
    public async Task BakSidecars_AreLeftOutAtEveryDepth()
    {
        await File.WriteAllTextAsync(Path.Combine(Home, "settings.json.pre-restore-20260101-120000.bak"),
            "(old settings sidecar)", TestContext.Current.CancellationToken);
        string agents = Path.Combine(Home, "agents");
        Directory.CreateDirectory(agents);
        await File.WriteAllTextAsync(Path.Combine(agents, "agent.md"), "real", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(agents, "agent.md.pre-restore-20260101-120000.bak"),
            "(old agent sidecar)", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(agents, "hand-edited.md.bak"), "(editor bak)",
            TestContext.Current.CancellationToken);
        string dest = Path.Combine(_root, "bak.zip");

        BackupResult result = await CreateAsync(dest);

        Assert.True(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.Contains(Entry(NeutralTestProduct.HomeSection, "agents/agent.md"), entries);
        List<string> baks = entries.Where(e => e.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)).ToList();
        MessageAssert.Equal(0, baks.Count, $"No .bak file may be archived. Found: {string.Join(", ", baks)}");
    }

    [Fact]
    public async Task TheCredentialFile_IsArchivedOnlyOnOptIn()
    {
        await File.WriteAllTextAsync(Path.Combine(Home, NeutralTestProduct.CredentialFileName),
            """{"token":"opaque"}""", TestContext.Current.CancellationToken);
        string without = Path.Combine(_root, "no-creds.zip");
        string with = Path.Combine(_root, "creds.zip");

        Assert.True((await CreateAsync(without)).Succeeded);
        Assert.True((await CreateAsync(with, includeCredentials: true)).Succeeded);

        string credential = Entry(NeutralTestProduct.HomeSection, NeutralTestProduct.CredentialFileName);
        MessageAssert.DoesNotContain(credential, ListEntries(without),
            "The descriptor's credential file must stay out unless the user opts in.");
        MessageAssert.Contains(credential, ListEntries(with),
            "Opting in must archive the file the descriptor names, whatever it is called.");
    }

    [Fact]
    public async Task TheManifest_IsReadable_AndNamesTheProductsArchiveFolder()
    {
        string dest = Path.Combine(_root, "manifest.zip");

        BackupResult result = await CreateAsync(dest);

        Assert.True(result.Succeeded, result.Message);
        BackupManifest manifest = await ReadManifestAsync(dest);
        Assert.Equal("backup", manifest.Kind);
        Assert.Equal(BackupMode.SettingsOnly, manifest.Mode);
        Assert.True(manifest.SizeBytes > 0, "SizeBytes should be finalised after the rewrite pass.");
        Assert.Contains(NeutralTestProduct.ArchiveFolder, manifest.Clients);
    }

    [Fact]
    public async Task AMissingDestinationDirectory_IsCreated()
    {
        string dest = Path.Combine(_root, "a", "b", "out.zip");

        BackupResult result = await CreateAsync(dest);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public async Task AnAlreadyCancelledToken_LeavesNoArchive()
    {
        string dest = Path.Combine(_root, "cancelled.zip");

        try
        {
            BackupResult result = await CreateAsync(dest, ct: new CancellationToken(canceled: true));
            Assert.False(result.Succeeded);
            Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            // Propagating is the other acceptable outcome; the archive check below is the contract.
        }

        Assert.False(File.Exists(dest), "Cancellation must not leave a final archive on disk.");
    }

    // ── Listing and retention ─────────────────────────────────────────────

    [Fact]
    public async Task List_SortsNewestFirst()
    {
        string first = Path.Combine(_root, "backup-20300101-000000.zip");
        string second = Path.Combine(_root, "backup-20300101-000001.zip");
        Assert.True((await CreateAsync(first)).Succeeded);
        Assert.True((await CreateAsync(second)).Succeeded);
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddSeconds(-10));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow);

        IReadOnlyList<BackupEntry> entries = _engine.List(_root);

        Assert.Equal(2, entries.Count);
        Assert.Equal(Path.GetFileName(second), entries[0].FileName);
        Assert.Equal(Path.GetFileName(first), entries[1].FileName);
    }

    [Fact]
    public async Task Retention_KeepsOnlyTheNewest()
    {
        string dir = Path.Combine(_root, "retention");
        Directory.CreateDirectory(dir);
        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(dir, $"backup-2030010{i}-000000.zip");
            Assert.True((await CreateAsync(path)).Succeeded);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-5 + i));
        }

        Assert.True((await CreateAsync(Path.Combine(dir, "backup-20300106-000000.zip"), keepLast: 2)).Succeeded);

        MessageAssert.Equal(2, Directory.GetFiles(dir, "backup-*.zip").Length,
            "Retention should have pruned every backup except the 2 newest.");
    }

    // ── Restore ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_RestoresEverySectionToItsDestination_AndSweepsItsSidecars()
    {
        string dir = Path.Combine(_root, "backups");
        Directory.CreateDirectory(dir);
        string settings = Path.Combine(Home, "settings.json");
        string config = NeutralTestProduct.ConfigPath(_root);
        string originalSettings = await File.ReadAllTextAsync(settings, TestContext.Current.CancellationToken);
        string originalConfig = await File.ReadAllTextAsync(config, TestContext.Current.CancellationToken);
        Assert.True((await CreateAsync(Path.Combine(dir, "backup-20300101-000000.zip"))).Succeeded);
        await File.WriteAllTextAsync(settings, """{"theme":"light"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(config, """{"neutral":false}""", TestContext.Current.CancellationToken);

        IReadOnlyList<BackupEntry> entries = _engine.List(dir);
        Assert.Single(entries);
        RestoreResult restore = await _engine.RestoreAsync(entries[0], progress: null, TestContext.Current.CancellationToken);

        Assert.True(restore.Succeeded, restore.Message);
        Assert.Equal(originalSettings, await File.ReadAllTextAsync(settings, TestContext.Current.CancellationToken));
        Assert.Equal(originalConfig, await File.ReadAllTextAsync(config, TestContext.Current.CancellationToken));
        MessageAssert.Equal(0, Directory.EnumerateFiles(Home, "*.pre-restore-*.bak").Count(),
            "A restore that completed without a failure must leave no sidecars.");
        // Two: the config file and the home's settings.json were overwritten. The count is what
        // tells "written then removed" from "never written".
        MessageAssert.Contains("Cleaned up 2 ", restore.Message,
            $"The result must report the sweep: {restore.Message}");
    }

    [Fact]
    public async Task Restore_ReportsProgressDuringTheApplyPhase()
    {
        string dir = Path.Combine(_root, "backups");
        Directory.CreateDirectory(dir);
        Assert.True((await CreateAsync(Path.Combine(dir, "backup-20300101-000000.zip"))).Succeeded);
        IReadOnlyList<BackupEntry> entries = _engine.List(dir);
        List<BackupProgress> reports = [];

        RestoreResult restore = await _engine.RestoreAsync(entries[0], new SyncProgress(reports.Add),
            TestContext.Current.CancellationToken);

        Assert.True(restore.Succeeded, restore.Message);
        int apply = reports.FindIndex(r => r.CurrentItem.Equals("Applying restore…", StringComparison.Ordinal));
        Assert.True(apply >= 0, "At least one 'Applying restore…' report must be emitted.");
        List<string> after = reports.Skip(apply + 1).Select(r => r.CurrentItem).ToList();
        MessageAssert.Contains("Restoring the neutral config…", after,
            "Each section reports the progress label its descriptor gives it.");
        MessageAssert.Contains("Restoring the neutral home…", after,
            "Each section reports the progress label its descriptor gives it.");
    }

    // ── The sanitized contract ────────────────────────────────────────────

    [Fact]
    public async Task Sanitized_RedactsSecretsInJsonFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(Home, "settings.json"), """
            {
              "theme": "dark",
              "env": { "SERVICE_API_KEY": "real-secret-value" },
              "mcpServers": { "github": { "headers": { "Authorization": "Bearer ghp_xyz" } } }
            }
            """, TestContext.Current.CancellationToken);
        string dest = Path.Combine(_root, "sanitized.zip");

        BackupResult result = await CreateAsync(dest, BackupMode.Sanitized);

        Assert.True(result.Succeeded, result.Message);
        string text = ReadEntryText(dest, Entry(NeutralTestProduct.HomeSection, "settings.json"));
        Assert.DoesNotContain("real-secret-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_xyz", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        Assert.Contains("\"theme\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sanitized_LeavesOutTheCredentialFile_EvenOnOptIn()
    {
        await File.WriteAllTextAsync(Path.Combine(Home, NeutralTestProduct.CredentialFileName),
            """{"token":"opaque"}""", TestContext.Current.CancellationToken);
        string dest = Path.Combine(_root, "sanitized-creds.zip");

        BackupResult result = await CreateAsync(dest, BackupMode.Sanitized, includeCredentials: true);

        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(ListEntries(dest),
            e => e.EndsWith(NeutralTestProduct.CredentialFileName, StringComparison.Ordinal));
        BackupManifest manifest = await ReadManifestAsync(dest);
        Assert.False(manifest.IncludedCredentials, "The manifest must declare credentials excluded.");
        Assert.Equal(BackupMode.Sanitized, manifest.Mode);
        Assert.Contains(manifest.Warnings, w =>
            w.Contains("Sanitized backup", StringComparison.OrdinalIgnoreCase)
            && w.Contains("Not restorable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sanitized_IsRefusedByRestore()
    {
        string dir = Path.Combine(_root, "backups");
        Directory.CreateDirectory(dir);
        Assert.True((await CreateAsync(Path.Combine(dir, "backup-20300101-000000.zip"), BackupMode.Sanitized)).Succeeded);
        IReadOnlyList<BackupEntry> entries = _engine.List(dir);

        RestoreResult restore = await _engine.RestoreAsync(entries[0], progress: null, TestContext.Current.CancellationToken);

        Assert.False(restore.Succeeded, "A sanitized backup must not be restorable.");
        Assert.Contains("sanitized", restore.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, restore.ItemsRestored);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private sealed class SyncProgress(Action<BackupProgress> action) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => action(value);
    }

    private static List<string> ListEntries(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    private static string ReadEntryText(string zipPath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = archive.GetEntry(entryName)
                                ?? throw new InvalidOperationException($"Entry '{entryName}' not found in '{zipPath}'.");
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }

    private static async Task<BackupManifest> ReadManifestAsync(string zipPath)
    {
        await using ZipArchive archive = await ZipFile.OpenReadAsync(zipPath, TestContext.Current.CancellationToken);
        await using Stream stream = await archive.GetEntry("manifest.json")!.OpenAsync(TestContext.Current.CancellationToken);
        return JsonSerializer.Deserialize(stream, BackupJsonContext.Default.BackupManifest)!;
    }
}
