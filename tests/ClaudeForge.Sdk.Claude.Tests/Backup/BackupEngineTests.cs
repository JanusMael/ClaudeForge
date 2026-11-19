using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

// Interlocked + Thread for the parallel-precompute test

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Backup;

/// <summary>
/// End-to-end round-trip tests for <see cref="BackupEngine"/>. Each test isolates its
/// own fake <c>$HOME</c> via the <c>USERPROFILE</c> (Windows) or <c>HOME</c> (Unix)
/// environment variable so the real user's Claude data is never touched.
/// </summary>
/// <remarks>
/// ⚠ <b>Only what is about Claude is tested here.</b> The engine mechanics that read the descriptor
/// (sections, skip rules, the credential opt-in, listing, retention, restore, cancellation and the
/// sanitized contract) are tested on a product that is not Claude, in
/// <see cref="NeutralProductBackupTests"/> (<c>plans/00008</c> step 5).
/// </remarks>
public sealed class BackupEngineTests : IDisposable
{
    private string _fakeHome = string.Empty;

    public BackupEngineTests() => Setup();

    private void Setup()
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

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateAsync_SkipsBackupsSubdirectoryToAvoidNesting()
    {
        // Create a nested backup that would be included if the engine weren't smart.
        string nested = Path.Combine(_fakeHome, ".claude", "backups", "backup-old.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        await File.WriteAllTextAsync(nested, "fake zip contents", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "top.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(result.Succeeded);

        List<string> entries = ListEntries(dest);
        Assert.False(entries.Any(e => e.Contains("claude-dir/backups/", StringComparison.Ordinal)),
            "The engine must not bundle its own output directory into a backup.");
    }

    [Fact]
    public async Task CreateAsync_NeverIncludesCacheDirectory()
    {
        // ClaudeForge-gui-state.json lives at ~/.claude/cache/ClaudeForge-gui-state.json
        // and must never appear in any backup archive (it contains ephemeral UI state,
        // not config).
        string cacheDir = Path.Combine(_fakeHome, ".claude", "cache");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "ClaudeForge-gui-state.json"), """{"w":1440}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "other.dat"), "blob", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "no-cache.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Full,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(result.Succeeded, result.Message);

        List<string> entries = ListEntries(dest);
        Assert.False(entries.Any(e => e.Contains("cache/", StringComparison.Ordinal)),
            "The cache/ directory must never appear in the archive.");
    }

    [Fact]
    public async Task CreateAsync_ExcludesRuntimeAndBinarySubdirectories()
    {
        // Populate the runtime / binary directories that must never appear in backups.
        string claudeDir = Path.Combine(_fakeHome, ".claude");

        // downloads/ — update binaries (e.g. claude-2.0.0-win32-x64.exe)
        string downloadsDir = Path.Combine(claudeDir, "downloads");
        Directory.CreateDirectory(downloadsDir);
        await File.WriteAllTextAsync(Path.Combine(downloadsDir, "claude-2.0.0-win32-x64.exe"), "MZ", TestContext.Current.CancellationToken);

        // statsig/ — telemetry & feature-flag data
        string statsigDir = Path.Combine(claudeDir, "statsig");
        Directory.CreateDirectory(statsigDir);
        await File.WriteAllTextAsync(Path.Combine(statsigDir, "user_config.json"), """{"flags":{}}""", TestContext.Current.CancellationToken);

        // shell-snapshots/ — ephemeral bash command snapshots
        string shellSnapshotsDir = Path.Combine(claudeDir, "shell-snapshots");
        Directory.CreateDirectory(shellSnapshotsDir);
        await File.WriteAllTextAsync(Path.Combine(shellSnapshotsDir, "snapshot.jsonl"), """{"cmd":"ls"}""", TestContext.Current.CancellationToken);

        // local/ — Claude Code binary install directory
        string localDir = Path.Combine(claudeDir, "local");
        Directory.CreateDirectory(localDir);
        await File.WriteAllTextAsync(Path.Combine(localDir, "claude.exe"), "MZ", TestContext.Current.CancellationToken);

        // cache/ — schema / app cache, regenerated on demand. ⚠ Added when the eight skip `if`s
        // became ClaudeHomeSkipRules: it was the one rule of the seven with no test, so a typo in
        // its row would have shipped an app-cache directory into every user's archive silently.
        string cacheDir = Path.Combine(claudeDir, "cache");
        Directory.CreateDirectory(cacheDir);
        await File.WriteAllTextAsync(Path.Combine(cacheDir, "schema.json"), """{"cached":true}""", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "no-runtime.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Full,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(result.Succeeded, result.Message);

        List<string> entries = ListEntries(dest);

        Assert.False(entries.Any(e => e.Contains("claude-dir/downloads/", StringComparison.Ordinal)),
            "downloads/ must be excluded (update binaries are not config data).");
        Assert.False(entries.Any(e => e.Contains("claude-dir/statsig/", StringComparison.Ordinal)),
            "statsig/ must be excluded (telemetry data is not config data).");
        Assert.False(entries.Any(e => e.Contains("claude-dir/shell-snapshots/", StringComparison.Ordinal)),
            "shell-snapshots/ must be excluded (runtime snapshots are not config data).");
        Assert.False(entries.Any(e => e.Contains("claude-dir/local/", StringComparison.Ordinal)),
            "local/ must be excluded (binary install directory is not config data).");
        Assert.False(entries.Any(e => e.Contains("claude-dir/cache/", StringComparison.Ordinal)),
            "cache/ must be excluded (regenerated on demand; not config data).");

        // Sanity: the regular settings file should still be present.
        Assert.True(entries.Any(e => e.Contains("claude-dir/settings.json", StringComparison.Ordinal)),
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

    [Fact]
    public async Task CreateAsync_ClaudeDesktopOnly_BundlesDesktopConfigFile()
    {
        // Setup() seeds Code data only.  Add a Desktop config file so the
        // Desktop branch has something to bundle.
        string desktopDir = PlatformPaths.DesktopConfigDir;
        Directory.CreateDirectory(desktopDir);
        string desktopCfg = PlatformPaths.DesktopConfigPath;
        await File.WriteAllTextAsync(desktopCfg, """{"theme":"system"}""", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "desktop-only.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeDesktopProduct],
        }, ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.True(
            entries.Any(e => e.EndsWith("ClaudeDesktop/claude_desktop_config.json", StringComparison.Ordinal)),
            "Desktop config file must be bundled when its product is in the requested set.");
        Assert.False(entries.Any(e => e.StartsWith("ClaudeCode/", StringComparison.Ordinal)
                                        && e != "ClaudeCode/claude.json"),
            "When Claude Code is absent from the requested set, no ClaudeCode/* files should be bundled.");
    }

    [Fact]
    public async Task CreateAsync_DesktopProfilesAndPointer_BundledWhenPresent()
    {
        string desktopDir = PlatformPaths.DesktopConfigDir;
        Directory.CreateDirectory(desktopDir);
        await File.WriteAllTextAsync(PlatformPaths.DesktopConfigPath, """{"theme":"system"}""", TestContext.Current.CancellationToken);

        // A profile + the .desktop-current pointer.
        string profilesDir = PlatformPaths.DesktopProfilesDirectory;
        Directory.CreateDirectory(Path.Combine(profilesDir, "work"));
        await File.WriteAllTextAsync(
            Path.Combine(profilesDir, "work", "claude_desktop_config.json"),
            """{"profile":"work"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            PlatformPaths.DesktopCurrentProfileFilePath, "work", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "desktop-profiles.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeDesktopProduct],
        }, ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(result.Succeeded);
        List<string> entries = ListEntries(dest);
        Assert.True(
            entries.Any(e =>
                e.Contains("ClaudeDesktop/profiles/work/claude_desktop_config.json", StringComparison.Ordinal)),
            "Desktop profiles directory must be bundled when present.");
        Assert.True(entries.Any(e => e.EndsWith("ClaudeDesktop/.desktop-current", StringComparison.Ordinal)),
            ".desktop-current pointer must be bundled when present.");
    }

    [Fact]
    public async Task RoundTrip_DesktopProfilesAndPointer_RestoreToTheirRealPaths()
    {
        // ⚠ The sibling above covers only the WRITE side. Restoring these two sections was
        // uncovered — the frozen archive in BackupArchiveCompatibilityTests carries neither — so
        // when the five hand-written restore blocks became rows in RestoreEngine.ArchiveSections,
        // two of the five rows had no test at all. A row with a wrong sub-path restores nothing
        // and still reports success, which is the failure mode that needs a test rather than a
        // reading.
        string desktopDir = PlatformPaths.DesktopConfigDir;
        Directory.CreateDirectory(desktopDir);
        await File.WriteAllTextAsync(PlatformPaths.DesktopConfigPath, """{"theme":"system"}""", TestContext.Current.CancellationToken);

        string profilesDir = PlatformPaths.DesktopProfilesDirectory;
        Directory.CreateDirectory(Path.Combine(profilesDir, "work"));
        string profileConfig = Path.Combine(profilesDir, "work", "claude_desktop_config.json");
        await File.WriteAllTextAsync(profileConfig, """{"profile":"work"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(PlatformPaths.DesktopCurrentProfileFilePath, "work", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "backup-desktop-roundtrip.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeDesktopProduct],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded, create.Message);

        // Mutate both so a no-op restore cannot pass.
        await File.WriteAllTextAsync(profileConfig, """{"profile":"CLOBBERED"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(PlatformPaths.DesktopCurrentProfileFilePath, "clobbered", TestContext.Current.CancellationToken);

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        Assert.Single(entries);

        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(restore.Succeeded, restore.Message);

        MessageAssert.Contains("\"work\"", await File.ReadAllTextAsync(profileConfig, TestContext.Current.CancellationToken),
            "The profiles subtree must restore, not just the top-level Desktop config.");
        MessageAssert.Equal("work",
            await File.ReadAllTextAsync(PlatformPaths.DesktopCurrentProfileFilePath, TestContext.Current.CancellationToken),
            "The .desktop-current pointer is a single file, not a directory — a row that gets that "
            + "wrong restores nothing and still succeeds.");
    }

    [Fact]
    public async Task CreateAsync_NoOnDiskData_ProducesArchiveWithJustManifestAndSchemas()
    {
        // Wipe everything Setup() seeded so the engine has nothing to
        // bundle. The result should still succeed — an empty backup is
        // valid (manifest + bundled schemas + nothing else).
        Directory.Delete(_fakeHome, recursive: true);
        Directory.CreateDirectory(_fakeHome);

        string dest = Path.Combine(_fakeHome, "empty.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), SchemaRegistry.ClaudeDesktopProduct],
        }, ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(result.Succeeded, result.Message);
        List<string> entries = ListEntries(dest);
        Assert.True(entries.Any(e => e == "manifest.json"),
            "Manifest must be present even in an empty backup.");
        // Schemas are always bundled regardless of user data.
        Assert.True(entries.Any(e => e.StartsWith("Schemas/", StringComparison.Ordinal)),
            "Bundled schemas must be present even in an empty backup.");
        // Zero ClaudeCode / ClaudeDesktop entries (nothing on disk to bundle).
        Assert.DoesNotContain(entries, e => e.StartsWith("ClaudeCode/", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.StartsWith("ClaudeDesktop/", StringComparison.Ordinal));
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

    [Fact]
    public async Task RestoreAsync_ValidationWarnings_PopulatedForSchemaViolatingSettings()
    {
        // cleanupPeriodDays must be type=integer per the bundled schema.
        // A string violates the schema; the validator should produce at
        // least one warning naming that property.
        await File.WriteAllTextAsync(
            Path.Combine(_fakeHome, ".claude", "settings.json"),
            "{\"cleanupPeriodDays\":\"not-a-number\"}", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "backup-violation.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded, create.Message);

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        Assert.Single(entries);

        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(restore.Succeeded,
            "Restore must succeed even when validation finds violations — "
            + "validation is informational, not gating.");
        MessageAssert.NotNull(restore.ValidationWarnings,
            "ValidationWarnings must be populated when a settings file violates the schema.");
        Assert.True(restore.ValidationWarnings!.Count > 0);
        Assert.True(
            restore.ValidationWarnings!.Any(w => w.Contains("cleanupPeriodDays", StringComparison.Ordinal)),
            "Warning text must surface the violating property name (cleanupPeriodDays).");
    }

    [Fact]
    public async Task RestoreAsync_NoValidationWarnings_WhenAllSettingsAreValid()
    {
        // Setup() already wrote {"theme":"dark"} as settings.json — that's
        // schema-valid for ClaudeCode (theme is a free property allowed
        // anywhere in the schema). Roundtrip backup → restore.
        string dest = Path.Combine(_fakeHome, "backup-clean.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded);

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(restore.Succeeded);
        // ValidationWarnings is null OR empty when nothing violates.
        // The implementation returns null when the warnings list is empty
        // (RestoreResult ctor argument), so prefer the IsNull check; fall
        // back to Count == 0 for resilience to a future shape change.
        if (restore.ValidationWarnings is not null)
        {
            Assert.Empty(restore.ValidationWarnings);
        }
    }

    [Fact]
    public async Task RestoreAsync_SchemasDirectoryStripped_GracefullySkipsValidation()
    {
        // Old backups (pre-Schemas-bundle) didn't include a Schemas/
        // directory. The validator must early-return cleanly rather than
        // crashing — `if (!Directory.Exists(schemasDir)) return warnings;`.
        await File.WriteAllTextAsync(
            Path.Combine(_fakeHome, ".claude", "settings.json"),
            "{\"cleanupPeriodDays\":\"would-violate-but-no-schema\"}", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "backup-noschemas.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded);

        // Strip every Schemas/ entry from the zip after creation.
        StripZipEntries(dest, e => e.FullName.StartsWith("Schemas/", StringComparison.Ordinal));

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(restore.Succeeded,
            "Restore from a pre-Schemas backup must still succeed — old backups are valid.");

        // No schemas → no warnings, even though the settings file would
        // have violated had a schema been present.
        if (restore.ValidationWarnings is not null)
        {
            Assert.Empty(restore.ValidationWarnings);
        }
    }

    [Fact]
    public async Task RestoreAsync_CorruptSchemaEntryInBackup_DoesNotCrash()
    {
        // The validator catches per-schema parse failures inside the
        // schemaByName loop — a single bad entry must not prevent the
        // others from loading or the restore from succeeding.
        string dest = Path.Combine(_fakeHome, "backup-badschema.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded);

        // Mangle one schema entry's content to be unparseable.
        OverwriteZipEntry(dest, "Schemas/claude-code-settings.json",
            "{ this is not / valid : JSON ");

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(restore.Succeeded,
            "A corrupt bundled schema must not abort the restore — validation is informational.");
    }

    [Fact]
    public async Task RestoreAsync_InvalidSchemaEntryInBackup_DoesNotCrash()
    {
        // Sibling of RestoreAsync_CorruptSchemaEntryInBackup_DoesNotCrash, for the OTHER failure
        // mode: a schema entry that is well-formed JSON but NOT a valid JSON-Schema (here the core
        // `type` keyword carries a number instead of a string/array). SchemaRegistry.ParseSchema
        // then throws Json.Schema.JsonSchemaException — a DIFFERENT type than the malformed-JSON
        // case (System.Text.Json.JsonException). The validator must catch it too and skip the bad
        // entry rather than letting it abort the whole restore. Pins the latent-crash fix.
        string dest = Path.Combine(_fakeHome, "backup-invalidschema.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded);

        // Valid JSON, invalid schema: `type` must be a string or array of strings, not a number,
        // so JsonSchema.FromText throws JsonSchemaException while building the node.
        OverwriteZipEntry(dest, "Schemas/claude-code-settings.json",
            """{ "$schema": "http://json-schema.org/draft-07/schema#", "type": 12345 }""");

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(restore.Succeeded,
            "A bundled schema that is valid JSON but an invalid JSON-Schema must not abort the restore.");
    }

    [Fact]
    public async Task RestoreAsync_CorruptSettingsFileInBackup_SkippedFromValidation()
    {
        // The validator's per-file try/catch around JsonNode.Parse silently
        // skips files that aren't valid JSON. The restore still succeeds
        // because applying a malformed file is a separate path (the apply
        // phase swallows it as a file-failure rather than aborting).
        string dest = Path.Combine(_fakeHome, "backup-badsettings.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded);

        // The bundled archive's settings.json appears under
        // ClaudeCode/claude-dir/settings.json. Mangle it.
        OverwriteZipEntry(dest, "ClaudeCode/claude-dir/settings.json",
            "{ corrupt JSON without close brace");

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(restore.Succeeded,
            "Restore must tolerate a corrupt settings file in the backup — validation skips it silently.");

        // Validation should produce no warnings for the unparseable file
        // (the catch silently skips it; nothing to report).
        if (restore.ValidationWarnings is not null)
        {
            Assert.False(
                restore.ValidationWarnings.Any(w => w.Contains("settings.json", StringComparison.Ordinal)),
                "Corrupt settings file must NOT produce a warning — the validator skips it before evaluation.");
        }
    }

    [Fact]
    public async Task RestoreAsync_ValidatesEveryConfigLocationAgainstItsOwnProductsSchema()
    {
        // Written because nothing covered this. Every pre-existing validation test above
        // seeds Claude Code data only, so the Desktop half of the routing — and two of the
        // four archive locations the validator looks in — were completely unguarded.
        //
        // Each file below violates ITS OWN product's schema while being perfectly valid
        // under the other's, which makes this the transposition canary Phase 4a's finding
        // asked for: point either product's descriptor at the other's schema, or mis-assign
        // a row in RestoreEngine's ValidatableConfigs table, and these assertions go quiet
        // rather than merely changing.
        //   • claude-code-settings.json types cleanupPeriodDays as integer, and does not
        //     mention `preferences` at all (root additionalProperties: true).
        //   • claude-desktop-config.json types preferences as object, and does not mention
        //     `cleanupPeriodDays` at all (root allows extras too).
        const string violatesClaudeCode = """{"cleanupPeriodDays":"not-a-number"}""";
        const string violatesDesktop = """{"preferences":"not-an-object"}""";

        // ClaudeCode/claude.json  (top-level, non-recursive row)
        await File.WriteAllTextAsync(Path.Combine(_fakeHome, ".claude.json"), violatesClaudeCode, TestContext.Current.CancellationToken);
        // ClaudeCode/claude-dir/settings.json + settings.local.json  (recursive row, both names)
        await File.WriteAllTextAsync(
            Path.Combine(_fakeHome, ".claude", "settings.json"), violatesClaudeCode, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(_fakeHome, ".claude", "settings.local.json"), violatesClaudeCode, TestContext.Current.CancellationToken);

        // ClaudeDesktop/claude_desktop_config.json  (top-level, non-recursive row)
        Directory.CreateDirectory(PlatformPaths.DesktopConfigDir);
        await File.WriteAllTextAsync(PlatformPaths.DesktopConfigPath, violatesDesktop, TestContext.Current.CancellationToken);
        // ClaudeDesktop/profiles/work/...  (recursive row)
        Directory.CreateDirectory(Path.Combine(PlatformPaths.DesktopProfilesDirectory, "work"));
        await File.WriteAllTextAsync(
            Path.Combine(PlatformPaths.DesktopProfilesDirectory, "work", "claude_desktop_config.json"),
            violatesDesktop, TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "backup-both-products.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), SchemaRegistry.ClaudeDesktopProduct],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded, create.Message);

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entries[0], ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(restore.Succeeded, "Validation is informational — restore must still succeed.");
        MessageAssert.NotNull(restore.ValidationWarnings, "Five violating config files must produce warnings.");

        // Warnings are formatted "<archive-relative path>: <instance path>: <message>", so
        // each row of the layout table is attributable by path.
        (string ArchivePath, string Property)[] cases =
        [
            ("ClaudeCode/claude.json", "cleanupPeriodDays"),
            ("ClaudeCode/claude-dir/settings.json", "cleanupPeriodDays"),
            ("ClaudeCode/claude-dir/settings.local.json", "cleanupPeriodDays"),
            ("ClaudeDesktop/claude_desktop_config.json", "preferences"),
            ("ClaudeDesktop/profiles/work/claude_desktop_config.json", "preferences"),
        ];

        foreach ((string archivePath, string property) in cases)
        {
            Assert.True(
                restore.ValidationWarnings!.Any(w =>
                    w.Contains(archivePath, StringComparison.Ordinal)
                    && w.Contains(property, StringComparison.Ordinal)),
                $"'{archivePath}' must be validated against its own product's schema and report "
                + $"'{property}'. Either that archive location dropped out of "
                + "RestoreEngine.ValidatableConfigs, or it is now routed to the other product's "
                + $"schema — under which '{property}' is legal. Warnings were: "
                + string.Join(" | ", restore.ValidationWarnings!));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ProductDescriptor.ArchiveFolder is a PERSISTED contract
    //
    //  Phase 4d-2 made one property the single source for both the archive's top-level
    //  folder names and the manifest's `clients` entries — replacing the same two literals
    //  restated in the manifest builder, in every folder path, and again in RestoreEngine.
    //
    //  That centralisation has a cost worth guarding: because both the writer and the
    //  reader now read the SAME property, changing it moves both sides at once and stays
    //  self-consistent. New archives would work perfectly while every archive already on
    //  the user's disk quietly stopped matching. Renaming Claude Code's folder does fail
    //  ten tests today, but only incidentally — via hardcoded path strings in unrelated
    //  assertions — and NOTHING asserted the value the engine writes into `clients`.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ArchiveFolderNames_AreTheValuesAlreadyOnUsersDisks()
    {
        // Deliberately a value-pinning test. These two strings are baked into every archive
        // ClaudeForge has ever written; they are not free to change, and the only way to say
        // so is to write them down somewhere a change has to walk past.
        Assert.Equal("ClaudeCode", SchemaRegistry.ClaudeCodeArchiveFolder);
        Assert.Equal("ClaudeDesktop", SchemaRegistry.ClaudeDesktopProduct.ArchiveFolder);

        // Not derived from Id, and must not be "fixed" to match it: the ids were chosen for
        // code, the folder names were already on disk.
        MessageAssert.NotEqual(
            SchemaRegistry.ClaudeCodeProductId,
            SchemaRegistry.ClaudeCodeArchiveFolder,
            "The two vocabularies differ on purpose — claude-code vs ClaudeCode. Collapsing "
            + "them would orphan every existing archive.");
    }

    [Fact]
    public async Task CreateAsync_WritesEachRequestedProductsArchiveFolderIntoTheManifest()
    {
        // The writer side of the same contract, and the gap the folder-rename canary exposed:
        // the restore browser reads manifest.clients to label each archive, and renders an
        // unrecognised value raw. So a changed folder name produces archives that look wrong
        // in the UI without any test noticing.
        Directory.CreateDirectory(PlatformPaths.DesktopConfigDir);
        await File.WriteAllTextAsync(PlatformPaths.DesktopConfigPath, """{"theme":"system"}""", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "clients-manifest.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), SchemaRegistry.ClaudeDesktopProduct],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded, create.Message);
        Assert.NotNull(create.Manifest);

        MessageAssert.SequenceEqual(
            new[] { "ClaudeCode", "ClaudeDesktop" },
            create.Manifest!.Clients.ToArray(),
            "The manifest's clients array must carry each requested product's ArchiveFolder, "
            + "in request order. These strings are persisted and read back by the restore "
            + "browser's client column.");

        // And the folders those names describe must be where the files actually landed —
        // the two halves of the contract have to agree.
        List<string> entries = ListEntries(dest);
        foreach (string folder in create.Manifest!.Clients)
        {
            Assert.True(
                entries.Any(e => e.StartsWith(folder + "/", StringComparison.Ordinal)),
                $"Manifest lists '{folder}' but no archive entry lives under '{folder}/'.");
        }
    }

    [Fact]
    public async Task CreateAsync_OmittedProduct_IsAbsentFromTheManifestClientsArray()
    {
        // Counter-direction: without this, a BuildClientList that ignored the request and
        // always listed both products would satisfy the test above.
        string dest = Path.Combine(_fakeHome, "clients-manifest-cc-only.zip");
        BackupResult create = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(create.Succeeded, create.Message);

        MessageAssert.SequenceEqual(
            new[] { "ClaudeCode" },
            create.Manifest!.Clients.ToArray(),
            "Only the requested product may appear in the manifest.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>Schemas/</c> entries of an archive, without the folder prefix, sorted.
    /// </summary>
    private static List<string> ListBundledSchemas(string zipPath)
        => ListEntries(zipPath)
            .Where(e => e.StartsWith("Schemas/", StringComparison.Ordinal))
            .Select(e => e["Schemas/".Length..])
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// An archive carries the schemas its own products are validated against, and no
    /// others. Before this was enforced, <c>BundleSchemas</c> copied every resource under
    /// <c>Assets/Schemas/</c> regardless of product — harmless while every bundled schema
    /// belonged to a Claude product, and a silent ~1.1 MB per-archive cost the moment a
    /// second agent's schemas ship in the same folder.
    /// <para>
    /// Claude Desktop's schema is the stand-in for "a schema that exists in the assembly
    /// but belongs to a product this archive does not contain" — the same shape a future
    /// OpenCode schema has, and the reason this test is not tautological.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CreateAsync_BundlesOnlyTheRequestedProductsSchemas()
    {
        string dest = Path.Combine(_fakeHome, "one-product.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(result.Succeeded, result.Message);

        List<string> schemas = ListBundledSchemas(dest);

        MessageAssert.Contains(SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).SchemaFileName, schemas,
            "A backup must carry the schema its own config is validated against.");
        MessageAssert.DoesNotContain(SchemaRegistry.ClaudeDesktopProduct.SchemaFileName, schemas,
            "A Claude Code-only archive must not carry another product's schema. That file "
            + "can never match anything RestoreEngine routes, so it is pure weight — and the "
            + "same rule is what keeps a second agent's schemas out of this app's backups.");
    }

    /// <summary>
    /// The overlay travels with its base. <c>SchemaRegistry</c> merges
    /// <c>foo.overlay.json</c> onto <c>foo.json</c> at load time, so an archive carrying
    /// the base without the overlay would validate on restore against rules the running app
    /// never uses — and nothing would report a difference.
    /// </summary>
    [Fact]
    public async Task CreateAsync_BundlesEachSchemasOverlayAlongsideIt()
    {
        string dest = Path.Combine(_fakeHome, "overlay.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(result.Succeeded, result.Message);

        // Claude Code is the product that actually has an overlay resource; asserting the
        // pairing on a product without one would prove nothing.
        string overlay = SchemaRegistry.OverlayFileNameFor(
            SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).SchemaFileName);
        MessageAssert.Contains(overlay, ListBundledSchemas(dest),
            $"'{overlay}' is merged onto the base schema at load time, so it has to be in "
            + "the archive too or restore validates against rules the app does not use.");
    }

    /// <summary>
    /// Two products requested, both products' schemas present. The suite overwhelmingly
    /// exercises one product at a time, which is how a filter that silently keeps only the
    /// first would survive — so this constructs two deliberately.
    /// </summary>
    [Fact]
    public async Task CreateAsync_BundlesEveryRequestedProductsSchema_NotJustTheFirst()
    {
        string dest = Path.Combine(_fakeHome, "two-products.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.SettingsOnly,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), SchemaRegistry.ClaudeDesktopProduct],
        }, ct: TestContext.Current.CancellationToken, progress: null);

        Assert.True(result.Succeeded, result.Message);

        List<string> schemas = ListBundledSchemas(dest);
        Assert.Contains(SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty).SchemaFileName, schemas);
        MessageAssert.Contains(SchemaRegistry.ClaudeDesktopProduct.SchemaFileName, schemas,
            "The second requested product's schema is missing — a loop that stops after the "
            + "first product looks identical to a correct one in every single-product test.");
    }

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

    [Fact]
    public async Task CreateAsync_Sanitized_SkipsCredentialsFileEvenWhenIncludeCredentialsTrue()
    {
        // Seed a credentials file under ~/.claude — in non-sanitized backups
        // with IncludeCredentials=true it would be archived; in sanitized
        // mode it must be skipped regardless.
        string home = Path.Combine(_fakeHome, ".claude");
        await File.WriteAllTextAsync(Path.Combine(home, ".credentials.json"),
            """{"token": "anthropic-oauth-token-abc"}""", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "sanitized-no-creds.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Sanitized,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
            IncludeCredentials = true, // user opted in — sanitized still wins
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(result.Succeeded, result.Message);

        List<string> entries = ListEntries(dest);
        Assert.False(entries.Any(e => e.EndsWith(".credentials.json", StringComparison.Ordinal)),
            "Credentials file must be hard-skipped in Sanitized mode regardless of the flag.");

        // Manifest must agree — IncludedCredentials=false records that the
        // archive carries no credentials data.
        await using FileStream fs = File.OpenRead(dest);
        await using ZipArchive archive = new(fs, ZipArchiveMode.Read);
        ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json")!;
        await using Stream ms = await manifestEntry.OpenAsync(TestContext.Current.CancellationToken);
        BackupManifest manifest = JsonSerializer.Deserialize(ms, BackupJsonContext.Default.BackupManifest)!;
        Assert.False(manifest.IncludedCredentials,
            "Manifest must declare credentials excluded in Sanitized mode.");
        Assert.Equal(BackupMode.Sanitized, manifest.Mode);
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

    [Fact]
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
            "#!/bin/bash\nexport ANTHROPIC_API_KEY=sk-ant-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n", TestContext.Current.CancellationToken);

        string dest = Path.Combine(_fakeHome, "sanitized-with-hook.zip");
        BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Mode = BackupMode.Sanitized,
            Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
        }, ct: TestContext.Current.CancellationToken, progress: null);
        Assert.True(result.Succeeded, result.Message);

        string hookEntryName = ListEntries(dest)
            .Single(e => e.EndsWith("hooks/post-prompt.sh", StringComparison.Ordinal));
        string text = ReadEntryText(dest, hookEntryName);

        Assert.False(text.Contains("sk-ant-AAA", StringComparison.Ordinal),
            "Raw Anthropic key inside a hook .sh must not survive Sanitized backup.");
        Assert.True(text.Contains("[redacted]", StringComparison.Ordinal),
            "Redaction marker must appear in the sanitized hook script.");
        Assert.True(text.Contains("ANTHROPIC_API_KEY"),
            "Shell-style redaction must preserve the variable name (diagnostic value).");
        Assert.True(text.Contains("#!/bin/bash"),
            "Non-secret content (shebang) must round-trip unchanged.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  M2 — BuildSanitizationPlaceholder uses JsonSerializer (escape correctness)
    // ═══════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════
    //  MOutOfMemoryException in transformer is caught + skipped (proxy)
    // ═══════════════════════════════════════════════════════════════════════

    // ─────────────────────────────────────────────────────────────────────────
    // ExplicitProjectDirs end-to-end coverage.
    //
    // Pre-fix BackupRestoreViewModel.CreateBackupAsync built the BackupRequest
    // without setting ExplicitProjectDirs.  AddProjectClaudeData is only
    // invoked for items in that list — so the open project's `.claude`
    // directory was silently absent from every backup the app produced.
    // These tests pin the end-to-end picture (given a populated
    // ExplicitProjectDirs, the archive contains the project's settings +
    // CLAUDE.md, and the AdditionalDirectoriesResolver picks up sibling
    // additionalDirectories declarations).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithExplicitProjectDir_IncludesProjectClaudeDirectory()
    {
        // Build a sandboxed project root SEPARATE from _fakeHome so the engine
        // sees it as a distinct project to back up.  Layout:
        //   <projectRoot>/.claude/settings.json
        //   <projectRoot>/CLAUDE.md
        // Expected archive layout (per BackupEngine.AddProjectClaudeData):
        //   ClaudeCode/projects/<projectName>/.claude/settings.json
        //   ClaudeCode/projects/<projectName>/CLAUDE.md
        string projectRoot = Path.Combine(Path.GetTempPath(), "be-proj-" + Guid.NewGuid().ToString("N"));
        string projectName = Path.GetFileName(projectRoot);
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, ".claude"));
            File.WriteAllText(Path.Combine(projectRoot, ".claude", "settings.json"),
                """{"theme":"project-light"}""");
            File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "# Project memory");

            string dest = Path.Combine(_fakeHome, "with-project.zip");
            BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = dest,
                Mode = BackupMode.SettingsOnly,
                Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
                ExplicitProjectDirs = new[] { projectRoot },
            }, ct: TestContext.Current.CancellationToken, progress: null);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(File.Exists(dest));

            List<string> entries = ListEntries(dest);
            string expectedSettingsEntry = $"ClaudeCode/projects/{projectName}/.claude/settings.json";
            string expectedClaudeMdEntry = $"ClaudeCode/projects/{projectName}/CLAUDE.md";

            Assert.True(entries.Any(e => e.EndsWith(expectedSettingsEntry, StringComparison.Ordinal)),
                $"Archive must contain '{expectedSettingsEntry}' — the project's `.claude/settings.json` " +
                "is the primary file ExplicitProjectDirs exists to surface.  " +
                $"Archive contained {entries.Count} entries.");
            Assert.True(entries.Any(e => e.EndsWith(expectedClaudeMdEntry, StringComparison.Ordinal)),
                $"Archive must contain '{expectedClaudeMdEntry}' — project-level CLAUDE.md " +
                "is added by the same code path as the `.claude` directory.");
        }
        finally
        {
            try { Directory.Delete(projectRoot, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
        }
    }

    /// <summary>
    /// Writes a minimal <c>~/.claude.json</c> into the sandbox with the
    /// given project paths registered in the top-level <c>projects</c>
    /// map.  Used by the Full-mode multi-project tests so each test
    /// doesn't have to hand-escape Windows path separators inside a raw
    /// JSON string template.
    /// </summary>
    private void WriteFakeClaudeJson(params string[] projectPaths)
    {
        JsonObject projectsMap = new();
        foreach (string p in projectPaths)
        {
            projectsMap[p] = new JsonObject();
        }
        JsonObject root = new() { ["projects"] = projectsMap };
        File.WriteAllText(Path.Combine(_fakeHome, ".claude.json"), root.ToJsonString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Full-mode multi-project expansion (Part 1.5).
    //
    // Pre-fix Full mode only backed up the explicit/discovered project set
    // (often empty when no project is open).  Per-project hooks, MCP config,
    // agents, skills, CLAUDE.md, etc. for every OTHER project Claude had
    // ever seen were silently absent from "Full" archives.  These tests
    // pin the four contracts that close the gap:
    //   1. Full mode walks every project in ~/.claude.json's `projects` map.
    //   2. Non-existent paths are skipped silently + counted in the manifest
    //      warnings (no path strings logged — could be identifying).
    //   3. SettingsOnly does NOT expand to known projects (single-project
    //      contract preserved).
    //   4. Sanitized does NOT expand to known projects (sharing-mode
    //      contract preserved — extending it would defeat the redaction
    //      premise).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_FullMode_IncludesAllKnownProjectsClaudeDirectories()
    {
        // Two project roots on disk, both registered in ~/.claude.json.
        // Each has a `.claude/` directory with a small file + a CLAUDE.md.
        // Full mode (no explicit project selected) must back up BOTH.
        string projA = Path.Combine(Path.GetTempPath(), "be-known-a-" + Guid.NewGuid().ToString("N"));
        string projB = Path.Combine(Path.GetTempPath(), "be-known-b-" + Guid.NewGuid().ToString("N"));
        string nameA = Path.GetFileName(projA);
        string nameB = Path.GetFileName(projB);
        try
        {
            foreach (string p in new[] { projA, projB })
            {
                Directory.CreateDirectory(Path.Combine(p, ".claude"));
                File.WriteAllText(Path.Combine(p, ".claude", "settings.json"), """{"k":"v"}""");
                File.WriteAllText(Path.Combine(p, "CLAUDE.md"), "# Project memory");
            }

            // Synthesize ~/.claude.json with a `projects` map keyed by both paths.
            // The values can be any opaque shape; the discovery helper only
            // reads the keys.
            WriteFakeClaudeJson(projA, projB);

            string dest = Path.Combine(_fakeHome, "full-multi.zip");
            BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = dest,
                Mode = BackupMode.Full,
                Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
                // NO explicit project — exercises the "Full backup with no
                // project selected" path the user asked about.
            }, ct: TestContext.Current.CancellationToken, progress: null);

            Assert.True(result.Succeeded, result.Message);
            List<string> entries = ListEntries(dest);

            Assert.True(entries.Any(e =>
                e.EndsWith($"ClaudeCode/projects/{nameA}/.claude/settings.json", StringComparison.Ordinal)),
                $"Full mode must include project A's `.claude/settings.json`. " +
                $"Archive contained {entries.Count} entries.");
            Assert.True(entries.Any(e =>
                e.EndsWith($"ClaudeCode/projects/{nameA}/CLAUDE.md", StringComparison.Ordinal)),
                "Full mode must include project A's CLAUDE.md.");

            Assert.True(entries.Any(e =>
                e.EndsWith($"ClaudeCode/projects/{nameB}/.claude/settings.json", StringComparison.Ordinal)),
                "Full mode must include project B's `.claude/settings.json` " +
                "(known via ~/.claude.json — pre-fix this was silently absent).");
            Assert.True(entries.Any(e =>
                e.EndsWith($"ClaudeCode/projects/{nameB}/CLAUDE.md", StringComparison.Ordinal)),
                "Full mode must include project B's CLAUDE.md.");
        }
        finally
        {
            foreach (string p in new[] { projA, projB })
            {
                try { Directory.Delete(p, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
            }
        }
    }

    [Fact]
    public async Task CreateAsync_FullMode_SilentlySkipsNonExistentKnownProjects_WithManifestWarning()
    {
        // ~/.claude.json lists two paths: one exists on disk, one doesn't.
        // Backup must succeed; archive includes only the existing one;
        // manifest.Warnings carries the skipped count (no path strings —
        // those could be identifying).
        string projExisting = Path.Combine(Path.GetTempPath(), "be-known-exists-" + Guid.NewGuid().ToString("N"));
        string projGhost = Path.Combine(Path.GetTempPath(), "be-known-ghost-" + Guid.NewGuid().ToString("N"));
        string nameExisting = Path.GetFileName(projExisting);
        try
        {
            Directory.CreateDirectory(Path.Combine(projExisting, ".claude"));
            File.WriteAllText(Path.Combine(projExisting, ".claude", "settings.json"), """{"k":"v"}""");
            // Deliberately do NOT create projGhost on disk.
            WriteFakeClaudeJson(projExisting, projGhost);

            string dest = Path.Combine(_fakeHome, "full-with-ghost.zip");
            BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = dest,
                Mode = BackupMode.Full,
                Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
            }, ct: TestContext.Current.CancellationToken, progress: null);

            Assert.True(result.Succeeded, result.Message);
            Assert.NotNull(result.Manifest);
            Assert.True(result.Manifest!.Warnings.Any(w => w.Contains("skipped 1 known project", StringComparison.OrdinalIgnoreCase)),
                "Manifest.Warnings must carry the stale-project count.  Found: [" +
                string.Join(", ", result.Manifest.Warnings) + "]");

            // The existing project's content IS in the archive.
            List<string> entries = ListEntries(dest);
            Assert.True(entries.Any(e =>
                e.EndsWith($"ClaudeCode/projects/{nameExisting}/.claude/settings.json", StringComparison.Ordinal)),
                "The existing project's settings.json must still appear in the archive.");

            // The ghost project's name must NOT appear anywhere.
            string ghostName = Path.GetFileName(projGhost);
            Assert.False(entries.Any(e => e.Contains($"projects/{ghostName}/", StringComparison.Ordinal)),
                "The non-existent project must be silently dropped from the archive.");
        }
        finally
        {
            try { Directory.Delete(projExisting, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
        }
    }

    [Fact]
    public async Task CreateAsync_SettingsOnly_DoesNotExpandToKnownProjects()
    {
        // Same ~/.claude.json fixture as Full-mode test, but with
        // SettingsOnly mode.  Known-projects expansion must NOT fire —
        // SettingsOnly is the "small, fast, just-my-config" mode.
        string projKnown = Path.Combine(Path.GetTempPath(), "be-known-so-" + Guid.NewGuid().ToString("N"));
        string nameKnown = Path.GetFileName(projKnown);
        try
        {
            Directory.CreateDirectory(Path.Combine(projKnown, ".claude"));
            File.WriteAllText(Path.Combine(projKnown, ".claude", "settings.json"), """{"k":"v"}""");
            WriteFakeClaudeJson(projKnown);

            string dest = Path.Combine(_fakeHome, "settings-only.zip");
            BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = dest,
                Mode = BackupMode.SettingsOnly,
                Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
            }, ct: TestContext.Current.CancellationToken, progress: null);

            Assert.True(result.Succeeded, result.Message);
            List<string> entries = ListEntries(dest);

            Assert.False(entries.Any(e =>
                e.Contains($"projects/{nameKnown}/", StringComparison.Ordinal)),
                "SettingsOnly mode must NOT expand to known projects from ~/.claude.json — " +
                "that's a Full-mode-only behaviour by design.  Found unexpected project entries.");
        }
        finally
        {
            try { Directory.Delete(projKnown, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
        }
    }

    [Fact]
    public async Task CreateAsync_FullMode_DedupesExplicitProjectAgainstKnownProjects()
    {
        // A single project is both explicitly selected AND listed in
        // ~/.claude.json.  Result: the project's content appears in the
        // archive exactly once — no duplicate ClaudeCode/projects/<name>/
        // entries.  Catches a sloppy union that would back up the same
        // tree twice.
        string projShared = Path.Combine(Path.GetTempPath(), "be-known-dedup-" + Guid.NewGuid().ToString("N"));
        string nameShared = Path.GetFileName(projShared);
        try
        {
            Directory.CreateDirectory(Path.Combine(projShared, ".claude"));
            File.WriteAllText(Path.Combine(projShared, ".claude", "settings.json"), """{"k":"v"}""");
            File.WriteAllText(Path.Combine(projShared, "CLAUDE.md"), "# Shared memory");
            WriteFakeClaudeJson(projShared);

            string dest = Path.Combine(_fakeHome, "full-dedup.zip");
            BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = dest,
                Mode = BackupMode.Full,
                Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
                ExplicitProjectDirs = new[] { projShared },  // same path also in .claude.json
            }, ct: TestContext.Current.CancellationToken, progress: null);

            Assert.True(result.Succeeded, result.Message);
            List<string> entries = ListEntries(dest);

            // The project's settings.json + CLAUDE.md appear exactly once.
            int settingsCount = entries.Count(e =>
                e.EndsWith($"ClaudeCode/projects/{nameShared}/.claude/settings.json", StringComparison.Ordinal));
            int memoryCount = entries.Count(e =>
                e.EndsWith($"ClaudeCode/projects/{nameShared}/CLAUDE.md", StringComparison.Ordinal));

            MessageAssert.Equal(1, settingsCount,
                $"Explicit-project + same-path-in-.claude.json must dedup to one settings.json " +
                $"entry.  Got {settingsCount}.");
            MessageAssert.Equal(1, memoryCount,
                $"Same dedup contract applies to CLAUDE.md.  Got {memoryCount}.");
        }
        finally
        {
            try { Directory.Delete(projShared, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
        }
    }

    [Fact]
    public async Task CreateAsync_WithExplicitProjectDir_DiscoversAdditionalDirectories()
    {
        // The project declares `additionalDirectories` in its settings.json
        // pointing at a sibling folder.  The engine's
        // AdditionalDirectoriesResolver discovers that path AFTER
        // ExplicitProjectDirs is populated — pre-fix the resolver received
        // an empty list of settings files for project context and never saw
        // the declaration.  Wiring ExplicitProjectDirs through fixes this for
        // free; this test pins that secondary contract.
        //
        // Note on what counts as "discovered": resolver output is merged
        // into the engine's projects list and each entry runs through
        // AddProjectClaudeData, which copies ONLY the 4 well-known items
        // (`.claude`, `.mcp.json`, `CLAUDE.md`, `CLAUDE.local.md`).  So
        // the sibling needs at least one of those — we give it a CLAUDE.md
        // — for the test to detect that it was treated as a project root.
        string projectRoot = Path.Combine(Path.GetTempPath(), "be-proj-addtl-" + Guid.NewGuid().ToString("N"));
        string siblingDir = Path.Combine(Path.GetTempPath(), "be-sibling-" + Guid.NewGuid().ToString("N"));
        string projectName = Path.GetFileName(projectRoot);
        string siblingName = Path.GetFileName(siblingDir);
        try
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, ".claude"));
            Directory.CreateDirectory(siblingDir);
            File.WriteAllText(Path.Combine(siblingDir, "CLAUDE.md"),
                "# Sibling project memory (discovered via additionalDirectories)");

            // settings.json declares additionalDirectories → [siblingDir]
            string settingsJson = $$"""{"additionalDirectories":["{{siblingDir.Replace("\\", "\\\\")}}"]}""";
            File.WriteAllText(Path.Combine(projectRoot, ".claude", "settings.json"), settingsJson);

            string dest = Path.Combine(_fakeHome, "with-addtl.zip");
            BackupResult result = await TestBackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = dest,
                Mode = BackupMode.SettingsOnly,
                Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty)],
                ExplicitProjectDirs = new[] { projectRoot },
            }, ct: TestContext.Current.CancellationToken, progress: null);

            Assert.True(result.Succeeded, result.Message);
            List<string> entries = ListEntries(dest);

            // Primary archive entry for the project remains present.
            Assert.True(entries.Any(e =>
                e.EndsWith($"ClaudeCode/projects/{projectName}/.claude/settings.json", StringComparison.Ordinal)),
                "Primary project settings entry must still appear (sanity check on the additionalDirs test).");

            // The discovered sibling appears as ANOTHER project entry under
            // ClaudeCode/projects/<siblingName>/ — that's how the engine
            // treats merged-and-discovered project roots.  Pre-fix the
            // resolver never saw the declaration (because the project's
            // settings file was missing from its input list), so this
            // CLAUDE.md was silently absent from the archive.
            string expectedSiblingClaude = $"ClaudeCode/projects/{siblingName}/CLAUDE.md";
            Assert.True(entries.Any(e => e.EndsWith(expectedSiblingClaude, StringComparison.Ordinal)),
                $"Archive must contain '{expectedSiblingClaude}' — the sibling's CLAUDE.md.  " +
                $"AdditionalDirectoriesResolver receives the project's `.claude/settings.json` " +
                $"via CollectSettingsFilesForDiscovery, picks up the absolute path declaration, " +
                $"merges it into the projects list, and AddProjectClaudeData copies the CLAUDE.md.  " +
                $"Archive contained {entries.Count} entries.");
        }
        finally
        {
            try { Directory.Delete(projectRoot, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
            try { Directory.Delete(siblingDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
        }
    }
}