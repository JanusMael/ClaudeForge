using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Profile;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Profile;

/// <summary>
/// error and edge-case coverage for the
/// async methods on <see cref="ProfileEngine"/>.  The existing
/// <c>ProfileEngineTests</c> covers happy paths; this file covers
/// argument validation, cancellation, error propagation, auto-sync side
/// effects, and the Desktop-side methods that previously had no
/// dedicated coverage.
/// </summary>
/// <remarks>
/// Uses the same <see cref="PlatformPaths.TestUserProfileOverride"/>
/// sandbox seam as the existing tests — no additional injection
/// surface added to <see cref="ProfileEngine"/>.
/// </remarks>
public sealed class ProfileEngineAsyncTests : IDisposable
{
    private string _sandbox = string.Empty;

    public ProfileEngineAsyncTests() => Setup();

    private void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            /* best effort — file-system indexer may hold transient locks */
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // ── Path helpers ──────────────────────────────────────────────────

    private string ClaudeHome => Path.Combine(_sandbox, ".claude");
    private string ProfilesDir => Path.Combine(ClaudeHome, "profiles");
    private string LiveSettings => Path.Combine(ClaudeHome, "settings.json");
    private string ClaudeJsonPath => Path.Combine(_sandbox, ".claude.json");

    private string ProfileDir(string name)
    {
        return Path.Combine(ProfilesDir, name);
    }

    private string ProfileSettings(string name)
    {
        return Path.Combine(ProfileDir(name), "settings.json");
    }

    private string ProfileMcp(string name)
    {
        return Path.Combine(ProfileDir(name), "mcp.json");
    }

    private string DesktopProfilesDir => PlatformPaths.DesktopProfilesDirectory;
    private string DesktopLiveConfig => PlatformPaths.DesktopConfigPath;

    private string DesktopProfileDir(string name)
    {
        return Path.Combine(DesktopProfilesDir, name);
    }

    private string DesktopProfileConfig(string name)
    {
        return Path.Combine(DesktopProfileDir(name), "claude_desktop_config.json");
    }

    private void CreateProfileWithSettings(string name, string json = "{}")
    {
        Directory.CreateDirectory(ProfileDir(name));
        File.WriteAllText(ProfileSettings(name), json);
    }

    private void CreateDesktopProfileWithConfig(string name, string json = "{}")
    {
        Directory.CreateDirectory(DesktopProfileDir(name));
        File.WriteAllText(DesktopProfileConfig(name), json);
    }

    // ── Argument validation ──────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task CreateFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ApplyProfileToLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SyncFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ProfileEngine.SyncFromLiveAsync(ClaudeEnvironment.Empty, name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task CreateDesktopProfileFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProfileEngine.CreateDesktopProfileFromLiveAsync(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ApplyDesktopProfileToLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ProfileEngine.ApplyDesktopProfileToLiveAsync(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SyncDesktopFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ProfileEngine.SyncDesktopFromLiveAsync(name));
    }

    [Fact]
    public async Task CreateFromLiveAsync_NullName_ThrowsArgumentNullException()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace specifically raises
        // ArgumentNullException for null; ArgumentException for whitespace.
        await Assert.ThrowsAsync<ArgumentNullException>(() => ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, null!));
    }

    // ── Cancellation propagation ─────────────────────────────────────

    [Fact]
    public async Task CreateFromLiveAsync_PreCancelledToken_ThrowsOperationCancelled()
    {
        // Live settings.json must exist so the method actually awaits the file
        // copy — that's the await that observes the cancellation.
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"model":"sonnet"}""");

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        // .NET's File.WriteAllTextAsync raises TaskCanceledException
        // (a subclass of OperationCanceledException) on a pre-cancelled
        // token; both exception types are valid signals here.
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "p", cts.Token));
    }

    [Fact]
    public async Task ApplyProfileToLiveAsync_PreCancelledToken_ThrowsOperationCancelled()
    {
        CreateProfileWithSettings("p", """{"model":"opus"}""");

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        // .NET's File.WriteAllTextAsync raises TaskCanceledException
        // (a subclass of OperationCanceledException) on a pre-cancelled
        // token; both exception types are valid signals here.
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "p", autoSync: false, cts.Token));
    }

    // ── ApplyProfileToLiveAsync error path ───────────────────────────

    [Fact]
    public async Task ApplyProfileToLiveAsync_MissingSettings_ThrowsFileNotFound()
    {
        // Profile directory exists but contains no settings.json.
        Directory.CreateDirectory(ProfileDir("orphan"));

        FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "orphan"));

        MessageAssert.Contains("orphan", ex.Message,
            "The error message should name the offending profile.");
        MessageAssert.Contains("settings.json", ex.Message,
            "The error message should name the missing file.");
    }

    // ── Auto-sync side effect ────────────────────────────────────────

    [Fact]
    public async Task ApplyProfileToLiveAsync_AutoSync_UpdatesPreviousActiveProfileFromLive()
    {
        // Setup: profile A is CLI-active, live settings have an external edit
        // not yet captured into A's directory. Apply profile B with autoSync.
        // A's directory should be updated from the live state before the switch.
        CreateProfileWithSettings("A", """{"model":"sonnet"}""");
        CreateProfileWithSettings("B", """{"model":"opus"}""");

        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"model":"haiku","external":"edit"}""");
        ProfileEngine.WriteCurrentProfileName(ClaudeEnvironment.Empty, "A");

        await ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "B", autoSync: true);

        // Profile A should now contain the live state (haiku + external edit)
        // because auto-sync ran before the switch to B.
        string aSettings = await File.ReadAllTextAsync(ProfileSettings("A"));
        MessageAssert.Contains("haiku", aSettings,
            "Auto-sync must capture live edits into the previously-active profile.");
        Assert.Contains("external", aSettings);

        // Live now reflects B's state.
        string liveAfter = await File.ReadAllTextAsync(LiveSettings);
        Assert.Contains("opus", liveAfter);

        // CLI-active pointer flipped to B.
        Assert.Equal("B", ProfileEngine.ReadCurrentProfileName(ClaudeEnvironment.Empty));
    }

    [Fact]
    public async Task ApplyProfileToLiveAsync_AutoSyncDisabled_DoesNotUpdatePreviousProfile()
    {
        CreateProfileWithSettings("A", """{"model":"sonnet"}""");
        CreateProfileWithSettings("B", """{"model":"opus"}""");

        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"model":"haiku"}""");
        ProfileEngine.WriteCurrentProfileName(ClaudeEnvironment.Empty, "A");

        await ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "B", autoSync: false);

        // A's directory must remain at its pre-apply content because we
        // explicitly opted out of auto-sync.
        string aSettings = await File.ReadAllTextAsync(ProfileSettings("A"));
        MessageAssert.Contains("sonnet", aSettings,
            "With autoSync=false the previously-active profile must NOT pick up live edits.");
        Assert.DoesNotContain("haiku", aSettings);
    }

    // ── MCP key handling ─────────────────────────────────────────────

    [Fact]
    public async Task ApplyProfileToLiveAsync_NoProfileMcp_RemovesLiveMcpServers()
    {
        // Profile has settings.json but no mcp.json — applying must
        // strip mcpServers from ~/.claude.json so the live config matches
        // the profile's intent.
        CreateProfileWithSettings("p", """{"model":"opus"}""");

        // Live ~/.claude.json has mcpServers PLUS unrelated keys we want preserved.
        JsonObject live = new()
        {
            ["mcpServers"] = new JsonObject
            {
                ["github"] = new JsonObject { ["url"] = "https://example.com" },
            },
            ["unrelated"] = "keep-me",
        };
        await File.WriteAllTextAsync(ClaudeJsonPath, live.ToJsonString());

        await ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "p", autoSync: false);

        JsonObject after = JsonNode.Parse(await File.ReadAllTextAsync(ClaudeJsonPath))!.AsObject();
        Assert.False(after.ContainsKey("mcpServers"),
            "ApplyProfileToLiveAsync must remove mcpServers when the profile has no mcp.json.");
        Assert.True(after.ContainsKey("unrelated"),
            "Unrelated keys in ~/.claude.json must be preserved by the MCP-removal path.");
        Assert.Equal("keep-me", after["unrelated"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyProfileToLiveAsync_WithProfileMcp_PreservesNonMcpKeys()
    {
        // Profile has mcp.json — applying must merge it as the mcpServers key
        // of ~/.claude.json without disturbing the file's other keys.
        CreateProfileWithSettings("p", """{"model":"opus"}""");
        JsonObject profileMcp = new()
        {
            ["github"] = new JsonObject { ["url"] = "https://from-profile.example" },
        };
        await File.WriteAllTextAsync(ProfileMcp("p"), profileMcp.ToJsonString());

        // Live ~/.claude.json starts with a different mcpServers PLUS unrelated keys.
        JsonObject live = new()
        {
            ["mcpServers"] = new JsonObject
            {
                ["old-server"] = new JsonObject { ["url"] = "https://old.example" },
            },
            ["sessionToken"] = "preserve-me",
            ["lastChat"] = 12345,
        };
        await File.WriteAllTextAsync(ClaudeJsonPath, live.ToJsonString());

        await ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "p", autoSync: false);

        JsonObject after = JsonNode.Parse(await File.ReadAllTextAsync(ClaudeJsonPath))!.AsObject();

        // mcpServers replaced with the profile's content.
        Assert.True(after.ContainsKey("mcpServers"));
        JsonObject mcpAfter = after["mcpServers"]!.AsObject();
        Assert.True(mcpAfter.ContainsKey("github"),
            "Profile's mcp.json must replace the live mcpServers content.");
        Assert.False(mcpAfter.ContainsKey("old-server"),
            "Old mcpServers keys must NOT survive the merge.");

        // Non-mcp keys preserved verbatim.
        Assert.Equal("preserve-me", after["sessionToken"]!.GetValue<string>());
        Assert.Equal(12345, after["lastChat"]!.GetValue<int>());
    }

    // ── Desktop async happy paths ────────────────────────────────────

    [Fact]
    public async Task CreateDesktopProfileFromLiveAsync_CopiesLiveConfigIntoProfile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopLiveConfig)!);
        await File.WriteAllTextAsync(DesktopLiveConfig, """{"theme":"dark"}""");

        bool created = await ProfileEngine.CreateDesktopProfileFromLiveAsync("d-test");

        Assert.True(created, "Create must report true on first creation.");
        Assert.True(File.Exists(DesktopProfileConfig("d-test")));
        string copied = await File.ReadAllTextAsync(DesktopProfileConfig("d-test"));
        Assert.Contains("dark", copied);
    }

    [Fact]
    public async Task CreateDesktopProfileFromLiveAsync_NoLiveConfig_WritesEmptyObject()
    {
        // Live config does not exist — the method should still create the
        // profile dir and write {} so the profile is valid.
        Assert.False(File.Exists(DesktopLiveConfig), "Pre-condition: no live config.");

        bool created = await ProfileEngine.CreateDesktopProfileFromLiveAsync("empty");

        Assert.True(created);
        Assert.True(File.Exists(DesktopProfileConfig("empty")));
        Assert.Equal("{}", await File.ReadAllTextAsync(DesktopProfileConfig("empty")));
    }

    [Fact]
    public async Task CreateDesktopProfileFromLiveAsync_AlreadyExists_ReturnsFalse()
    {
        CreateDesktopProfileWithConfig("dup", """{"existing":true}""");

        bool created = await ProfileEngine.CreateDesktopProfileFromLiveAsync("dup");

        Assert.False(created,
            "Create must report false (no overwrite) when the profile dir already exists.");
        // Existing content untouched.
        Assert.Contains(
            "existing",
            await File.ReadAllTextAsync(DesktopProfileConfig("dup")));
    }

    [Fact]
    public async Task ApplyDesktopProfileToLiveAsync_CopiesProfileConfigToLiveAndUpdatesPointer()
    {
        CreateDesktopProfileWithConfig("d-apply", """{"theme":"light"}""");

        await ProfileEngine.ApplyDesktopProfileToLiveAsync("d-apply", autoSync: false);

        Assert.True(File.Exists(DesktopLiveConfig));
        Assert.Contains(
            "light", await File.ReadAllTextAsync(DesktopLiveConfig));
        Assert.Equal("d-apply", ProfileEngine.ReadCurrentDesktopProfileName());
    }

    [Fact]
    public async Task ApplyDesktopProfileToLiveAsync_MissingConfig_ThrowsFileNotFound()
    {
        // Profile directory exists but contains no claude_desktop_config.json.
        Directory.CreateDirectory(DesktopProfileDir("d-orphan"));

        FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            ProfileEngine.ApplyDesktopProfileToLiveAsync("d-orphan"));

        Assert.Contains("d-orphan", ex.Message);
        Assert.Contains("claude_desktop_config.json", ex.Message);
    }

    [Fact]
    public async Task SyncDesktopFromLiveAsync_RoundTrips_LiveIntoProfile()
    {
        CreateDesktopProfileWithConfig("d-sync", """{"old":true}""");
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopLiveConfig)!);
        await File.WriteAllTextAsync(DesktopLiveConfig, """{"theme":"system"}""");

        await ProfileEngine.SyncDesktopFromLiveAsync("d-sync");

        string afterSync = await File.ReadAllTextAsync(DesktopProfileConfig("d-sync"));
        MessageAssert.Contains("system", afterSync,
            "Sync must overwrite the profile config with the current live content.");
        Assert.False(afterSync.Contains("\"old\""),
            "Old profile content must not survive the sync.");
    }

    [Fact]
    public async Task SyncDesktopFromLiveAsync_NoLiveConfig_WritesEmptyProfile()
    {
        // No live config exists — the sync should still produce a valid {}.
        Assert.False(File.Exists(DesktopLiveConfig));

        await ProfileEngine.SyncDesktopFromLiveAsync("d-blank");

        Assert.True(File.Exists(DesktopProfileConfig("d-blank")));
        Assert.Equal("{}", await File.ReadAllTextAsync(DesktopProfileConfig("d-blank")));
    }

    [Fact]
    public async Task ApplyDesktopProfileToLiveAsync_AutoSync_UpdatesPreviousActiveProfile()
    {
        CreateDesktopProfileWithConfig("a", """{"theme":"old-a"}""");
        CreateDesktopProfileWithConfig("b", """{"theme":"new-b"}""");

        Directory.CreateDirectory(Path.GetDirectoryName(DesktopLiveConfig)!);
        await File.WriteAllTextAsync(DesktopLiveConfig, """{"theme":"live-edit"}""");
        ProfileEngine.WriteCurrentDesktopProfileName("a");

        await ProfileEngine.ApplyDesktopProfileToLiveAsync("b", autoSync: true);

        // Profile a should now contain the live state because auto-sync ran.
        MessageAssert.Contains(
            "live-edit",
            await File.ReadAllTextAsync(DesktopProfileConfig("a")),
            "Auto-sync must capture live edits into the previously-active Desktop profile.");
        Assert.Equal("b", ProfileEngine.ReadCurrentDesktopProfileName());
    }

    // ── CreateFromLiveAsync happy paths (B.3 coverage gap #4) ─────────

    [Fact]
    public async Task CreateFromLiveAsync_NoLiveSettings_WritesEmptyObject()
    {
        // No ~/.claude/settings.json exists.  The fallback path should
        // write "{}" into the profile's settings.json so the new profile
        // is at least valid (empty config) rather than missing the file.
        bool created = await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "fresh");

        Assert.True(created);
        string content = await File.ReadAllTextAsync(ProfileSettings("fresh"));
        Assert.Equal("{}", content);
    }

    [Fact]
    public async Task CreateFromLiveAsync_WithLiveSettings_CopiesContent()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"model":"haiku"}""");

        bool created = await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "seed");

        Assert.True(created);
        string content = await File.ReadAllTextAsync(ProfileSettings("seed"));
        Assert.Contains("haiku", content);
    }

    [Fact]
    public async Task CreateFromLiveAsync_WithClaudeMd_CopiesIt()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        await File.WriteAllTextAsync(PlatformPaths.ClaudeMdPath(ClaudeEnvironment.Empty), "# Project memory\nSome notes.");

        await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "withmd");

        string profileMd = Path.Combine(ProfileDir("withmd"), "CLAUDE.md");
        Assert.True(File.Exists(profileMd),
            "When live CLAUDE.md exists, it must be copied into the new profile dir.");
        Assert.Contains("Project memory", await File.ReadAllTextAsync(profileMd));
    }

    [Fact]
    public async Task CreateFromLiveAsync_NoClaudeMd_DoesNotCreateOne()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        // Deliberately no CLAUDE.md.

        await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "nomd");

        string profileMd = Path.Combine(ProfileDir("nomd"), "CLAUDE.md");
        Assert.False(File.Exists(profileMd),
            "When live has no CLAUDE.md, the profile dir must not contain a stray file.");
    }

    [Fact]
    public async Task CreateFromLiveAsync_WithMcpServersInClaudeJson_ExtractsToProfileMcpJson()
    {
        // ExtractMcpToProfileAsync side effect: when ~/.claude.json contains
        // mcpServers, the new profile's mcp.json should hold a copy.
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");

        JsonObject live = new()
        {
            ["mcpServers"] = new JsonObject
            {
                ["s"] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = "echo",
                },
            },
            ["unrelated"] = "keep",
        };
        await File.WriteAllTextAsync(ClaudeJsonPath, live.ToJsonString());

        await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "hasmcp");

        string profileMcp = ProfileMcp("hasmcp");
        Assert.True(File.Exists(profileMcp),
            "When live ~/.claude.json has mcpServers, profile/mcp.json must be written.");
        Assert.Contains("echo", await File.ReadAllTextAsync(profileMcp));
    }

    [Fact]
    public async Task CreateFromLiveAsync_AlreadyExists_ReturnsFalseWithoutOverwrite()
    {
        // Pre-existing profile dir with custom content — Create should
        // refuse to overwrite and return false.
        CreateProfileWithSettings("dup", """{"existing":"keep"}""");

        bool result = await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "dup");

        Assert.False(result);
        string preserved = await File.ReadAllTextAsync(ProfileSettings("dup"));
        MessageAssert.Contains("keep", preserved,
            "Pre-existing profile content must NOT be overwritten when Create returns false.");
    }

    // ── SyncFromLiveAsync direct coverage (B.3 coverage gap #4) ──────

    [Fact]
    public async Task SyncFromLiveAsync_CopiesLiveSettingsIntoProfile()
    {
        CreateProfileWithSettings("p", """{"old":"value"}""");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"new":"value"}""");

        await ProfileEngine.SyncFromLiveAsync(ClaudeEnvironment.Empty, "p");

        string profileSettings = await File.ReadAllTextAsync(ProfileSettings("p"));
        MessageAssert.Contains("new", profileSettings,
            "Live settings.json must overwrite the profile's settings.json on sync.");
        Assert.False(profileSettings.Contains("old"),
            "The old profile content is replaced wholesale.");
    }

    [Fact]
    public async Task SyncFromLiveAsync_NoLiveSettings_WritesEmptyObject()
    {
        // Edge case: profile exists, but live has been deleted (e.g. user
        // ran Clear App Data).  Sync should reset the profile to "{}"
        // rather than fail or leave the old content stale.
        CreateProfileWithSettings("p", """{"old":"value"}""");

        await ProfileEngine.SyncFromLiveAsync(ClaudeEnvironment.Empty, "p");

        string profileSettings = await File.ReadAllTextAsync(ProfileSettings("p"));
        Assert.Equal("{}", profileSettings);
    }

    [Fact]
    public async Task SyncFromLiveAsync_WithLiveClaudeMd_CopiesIt()
    {
        CreateProfileWithSettings("p");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        await File.WriteAllTextAsync(PlatformPaths.ClaudeMdPath(ClaudeEnvironment.Empty), "# updated");

        await ProfileEngine.SyncFromLiveAsync(ClaudeEnvironment.Empty, "p");

        string profileMd = Path.Combine(ProfileDir("p"), "CLAUDE.md");
        Assert.True(File.Exists(profileMd));
        Assert.Contains("updated", await File.ReadAllTextAsync(profileMd));
    }

    [Fact]
    public async Task SyncFromLiveAsync_LiveClaudeMdRemoved_RemovesFromProfile()
    {
        // The profile previously had a CLAUDE.md; live is now empty.
        // Sync should bring the profile in line — no stale CLAUDE.md.
        CreateProfileWithSettings("p");
        string profileMd = Path.Combine(ProfileDir("p"), "CLAUDE.md");
        await File.WriteAllTextAsync(profileMd, "# stale");

        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        // No live CLAUDE.md.

        await ProfileEngine.SyncFromLiveAsync(ClaudeEnvironment.Empty, "p");

        Assert.False(File.Exists(profileMd),
            "When live has no CLAUDE.md, sync must remove the profile's stale copy.");
    }
}