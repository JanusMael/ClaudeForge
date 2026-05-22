using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests;

/// <summary>
/// End-to-end tests for the SDK's <see cref="IBackupClient"/> bridge over
/// <see cref="Bennewitz.Ninja.ClaudeForge.Core.Backup.BackupEngine"/>. Each test exercises
/// the full pipeline against a real on-disk profile via
/// <see cref="PlatformPaths.TestUserProfileOverride"/> — no mocks, no fakes
/// at the engine layer.
/// </summary>
[TestClass]
public class BackupClientTests
{
    private string _profileDir = null!;
    private string _backupDir = null!;
    private string? _previousOverride;

    [TestInitialize]
    public void Setup()
    {
        string root = Path.Combine(Path.GetTempPath(), "claudeforge-sdk-bk-" + Guid.NewGuid().ToString("N"));
        _profileDir = Path.Combine(root, "profile");
        _backupDir = Path.Combine(root, "backups");
        Directory.CreateDirectory(_profileDir);
        Directory.CreateDirectory(_backupDir);

        _previousOverride = PlatformPaths.TestUserProfileOverride;
        PlatformPaths.TestUserProfileOverride = _profileDir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = _previousOverride;
        try
        {
            if (Directory.Exists(_profileDir))
            {
                Directory.Delete(Path.GetDirectoryName(_profileDir)!, recursive: true);
            }
        }
        catch (IOException)
        {
            /* best-effort */
        }
    }

    private async Task<ClaudeCodeClient> OpenClientWithSettingsAsync()
    {
        ClaudeCodeClient client = new();
        await client.OpenAsync(projectRoot: null, ct: CancellationToken.None);
        // Seed real settings so the backup archive has content.
        client.SetValue("model", "claude-sonnet-4");
        await client.SaveAsync(force: true, CancellationToken.None);
        return client;
    }

    // ── Create ────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateAsync_WritesArchiveToDestinationDirectory()
    {
        using ClaudeCodeClient client = await OpenClientWithSettingsAsync();

        BackupArchive archive = await client.Backup.CreateAsync(
            new BackupRequest(
                Mode: BackupMode.SettingsOnly,
                OutputDirectory: _backupDir,
                IncludeCredentials: false,
                KeepLast: 0),
            onProgress: null,
            ct: CancellationToken.None);

        Assert.IsNotNull(archive);
        Assert.IsTrue(File.Exists(archive.FilePath),
            $"Backup archive should exist on disk: {archive.FilePath}");
        Assert.IsTrue(Path.GetFileName(archive.FilePath).StartsWith("backup-", StringComparison.Ordinal));
        StringAssert.EndsWith(archive.FilePath, ".zip");
        Assert.AreEqual("backup", archive.Manifest.Kind);
        Assert.AreEqual(BackupMode.SettingsOnly, archive.Manifest.Mode);
    }

    [TestMethod]
    public async Task CreateAsync_WithCredentialsFlag_ProducesPrefixedFilename()
    {
        using ClaudeCodeClient client = await OpenClientWithSettingsAsync();

        BackupArchive archive = await client.Backup.CreateAsync(
            new BackupRequest(
                Mode: BackupMode.SettingsOnly,
                OutputDirectory: _backupDir,
                IncludeCredentials: true,
                KeepLast: 0),
            onProgress: null,
            ct: CancellationToken.None);

        Assert.IsTrue(Path.GetFileName(archive.FilePath).StartsWith("backup-with-creds-", StringComparison.Ordinal),
            $"Filename should encode the IncludeCredentials flag: {archive.FilePath}");
    }

    [TestMethod]
    public async Task CreateAsync_OnProgressHandler_FiresMultipleTimes()
    {
        using ClaudeCodeClient client = await OpenClientWithSettingsAsync();

        List<BackupProgress> progressEvents = new();
        BackupProgressHandler handler = p =>
        {
            // Capture under lock — Progress<T> may marshal callbacks across
            // threads; List.Add is not thread-safe.
            lock (progressEvents)
            {
                progressEvents.Add(p);
            }

            return ValueTask.CompletedTask;
        };

        await client.Backup.CreateAsync(
            new BackupRequest(BackupMode.SettingsOnly, _backupDir, IncludeCredentials: false),
            handler,
            CancellationToken.None);

        // Drain Progress<T>'s asynchronous ThreadPool dispatch before
        // asserting.  The SDK wraps BackupProgressHandler in Progress<T>
        // (see BackupClient.WrapProgress), which posts callbacks to the
        // ThreadPool when no SynchronizationContext is captured (which is
        // the default for async test methods under MSTest).  Those callbacks
        // run asynchronously — by the time CreateAsync's await returns,
        // queued progress dispatches may not have reached the handler yet.
        // The race is hidden on Windows / Linux CI runners but lost
        // consistently on the macOS-latest ARM64 runner.  A short settle
        // drains the queue before the count assertion fires.
        await Task.Delay(250);

        // The producer fires several phase transitions during a backup
        // (discovery, individual file additions, manifest rewrite). The exact
        // count varies by platform; assert we got at least one event.
        int eventCount;
        lock (progressEvents)
        {
            eventCount = progressEvents.Count;
        }

        Assert.IsTrue(eventCount > 0,
            "Progress handler must be invoked at least once during a backup.");
    }

    // ── List ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ListAsync_ReturnsCreatedArchives()
    {
        using ClaudeCodeClient client = await OpenClientWithSettingsAsync();

        BackupArchive first = await client.Backup.CreateAsync(
            new BackupRequest(BackupMode.SettingsOnly, _backupDir, IncludeCredentials: false),
            null, CancellationToken.None);

        // Sleep briefly so the second archive's filename timestamp differs.
        await Task.Delay(1_100);

        BackupArchive second = await client.Backup.CreateAsync(
            new BackupRequest(BackupMode.SettingsOnly, _backupDir, IncludeCredentials: false),
            null, CancellationToken.None);

        IReadOnlyList<BackupArchive> listed = await client.Backup.ListAsync(_backupDir, CancellationToken.None);

        Assert.AreEqual(2, listed.Count);
        Assert.IsTrue(listed.Any(a => a.FilePath == first.FilePath),
            "ListAsync must return the first archive.");
        Assert.IsTrue(listed.Any(a => a.FilePath == second.FilePath),
            "ListAsync must return the second archive.");
    }

    [TestMethod]
    public async Task ListAsync_OnEmptyOrMissingDirectory_ReturnsEmptyList()
    {
        using ClaudeCodeClient client = await OpenClientWithSettingsAsync();
        string emptyDir = Path.Combine(Path.GetDirectoryName(_backupDir)!, "no-archives");
        Directory.CreateDirectory(emptyDir);

        IReadOnlyList<BackupArchive> listed = await client.Backup.ListAsync(emptyDir, CancellationToken.None);
        Assert.AreEqual(0, listed.Count);
    }

    // ── Restore ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RestoreAsync_RoundTripsSettingsViaArchive()
    {
        using ClaudeCodeClient client = await OpenClientWithSettingsAsync();

        // Capture the value we'll re-instate after a destructive change.
        string? originalModel = client.GetEffective<string>("model");
        Assert.AreEqual("claude-sonnet-4", originalModel);

        BackupArchive archive = await client.Backup.CreateAsync(
            new BackupRequest(BackupMode.SettingsOnly, _backupDir, IncludeCredentials: false),
            null, CancellationToken.None);

        // Mutate live state — this is what we want Restore to undo.
        client.SetValue("model", "tampered");
        await client.SaveAsync(force: true, CancellationToken.None);
        Assert.AreEqual("tampered", client.GetEffective<string>("model"));

        RestoreResult result = await client.Backup.RestoreAsync(archive, onProgress: null, ct: CancellationToken.None);
        Assert.IsTrue(result.Success, $"Restore should succeed. Message: {result.Message}");
        Assert.IsTrue(result.FilesRestored > 0, "Restore must report at least one file.");

        // Reload from disk to confirm the restored content is what we backed up.
        await client.ReloadAsync(CancellationToken.None);
        Assert.AreEqual(originalModel, client.GetEffective<string>("model"),
            "After Restore + Reload, the model value must match the pre-tamper backup.");
    }
}