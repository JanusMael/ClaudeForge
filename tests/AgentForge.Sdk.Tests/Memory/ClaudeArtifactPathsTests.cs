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
public sealed class ClaudeArtifactPathsTests : IDisposable
{
    private string _profile = null!;
    private string _other = null!;

    public ClaudeArtifactPathsTests() => Setup();

    private void Setup()
    {
        _profile = NewSandbox("paths-a");
        _other = NewSandbox("paths-b");
        PlatformPaths.TestUserProfileOverride = _profile;
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
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

    [Fact]
    public void EveryPath_AgreesWithPlatformPaths_ForTheSameProfile()
    {
        // ⛔ THE guard that makes the duplicated literals safe. ClaudeArtifactPaths restates
        // ".claude", "settings.json", "mcp.json", "managed-settings.json", "managed-settings.d",
        // ".claude.json" and ".credentials.json" so that a different root is a constructor argument
        // rather than a mutation of a process-wide static — and the cost of that choice is exactly
        // one way to drift. This is it. If PlatformPaths renames a file, this fails and names it.
        var paths = new ClaudeArtifactPaths(_profile, ClaudeEnvironment.Empty);

        MessageAssert.Equal(PlatformPaths.UserProfile, paths.UserProfile, nameof(paths.UserProfile));
        MessageAssert.Equal(PlatformPaths.ClaudeHome(ClaudeEnvironment.Empty), paths.ClaudeHome, nameof(paths.ClaudeHome));
        MessageAssert.Equal(PlatformPaths.UserSettingsPath(ClaudeEnvironment.Empty), paths.UserSettingsPath, nameof(paths.UserSettingsPath));
        MessageAssert.Equal(PlatformPaths.UserMcpPath(ClaudeEnvironment.Empty), paths.UserMcpPath, nameof(paths.UserMcpPath));
        MessageAssert.Equal(
            PlatformPaths.ManagedSettingsPath, paths.ManagedSettingsPath, nameof(paths.ManagedSettingsPath));
        MessageAssert.Equal(
            PlatformPaths.ManagedSettingsDropInDir, paths.ManagedSettingsDropInDir,
            nameof(paths.ManagedSettingsDropInDir));
        MessageAssert.Equal(PlatformPaths.ClaudeJsonPath, paths.ClaudeJsonPath, nameof(paths.ClaudeJsonPath));
        MessageAssert.Equal(PlatformPaths.CredentialsPath(ClaudeEnvironment.Empty), paths.CredentialsPath, nameof(paths.CredentialsPath));
    }

    [Fact]
    public void TheProfileIsTheRoot_NotClaudeHome()
    {
        // ⭐ Two of these sit BESIDE ~/.claude rather than inside it, which is why the root is the
        // profile: a ClaudeHome-rooted provider could produce neither.
        var paths = new ClaudeArtifactPaths(_profile, ClaudeEnvironment.Empty);

        Assert.Equal(Path.Combine(_profile, ".claude.json"), paths.ClaudeJsonPath);
        Assert.Equal(Path.Combine(_profile, ".claude"), paths.ClaudeHome);
        Assert.Equal(_profile, paths.UserProfile);
    }

    [Fact]
    public void Default_ReflectsTheCurrentOverride_NotACapturedOne()
    {
        // ⛔ Default is a property returning a fresh instance for exactly this reason: the
        // underlying profile is AsyncLocal-backed, so a cached instance would freeze whichever
        // sandbox was current when it was first touched — and that failure presents as flakiness
        // in an unrelated test rather than as a stale cache here.
        Assert.Equal(_profile, ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty).UserProfile);

        PlatformPaths.TestUserProfileOverride = _other;
        Assert.Equal(_other, ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty).UserProfile);

        PlatformPaths.TestUserProfileOverride = _profile;
    }

    // ── Injection actually redirects ─────────────────────────────────────────

    [Fact]
    public void SnapshotFiles_ReadsTheInjectedProfile_NotTheProcessDefault()
    {
        // The process default points at _profile; the injected provider points at _other. Only one
        // of the two files below may appear.
        Write(Path.Combine(_profile, ".claude", "CLAUDE.md"), "# default profile");
        Write(Path.Combine(_other, ".claude", "CLAUDE.md"), "# injected profile");

        IReadOnlyList<UserMemoryFile> files =
            UserMemoryService.SnapshotFiles(new ClaudeArtifactPaths(_other, ClaudeEnvironment.Empty), projectRoot: null);

        UserMemoryFile claudeMd = files.Single(f => f.Category == UserMemoryCategory.PrimaryMemory);
        OrdinalAssert.StartsWith(_other, claudeMd.AbsolutePath);
    }

    [Fact]
    public void Snapshot_ReadsTheInjectedProfile_NotTheProcessDefault()
    {
        Write(Path.Combine(_profile, ".claude", "agents", "from-default.md"));
        Write(Path.Combine(_other, ".claude", "agents", "from-injected.md"));

        IReadOnlyList<EditableMemoryEntry> entries =
            EditableMemoryService.Snapshot(new ClaudeArtifactPaths(_other, ClaudeEnvironment.Empty), projectRoot: null);

        Assert.Equal("from-injected", entries.Single().DisplayName);
    }

    [Fact]
    public void ConfigurationProbes_FollowTheInjectedProfileToo()
    {
        // The five configuration probes were the last static reads in the source list — the ones
        // this phase's plan called "specific files, not directories under a root". They move with
        // the injected profile like everything else.
        Write(Path.Combine(_profile, ".claude", "settings.json"), "{}");
        Write(Path.Combine(_other, ".claude", "settings.json"), "{}");
        Write(Path.Combine(_other, ".claude.json"), "{}");

        string[] names = [.. UserMemoryService
            .SnapshotFiles(new ClaudeArtifactPaths(_other, ClaudeEnvironment.Empty), projectRoot: null)
            .Where(f => f.Category == UserMemoryCategory.Configuration)
            .Select(f => f.DisplayName)];

        MessageAssert.SameElements(new[] { "settings.json", ".claude.json" }, names);
    }

    [Fact]
    public async Task FootprintService_ResolvesItsDefaultLazily_NotAtConstruction()
    {
        // ⛔ The service is cached for the lifetime of an AgentConfigClientCore, so capturing
        // ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty) in the constructor would pin whichever sandbox was current
        // when the client was first built. A test that set its override afterwards would then read
        // another test's directory — which reads as flakiness, not as a stale cache.
        //
        // ⚠ This must exercise an INSTANCE method. An earlier draft constructed the service and
        // then asserted through the STATIC ResolveCategoryPath wrapper, which never touches the
        // instance's field at all — it passed whether the constructor captured or not.
        var service = new FootprintService(() => ClaudeArtifactPaths.DefaultFor(ClaudeEnvironment.Empty), FootprintCatalog.Default);

        // Only the OTHER profile has transcripts, and the service was built while _profile was current.
        Write(Path.Combine(_other, ".claude", "projects", "proj-a", "session.jsonl"), "{}");

        PlatformPaths.TestUserProfileOverride = _other;
        try
        {
            IReadOnlyList<ProjectTranscriptStats> rows =
                await service.GetProjectTranscriptStatsAsync(CancellationToken.None);

            MessageAssert.Equal(
                1, rows.Count,
                "The service must read the profile that is current at CALL time, not at construction.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = _profile;
        }
    }

    [Fact]
    public void FootprintService_ReadsAnInjectedProfile()
    {
        Write(Path.Combine(_other, ".claude", "projects", "proj-a", "session.jsonl"), "{}");

        string resolved = FootprintService.ResolveCategoryPath(
            new ClaudeArtifactPaths(_other, ClaudeEnvironment.Empty), FootprintCategory.SessionTranscripts);

        Assert.Equal(Path.Combine(_other, ".claude", "projects"), resolved);
    }

    // ── The excluded file, compared exactly ──────────────────────────────────

    [Fact]
    public void TheCredentialsFile_IsNeverAnInventoryLocation()
    {
        // ⭐ An exact-path comparison, which is what CredentialsPath exists on the provider for.
        // The long-standing guard searches each row for the substring "credentials" — that passes
        // for the wrong reasons (any path containing the word) and would fail for them too (a
        // sandbox called C:\credentials-test). This one cannot.
        var paths = new ClaudeArtifactPaths(_other, ClaudeEnvironment.Empty);
        Write(paths.CredentialsPath, "{\"token\":\"secret\"}");
        Write(paths.UserSettingsPath, "{}");

        IReadOnlyList<UserMemoryFile> files =
            UserMemoryService.SnapshotFiles(paths, projectRoot: null);

        Assert.True(files.Any(f => f.DisplayName == "settings.json"), "The sandbox must not be empty.");
        Assert.False(
            files.Any(f => string.Equals(f.AbsolutePath, paths.CredentialsPath, StringComparison.Ordinal)),
            "The credentials file must never be surfaced in a browsable inventory.");
    }

    // ── A relocated home ─────────────────────────────────────────────────────

    [Fact]
    public void ARelocatedEnvironment_MovesTheHomeAndEverythingUnderIt()
    {
        // ⭐ The case the type could not express before: CLAUDE_CONFIG_DIR is documented as
        // "every ~/.claude path lives under that directory instead", and this provider hardcoded
        // Path.Combine(UserProfile, ".claude"). The Memory and Agents & Skills pages therefore
        // read ~/.claude while the settings pages read the relocated tree, with nothing failing.
        string relocated = Path.Combine(_other, "relocated-claude");
        var paths = new ClaudeArtifactPaths(_profile, new ClaudeEnvironment(relocated));

        MessageAssert.Equal(relocated, paths.ClaudeHome, nameof(paths.ClaudeHome));
        MessageAssert.Equal(
            Path.Combine(relocated, "settings.json"), paths.UserSettingsPath,
            nameof(paths.UserSettingsPath));
        MessageAssert.Equal(
            Path.Combine(relocated, "mcp.json"), paths.UserMcpPath, nameof(paths.UserMcpPath));
        MessageAssert.Equal(
            Path.Combine(relocated, ".credentials.json"), paths.CredentialsPath,
            nameof(paths.CredentialsPath));
    }

    [Fact]
    public void ARelocatedEnvironment_LeavesClaudeJsonBesideTheProfile()
    {
        // ⛔ NOT a "~/.claude path". ~/.claude.json is Claude Code's global config file sitting
        // BESIDE the home, so relocating the home must not move it — plans/00002 settles this
        // explicitly, and moving it would point the app at a file Claude Code never reads.
        string relocated = Path.Combine(_other, "relocated-claude");
        var paths = new ClaudeArtifactPaths(_profile, new ClaudeEnvironment(relocated));

        Assert.Equal(Path.Combine(_profile, ".claude.json"), paths.ClaudeJsonPath);
    }

    [Fact]
    public void ARelocatedHome_AgreesWithPlatformPaths()
    {
        // ⛔ The drift guard above only ever compares the EMPTY environment, so it could not see
        // a relocation disagreement at all. This is that half.
        //
        // ⚠ The override has to be cleared first: PlatformPaths gives TestUserProfileOverride
        // priority over the environment by design, so with it set the left-hand side is the
        // sandbox no matter what the environment says — the assertion would pass while proving
        // nothing about relocation.
        string relocated = Path.Combine(_other, "relocated-claude");
        var env = new ClaudeEnvironment(relocated);

        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            var paths = new ClaudeArtifactPaths(PlatformPaths.UserProfile, env);

            MessageAssert.Equal(PlatformPaths.ClaudeHome(env), paths.ClaudeHome, nameof(paths.ClaudeHome));
            MessageAssert.Equal(
                PlatformPaths.UserSettingsPath(env), paths.UserSettingsPath, nameof(paths.UserSettingsPath));
            MessageAssert.Equal(PlatformPaths.UserMcpPath(env), paths.UserMcpPath, nameof(paths.UserMcpPath));
            MessageAssert.Equal(
                PlatformPaths.CredentialsPath(env), paths.CredentialsPath, nameof(paths.CredentialsPath));
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = _profile;
        }
    }

    [Fact]
    public void DefaultFor_CarriesTheEnvironmentThrough()
    {
        // ⭐ DefaultFor is what every static convenience overload resolves through, so if it
        // dropped the environment the whole thread would be decorative — the services would
        // compile, pass, and read ~/.claude. It reads PlatformPaths.UserProfile for the root and
        // must carry the environment it was handed verbatim.
        string relocated = Path.Combine(_other, "relocated-claude");
        var env = new ClaudeEnvironment(relocated);

        ClaudeArtifactPaths paths = ClaudeArtifactPaths.DefaultFor(env);

        MessageAssert.Same(env, paths.Env, "DefaultFor must carry the environment, not replace it.");
        Assert.Equal(relocated, paths.ClaudeHome);
    }
}
