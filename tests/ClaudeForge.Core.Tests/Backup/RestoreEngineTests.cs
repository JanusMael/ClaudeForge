using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

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
[TestClass]
public sealed class RestoreEngineTests
{
    private string _baseDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "re-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDir);
    }

    [TestCleanup]
    public void Teardown()
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

    [TestMethod]
    public void ResolveSafeExtractPath_AllowsNormalEntry()
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, "ClaudeCode/claude.json");
        Assert.IsNotNull(resolved);
        Assert.IsTrue(resolved!.StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase),
            "Resolved path must remain inside the extraction root.");
    }

    [TestMethod]
    public void ResolveSafeExtractPath_AllowsNestedSubdirectory()
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(
            _baseDir, "ClaudeCode/projects/session-2026-01.jsonl");
        Assert.IsNotNull(resolved);
        Assert.IsTrue(resolved!.Contains("session-2026-01.jsonl", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("../../etc/passwd")] // POSIX traversal
    [DataRow("..\\..\\Windows\\System32\\evil.dll")] // Windows traversal
    [DataRow("subdir/../../escape.txt")] // mid-path traversal
    [DataRow("subdir\\..\\..\\escape.txt")] // mid-path Windows
    public void ResolveSafeExtractPath_RejectsTraversal(string entry)
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, entry);
        Assert.IsNull(resolved, $"Traversal entry '{entry}' must be rejected.");
    }

    [TestMethod]
    [DataRow("/etc/passwd")] // POSIX absolute
    [DataRow("\\server\\share\\evil.txt")] // UNC-style
    [DataRow("C:\\Windows\\evil.exe")] // Windows drive-rooted
    public void ResolveSafeExtractPath_RejectsAbsolutePaths(string entry)
    {
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, entry);
        Assert.IsNull(resolved, $"Absolute path '{entry}' must be rejected.");
    }

    [TestMethod]
    [DataRow("file.txt:evil")] // ADS attempt — colon after filename
    [DataRow("normal/path:stream")] // ADS in nested
    public void ResolveSafeExtractPath_RejectsAlternateDataStreamSyntax(string entry)
    {
        // On Windows, "file.txt:evil" creates an Alternate Data Stream
        // attached to file.txt. The guard rejects ANY colon to be safe
        // cross-platform.
        string? resolved = RestoreEngine.ResolveSafeExtractPath(_baseDir, entry);
        Assert.IsNull(resolved, $"ADS-style path '{entry}' must be rejected.");
    }

    [TestMethod]
    public void ResolveSafeExtractPath_RejectsEmptyAndNullishEntry()
    {
        Assert.IsNull(RestoreEngine.ResolveSafeExtractPath(_baseDir, string.Empty));
    }

    [TestMethod]
    public void ResolveSafeExtractPath_NormalisesSeparators()
    {
        // Forward and back slashes both resolve to the OS form.
        string? posix = RestoreEngine.ResolveSafeExtractPath(_baseDir, "ClaudeCode/sub/file.json");
        string? windows = RestoreEngine.ResolveSafeExtractPath(_baseDir, "ClaudeCode\\sub\\file.json");

        Assert.IsNotNull(posix);
        Assert.IsNotNull(windows);
        Assert.AreEqual(posix, windows,
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

    [TestMethod]
    public void ContainsRedactedMarker_TrueOnRedactedValueInsideJson()
    {
        string probe = Path.Combine(_baseDir, "ClaudeCode");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "claude.json"),
            """{ "env": "[redacted]", "theme": "dark" }""");

        Assert.IsTrue(RestoreEngine.ContainsRedactedMarker(_baseDir),
            "Redacted-marker leaf must trigger tamper-detection.");
    }

    [TestMethod]
    public void ContainsRedactedMarker_FalseOnNormalConfig()
    {
        string probe = Path.Combine(_baseDir, "ClaudeCode");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "claude.json"),
            """{ "theme": "dark", "model": "claude-opus" }""");

        Assert.IsFalse(RestoreEngine.ContainsRedactedMarker(_baseDir),
            "Plain config with no marker must not trigger the scan.");
    }

    [TestMethod]
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

        Assert.IsFalse(RestoreEngine.ContainsRedactedMarker(_baseDir),
            "Prose mentioning the marker word must not fire tamper-detection — only exact-equal leaf values do.");
    }

    [TestMethod]
    public void ContainsRedactedMarker_TrueOnNestedRedactedValue()
    {
        // Deep nested case — marker buried inside an object/array tree.
        string probe = Path.Combine(_baseDir, "ClaudeCode", "claude-dir");
        Directory.CreateDirectory(probe);
        File.WriteAllText(Path.Combine(probe, "settings.json"),
            """{ "mcpServers": { "gh": { "headers": "[redacted]" } } }""");

        Assert.IsTrue(RestoreEngine.ContainsRedactedMarker(_baseDir));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  H5 — Sidecar cap at write time
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
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
        Assert.AreEqual(RestoreEngine.MaxSidecarsPerFile - 1, surviving.Length,
            $"Expected {RestoreEngine.MaxSidecarsPerFile - 1} sidecars to survive eviction. Got: {string.Join(", ", surviving)}");

        // Survivors should be the 2 most recent stamps (lexical = chronological).
        Assert.IsTrue(surviving.Any(n => n!.Contains("20260103")));
        Assert.IsTrue(surviving.Any(n => n!.Contains("20260104")));
        Assert.IsFalse(surviving.Any(n => n!.Contains("20260101")),
            "Oldest sidecar must be evicted.");
        Assert.IsFalse(surviving.Any(n => n!.Contains("20260102")),
            "Second-oldest sidecar must also be evicted (cap-1 = 2 survivors).");
    }

    [TestMethod]
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

        Assert.IsTrue(File.Exists(handRolled1),
            "settings.json.bak (editor-style, no .pre-restore- stamp) must NOT be evicted.");
        Assert.IsTrue(File.Exists(handRolled2),
            "Unrelated notes.md.bak must NOT be evicted.");
    }

    [TestMethod]
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
            Assert.Inconclusive("Read-only retry path is Windows-specific.");
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
            Assert.IsFalse(File.Exists(oldest),
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

    [TestMethod]
    public void EvictOldSidecarsIfNeeded_BelowCap_DoesNothing()
    {
        // 2 sidecars + the new one about to be written = 3 total, at
        // the cap but not over.  No eviction should happen.
        string liveFile = Path.Combine(_baseDir, "settings.json");
        File.WriteAllText(liveFile, "real");
        File.WriteAllText(Path.Combine(_baseDir, "settings.json.pre-restore-20260101-100000.bak"), "s1");
        File.WriteAllText(Path.Combine(_baseDir, "settings.json.pre-restore-20260102-100000.bak"), "s2");

        RestoreEngine.EvictOldSidecarsIfNeeded(liveFile);

        Assert.AreEqual(2, Directory.GetFiles(_baseDir, "settings.json.pre-restore-*.bak").Length,
            "Below-cap eviction must be a no-op.");
    }

    // ── IsUnderUserProfile (security guard for manifest-provided paths) ──
    //
    // added during the COVERAGE-B3 refresh pass.  These
    // tests cover the IsUnderUserProfile predicate that gates
    // RestoreProjects / RestoreWorktrees against manifest-provided paths
    // outside the user's home directory.

    [TestMethod]
    public void IsUnderUserProfile_EmptyOrNull_Rejected()
    {
        Assert.IsFalse(RestoreEngine.IsUnderUserProfile(null));
        Assert.IsFalse(RestoreEngine.IsUnderUserProfile(""));
        Assert.IsFalse(RestoreEngine.IsUnderUserProfile("   "));
    }

    [TestMethod]
    [DataRow(@"\\server\share\file")]
    [DataRow(@"\\?\C:\file")]
    [DataRow("//server/share/file")]
    public void IsUnderUserProfile_UncPaths_Rejected(string candidate)
    {
        Assert.IsFalse(RestoreEngine.IsUnderUserProfile(candidate),
            $"UNC path '{candidate}' must be rejected — could redirect writes to a remote host.");
    }

    [TestMethod]
    public void IsUnderUserProfile_PathOutsideProfile_Rejected()
    {
        // Cross-platform "definitely outside the user profile" probe:
        // a sibling directory at the filesystem root.
        string systemRoot = OperatingSystem.IsWindows()
            ? @"C:\Windows\System32"
            : "/etc";
        Assert.IsFalse(RestoreEngine.IsUnderUserProfile(systemRoot),
            "System path outside user profile must be rejected.");
    }

    [TestMethod]
    public void IsUnderUserProfile_PathUnderProfile_Accepted()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.IsFalse(string.IsNullOrEmpty(home), "Test host has no UserProfile — cannot exercise positive case.");
        string underProfile = Path.Combine(home, ".claude", "test-only-not-real");
        Assert.IsTrue(RestoreEngine.IsUnderUserProfile(underProfile),
            $"Path under user profile must be accepted: {underProfile}");
    }

    [TestMethod]
    public void IsUnderUserProfile_UserProfileItself_Accepted()
    {
        // Edge case: the exact home-directory path (no trailing
        // separator) hits the equality branch rather than the
        // StartsWith-trailing-separator branch.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.IsFalse(string.IsNullOrEmpty(home));
        Assert.IsTrue(RestoreEngine.IsUnderUserProfile(home));
    }

    // ── RestoreSection (single-file restore + sidecar) ────────────────

    [TestMethod]
    public void RestoreSection_MissingSource_ReturnsZero()
    {
        // Source doesn't exist → restore is a no-op, count 0, no
        // failure message (it's a normal "file wasn't in this backup"
        // case, not an error).
        string src = Path.Combine(_baseDir, "missing.txt");
        string dest = Path.Combine(_baseDir, "live", "missing.txt");

        (int restored, string? failure) = RestoreEngine.RestoreSection(src, dest, "20260519-120000");

        Assert.AreEqual(0, restored);
        Assert.IsNull(failure);
        Assert.IsFalse(File.Exists(dest));
    }

    [TestMethod]
    public void RestoreSection_NewDestination_CopiesNoSidecar()
    {
        // Dest doesn't exist → no .pre-restore-{stamp}.bak sidecar
        // should be created.  Only the new copy lands.
        string src = Path.Combine(_baseDir, "src.txt");
        string dest = Path.Combine(_baseDir, "live", "dest.txt");
        File.WriteAllText(src, "from backup");

        (int restored, string? failure) = RestoreEngine.RestoreSection(src, dest, "20260519-120000");

        Assert.AreEqual(1, restored);
        Assert.IsNull(failure);
        Assert.AreEqual("from backup", File.ReadAllText(dest));
        Assert.IsFalse(File.Exists($"{dest}.pre-restore-20260519-120000.bak"),
            "No sidecar should be created when the destination didn't exist.");
    }

    [TestMethod]
    public void RestoreSection_ExistingDestination_CreatesSidecar()
    {
        string src = Path.Combine(_baseDir, "src.txt");
        string dest = Path.Combine(_baseDir, "live", "dest.txt");
        Directory.CreateDirectory(Path.Combine(_baseDir, "live"));
        File.WriteAllText(src, "new from backup");
        File.WriteAllText(dest, "old live content");

        (int restored, string? failure) = RestoreEngine.RestoreSection(src, dest, "20260519-120000");

        Assert.AreEqual(1, restored);
        Assert.IsNull(failure);
        Assert.AreEqual("new from backup", File.ReadAllText(dest));
        string sidecar = $"{dest}.pre-restore-20260519-120000.bak";
        Assert.IsTrue(File.Exists(sidecar), "Sidecar must be written when destination existed.");
        Assert.AreEqual("old live content", File.ReadAllText(sidecar));
    }

    // ── RestoreDirectory (recursive directory restore) ────────────────

    [TestMethod]
    public void RestoreDirectory_MissingSource_NoOp()
    {
        string src = Path.Combine(_baseDir, "no-such-src");
        string dest = Path.Combine(_baseDir, "dest");
        (int restored, List<string> failures) = RestoreEngine.RestoreDirectory(src, dest, "20260519-120000");

        Assert.AreEqual(0, restored);
        Assert.AreEqual(0, failures.Count);
        Assert.IsFalse(Directory.Exists(dest), "Missing source must not create the destination directory.");
    }

    [TestMethod]
    public void RestoreDirectory_HappyPath_CopiesAllFilesPreservingTree()
    {
        string src = Path.Combine(_baseDir, "src");
        string dest = Path.Combine(_baseDir, "dest");
        Directory.CreateDirectory(Path.Combine(src, "nested", "deeper"));
        File.WriteAllText(Path.Combine(src, "root.txt"), "root");
        File.WriteAllText(Path.Combine(src, "nested", "child.txt"), "child");
        File.WriteAllText(Path.Combine(src, "nested", "deeper", "leaf.txt"), "leaf");

        (int restored, List<string> failures) = RestoreEngine.RestoreDirectory(src, dest, "20260519-120000");

        Assert.AreEqual(3, restored);
        Assert.AreEqual(0, failures.Count);
        Assert.AreEqual("root", File.ReadAllText(Path.Combine(dest, "root.txt")));
        Assert.AreEqual("child", File.ReadAllText(Path.Combine(dest, "nested", "child.txt")));
        Assert.AreEqual("leaf", File.ReadAllText(Path.Combine(dest, "nested", "deeper", "leaf.txt")));
    }

    [TestMethod]
    public void RestoreDirectory_ExistingFiles_CreateSidecars()
    {
        string src = Path.Combine(_baseDir, "src");
        string dest = Path.Combine(_baseDir, "dest");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(src, "shared.txt"), "from backup");
        File.WriteAllText(Path.Combine(dest, "shared.txt"), "live content");

        (int restored, List<string> failures) = RestoreEngine.RestoreDirectory(src, dest, "20260519-120000");

        Assert.AreEqual(1, restored);
        Assert.AreEqual(0, failures.Count);
        Assert.AreEqual("from backup", File.ReadAllText(Path.Combine(dest, "shared.txt")));
        Assert.AreEqual("live content",
            File.ReadAllText(Path.Combine(dest, "shared.txt.pre-restore-20260519-120000.bak")),
            "Sidecar must capture the pre-restore live content.");
    }

    // ── RestoreProjects (manifest-driven projects subtree) ────────────

    [TestMethod]
    public void RestoreProjects_NoProjectsDir_ReturnsZero()
    {
        // tempRoot has no ClaudeCode/projects/ subtree → 0 restored, no
        // skipped, no failures.  This is the common SettingsOnly case.
        BackupManifest manifest = new();
        List<string> skipped = [];
        List<string> failures = [];

        int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", skipped, failures);

        Assert.AreEqual(0, count);
        Assert.AreEqual(0, skipped.Count);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
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
        List<string> skipped = [];
        List<string> failures = [];

        int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", skipped, failures);

        Assert.AreEqual(0, count);
        Assert.AreEqual(1, skipped.Count);
        Assert.IsTrue(skipped[0].Contains("path missing", StringComparison.Ordinal),
            $"Skip note should explain why: '{skipped[0]}'");
    }

    [TestMethod]
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
            Assert.Inconclusive($"Cannot prepare system path '{systemRoot}' for the refusal test: {ex.Message}");
            return;
        }

        try
        {
            BackupManifest manifest = new() { Projects = { systemRoot } };
            List<string> skipped = [];
            List<string> failures = [];

            int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", skipped, failures);

            Assert.AreEqual(0, count);
            Assert.AreEqual(1, skipped.Count);
            Assert.IsTrue(skipped[0].Contains("outside user profile", StringComparison.Ordinal),
                $"Skip note should name the security reason: '{skipped[0]}'");
            // Verify nothing actually landed there.
            Assert.IsFalse(File.Exists(Path.Combine(systemRoot, "evil.txt")),
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

    [TestMethod]
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
            List<string> skipped = [];
            List<string> failures = [];

            int count = RestoreEngine.RestoreProjects(_baseDir, manifest, "20260519-120000", skipped, failures);

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, skipped.Count);
            Assert.AreEqual(0, failures.Count);
            Assert.AreEqual("{\"hello\":\"world\"}",
                File.ReadAllText(Path.Combine(liveProjectRoot, "claude.json")));
        }
        finally
        {
            CleanUnderProfile(parent);
        }
    }

    // ── RestoreWorktrees (worktree-metadata-driven restore) ───────────

    [TestMethod]
    public void RestoreWorktrees_NoWorktreesDir_ReturnsZero()
    {
        List<string> skipped = [];
        List<string> failures = [];

        int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", skipped, failures);

        Assert.AreEqual(0, count);
        Assert.AreEqual(0, skipped.Count);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void RestoreWorktrees_MissingMeta_Skipped()
    {
        // tempRoot/ClaudeCode/worktrees/foo/ exists but has no
        // .worktree-meta.json → skipped with "(no worktree metadata)".
        string wtDir = Path.Combine(_baseDir, "ClaudeCode", "worktrees", "foo");
        Directory.CreateDirectory(wtDir);

        List<string> skipped = [];
        List<string> failures = [];

        int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", skipped, failures);

        Assert.AreEqual(0, count);
        Assert.AreEqual(1, skipped.Count);
        Assert.IsTrue(skipped[0].Contains("no worktree metadata", StringComparison.Ordinal),
            $"Skip note: '{skipped[0]}'");
    }

    [TestMethod]
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
            Assert.Inconclusive($"Cannot prepare system path for the refusal test: {ex.Message}");
            return;
        }

        try
        {
            File.WriteAllText(Path.Combine(wtDir, ".worktree-meta.json"),
                $"{{\"projectRoot\":\"\",\"worktreePath\":\"{systemRoot.Replace("\\", "\\\\")}\"}}");

            List<string> skipped = [];
            List<string> failures = [];

            int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", skipped, failures);

            Assert.AreEqual(0, count);
            Assert.AreEqual(1, skipped.Count);
            Assert.IsTrue(skipped[0].Contains("outside user profile", StringComparison.Ordinal),
                $"Skip note should name the security reason: '{skipped[0]}'");
            Assert.IsFalse(File.Exists(Path.Combine(systemRoot, "evil.txt")));
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

    [TestMethod]
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

            List<string> skipped = [];
            List<string> failures = [];

            int count = RestoreEngine.RestoreWorktrees(_baseDir, "20260519-120000", skipped, failures);

            // Count of 1 — settings.json restored.  The .worktree-meta.json
            // itself is also under wtDir so RestoreDirectory will copy it
            // too — total 2.  Either result is acceptable as long as
            // settings.json landed; assert that specifically.
            Assert.IsTrue(count >= 1, $"Expected ≥1 restored, got {count}.");
            Assert.AreEqual(0, skipped.Count);
            Assert.AreEqual(0, failures.Count);
            Assert.AreEqual("{\"wt\":true}",
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