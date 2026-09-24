using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// isolated tests for <see cref="RestoreEngine"/>. End-to-end
/// restore behaviour is covered by <c>BackupEngineTests.RoundTrip_*</c>
/// and <c>BackupEngineTests.RestoreAsync_ReportsProgressDuringApplyPhase</c>;
/// this suite focuses on the security-critical pure-function path
/// <see cref="RestoreEngine.ResolveSafeExtractPath"/>, which determines
/// whether a crafted zip entry can escape the extraction root
/// (the ZipSlip class of vulnerabilities).
/// </summary>
/// <remarks>
/// The single ZipSlip case in <c>BackupEngineTests</c> exercises the
/// happy-path + two basic rejection cases. This file fans the rejection
/// cases out so a future refactor that loosens the guard fails loudly.
/// </remarks>
public sealed class RestoreEngineTests : IDisposable
{
    private string _baseDir = string.Empty;

    public RestoreEngineTests() => Setup();

    private void Setup()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "re-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
    }

    private void Teardown()
    {
        try
        {
            Directory.Delete(_baseDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }

        // Drain the under-user-profile cleanup queue.  Tests that exercise
        // RestoreProjects / RestoreWorktrees happy paths populate this list
        // via CreateUnderUserProfile; without draining here, a test that
        // throws before reaching its own `finally CleanUnderProfile` would
        // leak a real home-directory subtree.
        foreach (string dir in _underProfileCleanup)
        {
            CleanUnderProfile(dir);
        }

        _underProfileCleanup.Clear();
    }

    public void Dispose()
    {
        Teardown();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ResolveSafeExtractPath_AllowsNormalEntry()
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, "ClaudeCode/claude.json");
        Assert.NotNull(resolved);
        Assert.True(resolved!.StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase),
            "Resolved path must remain inside the extraction root.");
    }

    [Fact]
    public void ResolveSafeExtractPath_AllowsNestedSubdirectory()
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(
            _baseDir, "ClaudeCode/projects/session-2026-01.jsonl");
        Assert.NotNull(resolved);
        Assert.True(resolved!.Contains("session-2026-01.jsonl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../../etc/passwd")] // POSIX traversal
    [InlineData("..\\..\\Windows\\System32\\evil.dll")] // Windows traversal
    [InlineData("subdir/../../escape.txt")] // mid-path traversal
    [InlineData("subdir\\..\\..\\escape.txt")] // mid-path Windows
    public void ResolveSafeExtractPath_RejectsTraversal(string entry)
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, entry);
        MessageAssert.Null(resolved, $"Traversal entry '{entry}' must be rejected.");
    }

    [Theory]
    [InlineData("/etc/passwd")] // POSIX absolute
    [InlineData("\\server\\share\\evil.txt")] // UNC-style
    [InlineData("C:\\Windows\\evil.exe")] // Windows drive-rooted
    public void ResolveSafeExtractPath_RejectsAbsolutePaths(string entry)
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, entry);
        MessageAssert.Null(resolved, $"Absolute path '{entry}' must be rejected.");
    }

    [Theory]
    [InlineData("file.txt:evil")] // ADS attempt — colon after filename
    [InlineData("normal/path:stream")] // ADS in nested
    public void ResolveSafeExtractPath_RejectsAlternateDataStreamSyntax(string entry)
    {
        // On Windows, "file.txt:evil" creates an Alternate Data Stream
        // attached to file.txt. The guard rejects ANY colon to be safe
        // cross-platform.
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, entry);
        MessageAssert.Null(resolved, $"ADS-style path '{entry}' must be rejected.");
    }

    [Fact]
    public void ResolveSafeExtractPath_RejectsEmptyAndNullishEntry()
    {
        Assert.Null(RestoreEngine.ResolveSafeExtractPath(_baseDir, string.Empty));
    }

    [Fact]
    public void ResolveSafeExtractPath_NormalisesSeparators()
    {
        // Forward and back slashes both resolve to the OS form.
        string? posix = RestoreEngine.ResolveSafeExtractPath(_baseDir, "ClaudeCode/sub/file.json");
        string? windows = RestoreEngine.ResolveSafeExtractPath(_baseDir, "ClaudeCode\\sub\\file.json");

        Assert.NotNull(posix);
        Assert.NotNull(windows);
        MessageAssert.Equal(posix, windows,
            "Both separator styles must resolve to the same canonical path.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  M3 — Tamper detection: redacted-marker content scan
    //
    //  The Sanitized refusal at the top of RestoreAsync reads
    //  Manifest.Mode.  Defence-in-depth: ContainsRedactedMarker scans the
    //  extracted JSON tree for the [redacted] literal as a string-valued
    //  leaf.  Tested via the helper directly because exercising the full
    //  RestoreAsync path requires a hex-edited zip manifest (covered by a
    //  manual smoke step in the plan).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ContainsRedactedMarker_TrueOnRedactedValueInsideJson()
    {
        string probe = Path.Combine(_baseDir, "ClaudeCode");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "claude.json"),
            """{ "env": "[redacted]", "theme": "dark" }""");

        Assert.True(RestoreEngine.ContainsRedactedMarker(_baseDir),
            "Redacted-marker leaf must trigger tamper-detection.");
    }

    [Fact]
    public void ContainsRedactedMarker_FalseOnNormalConfig()
    {
        string probe = Path.Combine(_baseDir, "ClaudeCode");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "claude.json"),
            """{ "theme": "dark", "model": "claude-opus" }""");

        Assert.False(RestoreEngine.ContainsRedactedMarker(_baseDir),
            "Plain config with no marker must not trigger the scan.");
    }

    [Fact]
    public void ContainsRedactedMarker_FalseOnMarkerInsideStringNotMatchingValue()
    {
        // An innocent description text that mentions the literal
        // "[redacted]" as prose — NOT a marker-value leaf.  The scan
        // matches only when the leaf's VALUE equals the marker exactly,
        // so this should NOT fire (prevents false positives on docs
        // describing the redaction marker).
        string probe = Path.Combine(_baseDir, "ClaudeCode");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "claude.json"),
            """{ "description": "Replace sensitive values with [redacted] before sharing." }""");

        Assert.False(RestoreEngine.ContainsRedactedMarker(_baseDir),
            "Prose mentioning the marker word must not fire tamper-detection — only exact-equal leaf values do.");
    }

    [Fact]
    public void ContainsRedactedMarker_TrueOnNestedRedactedValue()
    {
        // Deep nested case — marker buried inside an object/array tree.
        string probe = Path.Combine(_baseDir, "ClaudeCode", "claude-dir");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "settings.json"),
            """{ "mcpServers": { "gh": { "headers": "[redacted]" } } }""");

        Assert.True(RestoreEngine.ContainsRedactedMarker(_baseDir));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  H5 — Sidecar cap at write time
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EvictOldSidecarsIfNeeded_LeavesRoomForNewSidecar_AtThreeCap()
    {
        // Seed FOUR sidecars (one over the cap that the caller is about
        // to push to with a new write).  Evict-then-write should bring
        // the on-disk count to (cap - 1), leaving room for the new one
        // that the caller is about to write.
        string liveFile = Path.Combine(_baseDir, "settings.json");
        File.WriteAllText(liveFile, "real");

        string[] stamps = ["20260101-100000", "20260102-100000", "20260103-100000", "20260104-100000"];
        foreach (string s in stamps)
        {
            File.WriteAllText(Path.Combine(_baseDir, $"settings.json.pre-restore-{s}.bak"), $"old-{s}");
        }

        RestoreEngine.EvictOldSidecarsIfNeeded(liveFile);

        string?[] surviving = Directory.GetFiles(_baseDir, "settings.json.pre-restore-*.bak")
                                       .Select(Path.GetFileName)
                                       .OrderBy(n => n, StringComparer.Ordinal)
                                       .ToArray();

        // After eviction: keep (MaxSidecarsPerFile - 1) = 2 most recent
        // so the new write brings the total to exactly 3.
        MessageAssert.Equal(RestoreEngine.MaxSidecarsPerFile - 1, surviving.Length,
            $"Expected {RestoreEngine.MaxSidecarsPerFile - 1} sidecars to survive eviction. Got: {string.Join(", ", surviving)}");

        // Survivors should be the 2 most recent stamps (lexical = chronological).
        Assert.Contains(surviving, n => n!.Contains("20260103"));
        Assert.Contains(surviving, n => n!.Contains("20260104"));
        Assert.False(surviving.Any(n => n!.Contains("20260101")),
            "Oldest sidecar must be evicted.");
        Assert.False(surviving.Any(n => n!.Contains("20260102")),
            "Second-oldest sidecar must also be evicted (cap-1 = 2 survivors).");
    }

    [Fact]
    public void EvictOldSidecarsIfNeeded_PreservesEditorStyleBak()
    {
        // Hand-rolled / editor-style .bak files (vim, sed -i.bak,
        // notes.md.bak) do NOT match the strict pre-restore-{stamp}
        // regex and must be left alone even when the cap forces
        // evictions on the canonical sidecars.
        string liveFile = Path.Combine(_baseDir, "settings.json");
        File.WriteAllText(liveFile, "real");

        // Seed three canonical sidecars (at the cap) so eviction will
        // trigger when one more is "about to" be written.  We also
        // need to push it to FOUR to force eviction.
        for (int i = 1; i <= 4; i++)
        {
            File.WriteAllText(
                Path.Combine(_baseDir, $"settings.json.pre-restore-2026010{i}-100000.bak"),
                $"sidecar-{i}");
        }

        // Hand-rolled .bak files — must survive.
        string handRolled1 = Path.Combine(_baseDir, "settings.json.bak");
        string handRolled2 = Path.Combine(_baseDir, "notes.md.bak");
        File.WriteAllText(handRolled1, "user-made backup");
        File.WriteAllText(handRolled2, "vim swap-style bak");

        RestoreEngine.EvictOldSidecarsIfNeeded(liveFile);

        Assert.True(File.Exists(handRolled1),
            "settings.json.bak (editor-style, no .pre-restore- stamp) must NOT be evicted.");
        Assert.True(File.Exists(handRolled2),
            "Unrelated notes.md.bak must NOT be evicted.");
    }

    [Fact]
    public void EvictOldSidecarsIfNeeded_ReadOnlySidecar_DeletedAfterAttributeClear()
    {
        // Git pack-object sidecars inherit 0444 from their source.  The
        // initial File.Delete throws UnauthorizedAccessException; the
        // retry clears the read-only attribute and tries again.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // POSIX File.SetAttributes Normal is a no-op on Linux/macOS
            // and the read-only attribute is not enforced the same way.
            // Skip on non-Windows; the Windows path covers the
            // production scenario (Git's read-only pack-object .bak).
            Assert.Skip("Read-only retry path is Windows-specific.");
            return;
        }

        string liveFile = Path.Combine(_baseDir, "settings.json");
        File.WriteAllText(liveFile, "real");

        // Seed 4 sidecars — the OLDEST will be evicted, and we make
        // that one read-only so the retry path is exercised.
        for (int i = 1; i <= 4; i++)
        {
            string p = Path.Combine(_baseDir, $"settings.json.pre-restore-2026010{i}-100000.bak");
            File.WriteAllText(p, $"sidecar-{i}");
        }

        string oldest = Path.Combine(_baseDir, "settings.json.pre-restore-20260101-100000.bak");
        File.SetAttributes(oldest, FileAttributes.ReadOnly);

        try
        {
            RestoreEngine.EvictOldSidecarsIfNeeded(liveFile);
            Assert.False(File.Exists(oldest),
                "Read-only sidecar must be evicted via the SetAttributes(Normal) + retry path.");
        }
        finally
        {
            // Defensive cleanup so the [TestCleanup] Directory.Delete
            // doesn't trip on a read-only leftover.
            if (File.Exists(oldest))
            {
                try
                {
                    File.SetAttributes(oldest, FileAttributes.Normal);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _ = ex;
                }

                try
                {
                    File.Delete(oldest);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _ = ex;
                }
            }
        }
    }

    [Fact]
    public void EvictOldSidecarsIfNeeded_BelowCap_DoesNothing()
    {
        // 2 sidecars + the new one about to be written = 3 total, at
        // the cap but not over.  No eviction should happen.
        string liveFile = Path.Combine(_baseDir, "settings.json");
        File.WriteAllText(liveFile, "real");
        File.WriteAllText(Path.Combine(_baseDir, "settings.json.pre-restore-20260101-100000.bak"), "s1");
        File.WriteAllText(Path.Combine(_baseDir, "settings.json.pre-restore-20260102-100000.bak"), "s2");

        RestoreEngine.EvictOldSidecarsIfNeeded(liveFile);

        MessageAssert.Equal(2, Directory.GetFiles(_baseDir, "settings.json.pre-restore-*.bak").Length,
            "Below-cap eviction must be a no-op.");
    }

    // ── IsUnderUserProfile (security guard for manifest-provided paths) ──
    //
    // added during the COVERAGE-B3 refresh pass.  These
    // tests cover the IsUnderUserProfile predicate that gates
    // RestoreProjects / RestoreWorktrees against manifest-provided paths
    // outside the user's home directory.

    [Fact]
    public void IsUnderUserProfile_EmptyOrNull_Rejected()
    {
        Assert.False(RestoreEngine.IsUnderUserProfile(null));
        Assert.False(RestoreEngine.IsUnderUserProfile(""));
        Assert.False(RestoreEngine.IsUnderUserProfile("   "));
    }

    [Theory]
    [InlineData(@"\\server\share\file")]
    [InlineData(@"\\?\C:\file")]
    [InlineData("//server/share/file")]
    public void IsUnderUserProfile_UncPaths_Rejected(string candidate)
    {
        Assert.False(RestoreEngine.IsUnderUserProfile(candidate),
            $"UNC path '{candidate}' must be rejected — could redirect writes to a remote host.");
    }

    [Fact]
    public void IsUnderUserProfile_PathOutsideProfile_Rejected()
    {
        // Cross-platform "definitely outside the user profile" probe:
        // a sibling directory at the filesystem root.
        string systemRoot = OperatingSystem.IsWindows()
            ? @"C:\Windows\System32"
            : "/etc";
        Assert.False(RestoreEngine.IsUnderUserProfile(systemRoot),
            "System path outside user profile must be rejected.");
    }

    [Fact]
    public void IsUnderUserProfile_PathUnderProfile_Accepted()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(home), "Test host has no UserProfile — cannot exercise positive case.");
        string underProfile = Path.Combine(home, ".claude", "test-only-not-real");
        Assert.True(RestoreEngine.IsUnderUserProfile(underProfile),
            $"Path under user profile must be accepted: {underProfile}");
    }

    [Fact]
    public void IsUnderUserProfile_UserProfileItself_Accepted()
    {
        // Edge case: the exact home-directory path (no trailing
        // separator) hits the equality branch rather than the
        // StartsWith-trailing-separator branch.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(home));
        Assert.True(RestoreEngine.IsUnderUserProfile(home));
    }

    // ── RestoreSection (single-file restore + sidecar) ────────────────

    [Fact]
    public void RestoreSection_MissingSource_ReturnsZero()
    {
        // Source doesn't exist → restore is a no-op, count 0, no
        // failure message (it's a normal "file wasn't in this backup"
        // case, not an error).
        string src = Path.Combine(_baseDir, "missing.txt");
        string dest = Path.Combine(_baseDir, "live", "missing.txt");

        (int restored, string? failure) = RestoreEngine.RestoreSection(src, dest, "20260519-120000", new RestoreJournal());

        Assert.Equal(0, restored);
        Assert.Null(failure);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void RestoreSection_NewDestination_CopiesNoSidecar()
    {
        // Dest doesn't exist → no .pre-restore-{stamp}.bak sidecar
        // should be created.  Only the new copy lands.
        string src = Path.Combine(_baseDir, "src.txt");
        string dest = Path.Combine(_baseDir, "live", "dest.txt");
        File.WriteAllText(src, "from backup");

        (int restored, string? failure) = RestoreEngine.RestoreSection(src, dest, "20260519-120000", new RestoreJournal());

        Assert.Equal(1, restored);
        Assert.Null(failure);
        Assert.Equal("from backup", File.ReadAllText(dest));
        Assert.False(File.Exists($"{dest}.pre-restore-20260519-120000.bak"),
            "No sidecar should be created when the destination didn't exist.");
    }

    [Fact]
    public void RestoreSection_ExistingDestination_CreatesSidecar()
    {
        string src = Path.Combine(_baseDir, "src.txt");
        string dest = Path.Combine(_baseDir, "live", "dest.txt");
        Directory.CreateDirectory(Path.Combine(_baseDir, "live"));
        File.WriteAllText(src, "new from backup");
        File.WriteAllText(dest, "old live content");

        (int restored, string? failure) = RestoreEngine.RestoreSection(src, dest, "20260519-120000", new RestoreJournal());

        Assert.Equal(1, restored);
        Assert.Null(failure);
        Assert.Equal("new from backup", File.ReadAllText(dest));
        string sidecar = $"{dest}.pre-restore-20260519-120000.bak";
        Assert.True(File.Exists(sidecar), "Sidecar must be written when destination existed.");
        Assert.Equal("old live content", File.ReadAllText(sidecar));
    }

    // ── RestoreDirectory (recursive directory restore) ────────────────

    [Fact]
    public void RestoreDirectory_MissingSource_NoOp()
    {
        string src = Path.Combine(_baseDir, "no-such-src");
        string dest = Path.Combine(_baseDir, "dest");
        (int restored, List<string> failures) = RestoreEngine.RestoreDirectory(src, dest, "20260519-120000", new RestoreJournal());

        Assert.Equal(0, restored);
        Assert.Empty(failures);
        Assert.False(Directory.Exists(dest), "Missing source must not create the destination directory.");
    }

    [Fact]
    public void RestoreDirectory_HappyPath_CopiesAllFilesPreservingTree()
    {
        string src = Path.Combine(_baseDir, "src");
        string dest = Path.Combine(_baseDir, "dest");
        Directory.CreateDirectory(Path.Combine(src, "nested", "deeper"));
        File.WriteAllText(Path.Combine(src, "root.txt"), "root");
        File.WriteAllText(Path.Combine(src, "nested", "child.txt"), "child");
        File.WriteAllText(Path.Combine(src, "nested", "deeper", "leaf.txt"), "leaf");

        (int restored, List<string> failures) = RestoreEngine.RestoreDirectory(src, dest, "20260519-120000", new RestoreJournal());

        Assert.Equal(3, restored);
        Assert.Empty(failures);
        Assert.Equal("root", File.ReadAllText(Path.Combine(dest, "root.txt")));
        Assert.Equal("child", File.ReadAllText(Path.Combine(dest, "nested", "child.txt")));
        Assert.Equal("leaf", File.ReadAllText(Path.Combine(dest, "nested", "deeper", "leaf.txt")));
    }

    [Fact]
    public void RestoreDirectory_ExistingFiles_CreateSidecars()
    {
        string src = Path.Combine(_baseDir, "src");
        string dest = Path.Combine(_baseDir, "dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(src, "shared.txt"), "from backup");
        File.WriteAllText(Path.Combine(dest, "shared.txt"), "live content");

        (int restored, List<string> failures) = RestoreEngine.RestoreDirectory(src, dest, "20260519-120000", new RestoreJournal());

        Assert.Equal(1, restored);
        Assert.Empty(failures);
        Assert.Equal("from backup", File.ReadAllText(Path.Combine(dest, "shared.txt")));
        MessageAssert.Equal("live content",
            File.ReadAllText(Path.Combine(dest, "shared.txt.pre-restore-20260519-120000.bak")),
            "Sidecar must capture the pre-restore live content.");
    }

    // ── F7 · a project outside the home folder is restorable when this machine knows it ──
    //
    // Backup captures whatever project is open. Restore refused anything outside the user
    // profile, called it "not present on this machine", and returned success — so for anyone
    // who keeps repositories outside their home directory the archive said the files were
    // there and nothing ever put them back. The authorisation set is what closes it, and it
    // is read from this machine, never from the archive.

    [Fact]
    public void IsAuthorisedRestoreTarget_UnderHome_AllowedWithNoKnownProjects()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.True(RestoreEngine.IsAuthorisedRestoreTarget(Path.Combine(home, "anything"), []),
            "The home-folder allow must stand on its own, so a machine with no ~/.claude.json "
            + "behaves exactly as it did before the authorisation set existed.");
    }

    [Fact]
    public void IsAuthorisedRestoreTarget_OutsideHome_RefusedUntilTheProjectListNamesIt()
    {
        string outside = OperatingSystem.IsWindows() ? @"D:\src\app" : "/srv/src/app";

        Assert.False(RestoreEngine.IsAuthorisedRestoreTarget(outside, []),
            "Premise: this path is outside the home folder, so it is refused by default — "
            + "otherwise the allow below would prove nothing.");
        Assert.True(RestoreEngine.IsAuthorisedRestoreTarget(outside, [outside]),
            "A path this machine's own project list names must be authorised.");
    }

    [Fact]
    public void IsAuthorisedRestoreTarget_DescendantOfAKnownProject_Allowed()
    {
        string root = OperatingSystem.IsWindows() ? @"D:\src\app" : "/srv/src/app";
        string child = Path.Combine(root, ".claude", "settings.json");

        Assert.True(RestoreEngine.IsAuthorisedRestoreTarget(child, [root]),
            "A project root authorises what is inside it, or the .claude subtree the backup "
            + "actually captures would still be refused.");
    }

    [Fact]
    public void IsAuthorisedRestoreTarget_SiblingWithASharedPrefix_Refused()
    {
        // The prefix trap: "D:\src\app" must not authorise "D:\src\app-secrets".
        string root = OperatingSystem.IsWindows() ? @"D:\src\app" : "/srv/src/app";
        string sibling = OperatingSystem.IsWindows() ? @"D:\src\app-secrets" : "/srv/src/app-secrets";

        Assert.False(RestoreEngine.IsAuthorisedRestoreTarget(sibling, [root]),
            "A shared name prefix is not containment. Without the explicit separator this "
            + "authorises a directory the user never opened.");
    }

    [Fact]
    public void IsAuthorisedRestoreTarget_SystemPath_RefusedEvenWithAProjectList()
    {
        // The whole point of keeping a check at all: a crafted manifest naming a system
        // directory must not become authorised just because the user has projects.
        string system = OperatingSystem.IsWindows() ? @"C:\Windows\System32" : "/etc";
        string root = OperatingSystem.IsWindows() ? @"D:\src\app" : "/srv/src/app";

        Assert.False(RestoreEngine.IsAuthorisedRestoreTarget(system, [root]),
            "A path in neither the home folder nor the project list stays refused. This is "
            + "the case the original under-profile check existed for.");
    }

    [Fact]
    public void IsAuthorisedRestoreTarget_UncPath_RefusedEvenWhenListed()
    {
        // A UNC path redirects writes to another host. IsUnderUserProfile rejects these and
        // this branch does not go through it, so the rule is restated — and measured.
        const string unc = @"\\evil-server\share\project";

        Assert.False(RestoreEngine.IsAuthorisedRestoreTarget(unc, [unc]),
            "A UNC path must be refused even when it appears in the project list, because a "
            + "list entry is not a reason to write to a network host.");
    }

    [Fact]
    public void RestoreProjects_OutsideHomeButAKnownProject_IsRestored()
    {
        // The F7 repro end to end: the archive carries a project's files, the live path is
        // outside the home folder, and this machine's project list names it.
        string projBackupDir = Path.Combine(_baseDir, "ClaudeCode", "projects", "OutsideProject");
        Directory.CreateDirectory(Path.Combine(projBackupDir, ".claude"));
        File.WriteAllText(Path.Combine(projBackupDir, ".claude", "settings.json"), """{"model":"restored"}""");

        // A real directory outside the user profile. _baseDir is a temp path; on Windows that
        // is normally outside the home folder, and where it is not, the test says so rather
        // than passing for the wrong reason.
        string livePath = Path.Combine(_baseDir, "live-outside", "OutsideProject");
        Directory.CreateDirectory(livePath);

        // Point "home" somewhere else entirely, so livePath really is outside it. On this
        // machine the temp root sits under the user profile, and without the redirect the
        // under-profile allow would carry the test and the project-list allow — the thing
        // being measured — would never be reached.
        PlatformPaths.TestUserProfileOverride = Path.Combine(_baseDir, "fake-home");
        try
        {
            Assert.False(RestoreEngine.IsUnderUserProfile(livePath),
                "Premise: with home redirected, the project path is outside it.");

            BackupManifest manifest = new() { Projects = { livePath } };
            RestoreJournal journal = new();

            int count = RestoreEngine.RestoreProjects(
                _baseDir, manifest, "20260519-120000", [livePath], journal);

            MessageAssert.Equal(1, count, "The project's file must be restored.");
            MessageAssert.Equal(0, journal.Skipped.Count,
                $"Nothing should be skipped: {string.Join(", ", journal.Skipped)}");
            MessageAssert.Equal("""{"model":"restored"}""",
                File.ReadAllText(Path.Combine(livePath, ".claude", "settings.json")),
                "F7: a backup that captured the project's files must put them back.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
        }
    }

    [Fact]
    public void RestoreProjects_OutsideHomeAndUnknown_IsStillRefused()
    {
        // The other half — remove the authorisation and the same restore must refuse.
        // Without this, the test above could be passing because the check is simply gone.
        string projBackupDir = Path.Combine(_baseDir, "ClaudeCode", "projects", "OutsideProject");
        Directory.CreateDirectory(projBackupDir);
        File.WriteAllText(Path.Combine(projBackupDir, "payload.txt"), "should not land");

        string livePath = Path.Combine(_baseDir, "live-outside2", "OutsideProject");
        Directory.CreateDirectory(livePath);

        PlatformPaths.TestUserProfileOverride = Path.Combine(_baseDir, "fake-home");
        try
        {
            Assert.False(RestoreEngine.IsUnderUserProfile(livePath),
                "Premise: with home redirected, the project path is outside it.");

            BackupManifest manifest = new() { Projects = { livePath } };
            RestoreJournal journal = new();

            int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", [], journal);

            Assert.Equal(0, count);
            Assert.Single(journal.Skipped);
            Assert.False(File.Exists(Path.Combine(livePath, "payload.txt")),
                "An unauthorised path must receive nothing.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
        }
    }

    [Fact]
    public void BuildAuthorisedRoots_IncludesTheOpenProject()
    {
        // ⛔ The case F7 actually reported. A *Settings only* backup captures exactly the
        // project the app has open; nothing else on the machine — not ~/.claude.json, not
        // additionalDirectories — need ever have heard of it. An authorisation set built
        // only from ~/.claude.json refuses that archive on restore, which is the first fix
        // failing at the one scenario the finding describes. Measured, not supposed: the
        // previous retest's fixture project was absent from a 62-entry project list.
        string open = Path.Combine(_baseDir, "OpenProject");
        Directory.CreateDirectory(open);

        IReadOnlyCollection<string> roots = RestoreEngine.BuildAuthorisedRoots(ClaudeEnvironment.Empty, [open]);

        Assert.True(roots.Contains(open),
            "The host's open project must be authorised — it is the path the archive was "
            + "taken for, and the user chose it in this app in this session.");
    }

    [Fact]
    public void BuildAuthorisedRoots_WithNoOpenProject_StillReturnsTheMachinesOwnSources()
    {
        // Null is legal (a restore with no project open) and must not throw or produce a
        // set that refuses everything the machine legitimately knows.
        IReadOnlyCollection<string> roots = RestoreEngine.BuildAuthorisedRoots(ClaudeEnvironment.Empty, null);

        Assert.NotNull(roots);
        // No count assertion: this machine's ~/.claude.json is real and may hold anything,
        // including nothing. Asserting a number here would make the test a property of the
        // developer's home directory rather than of the code.
    }

    [Fact]
    public void BuildAuthorisedRoots_IgnoresBlankOpenRoots()
    {
        // "No project open" reaches the host as an empty string as often as a null, and a
        // blank entry in the set would be compared against every candidate path.
        IReadOnlyCollection<string> roots = RestoreEngine.BuildAuthorisedRoots(ClaudeEnvironment.Empty, ["", "   "]);

        Assert.False(roots.Any(string.IsNullOrWhiteSpace),
            "A blank root must never enter the authorised set.");
    }

    // ── F8 · a committed restore sweeps the sidecars it wrote ─────────

    [Fact]
    public void Journal_RecordsEverySidecarWritten()
    {
        // The sweep deletes by exact path, so the ledger has to be complete. A sidecar
        // written but not recorded is one that survives for ever.
        string src = Path.Combine(_baseDir, "ledger-src");
        string dest = Path.Combine(_baseDir, "ledger-dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(src, "a.txt"), "new");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");
        File.WriteAllText(Path.Combine(src, "b.txt"), "new-b");   // no live counterpart
        RestoreJournal journal = new();

        RestoreEngine.RestoreDirectory(src, dest, "20260519-120000", journal);

        MessageAssert.Equal(1, journal.Sidecars.Count,
            "Exactly the overwritten file gets a sidecar — b.txt had no live copy to move aside.");
        OrdinalAssert.EndsWith("a.txt.pre-restore-20260519-120000.bak", journal.Sidecars[0]);
        Assert.True(File.Exists(journal.Sidecars[0]),
            "The recorded path must be the real one on disk, or the sweep deletes nothing.");
    }

    [Fact]
    public void SweepSidecars_DeletesWhatTheRunWrote_AndLeavesTheRestoredFile()
    {
        string src = Path.Combine(_baseDir, "sweep-src");
        string dest = Path.Combine(_baseDir, "sweep-dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(src, "a.txt"), "new");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "old");
        RestoreJournal journal = new();
        RestoreEngine.RestoreDirectory(src, dest, "20260519-120000", journal);
        MessageAssert.Equal(1, journal.Sidecars.Count, "Premise: a sidecar was written.");

        int swept = RestoreEngine.SweepSidecars(journal.Sidecars);

        Assert.Equal(1, swept);
        Assert.False(File.Exists(journal.Sidecars[0]), "F8: the sidecar must be gone.");
        MessageAssert.Equal("new", File.ReadAllText(Path.Combine(dest, "a.txt")),
            "The restored file itself must be untouched — the sweep removes copies, not content.");
    }

    [Fact]
    public void SweepSidecars_RefusesAnythingThatIsNotOurOwnSidecar()
    {
        // A hand-rolled notes.md.bak in ~/.claude is the user's, not ours. The ledger should
        // never contain one, so this is the second lock on the one operation here that deletes
        // user-visible files.
        string handRolled = Path.Combine(_baseDir, "notes.md.bak");
        File.WriteAllText(handRolled, "mine");

        int swept = RestoreEngine.SweepSidecars([handRolled]);

        Assert.Equal(0, swept);
        Assert.True(File.Exists(handRolled),
            "A .bak that does not match the pre-restore pattern must survive the sweep.");
    }

    [Fact]
    public void SweepSidecars_MissingFile_IsNotCountedAndDoesNotThrow()
    {
        // Something else removed it between restore and sweep. Counting it would overstate
        // what the message tells the user was cleaned up.
        string absent = Path.Combine(_baseDir, "gone.json.pre-restore-20260519-120000.bak");

        int swept = RestoreEngine.SweepSidecars([absent]);

        Assert.Equal(0, swept);
    }

    // ── RestoreProjects (manifest-driven projects subtree) ────────────

    [Fact]
    public void RestoreProjects_NoProjectsDir_ReturnsZero()
    {
        // tempRoot has no ClaudeCode/projects/ subtree → 0 restored, no
        // skipped, no failures.  This is the common SettingsOnly case.
        BackupManifest manifest = new();
        RestoreJournal journal = new();
        List<string> skipped = journal.Skipped;
        List<string> failures = journal.FileFailures;

        int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", [], journal);

        Assert.Equal(0, count);
        Assert.Empty(skipped);
        Assert.Empty(failures);
    }

    [Fact]
    public void RestoreProjects_PathMissing_Skipped()
    {
        // tempRoot/ClaudeCode/projects/SomeProject/ exists, but the
        // manifest's matching project livePath does NOT exist on disk
        // → that project gets skipped with a "(path missing)" note.
        string projectsDir = Path.Combine(_baseDir, "ClaudeCode", "projects", "GhostProject");
        Directory.CreateDirectory(projectsDir);
        File.WriteAllText(Path.Combine(projectsDir, "claude.json"), "{}");

        BackupManifest manifest = new()
        {
            Projects = { Path.Combine(_baseDir, "definitely-does-not-exist", "GhostProject") },
        };
        RestoreJournal journal = new();
        List<string> skipped = journal.Skipped;
        List<string> failures = journal.FileFailures;

        int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", [], journal);

        Assert.Equal(0, count);
        Assert.Single(skipped);
        Assert.True(skipped[0].Contains("path missing", StringComparison.Ordinal),
            $"Skip note should explain why: '{skipped[0]}'");
    }

    [Fact]
    public void RestoreProjects_PathOutsideUserProfile_Refused()
    {
        // tempRoot has a project subtree, manifest names a livePath
        // outside the user's profile (a system path) — must be refused
        // with "(path outside user profile)".  This is the security
        // gate that stops a crafted zip from writing to C:\Windows or /etc.
        string projectsDir = Path.Combine(_baseDir, "ClaudeCode", "projects", "EvilProject");
        Directory.CreateDirectory(projectsDir);
        File.WriteAllText(Path.Combine(projectsDir, "evil.txt"), "would-be malicious");

        string systemRoot = OperatingSystem.IsWindows()
            ? @"C:\Windows\Temp\EvilProject"
            : "/tmp/EvilProject";
        // Ensure the path actually exists so we hit IsUnderUserProfile
        // rather than the path-missing branch.
        try
        {
            Directory.CreateDirectory(systemRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Cannot prepare system path '{systemRoot}' for the refusal test: {ex.Message}");
            return;
        }

        try
        {
            BackupManifest manifest = new() { Projects = { systemRoot } };
            RestoreJournal journal = new();
            List<string> skipped = journal.Skipped;
            List<string> failures = journal.FileFailures;

            int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", [], journal);

            Assert.Equal(0, count);
            Assert.Single(skipped);
            Assert.True(skipped[0].Contains("not a path this machine recognises", StringComparison.Ordinal),
                $"Skip note should name the refusal, not invent a missing folder: '{skipped[0]}'");
            // Verify nothing actually landed there.
            Assert.False(File.Exists(Path.Combine(systemRoot, "evil.txt")),
                "RestoreProjects must NOT have copied the file to a path outside the user profile.");
        }
        finally
        {
            try
            {
                Directory.Delete(systemRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [Fact]
    public void RestoreProjects_HappyPath_CopiesToManifestPath()
    {
        // RestoreProjects matches projBackupDir's name (under tempRoot/
        // ClaudeCode/projects/) against Path.GetFileName(manifest.Projects[i])
        // — so the live path's terminal segment must equal the backup
        // entry's directory name.  Construct a unique parent under the
        // user profile, then create "MyProject" inside it so the
        // basename-match succeeds.
        string projectsDir = Path.Combine(_baseDir, "ClaudeCode", "projects", "MyProject");
        Directory.CreateDirectory(projectsDir);
        File.WriteAllText(Path.Combine(projectsDir, "claude.json"), "{\"hello\":\"world\"}");

        string parent = CreateUnderUserProfile("projects-happy");
        string liveProjectRoot = Path.Combine(parent, "MyProject");
        Directory.CreateDirectory(liveProjectRoot);
        try
        {
            BackupManifest manifest = new() { Projects = { liveProjectRoot } };
            RestoreJournal journal = new();
            List<string> skipped = journal.Skipped;
            List<string> failures = journal.FileFailures;

            int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", [], journal);

            Assert.Equal(1, count);
            Assert.Empty(skipped);
            Assert.Empty(failures);
            Assert.Equal("{\"hello\":\"world\"}",
                File.ReadAllText(Path.Combine(liveProjectRoot, "claude.json")));
        }
        finally
        {
            CleanUnderProfile(parent);
        }
    }

    // ── RestoreWorktrees (worktree-metadata-driven restore) ───────────

    [Fact]
    public void RestoreWorktrees_NoWorktreesDir_ReturnsZero()
    {
        RestoreJournal journal = new();
        List<string> skipped = journal.Skipped;
        List<string> failures = journal.FileFailures;

        int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", journal);

        Assert.Equal(0, count);
        Assert.Empty(skipped);
        Assert.Empty(failures);
    }

    [Fact]
    public void RestoreWorktrees_MissingMeta_Skipped()
    {
        // tempRoot/ClaudeCode/worktrees/foo/ exists but has no
        // .worktree-meta.json → skipped with "(no worktree metadata)".
        string wtDir = Path.Combine(_baseDir, "ClaudeCode", "worktrees", "foo");
        Directory.CreateDirectory(wtDir);

        RestoreJournal journal = new();
        List<string> skipped = journal.Skipped;
        List<string> failures = journal.FileFailures;

        int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", journal);

        Assert.Equal(0, count);
        Assert.Single(skipped);
        Assert.True(skipped[0].Contains("no worktree metadata", StringComparison.Ordinal),
            $"Skip note: '{skipped[0]}'");
    }

    [Fact]
    public void RestoreWorktrees_WorktreePathOutsideProfile_Refused()
    {
        // Crafted .worktree-meta.json points outside the user profile
        // → security guard kicks in.  No copy lands.
        string wtDir = Path.Combine(_baseDir, "ClaudeCode", "worktrees", "evil-wt");
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, "evil.txt"), "would-be malicious");

        string systemRoot = OperatingSystem.IsWindows()
            ? @"C:\Windows\Temp\EvilWt"
            : "/tmp/EvilWt";
        try
        {
            Directory.CreateDirectory(systemRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"Cannot prepare system path for the refusal test: {ex.Message}");
            return;
        }

        try
        {
            File.WriteAllText(Path.Combine(wtDir, ".worktree-meta.json"),
                $"{{\"projectRoot\":\"\",\"worktreePath\":\"{systemRoot.Replace("\\", "\\\\")}\"}}");

            RestoreJournal journal = new();
            List<string> skipped = journal.Skipped;
            List<string> failures = journal.FileFailures;

            int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", journal);

            Assert.Equal(0, count);
            Assert.Single(skipped);
            Assert.True(skipped[0].Contains("outside your home folder", StringComparison.Ordinal),
                $"Skip note should name the security reason: '{skipped[0]}'");
            Assert.False(File.Exists(Path.Combine(systemRoot, "evil.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(systemRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [Fact]
    public void RestoreWorktrees_HappyPath_RestoresToWorktreePath()
    {
        string wtDir = Path.Combine(_baseDir, "ClaudeCode", "worktrees", "feature-branch");
        Directory.CreateDirectory(wtDir);
        File.WriteAllText(Path.Combine(wtDir, "settings.json"), "{\"wt\":true}");

        string liveWtPath = CreateUnderUserProfile("worktree-feature-branch");
        try
        {
            File.WriteAllText(Path.Combine(wtDir, ".worktree-meta.json"),
                $"{{\"projectRoot\":\"\",\"worktreePath\":\"{liveWtPath.Replace("\\", "\\\\")}\"}}");

            RestoreJournal journal = new();
            List<string> skipped = journal.Skipped;
            List<string> failures = journal.FileFailures;

            int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", journal);

            // Count of 1 — settings.json restored.  The .worktree-meta.json
            // itself is also under wtDir so RestoreDirectory will copy it
            // too — total 2.  Either result is acceptable as long as
            // settings.json landed; assert that specifically.
            Assert.True(count >= 1, $"Expected ≥1 restored, got {count}.");
            Assert.Empty(skipped);
            Assert.Empty(failures);
            Assert.Equal("{\"wt\":true}",
                File.ReadAllText(Path.Combine(liveWtPath, "settings.json")));
        }
        finally
        {
            CleanUnderProfile(liveWtPath);
        }
    }

    // ── Helpers for under-user-profile temp dirs ──────────────────────
    //
    // RestoreProjects / RestoreWorktrees gate destination paths through
    // IsUnderUserProfile which checks the real Environment.UserProfile.
    // Tests that exercise the happy path must therefore use destinations
    // under the actual user profile, not the OS temp directory (which
    // is under UserProfile on Windows but NOT on macOS/Linux).

    private readonly List<string> _underProfileCleanup = [];

    private string CreateUnderUserProfile(string suffix)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".claudeforge-restoreengine-tests", $"{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _underProfileCleanup.Add(dir);
        return dir;
    }

    private void CleanUnderProfile(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }
}