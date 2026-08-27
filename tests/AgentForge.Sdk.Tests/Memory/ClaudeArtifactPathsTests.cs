using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// The injected path provider: that it agrees with <see cref="PlatformPaths"/>, and that injecting
/// it actually redirects the services.
/// </summary>
/// <remarks>
/// ⚠ <b>The second half is the point.</b> A provider that only ever returns the process-global
/// paths would be a seam in name only — every test would pass and profiles would still be
/// impossible. So the tests below set the process-global override to ONE sandbox and inject a
/// provider rooted at ANOTHER, then assert the services read the injected one. If injection were
/// decorative, they read the override instead and fail.
/// </remarks>
[TestClass]
public sealed class ClaudeArtifactPathsTests
{
    private string _profile = null!;
    private string _other = null!;

    [TestInitialize]
    public void Setup()
    {
        _profile = NewSandbox("paths-a");
        _other = NewSandbox("paths-b");
        PlatformPaths.TestUserProfileOverride = _profile;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        foreach (string dir in new[] { _profile, _other })
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Leave the temp directory if something still holds a handle.
            }
        }
    }

    private static string NewSandbox(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"claudeforge-{tag}-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(dir, ".claude"));
        return dir;
    }

    private static void Write(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ── Agreement with PlatformPaths ─────────────────────────────────────────

    [TestMethod]
    public void EveryPath_AgreesWithPlatformPaths_ForTheSameProfile()
    {
        // ⛔ THE guard that makes the duplicated literals safe. ClaudeArtifactPaths restates
        // ".claude", "settings.json", "mcp.json", "managed-settings.json", "managed-settings.d",
        // ".claude.json" and ".credentials.json" so that a different root is a constructor argument
        // rather than a mutation of a process-wide static — and the cost of that choice is exactly
        // one way to drift. This is it. If PlatformPaths renames a file, this fails and names it.
        var paths = new ClaudeArtifactPaths(_profile);

        Assert.AreEqual(PlatformPaths.UserProfile, paths.UserProfile, nameof(paths.UserProfile));
        Assert.AreEqual(PlatformPaths.ClaudeHome, paths.ClaudeHome, nameof(paths.ClaudeHome));
        Assert.AreEqual(PlatformPaths.UserSettingsPath, paths.UserSettingsPath, nameof(paths.UserSettingsPath));
        Assert.AreEqual(PlatformPaths.UserMcpPath, paths.UserMcpPath, nameof(paths.UserMcpPath));
        Assert.AreEqual(
            PlatformPaths.ManagedSettingsPath, paths.ManagedSettingsPath, nameof(paths.ManagedSettingsPath));
        Assert.AreEqual(
            PlatformPaths.ManagedSettingsDropInDir, paths.ManagedSettingsDropInDir,
            nameof(paths.ManagedSettingsDropInDir));
        Assert.AreEqual(PlatformPaths.ClaudeJsonPath, paths.ClaudeJsonPath, nameof(paths.ClaudeJsonPath));
        Assert.AreEqual(PlatformPaths.CredentialsPath, paths.CredentialsPath, nameof(paths.CredentialsPath));
    }

    [TestMethod]
    public void TheProfileIsTheRoot_NotClaudeHome()
    {
        // ⭐ Two of these sit BESIDE ~/.claude rather than inside it, which is why the root is the
        // profile: a ClaudeHome-rooted provider could produce neither.
        var paths = new ClaudeArtifactPaths(_profile);

        Assert.AreEqual(Path.Combine(_profile, ".claude.json"), paths.ClaudeJsonPath);
        Assert.AreEqual(Path.Combine(_profile, ".claude"), paths.ClaudeHome);
        Assert.AreEqual(_profile, paths.UserProfile);
    }

    [TestMethod]
    public void Default_ReflectsTheCurrentOverride_NotACapturedOne()
    {
        // ⛔ Default is a property returning a fresh instance for exactly this reason: the
        // underlying profile is AsyncLocal-backed, so a cached instance would freeze whichever
        // sandbox was current when it was first touched — and that failure presents as flakiness
        // in an unrelated test rather than as a stale cache here.
        Assert.AreEqual(_profile, ClaudeArtifactPaths.Default.UserProfile);

        PlatformPaths.TestUserProfileOverride = _other;
        Assert.AreEqual(_other, ClaudeArtifactPaths.Default.UserProfile);

        PlatformPaths.TestUserProfileOverride = _profile;
    }

    // ── Injection actually redirects ─────────────────────────────────────────

    [TestMethod]
    public void SnapshotFiles_ReadsTheInjectedProfile_NotTheProcessDefault()
    {
        // The process default points at _profile; the injected provider points at _other. Only one
        // of the two files below may appear.
        Write(Path.Combine(_profile, ".claude", "CLAUDE.md"), "# default profile");
        Write(Path.Combine(_other, ".claude", "CLAUDE.md"), "# injected profile");

        IReadOnlyList<UserMemoryFile> files =
            UserMemoryService.SnapshotFiles(new ClaudeArtifactPaths(_other), projectRoot: null);

        UserMemoryFile claudeMd = files.Single(f => f.Category == UserMemoryCategory.PrimaryMemory);
        StringAssert.StartsWith(claudeMd.AbsolutePath, _other);
    }

    [TestMethod]
    public void Snapshot_ReadsTheInjectedProfile_NotTheProcessDefault()
    {
        Write(Path.Combine(_profile, ".claude", "agents", "from-default.md"));
        Write(Path.Combine(_other, ".claude", "agents", "from-injected.md"));

        IReadOnlyList<EditableMemoryEntry> entries =
            EditableMemoryService.Snapshot(new ClaudeArtifactPaths(_other), projectRoot: null);

        Assert.AreEqual("from-injected", entries.Single().DisplayName);
    }

    [TestMethod]
    public void ConfigurationProbes_FollowTheInjectedProfileToo()
    {
        // The five configuration probes were the last static reads in the source list — the ones
        // this phase's plan called "specific files, not directories under a root". They move with
        // the injected profile like everything else.
        Write(Path.Combine(_profile, ".claude", "settings.json"), "{}");
        Write(Path.Combine(_other, ".claude", "settings.json"), "{}");
        Write(Path.Combine(_other, ".claude.json"), "{}");

        string[] names = [.. UserMemoryService
            .SnapshotFiles(new ClaudeArtifactPaths(_other), projectRoot: null)
            .Where(f => f.Category == UserMemoryCategory.Configuration)
            .Select(f => f.DisplayName)];

        CollectionAssert.AreEquivalent(new[] { "settings.json", ".claude.json" }, names);
    }

    [TestMethod]
    public async Task FootprintService_ResolvesItsDefaultLazily_NotAtConstruction()
    {
        // ⛔ The service is cached for the lifetime of an AgentConfigClientCore, so capturing
        // ClaudeArtifactPaths.Default in the constructor would pin whichever sandbox was current
        // when the client was first built. A test that set its override afterwards would then read
        // another test's directory — which reads as flakiness, not as a stale cache.
        //
        // ⚠ This must exercise an INSTANCE method. An earlier draft constructed the service and
        // then asserted through the STATIC ResolveCategoryPath wrapper, which never touches the
        // instance's field at all — it passed whether the constructor captured or not.
        var service = new FootprintService();

        // Only the OTHER profile has transcripts, and the service was built while _profile was current.
        Write(Path.Combine(_other, ".claude", "projects", "proj-a", "session.jsonl"), "{}");

        PlatformPaths.TestUserProfileOverride = _other;
        try
        {
            IReadOnlyList<ProjectTranscriptStats> rows =
                await service.GetProjectTranscriptStatsAsync(CancellationToken.None);

            Assert.AreEqual(
                1, rows.Count,
                "The service must read the profile that is current at CALL time, not at construction.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = _profile;
        }
    }

    [TestMethod]
    public void FootprintService_ReadsAnInjectedProfile()
    {
        Write(Path.Combine(_other, ".claude", "projects", "proj-a", "session.jsonl"), "{}");

        string resolved = FootprintService.ResolveCategoryPath(
            new ClaudeArtifactPaths(_other), FootprintCategory.SessionTranscripts);

        Assert.AreEqual(Path.Combine(_other, ".claude", "projects"), resolved);
    }

    // ── The excluded file, compared exactly ──────────────────────────────────

    [TestMethod]
    public void TheCredentialsFile_IsNeverAnInventoryLocation()
    {
        // ⭐ An exact-path comparison, which is what CredentialsPath exists on the provider for.
        // The long-standing guard searches each row for the substring "credentials" — that passes
        // for the wrong reasons (any path containing the word) and would fail for them too (a
        // sandbox called C:\credentials-test). This one cannot.
        var paths = new ClaudeArtifactPaths(_other);
        Write(paths.CredentialsPath, "{\"token\":\"secret\"}");
        Write(paths.UserSettingsPath, "{}");

        IReadOnlyList<UserMemoryFile> files =
            UserMemoryService.SnapshotFiles(paths, projectRoot: null);

        Assert.IsTrue(files.Any(f => f.DisplayName == "settings.json"), "The sandbox must not be empty.");
        Assert.IsFalse(
            files.Any(f => string.Equals(f.AbsolutePath, paths.CredentialsPath, StringComparison.Ordinal)),
            "The credentials file must never be surfaced in a browsable inventory.");
    }
}
