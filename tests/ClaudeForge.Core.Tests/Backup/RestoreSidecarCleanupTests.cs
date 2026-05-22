using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// pins the <c>--cleanup-restore-sidecars</c> CLI tool's
/// behaviour.  The tool walks <c>~/.claude/</c> recursively and deletes
/// every <c>*.bak</c> file (regardless of depth or naming variant) left
/// behind by <see cref="RestoreEngine"/>'s pre-restore sidecar pattern.
/// Non-<c>.bak</c> files MUST survive untouched — the whole point is
/// the user's real config keeps working after the cleanup.
/// </summary>
[TestClass]
public sealed class RestoreSidecarCleanupTests
{
    private string _fakeHome = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "sidecar-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".claude"));
    }

    [TestCleanup]
    public void TearDown()
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
    public void Run_DeletesPreRestoreSidecars_PreservesEditorStyleBakAndRealFiles()
    {
        string home = Path.Combine(_fakeHome, ".claude");

        // Top-level: one real file, one .pre-restore-…bak sidecar.
        File.WriteAllText(Path.Combine(home, "settings.json"), """{"theme":"dark"}""");
        File.WriteAllText(Path.Combine(home, "settings.json.pre-restore-20260101-120000.bak"),
            "(old settings)");

        // Nested in agents/: real files, a sidecar, a compounded sidecar
        // (a .bak of a .bak — the exact pathology the user hit), and an
        // editor-style .bak that DOES NOT match the pre-restore pattern
        // and must be left alone.
        string agents = Path.Combine(home, "agents");
        Directory.CreateDirectory(agents);
        File.WriteAllText(Path.Combine(agents, "agent-a.md"), "real a");
        File.WriteAllText(Path.Combine(agents, "agent-b.md"), "real b");
        File.WriteAllText(Path.Combine(agents, "agent-a.md.pre-restore-20260101-120000.bak"),
            "(old a)");
        File.WriteAllText(
            Path.Combine(agents, "agent-a.md.pre-restore-20260101-120000.bak.pre-restore-20260201-090000.bak"),
            "(compounded sidecar — sidecar of a sidecar)");
        // the cleanup tool's whitelist is the
        // `.pre-restore-{8-digit-date}-{6-digit-time}.bak` pattern that
        // RestoreEngine emits.  Editor-style .bak files (vim, sed -i.bak,
        // user-rolled `notes.md.bak`) do NOT match and must survive — the
        // user might be using them deliberately and the cleanup tool's
        // name ("restore sidecars") matches user expectations.
        string handEdited = Path.Combine(agents, "hand-edited.md.bak");
        File.WriteAllText(handEdited, "(editor-style .bak — NOT a restore sidecar)");

        // Deeply nested under skills/<skill>/: real SKILL.md plus a sidecar.
        string skill = Path.Combine(home, "skills", "my-skill");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "real skill");
        File.WriteAllText(Path.Combine(skill, "SKILL.md.pre-restore-20260301-100000.bak"),
            "(old skill)");

        RestoreSidecarCleanup.Result result = RestoreSidecarCleanup.Run(home);

        Assert.AreEqual(4, result.FilesDeleted,
            "Expected exactly the 4 pre-restore sidecars to be deleted (settings.json, agent-a, " +
            "compounded chain, SKILL.md).  The editor-style hand-edited.md.bak must NOT be touched.");
        Assert.AreEqual(0, result.Failures, "No I/O failures expected on the sandbox.");
        Assert.IsTrue(result.BytesReclaimed > 0, "Reclaimed-byte count should reflect the deleted files.");

        // Real files must survive.
        Assert.IsTrue(File.Exists(Path.Combine(home, "settings.json")));
        Assert.IsTrue(File.Exists(Path.Combine(agents, "agent-a.md")));
        Assert.IsTrue(File.Exists(Path.Combine(agents, "agent-b.md")));
        Assert.IsTrue(File.Exists(Path.Combine(skill, "SKILL.md")));
        // Editor-style hand-rolled .bak MUST survive — this is the
        // whitelist-narrowing contract.
        Assert.IsTrue(File.Exists(handEdited),
            "hand-edited.md.bak doesn't match the .pre-restore-… pattern and must NOT be touched.");

        // No pre-restore sidecars should remain at any depth.
        List<string> leftoverSidecars = Directory.EnumerateFiles(home, "*.bak", SearchOption.AllDirectories)
                                                 .Where(p => RestoreSidecarCleanup.LooksLikeRestoreSidecar(Path.GetFileName(p)))
                                                 .ToList();
        Assert.AreEqual(0, leftoverSidecars.Count,
            $"All restore sidecars should be gone.  Leftovers: {string.Join(", ", leftoverSidecars)}");
    }

    /// <summary>
    /// the cleanup tool's whitelist used to be "every *.bak"
    /// which would touch third-party / hand-rolled .bak files in
    /// ~/.claude/ as collateral damage.  Narrowed to the exact pattern
    /// RestoreEngine produces; this test pins the discrimination contract.
    /// </summary>
    [TestMethod]
    public void LooksLikeRestoreSidecar_MatchesOnlyRestoreEnginePattern()
    {
        // Matches: stamped .pre-restore-… sidecars at any depth in the
        // suffix chain.
        Assert.IsTrue(RestoreSidecarCleanup.LooksLikeRestoreSidecar(
            "settings.json.pre-restore-20260101-120000.bak"));
        Assert.IsTrue(RestoreSidecarCleanup.LooksLikeRestoreSidecar(
            "foo.md.pre-restore-20260101-120000.bak.pre-restore-20260201-090000.bak"));

        // Misses: editor-style and hand-rolled .bak files have no
        // `.pre-restore-{stamp}.` segment.
        Assert.IsFalse(RestoreSidecarCleanup.LooksLikeRestoreSidecar("notes.md.bak"));
        Assert.IsFalse(RestoreSidecarCleanup.LooksLikeRestoreSidecar("config.toml.bak"));
        Assert.IsFalse(RestoreSidecarCleanup.LooksLikeRestoreSidecar("my-backup.bak"));

        // Misses: malformed stamps (length-wrong, non-digit) — defensive
        // against future format changes accidentally matching.
        Assert.IsFalse(RestoreSidecarCleanup.LooksLikeRestoreSidecar("foo.pre-restore-20260101.bak"));
        Assert.IsFalse(RestoreSidecarCleanup.LooksLikeRestoreSidecar("foo.pre-restore-yyyymmdd-hhmmss.bak"));
        Assert.IsFalse(RestoreSidecarCleanup.LooksLikeRestoreSidecar("foo.pre-restore-X.bak"));

        // Misses: pattern not at end (anchored on $).
        Assert.IsFalse(RestoreSidecarCleanup.LooksLikeRestoreSidecar(
            "foo.pre-restore-20260101-120000.bak.txt"));
    }

    [TestMethod]
    public void Run_OnMissingHome_ReturnsZeroSummary()
    {
        string missing = Path.Combine(_fakeHome, ".claude", "does-not-exist");
        RestoreSidecarCleanup.Result result = RestoreSidecarCleanup.Run(missing);

        Assert.AreEqual(0, result.FilesScanned);
        Assert.AreEqual(0, result.FilesDeleted);
        Assert.AreEqual(0, result.BytesReclaimed);
        Assert.AreEqual(0, result.Failures);
    }

    [TestMethod]
    public void Run_OnTreeWithoutSidecars_DeletesNothing()
    {
        string home = Path.Combine(_fakeHome, ".claude");
        File.WriteAllText(Path.Combine(home, "settings.json"), """{"a":1}""");
        File.WriteAllText(Path.Combine(home, "claude.md"), "real");

        RestoreSidecarCleanup.Result result = RestoreSidecarCleanup.Run(home);

        Assert.AreEqual(0, result.FilesScanned, "EnumerateFiles('*.bak') should have produced no matches.");
        Assert.AreEqual(0, result.FilesDeleted);
        Assert.IsTrue(File.Exists(Path.Combine(home, "settings.json")));
        Assert.IsTrue(File.Exists(Path.Combine(home, "claude.md")));
    }

    /// <summary>
    /// first production run on a 99 307-sidecar profile left
    /// behind 6 read-only files inside the
    /// <c>everything-claude-code/.git/objects/pack/</c> directory: Git
    /// marks pack objects read-only, and the .bak sidecars inherited
    /// those attributes from the source files at restore time.  Pin the
    /// retry-after-clear-readonly behaviour so the next cleanup run
    /// actually finishes the job.
    /// </summary>
    [TestMethod]
    public void Run_ReadOnlySidecar_DeletedAfterClearingAttribute()
    {
        string home = Path.Combine(_fakeHome, ".claude");
        string readOnlyBak = Path.Combine(home, "locked.json.pre-restore-20260101-120000.bak");
        File.WriteAllText(readOnlyBak, "(read-only sidecar)");
        File.SetAttributes(readOnlyBak, FileAttributes.ReadOnly);

        try
        {
            RestoreSidecarCleanup.Result result = RestoreSidecarCleanup.Run(home);

            Assert.AreEqual(1, result.FilesDeleted,
                "Read-only sidecar should be deleted on retry after clearing the read-only attribute.");
            Assert.AreEqual(0, result.Failures,
                "No failures expected once the retry path handled the read-only attribute.");
            Assert.IsFalse(File.Exists(readOnlyBak),
                "The read-only file should no longer exist after cleanup.");
        }
        finally
        {
            // Defensive cleanup if the test fails: clear the read-only
            // attribute so the TestCleanup Directory.Delete doesn't trip.
            if (File.Exists(readOnlyBak))
            {
                File.SetAttributes(readOnlyBak, FileAttributes.Normal);
                File.Delete(readOnlyBak);
            }
        }
    }

    [TestMethod]
    public void Run_ProgressCallback_FiresEvery1000Deletions()
    {
        string home = Path.Combine(_fakeHome, ".claude");

        // Create 2 500 pre-restore sidecars so the progress callback
        // fires twice (at 1 000 and 2 000), plus 500 stragglers that
        // don't trigger another tick.  Files match the
        // `.pre-restore-{stamp}.bak` whitelist so the cleanup tool
        // actually deletes them.
        const int sidecarCount = 2_500;
        for (int i = 0; i < sidecarCount; i++)
        {
            File.WriteAllText(
                Path.Combine(home, $"file-{i}.txt.pre-restore-20260101-120000.bak"),
                "x");
        }

        List<int> progressTicks = new();
        RestoreSidecarCleanup.Result result = RestoreSidecarCleanup.Run(home, onProgress: progressTicks.Add);

        Assert.AreEqual(sidecarCount, result.FilesDeleted);
        CollectionAssert.AreEqual(new[] { 1000, 2000 }, progressTicks,
            "Progress should fire exactly at the 1 000 / 2 000 deletion boundaries.");
    }
}