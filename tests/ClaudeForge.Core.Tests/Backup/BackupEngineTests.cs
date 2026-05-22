using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

// Interlocked + Thread for the parallel-precompute test

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// End-to-end round-trip tests for <see cref="BackupEngine"/>. Each test isolates its
/// own fake <c>$HOME</c> via the <c>USERPROFILE</c> (Windows) or <c>HOME</c> (Unix)
/// environment variable so the real user's Claude data is never touched.
/// </summary>
[TestClass]
public sealed class BackupEngineTests
{
    private string _fakeHome = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "be-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);

        // BackupEngine.List memoises parsed manifests for process lifetime,
        // keyed by path + mtime + length. Per-test sandboxes use unique GUIDs
        // so collisions are unlikely, but invalidating defensively keeps the
        // assertion stack independent of test ordering.
        BackupEngine.InvalidateListCache();

        // Redirect every PlatformPaths call to the sandbox so the real user's Claude
        // data is never touched by the engine.
        PlatformPaths.TestUserProfileOverride = _fakeHome;

        // Populate a minimal Claude footprint the engine will back up.
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".claude"));
        File.WriteAllText(Path.Combine(_fakeHome, ".claude.json"), """{"user":"test"}""");
        File.WriteAllText(Path.Combine(_fakeHome, ".claude", "settings.json"), """{"theme":"dark"}""");
        // A "projects" sub-directory so the Settings-only / Full difference is testable.
        string projectsDir = Path.Combine(_fakeHome, ".claude", "projects");
        Directory.CreateDirectory(projectsDir);
        File.WriteAllText(Path.Combine(projectsDir, "session.jsonl"), """{"role":"user"}""");
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_fakeHome))
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    public async Task CreateAsync_SettingsOnly_ExcludesProjectsDirectory()
    {
        string dest = Path.Combine(_fakeHome, "backup.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsTrue(File.Exists(dest));

        List<string> entries = ListEntries(dest);
        Assert.IsTrue(entries.Any(e => e.EndsWith("claude.json", StringComparison.Ordinal)),
            "claude.json should always be present.");
        Assert.IsTrue(entries.Any(e => e.Contains("claude-dir/settings.json", StringComparison.Ordinal)),
            "settings.json should be included under claude-dir/.");
        Assert.IsFalse(entries.Any(e => e.Contains("claude-dir/projects/", StringComparison.Ordinal)),
            "Settings-only mode must skip ~/.claude/projects/.");
    }

    [TestMethod]
    public async Task CreateAsync_Full_IncludesProjectsDirectory()
    {
        string dest = Path.Combine(_fakeHome, "full.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Full,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        Assert.IsTrue(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.IsTrue(entries.Any(e => e.Contains("claude-dir/projects/session.jsonl", StringComparison.Ordinal)),
            "Full mode must include ~/.claude/projects/.");
    }

    [TestMethod]
    public async Task CreateAsync_ManifestIsReadableAndCorrect()
    {
        string dest = Path.Combine(_fakeHome, "manifest-check.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded);

        // Independently re-open and parse the manifest to confirm on-disk shape.
        await using FileStream fs = File.OpenRead(dest);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        ZipArchiveEntry? manifestEntry = archive.GetEntry("manifest.json");
        Assert.IsNotNull(manifestEntry);

        await using Stream ms = await manifestEntry!.OpenAsync();
        BackupManifest? manifest = JsonSerializer.Deserialize(ms, BackupJsonContext.Default.BackupManifest);
        Assert.IsNotNull(manifest);
        Assert.AreEqual("backup", manifest!.Kind);
        Assert.AreEqual(BackupMode.SettingsOnly, manifest.Mode);
        Assert.IsTrue(manifest.SizeBytes > 0, "SizeBytes should be finalised after the rewrite pass.");
    }

    [TestMethod]
    public async Task List_SortsNewestFirst()
    {
        // Create two backups a moment apart.
        string first = Path.Combine(_fakeHome, "first.zip");
        string second = Path.Combine(_fakeHome, "second.zip");

        // Rename to the expected "backup-*.zip" format so List sees them.
        first = Path.Combine(_fakeHome, "backup-20300101-000000.zip");
        second = Path.Combine(_fakeHome, "backup-20300101-000001.zip");

        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = first,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = second,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        // Force the second one to have a later LastWriteTime.
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddSeconds(-10));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow);

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(Path.GetFileName(second), entries[0].FileName);
        Assert.AreEqual(Path.GetFileName(first), entries[1].FileName);
    }

    [TestMethod]
    public async Task Retention_DeletesOldBackupsBeyondKeepLast()
    {
        string backupDir = Path.Combine(_fakeHome, "retention");
        Directory.CreateDirectory(backupDir);

        // Create 5 valid "backup-*.zip" archives and force their timestamps.
        for (int i = 0; i < 5; i++)
        {
            string path = Path.Combine(backupDir, $"backup-2030010{i}-000000.zip");
            await BackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = path,
                IncludeClaudeCode = true,
                IncludeClaudeDesktop = false,
            });
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-5 + i));
        }

        // Trigger retention via a 6th backup with KeepLast=2.
        string trigger = Path.Combine(backupDir, "backup-20300106-000000.zip");
        await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = trigger,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
            KeepLast = 2,
        });

        int remaining = Directory.GetFiles(backupDir, "backup-*.zip").Length;
        Assert.AreEqual(2, remaining,
            "Retention should have pruned every backup except the 2 newest.");
    }

    [TestMethod]
    public async Task RoundTrip_RestoreWritesFilesBackAndCreatesBakSidecars()
    {
        // Name it with the "backup-*" prefix so BackupEngine.List() finds it.
        string dest = Path.Combine(_fakeHome, "backup-round.zip");
        string original = await File.ReadAllTextAsync(Path.Combine(_fakeHome, ".claude", "settings.json"));

        BackupResult create = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        // Mutate the live settings — restore should roll it back.
        await File.WriteAllTextAsync(Path.Combine(_fakeHome, ".claude", "settings.json"), "{\"theme\":\"light\"}");

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(1, entries.Count);

        RestoreResult restore = await BackupEngine.Default.RestoreAsync(entries[0]);
        Assert.IsTrue(restore.Succeeded, restore.Message);

        string restored = await File.ReadAllTextAsync(Path.Combine(_fakeHome, ".claude", "settings.json"));
        Assert.AreEqual(original, restored);

        // A .pre-restore-*.bak file should exist alongside.
        List<string> baks = Directory.EnumerateFiles(Path.Combine(_fakeHome, ".claude"),
            "settings.json.pre-restore-*.bak").ToList();
        Assert.AreEqual(1, baks.Count, "Exactly one .bak sidecar should be written.");
    }

    [TestMethod]
    public void ResolveSafeExtractPath_RejectsZipSlip()
    {
        string baseDir = _fakeHome;
        string? safe = RestoreEngine.ResolveSafeExtractPath(baseDir, "ClaudeCode/claude.json");
        Assert.IsNotNull(safe);

        string? unsafe1 = RestoreEngine.ResolveSafeExtractPath(baseDir, "../../etc/passwd");
        string? unsafe2 = RestoreEngine.ResolveSafeExtractPath(baseDir, "/etc/passwd");
        Assert.IsNull(unsafe1, "Relative traversal must be rejected.");
        Assert.IsNull(unsafe2, "Absolute paths must be rejected.");
    }

    [TestMethod]
    public async Task CreateAsync_SkipsBackupsSubdirectoryToAvoidNesting()
    {
        // Create a nested backup that would be included if the engine weren't smart.
        string nested = Path.Combine(_fakeHome, ".claude", "backups", "backup-old.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        await File.WriteAllTextAsync(nested, "fake zip contents");

        string dest = Path.Combine(_fakeHome, "top.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded);

        List<string> entries = ListEntries(dest);
        Assert.IsFalse(entries.Any(e => e.Contains("claude-dir/backups/", StringComparison.Ordinal)),
            "The engine must not bundle its own output directory into a backup.");
    }

    [TestMethod]
    public async Task CreateAsync_NeverIncludesCacheDirectory()
    {
        // ClaudeForge-gui-state.json lives at ~/.claude/cache/ClaudeForge-gui-state.json
        // and must never appear in any backup archive (it contains ephemeral UI state,
        // not config).
        string cacheDir = Path.Combine(_fakeHome, ".claude", "cache");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "ClaudeForge-gui-state.json"), """{"w":1440}""");
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "other.dat"), "blob");

        string dest = Path.Combine(_fakeHome, "no-cache.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Full,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded, result.Message);

        List<string> entries = ListEntries(dest);
        Assert.IsFalse(entries.Any(e => e.Contains("cache/", StringComparison.Ordinal)),
            "The cache/ directory must never appear in the archive.");
    }

    /// <summary>
    /// user reported a slow Create Backup whose archive was
    /// dominated by <c>*.pre-restore-{stamp}.bak</c> files left behind by
    /// previous restores.  These are point-in-time sidecars and carry no
    /// user-authored content; they should not bloat new backups.  Pins
    /// the exclusion at both layers (top-level <c>~/.claude/</c> files via
    /// <c>BackupEngine.ShouldSkipHomeFile</c>, and nested files via
    /// <c>ZipArchiveWriter.EnumerateRecursive</c>).
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_ExcludesBakSidecarsAtAllDepths()
    {
        string home = Path.Combine(_fakeHome, ".claude");
        Directory.CreateDirectory(home);

        // Top-level .bak (caught by ShouldSkipHomeFile).
        await File.WriteAllTextAsync(
            Path.Combine(home, "settings.json.pre-restore-20260101-120000.bak"),
            "(old settings sidecar)");

        // Nested .bak in agents/ (caught by ZipArchiveWriter.EnumerateRecursive).
        string agents = Path.Combine(home, "agents");
        Directory.CreateDirectory(agents);
        await File.WriteAllTextAsync(Path.Combine(agents, "my-agent.md"), "real agent");
        await File.WriteAllTextAsync(
            Path.Combine(agents, "my-agent.md.pre-restore-20260101-120000.bak"),
            "(old agent sidecar)");

        // Plain *.bak — same convention, also excluded.
        await File.WriteAllTextAsync(Path.Combine(agents, "hand-edited.md.bak"),
            "(editor-style bak)");

        string dest = Path.Combine(_fakeHome, "bak-excluded.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded, result.Message);

        List<string> entries = ListEntries(dest);

        // The real agent must be in the archive.
        Assert.IsTrue(entries.Any(e => e.EndsWith("my-agent.md", StringComparison.Ordinal)),
            "The real agent file must be present in the archive.");

        // No .bak files at any depth.
        List<string> bakEntries = entries.Where(e => e.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.AreEqual(0, bakEntries.Count,
            $"No .bak files should be archived.  Found: {string.Join(", ", bakEntries)}");
    }

    [TestMethod]
    public async Task RestoreAsync_ReportsProgressDuringApplyPhase()
    {
        // Use the naming convention BackupEngine.List expects: backup-<stamp>.zip
        string backupDir = Path.Combine(_fakeHome, "backups");
        Directory.CreateDirectory(backupDir);
        string backupDest = Path.Combine(backupDir,
            $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        BackupResult createResult = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = backupDest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(createResult.Succeeded, createResult.Message);

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(backupDir);
        Assert.AreEqual(1, entries.Count);

        // Collect all progress reports emitted during restore.
        List<BackupProgress> reports = new();
        Progress<BackupProgress> progress = new(p => reports.Add(p));

        RestoreResult restoreResult = await BackupEngine.Default.RestoreAsync(entries[0], progress);
        Assert.IsTrue(restoreResult.Succeeded, restoreResult.Message);

        // Find "Applying restore…" and count distinct reports that come after it.
        int applyIndex = reports.FindIndex(r =>
            r.CurrentItem.Equals("Applying restore…", StringComparison.Ordinal));
        Assert.IsTrue(applyIndex >= 0, "At least one 'Applying restore…' progress report must be emitted.");

        List<BackupProgress> applyPhaseReports = reports.Skip(applyIndex + 1).ToList();
        Assert.IsTrue(applyPhaseReports.Count >= 2,
            $"Expected at least 2 progress reports after 'Applying restore…'; got {applyPhaseReports.Count}.");
    }

    [TestMethod]
    public async Task CreateAsync_ExcludesRuntimeAndBinarySubdirectories()
    {
        // Populate the runtime / binary directories that must never appear in backups.
        string claudeDir = Path.Combine(_fakeHome, ".claude");

        // downloads/ — update binaries (e.g. claude-2.0.0-win32-x64.exe)
        string downloadsDir = Path.Combine(claudeDir, "downloads");
        Directory.CreateDirectory(downloadsDir);
        await File.WriteAllTextAsync(Path.Combine(downloadsDir, "claude-2.0.0-win32-x64.exe"), "MZ");

        // statsig/ — telemetry & feature-flag data
        string statsigDir = Path.Combine(claudeDir, "statsig");
        Directory.CreateDirectory(statsigDir);
        await File.WriteAllTextAsync(Path.Combine(statsigDir, "user_config.json"), """{"flags":{}}""");

        // shell-snapshots/ — ephemeral bash command snapshots
        string shellSnapshotsDir = Path.Combine(claudeDir, "shell-snapshots");
        Directory.CreateDirectory(shellSnapshotsDir);
        await File.WriteAllTextAsync(Path.Combine(shellSnapshotsDir, "snapshot.jsonl"), """{"cmd":"ls"}""");

        // local/ — Claude Code binary install directory
        string localDir = Path.Combine(claudeDir, "local");
        Directory.CreateDirectory(localDir);
        await File.WriteAllTextAsync(Path.Combine(localDir, "claude.exe"), "MZ");

        string dest = Path.Combine(_fakeHome, "no-runtime.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Full,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded, result.Message);

        List<string> entries = ListEntries(dest);

        Assert.IsFalse(entries.Any(e => e.Contains("claude-dir/downloads/", StringComparison.Ordinal)),
            "downloads/ must be excluded (update binaries are not config data).");
        Assert.IsFalse(entries.Any(e => e.Contains("claude-dir/statsig/", StringComparison.Ordinal)),
            "statsig/ must be excluded (telemetry data is not config data).");
        Assert.IsFalse(entries.Any(e => e.Contains("claude-dir/shell-snapshots/", StringComparison.Ordinal)),
            "shell-snapshots/ must be excluded (runtime snapshots are not config data).");
        Assert.IsFalse(entries.Any(e => e.Contains("claude-dir/local/", StringComparison.Ordinal)),
            "local/ must be excluded (binary install directory is not config data).");

        // Sanity: the regular settings file should still be present.
        Assert.IsTrue(entries.Any(e => e.Contains("claude-dir/settings.json", StringComparison.Ordinal)),
            "settings.json must still be present in the archive.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  CreateAsync error / branch coverage
    //
    //  Existing tests cover happy paths (settings-only / full / retention /
    //  exclusion logic).  These add: argument validation, the Claude
    //  Desktop branch, the "no on-disk data at all" branch, and the
    //  cancellation contract.  The IO-failure catch at line 227 is
    //  difficult to trigger without injecting a fake ZipArchiveWriter
    //  — left uncovered for a future refactor that adds an injection
    //  seam.
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNull()
    {
        // Argument validation is contract — null request is a programmer
        // error, not a runtime "expected failure", and is allowed to throw.
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
            BackupEngine.Default.CreateAsync(null!));
    }

    [TestMethod]
    public async Task CreateAsync_ClaudeDesktopOnly_BundlesDesktopConfigFile()
    {
        // Setup() seeds Code data only.  Add a Desktop config file so the
        // Desktop branch has something to bundle.
        string desktopDir = PlatformPaths.DesktopConfigDir;
        Directory.CreateDirectory(desktopDir);
        string desktopCfg = PlatformPaths.DesktopConfigPath;
        await File.WriteAllTextAsync(desktopCfg, """{"theme":"system"}""");

        string dest = Path.Combine(_fakeHome, "desktop-only.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = false,
            IncludeClaudeDesktop = true,
        });

        Assert.IsTrue(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.IsTrue(
            entries.Any(e => e.EndsWith("ClaudeDesktop/claude_desktop_config.json", StringComparison.Ordinal)),
            "Desktop config file must be bundled when IncludeClaudeDesktop=true.");
        Assert.IsFalse(entries.Any(e => e.StartsWith("ClaudeCode/", StringComparison.Ordinal)
                                        && e != "ClaudeCode/claude.json"),
            "When IncludeClaudeCode=false, no ClaudeCode/* files should be bundled.");
    }

    [TestMethod]
    public async Task CreateAsync_DesktopProfilesAndPointer_BundledWhenPresent()
    {
        string desktopDir = PlatformPaths.DesktopConfigDir;
        Directory.CreateDirectory(desktopDir);
        await File.WriteAllTextAsync(PlatformPaths.DesktopConfigPath, """{"theme":"system"}""");

        // A profile + the .desktop-current pointer.
        string profilesDir = PlatformPaths.DesktopProfilesDirectory;
        Directory.CreateDirectory(Path.Combine(profilesDir, "work"));
        await File.WriteAllTextAsync(
            Path.Combine(profilesDir, "work", "claude_desktop_config.json"),
            """{"profile":"work"}""");
        await File.WriteAllTextAsync(
            PlatformPaths.DesktopCurrentProfileFilePath, "work");

        string dest = Path.Combine(_fakeHome, "desktop-profiles.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = false,
            IncludeClaudeDesktop = true,
        });

        Assert.IsTrue(result.Succeeded);
        List<string> entries = ListEntries(dest);
        Assert.IsTrue(
            entries.Any(e =>
                e.Contains("ClaudeDesktop/profiles/work/claude_desktop_config.json", StringComparison.Ordinal)),
            "Desktop profiles directory must be bundled when present.");
        Assert.IsTrue(entries.Any(e => e.EndsWith("ClaudeDesktop/.desktop-current", StringComparison.Ordinal)),
            ".desktop-current pointer must be bundled when present.");
    }

    [TestMethod]
    public async Task CreateAsync_NoOnDiskData_ProducesArchiveWithJustManifestAndSchemas()
    {
        // Wipe everything Setup() seeded so the engine has nothing to
        // bundle. The result should still succeed — an empty backup is
        // valid (manifest + bundled schemas + nothing else).
        Directory.Delete(_fakeHome, recursive: true);
        Directory.CreateDirectory(_fakeHome);

        string dest = Path.Combine(_fakeHome, "empty.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = true,
        });

        Assert.IsTrue(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.IsTrue(entries.Any(e => e == "manifest.json"),
            "Manifest must be present even in an empty backup.");
        // Schemas are always bundled regardless of user data.
        Assert.IsTrue(entries.Any(e => e.StartsWith("Schemas/", StringComparison.Ordinal)),
            "Bundled schemas must be present even in an empty backup.");
        // Zero ClaudeCode / ClaudeDesktop entries (nothing on disk to bundle).
        Assert.IsFalse(entries.Any(e => e.StartsWith("ClaudeCode/", StringComparison.Ordinal)));
        Assert.IsFalse(entries.Any(e => e.StartsWith("ClaudeDesktop/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CreateAsync_BothProductsExcluded_StillProducesValidArchive()
    {
        // Edge case: caller sets IncludeClaudeCode = IncludeClaudeDesktop = false.
        // Result should still be a valid empty backup (manifest + schemas);
        // the contract is permissive — we don't reject the request as a
        // programmer error.
        string dest = Path.Combine(_fakeHome, "no-products.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = false,
            IncludeClaudeDesktop = false,
        });

        Assert.IsTrue(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.IsTrue(entries.Any(e => e == "manifest.json"));
        Assert.IsFalse(entries.Any(e => e.StartsWith("ClaudeCode/", StringComparison.Ordinal)));
        Assert.IsFalse(entries.Any(e => e.StartsWith("ClaudeDesktop/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CreateAsync_DestinationDirDoesNotExist_AutoCreatesParents()
    {
        // BackupEngine auto-creates parent directories for the destination
        // path so callers don't have to pre-create them.  Locks the
        // contract — the caller passes a path and gets back a working
        // archive without intermediate Directory.CreateDirectory calls.
        string dest = Path.Combine(_fakeHome, "subdir1", "subdir2", "out.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsTrue(File.Exists(dest));
        Assert.IsTrue(Directory.Exists(Path.GetDirectoryName(dest)),
            "Parent directories must have been auto-created.");
    }

    [TestMethod]
    public async Task CreateAsync_AlreadyCancelledToken_DoesNotProduceFinalArchive()
    {
        // Exercise the cancellation contract: a pre-cancelled token
        // either:
        //   (a) returns a Cancelled result (line 223-225 OCE catch), OR
        //   (b) propagates OperationCanceledException out of the method.
        // Either is valid; the contract this test locks is that the
        // FINAL destination archive must NOT exist on disk after a
        // cancelled call (no half-formed backup left for the next List
        // call to surface).
        string dest = Path.Combine(_fakeHome, "cancelled.zip");
        CancellationToken ct = new(canceled: true);

        try
        {
            BackupResult result = await BackupEngine.Default.CreateAsync(
                new BackupRequest
                {
                    DestinationZipPath = dest,
                    Mode = BackupMode.SettingsOnly,
                    IncludeClaudeCode = true,
                    IncludeClaudeDesktop = false,
                },
                ct: ct);

            // Path (a): caught and returned as a failure result.
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(result.Message, "cancelled", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            // Path (b): propagated out. Also acceptable.
        }

        Assert.IsFalse(File.Exists(dest),
            "Cancellation must not leave a final backup archive on disk; "
            + "the temp file is cleaned up either by the OCE catch's writer "
            + "Dispose or by the early-throw before CommitAsync renames temp -> dest.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  RestoreEngine.ValidateExtractedConfigsAsync
    //
    //  ValidateExtractedConfigsAsync is private; we exercise it indirectly
    //  through RestoreAsync, which surfaces its output in
    //  RestoreResult.ValidationWarnings. The four tests below cover the
    //  branches the existing roundtrip case skipped over: schema violation
    //  produces warnings, missing Schemas/ dir is a graceful skip, corrupt
    //  schema entries don't crash, and corrupt settings JSON inside the
    //  backup is silently dropped from the validation set.
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RestoreAsync_ValidationWarnings_PopulatedForSchemaViolatingSettings()
    {
        // cleanupPeriodDays must be type=integer per the bundled schema.
        // A string violates the schema; the validator should produce at
        // least one warning naming that property.
        await File.WriteAllTextAsync(
            Path.Combine(_fakeHome, ".claude", "settings.json"),
            "{\"cleanupPeriodDays\":\"not-a-number\"}");

        string dest = Path.Combine(_fakeHome, "backup-violation.zip");
        BackupResult create = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(1, entries.Count);

        RestoreResult restore = await BackupEngine.Default.RestoreAsync(entries[0]);
        Assert.IsTrue(restore.Succeeded,
            "Restore must succeed even when validation finds violations — "
            + "validation is informational, not gating.");
        Assert.IsNotNull(restore.ValidationWarnings,
            "ValidationWarnings must be populated when a settings file violates the schema.");
        Assert.IsTrue(restore.ValidationWarnings!.Count > 0);
        Assert.IsTrue(
            restore.ValidationWarnings!.Any(w => w.Contains("cleanupPeriodDays", StringComparison.Ordinal)),
            "Warning text must surface the violating property name (cleanupPeriodDays).");
    }

    [TestMethod]
    public async Task RestoreAsync_NoValidationWarnings_WhenAllSettingsAreValid()
    {
        // Setup() already wrote {"theme":"dark"} as settings.json — that's
        // schema-valid for ClaudeCode (theme is a free property allowed
        // anywhere in the schema). Roundtrip backup → restore.
        string dest = Path.Combine(_fakeHome, "backup-clean.zip");
        BackupResult create = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(create.Succeeded);

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await BackupEngine.Default.RestoreAsync(entries[0]);

        Assert.IsTrue(restore.Succeeded);
        // ValidationWarnings is null OR empty when nothing violates.
        // The implementation returns null when the warnings list is empty
        // (RestoreResult ctor argument), so prefer the IsNull check; fall
        // back to Count == 0 for resilience to a future shape change.
        if (restore.ValidationWarnings is not null)
        {
            Assert.AreEqual(0, restore.ValidationWarnings.Count);
        }
    }

    [TestMethod]
    public async Task RestoreAsync_SchemasDirectoryStripped_GracefullySkipsValidation()
    {
        // Old backups (pre-Schemas-bundle) didn't include a Schemas/
        // directory. The validator must early-return cleanly rather than
        // crashing — `if (!Directory.Exists(schemasDir)) return warnings;`.
        await File.WriteAllTextAsync(
            Path.Combine(_fakeHome, ".claude", "settings.json"),
            "{\"cleanupPeriodDays\":\"would-violate-but-no-schema\"}");

        string dest = Path.Combine(_fakeHome, "backup-noschemas.zip");
        BackupResult create = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(create.Succeeded);

        // Strip every Schemas/ entry from the zip after creation.
        StripZipEntries(dest, e => e.FullName.StartsWith("Schemas/", StringComparison.Ordinal));

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await BackupEngine.Default.RestoreAsync(entries[0]);
        Assert.IsTrue(restore.Succeeded,
            "Restore from a pre-Schemas backup must still succeed — old backups are valid.");

        // No schemas → no warnings, even though the settings file would
        // have violated had a schema been present.
        if (restore.ValidationWarnings is not null)
        {
            Assert.AreEqual(0, restore.ValidationWarnings.Count);
        }
    }

    [TestMethod]
    public async Task RestoreAsync_CorruptSchemaEntryInBackup_DoesNotCrash()
    {
        // The validator catches per-schema parse failures inside the
        // schemaByName loop — a single bad entry must not prevent the
        // others from loading or the restore from succeeding.
        string dest = Path.Combine(_fakeHome, "backup-badschema.zip");
        BackupResult create = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(create.Succeeded);

        // Mangle one schema entry's content to be unparseable.
        OverwriteZipEntry(dest, "Schemas/claude-code-settings.json",
            "{ this is not / valid : JSON ");

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await BackupEngine.Default.RestoreAsync(entries[0]);
        Assert.IsTrue(restore.Succeeded,
            "A corrupt bundled schema must not abort the restore — validation is informational.");
    }

    [TestMethod]
    public async Task RestoreAsync_CorruptSettingsFileInBackup_SkippedFromValidation()
    {
        // The validator's per-file try/catch around JsonNode.Parse silently
        // skips files that aren't valid JSON. The restore still succeeds
        // because applying a malformed file is a separate path (the apply
        // phase swallows it as a file-failure rather than aborting).
        string dest = Path.Combine(_fakeHome, "backup-badsettings.zip");
        BackupResult create = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(create.Succeeded);

        // The bundled archive's settings.json appears under
        // ClaudeCode/claude-dir/settings.json. Mangle it.
        OverwriteZipEntry(dest, "ClaudeCode/claude-dir/settings.json",
            "{ corrupt JSON without close brace");

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await BackupEngine.Default.RestoreAsync(entries[0]);
        Assert.IsTrue(restore.Succeeded,
            "Restore must tolerate a corrupt settings file in the backup — validation skips it silently.");

        // Validation should produce no warnings for the unparseable file
        // (the catch silently skips it; nothing to report).
        if (restore.ValidationWarnings is not null)
        {
            Assert.IsFalse(
                restore.ValidationWarnings.Any(w => w.Contains("settings.json", StringComparison.Ordinal)),
                "Corrupt settings file must NOT produce a warning — the validator skips it before evaluation.");
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────────────────────────────

    private static List<string> ListEntries(string zipPath)
    {
        using FileStream fs = File.OpenRead(zipPath);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    private static string ReadEntryText(string zipPath, string entryName)
    {
        using FileStream fs = File.OpenRead(zipPath);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException(
            $"Entry '{entryName}' not found in '{zipPath}'.");
        using Stream es = entry.Open();
        using StreamReader sr = new(es);
        return sr.ReadToEnd();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Sanitized backup mode (2026-05-14)
    //
    //  Three contracts to pin:
    //   1. Sensitive values inside *.json files get rewritten to the
    //      "[redacted]" marker (JsonRedactor invocation reaches the wire).
    //   2. The credentials file is skipped entirely regardless of the
    //      IncludeCredentials flag.
    //   3. RestoreEngine refuses to apply the resulting archive — the
    //      mode is sharing-only.
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CreateAsync_Sanitized_RedactsSecretsInJsonFiles()
    {
        // Seed a settings.json with a secret-bearing env entry and an mcpServers
        // entry whose headers carry an Authorization token — the two real
        // shapes the sanitized mode exists to scrub.
        string home = Path.Combine(_fakeHome, ".claude");
        await File.WriteAllTextAsync(Path.Combine(home, "settings.json"), """
                                                                          {
                                                                            "theme": "dark",
                                                                            "env": { "ANTHROPIC_API_KEY": "sk-ant-real" },
                                                                            "mcpServers": {
                                                                              "github": {
                                                                                "headers": { "Authorization": "Bearer ghp_xyz" }
                                                                              }
                                                                            }
                                                                          }
                                                                          """);

        string dest = Path.Combine(_fakeHome, "sanitized.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Sanitized,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded, result.Message);

        // The settings.json entry in the archive must NOT contain the raw
        // secret strings (case-sensitive substring check), and must contain
        // the redaction marker.
        string settingsEntry = ListEntries(dest)
            .Single(e => e.EndsWith("claude-dir/settings.json", StringComparison.Ordinal));
        string text = ReadEntryText(dest, settingsEntry);

        Assert.IsFalse(text.Contains("sk-ant-real", StringComparison.Ordinal),
            "Raw env-var secret must not appear in a Sanitized archive.");
        Assert.IsFalse(text.Contains("ghp_xyz", StringComparison.Ordinal),
            "Raw MCP Authorization header must not appear in a Sanitized archive.");
        Assert.IsTrue(text.Contains("[redacted]", StringComparison.Ordinal),
            "Redaction marker must appear in the sanitized settings.json.");
        // Non-sensitive sibling is preserved.
        Assert.IsTrue(text.Contains("\"theme\"", StringComparison.Ordinal),
            "Non-sensitive keys must round-trip through the redactor unchanged.");
    }

    [TestMethod]
    public async Task CreateAsync_Sanitized_SkipsCredentialsFileEvenWhenIncludeCredentialsTrue()
    {
        // Seed a credentials file under ~/.claude — in non-sanitized backups
        // with IncludeCredentials=true it would be archived; in sanitized
        // mode it must be skipped regardless.
        string home = Path.Combine(_fakeHome, ".claude");
        await File.WriteAllTextAsync(Path.Combine(home, ".credentials.json"),
            """{"token": "anthropic-oauth-token-abc"}""");

        string dest = Path.Combine(_fakeHome, "sanitized-no-creds.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Sanitized,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
            IncludeCredentials = true, // user opted in — sanitized still wins
        });
        Assert.IsTrue(result.Succeeded, result.Message);

        List<string> entries = ListEntries(dest);
        Assert.IsFalse(entries.Any(e => e.EndsWith(".credentials.json", StringComparison.Ordinal)),
            "Credentials file must be hard-skipped in Sanitized mode regardless of the flag.");

        // Manifest must agree — IncludedCredentials=false records that the
        // archive carries no credentials data.
        await using FileStream fs = File.OpenRead(dest);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json")!;
        await using Stream ms = await manifestEntry.OpenAsync();
        BackupManifest manifest = JsonSerializer.Deserialize(ms, BackupJsonContext.Default.BackupManifest)!;
        Assert.IsFalse(manifest.IncludedCredentials,
            "Manifest must declare credentials excluded in Sanitized mode.");
        Assert.AreEqual(BackupMode.Sanitized, manifest.Mode);
    }

    [TestMethod]
    public async Task CreateAsync_Sanitized_AddsManifestSharingWarning()
    {
        string dest = Path.Combine(_fakeHome, "sanitized-warning.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Sanitized,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded);

        await using FileStream fs = File.OpenRead(dest);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json")!;
        await using Stream ms = await manifestEntry.OpenAsync();
        BackupManifest manifest = JsonSerializer.Deserialize(ms, BackupJsonContext.Default.BackupManifest)!;

        Assert.IsTrue(manifest.Warnings.Any(w =>
                w.Contains("Sanitized backup", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("Not restorable", StringComparison.OrdinalIgnoreCase)),
            "Sanitized backups must carry a manifest warning naming the contract " +
            "(redaction + non-restorable).");
    }

    [TestMethod]
    public async Task RestoreAsync_SanitizedBackup_IsRefusedWithClearMessage()
    {
        // Build a sanitized backup, then attempt to restore it — the engine
        // must refuse with a non-fatal RestoreResult naming the mode as the
        // reason (the GUI's Restore button is also disabled at the row
        // level via BackupRowViewModel.IsRestorable, but the engine guard
        // is the load-bearing check).
        string backupDir = Path.Combine(_fakeHome, "backups");
        Directory.CreateDirectory(backupDir);
        string backupDest = Path.Combine(backupDir,
            $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        BackupResult create = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = backupDest,
            Mode = BackupMode.Sanitized,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        IReadOnlyList<BackupEntry> entries = BackupEngine.Default.List(backupDir);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(BackupMode.Sanitized, entries[0].Manifest!.Mode);

        RestoreResult restore = await BackupEngine.Default.RestoreAsync(entries[0]);

        Assert.IsFalse(restore.Succeeded, "Sanitized backups must not be restorable.");
        Assert.IsTrue(restore.Message.Contains("sanitized", StringComparison.OrdinalIgnoreCase),
            $"Refusal message should name the sanitized contract; got: {restore.Message}");
        Assert.AreEqual(0, restore.ItemsRestored);
    }

    /// <summary>Removes every entry matching <paramref name="predicate"/> from the zip in-place.</summary>
    private static void StripZipEntries(string zipPath, Func<ZipArchiveEntry, bool> predicate)
    {
        using FileStream fs = new(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(fs, ZipArchiveMode.Update);
        List<ZipArchiveEntry> toRemove = archive.Entries.Where(predicate).ToList();
        foreach (ZipArchiveEntry e in toRemove)
        {
            e.Delete();
        }
    }

    /// <summary>
    /// Replaces <paramref name="entryName"/>'s content with <paramref name="newContent"/>
    /// in-place. Adds the entry if it wasn't already present.
    /// </summary>
    private static void OverwriteZipEntry(string zipPath, string entryName, string newContent)
    {
        using FileStream fs = new(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(fs, ZipArchiveMode.Update);
        archive.GetEntry(entryName)?.Delete();
        ZipArchiveEntry newEntry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        using Stream es = newEntry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(newContent);
        es.Write(bytes, 0, bytes.Length);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  H1 — Expanded sanitized redaction (text + JSON, parallel precompute)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CreateAsync_Sanitized_RedactsHookScriptShellAssignment()
    {
        // Plant a hook script (non-JSON) with a hard-coded Anthropic
        // key inside a shell-style export.  Sanitized mode must scrub it
        // via TextRedactor; pre-fix, the .json-only filter let it through
        // verbatim.
        string hooksDir = Path.Combine(_fakeHome, ".claude", "hooks");
        Directory.CreateDirectory(hooksDir);
        await File.WriteAllTextAsync(
            Path.Combine(hooksDir, "post-prompt.sh"),
            "#!/bin/bash\nexport ANTHROPIC_API_KEY=sk-ant-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n");

        string dest = Path.Combine(_fakeHome, "sanitized-with-hook.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Sanitized,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded, result.Message);

        string hookEntryName = ListEntries(dest)
            .Single(e => e.EndsWith("hooks/post-prompt.sh", StringComparison.Ordinal));
        string text = ReadEntryText(dest, hookEntryName);

        Assert.IsFalse(text.Contains("sk-ant-AAA", StringComparison.Ordinal),
            "Raw Anthropic key inside a hook .sh must not survive Sanitized backup.");
        Assert.IsTrue(text.Contains("[redacted]", StringComparison.Ordinal),
            "Redaction marker must appear in the sanitized hook script.");
        Assert.IsTrue(text.Contains("ANTHROPIC_API_KEY"),
            "Shell-style redaction must preserve the variable name (diagnostic value).");
        Assert.IsTrue(text.Contains("#!/bin/bash"),
            "Non-secret content (shebang) must round-trip unchanged.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  H1 — PrecomputeTransformsAsync (parallel pre-commit transform)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task PrecomputeTransformsAsync_RunsInParallel_AcrossMultipleEntries()
    {
        // Probe the parallel path directly via ZipArchiveWriter so the
        // assertion is independent of BackupEngine wiring.  Schedule 20
        // files with a slow transformer; assert that wall-clock time is
        // closer to (FileCount / MaxParallel × SleepMs) than to the
        // serial worst case (FileCount × SleepMs).
        const int fileCount = 20;
        const int sleepMs = 50;
        const int parallel = 4;
        const int worstCaseMs = fileCount * sleepMs; // 1000 ms
        // Theoretical floor with perfect parallelism would be
        // (fileCount / parallel) × sleepMs = 250 ms; the assertion
        // below uses worstCaseMs × 3/4 = 750 ms as a generous upper
        // bound that still catches serial-execution regressions.

        // Plant the source files.
        string srcDir = Path.Combine(_fakeHome, "parallel-src");
        Directory.CreateDirectory(srcDir);
        for (int i = 0; i < fileCount; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(srcDir, $"f{i}.txt"), $"content-{i}");
        }

        string dest = Path.Combine(_fakeHome, "parallel.zip");
        int hits = 0;

        await using ZipArchiveWriter writer = ZipArchiveWriter.Create(dest);
        for (int i = 0; i < fileCount; i++)
        {
            writer.AddFile(Path.Combine(srcDir, $"f{i}.txt"), $"x/f{i}.txt");
        }

        writer.FileTransformer = path =>
        {
            Interlocked.Increment(ref hits);
            Thread.Sleep(sleepMs);
            return $"transformed:{Path.GetFileName(path)}";
        };

        Stopwatch sw = Stopwatch.StartNew();
        await writer.PrecomputeTransformsAsync(maxDegreeOfParallelism: parallel);
        sw.Stop();

        Assert.AreEqual(fileCount, hits, "Every queued source entry must be transformed.");
        Assert.IsTrue(sw.ElapsedMilliseconds < worstCaseMs * 3 / 4,
            $"Parallel precompute should run far faster than serial worst case ({worstCaseMs} ms). " +
            $"Actual: {sw.ElapsedMilliseconds} ms; serial floor would be {worstCaseMs} ms.");

        // Sanity: the precomputed strings actually land in the archive.
        await writer.CommitAsync();
        string sample = ReadEntryText(dest, "x/f0.txt");
        Assert.AreEqual("transformed:f0.txt", sample);
    }

    [TestMethod]
    public async Task PrecomputeTransformsAsync_ExceptionPerFile_SkipsThatEntryOnly()
    {
        // A transformer that throws for one specific file: that file
        // must land in SkippedFiles and NOT be cached; the rest of the
        // archive must complete normally.
        string srcDir = Path.Combine(_fakeHome, "exc-src");
        Directory.CreateDirectory(srcDir);
        string goodPath = Path.Combine(srcDir, "good.txt");
        string badPath = Path.Combine(srcDir, "bad.txt");
        await File.WriteAllTextAsync(goodPath, "good");
        await File.WriteAllTextAsync(badPath, "bad");

        string dest = Path.Combine(_fakeHome, "per-file-exc.zip");
        await using ZipArchiveWriter writer = ZipArchiveWriter.Create(dest);
        writer.AddFile(goodPath, "good.txt");
        writer.AddFile(badPath, "bad.txt");
        writer.FileTransformer = path => Path.GetFileName(path) == "bad.txt"
            ? throw new IOException("simulated per-file failure")
            : "ok";

        await writer.PrecomputeTransformsAsync(maxDegreeOfParallelism: 2);

        Assert.IsTrue(writer.SkippedFiles.Any(p => p.EndsWith("bad.txt", StringComparison.Ordinal)),
            "Failed transformer file must appear in SkippedFiles.");

        // The commit must still succeed (and bad.txt will fail again on
        // the synchronous fallback path, adding to SkippedFiles a 2nd
        // time — that's expected mirroring of the non-precompute path).
        await writer.CommitAsync();
        Assert.IsTrue(File.Exists(dest));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  M2 — BuildSanitizationPlaceholder uses JsonSerializer (escape correctness)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void BuildSanitizationPlaceholder_ControlCharsInFilename_ProducesValidJson()
    {
        // Pre-fix, hand-rolled escaping only handled \ and " — a control
        // character in the filename would have emitted invalid JSON.
        // JsonSerializer handles all escape sequences correctly.
        string pathWithTab = "C:\\foo\\bar\twith-tab.json";
        string pathWithNewline = "/home/user/a\nb.json";

        string placeholder1 = BackupEngine.BuildSanitizationPlaceholder(pathWithTab, "io-failure");
        string placeholder2 = BackupEngine.BuildSanitizationPlaceholder(pathWithNewline, "io-failure");

        // Round-trip: both must parse cleanly as JSON.
        JsonNode? doc1 = JsonNode.Parse(placeholder1);
        JsonNode? doc2 = JsonNode.Parse(placeholder2);
        Assert.IsNotNull(doc1);
        Assert.IsNotNull(doc2);
    }

    [TestMethod]
    public void BuildSanitizationPlaceholder_FilenameOnly_DoesNotLeakFullPath()
    {
        // Path.GetFileName strips the directory; placeholder must not
        // disclose user directory layout (same hygiene principle as M1's
        // SanitiseExceptionForStatus).
        string fullPath = Path.Combine("C:\\Users\\someone\\.claude", "settings.json");
        string placeholder = BackupEngine.BuildSanitizationPlaceholder(fullPath, "io-failure");

        Assert.IsFalse(placeholder.Contains("someone", StringComparison.Ordinal),
            "Username from the source path must not appear in the placeholder.");
        Assert.IsFalse(placeholder.Contains(".claude", StringComparison.Ordinal),
            "Directory components must not appear in the placeholder.");

        JsonObject parsed = JsonNode.Parse(placeholder)!.AsObject();
        Assert.AreEqual("settings.json", (string?)parsed["_file"]);
    }

    [TestMethod]
    public void BuildSanitizationPlaceholder_ReasonWithQuotes_RoundTrips()
    {
        string reason = "redaction-failed: \"unexpected token\" at line 1";
        string placeholder = BackupEngine.BuildSanitizationPlaceholder("foo.json", reason);

        JsonObject parsed = JsonNode.Parse(placeholder)!.AsObject();
        Assert.AreEqual(reason, (string?)parsed["_claudeforge_sanitization_error"]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MOutOfMemoryException in transformer is caught + skipped (proxy)
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task CreateAsync_TransformerThrowsOutOfMemory_SkipsFileAndSucceeds()
    {
        // Throwing a real OOM is impractical and would crash the test
        // host; instead inject a probe transformer that throws OOM for
        // a specific file.  This is a faithful proxy for the
        // exception-propagation contract under test.
        //
        // The probe is wired AFTER BackupEngine.CreateAsync sets its
        // own RedactFileForSharing transformer; we test the underlying
        // ZipArchiveWriter behaviour directly so the assertion is
        // unambiguous.
        string srcDir = Path.Combine(_fakeHome, "oom-src");
        Directory.CreateDirectory(srcDir);
        string oomPath = Path.Combine(srcDir, "oom.json");
        string safePath = Path.Combine(srcDir, "safe.json");
        await File.WriteAllTextAsync(oomPath, """{"k":"v"}""");
        await File.WriteAllTextAsync(safePath, """{"k":"v"}""");

        string dest = Path.Combine(_fakeHome, "oom-proxy.zip");
        await using ZipArchiveWriter writer = ZipArchiveWriter.Create(dest);
        writer.AddFile(oomPath, "oom.json");
        writer.AddFile(safePath, "safe.json");
        writer.FileTransformer = path => Path.GetFileName(path) == "oom.json"
            ? throw new OutOfMemoryException("simulated OOM on a huge file")
            : """{"k":"redacted"}""";

        // Precompute should record the failure without throwing.
        await writer.PrecomputeTransformsAsync();
        Assert.IsTrue(writer.SkippedFiles.Any(p => p.EndsWith("oom.json", StringComparison.Ordinal)),
            "OOM during transform must add the file to SkippedFiles.");

        // Commit must still succeed for the other file.
        long bytes = await writer.CommitAsync();
        Assert.IsTrue(bytes > 0);
        Assert.IsTrue(File.Exists(dest));
    }
}