using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Profile;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Profile;

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
[TestClass]
public sealed class ProfileEngineAsyncTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
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

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t")]
    public async Task CreateFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => ProfileEngine.CreateFromLiveAsync(name));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task ApplyProfileToLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => ProfileEngine.ApplyProfileToLiveAsync(name));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task SyncFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => ProfileEngine.SyncFromLiveAsync(name));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task CreateDesktopProfileFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
            ProfileEngine.CreateDesktopProfileFromLiveAsync(name));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task ApplyDesktopProfileToLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => ProfileEngine.ApplyDesktopProfileToLiveAsync(name));
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public async Task SyncDesktopFromLiveAsync_BlankName_ThrowsArgumentException(string name)
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => ProfileEngine.SyncDesktopFromLiveAsync(name));
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_NullName_ThrowsArgumentNullException()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace specifically raises
        // ArgumentNullException for null; ArgumentException for whitespace.
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => ProfileEngine.CreateFromLiveAsync(null!));
    }

    // ── Cancellation propagation ─────────────────────────────────────

    [TestMethod]
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
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            ProfileEngine.CreateFromLiveAsync("p", cts.Token));
    }

    [TestMethod]
    public async Task ApplyProfileToLiveAsync_PreCancelledToken_ThrowsOperationCancelled()
    {
        CreateProfileWithSettings("p", """{"model":"opus"}""");

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        // .NET's File.WriteAllTextAsync raises TaskCanceledException
        // (a subclass of OperationCanceledException) on a pre-cancelled
        // token; both exception types are valid signals here.
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            ProfileEngine.ApplyProfileToLiveAsync("p", autoSync: false, cts.Token));
    }

    // ── ApplyProfileToLiveAsync error path ───────────────────────────

    [TestMethod]
    public async Task ApplyProfileToLiveAsync_MissingSettings_ThrowsFileNotFound()
    {
        // Profile directory exists but contains no settings.json.
        Directory.CreateDirectory(ProfileDir("orphan"));

        FileNotFoundException ex = await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            ProfileEngine.ApplyProfileToLiveAsync("orphan"));

        StringAssert.Contains(ex.Message, "orphan",
            "The error message should name the offending profile.");
        StringAssert.Contains(ex.Message, "settings.json",
            "The error message should name the missing file.");
    }

    // ── Auto-sync side effect ────────────────────────────────────────

    [TestMethod]
    public async Task ApplyProfileToLiveAsync_AutoSync_UpdatesPreviousActiveProfileFromLive()
    {
        // Setup: profile A is CLI-active, live settings have an external edit
        // not yet captured into A's directory. Apply profile B with autoSync.
        // A's directory should be updated from the live state before the switch.
        CreateProfileWithSettings("A", """{"model":"sonnet"}""");
        CreateProfileWithSettings("B", """{"model":"opus"}""");

        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"model":"haiku","external":"edit"}""");
        ProfileEngine.WriteCurrentProfileName("A");

        await ProfileEngine.ApplyProfileToLiveAsync("B", autoSync: true);

        // Profile A should now contain the live state (haiku + external edit)
        // because auto-sync ran before the switch to B.
        string aSettings = await File.ReadAllTextAsync(ProfileSettings("A"));
        StringAssert.Contains(aSettings, "haiku",
            "Auto-sync must capture live edits into the previously-active profile.");
        StringAssert.Contains(aSettings, "external");

        // Live now reflects B's state.
        string liveAfter = await File.ReadAllTextAsync(LiveSettings);
        StringAssert.Contains(liveAfter, "opus");

        // CLI-active pointer flipped to B.
        Assert.AreEqual("B", ProfileEngine.ReadCurrentProfileName());
    }

    [TestMethod]
    public async Task ApplyProfileToLiveAsync_AutoSyncDisabled_DoesNotUpdatePreviousProfile()
    {
        CreateProfileWithSettings("A", """{"model":"sonnet"}""");
        CreateProfileWithSettings("B", """{"model":"opus"}""");

        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"model":"haiku"}""");
        ProfileEngine.WriteCurrentProfileName("A");

        await ProfileEngine.ApplyProfileToLiveAsync("B", autoSync: false);

        // A's directory must remain at its pre-apply content because we
        // explicitly opted out of auto-sync.
        string aSettings = await File.ReadAllTextAsync(ProfileSettings("A"));
        StringAssert.Contains(aSettings, "sonnet",
            "With autoSync=false the previously-active profile must NOT pick up live edits.");
        Assert.IsFalse(aSettings.Contains("haiku"));
    }

    // ── MCP key handling ─────────────────────────────────────────────

    [TestMethod]
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

        await ProfileEngine.ApplyProfileToLiveAsync("p", autoSync: false);

        JsonObject after = JsonNode.Parse(await File.ReadAllTextAsync(ClaudeJsonPath))!.AsObject();
        Assert.IsFalse(after.ContainsKey("mcpServers"),
            "ApplyProfileToLiveAsync must remove mcpServers when the profile has no mcp.json.");
        Assert.IsTrue(after.ContainsKey("unrelated"),
            "Unrelated keys in ~/.claude.json must be preserved by the MCP-removal path.");
        Assert.AreEqual("keep-me", after["unrelated"]!.GetValue<string>());
    }

    [TestMethod]
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

        await ProfileEngine.ApplyProfileToLiveAsync("p", autoSync: false);

        JsonObject after = JsonNode.Parse(await File.ReadAllTextAsync(ClaudeJsonPath))!.AsObject();

        // mcpServers replaced with the profile's content.
        Assert.IsTrue(after.ContainsKey("mcpServers"));
        JsonObject mcpAfter = after["mcpServers"]!.AsObject();
        Assert.IsTrue(mcpAfter.ContainsKey("github"),
            "Profile's mcp.json must replace the live mcpServers content.");
        Assert.IsFalse(mcpAfter.ContainsKey("old-server"),
            "Old mcpServers keys must NOT survive the merge.");

        // Non-mcp keys preserved verbatim.
        Assert.AreEqual("preserve-me", after["sessionToken"]!.GetValue<string>());
        Assert.AreEqual(12345, after["lastChat"]!.GetValue<int>());
    }

    // ── Desktop async happy paths ────────────────────────────────────

    [TestMethod]
    public async Task CreateDesktopProfileFromLiveAsync_CopiesLiveConfigIntoProfile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopLiveConfig)!);
        await File.WriteAllTextAsync(DesktopLiveConfig, """{"theme":"dark"}""");

        bool created = await ProfileEngine.CreateDesktopProfileFromLiveAsync("d-test");

        Assert.IsTrue(created, "Create must report true on first creation.");
        Assert.IsTrue(File.Exists(DesktopProfileConfig("d-test")));
        string copied = await File.ReadAllTextAsync(DesktopProfileConfig("d-test"));
        StringAssert.Contains(copied, "dark");
    }

    [TestMethod]
    public async Task CreateDesktopProfileFromLiveAsync_NoLiveConfig_WritesEmptyObject()
    {
        // Live config does not exist — the method should still create the
        // profile dir and write {} so the profile is valid.
        Assert.IsFalse(File.Exists(DesktopLiveConfig), "Pre-condition: no live config.");

        bool created = await ProfileEngine.CreateDesktopProfileFromLiveAsync("empty");

        Assert.IsTrue(created);
        Assert.IsTrue(File.Exists(DesktopProfileConfig("empty")));
        Assert.AreEqual("{}", await File.ReadAllTextAsync(DesktopProfileConfig("empty")));
    }

    [TestMethod]
    public async Task CreateDesktopProfileFromLiveAsync_AlreadyExists_ReturnsFalse()
    {
        CreateDesktopProfileWithConfig("dup", """{"existing":true}""");

        bool created = await ProfileEngine.CreateDesktopProfileFromLiveAsync("dup");

        Assert.IsFalse(created,
            "Create must report false (no overwrite) when the profile dir already exists.");
        // Existing content untouched.
        StringAssert.Contains(
            await File.ReadAllTextAsync(DesktopProfileConfig("dup")),
            "existing");
    }

    [TestMethod]
    public async Task ApplyDesktopProfileToLiveAsync_CopiesProfileConfigToLiveAndUpdatesPointer()
    {
        CreateDesktopProfileWithConfig("d-apply", """{"theme":"light"}""");

        await ProfileEngine.ApplyDesktopProfileToLiveAsync("d-apply", autoSync: false);

        Assert.IsTrue(File.Exists(DesktopLiveConfig));
        StringAssert.Contains(
            await File.ReadAllTextAsync(DesktopLiveConfig), "light");
        Assert.AreEqual("d-apply", ProfileEngine.ReadCurrentDesktopProfileName());
    }

    [TestMethod]
    public async Task ApplyDesktopProfileToLiveAsync_MissingConfig_ThrowsFileNotFound()
    {
        // Profile directory exists but contains no claude_desktop_config.json.
        Directory.CreateDirectory(DesktopProfileDir("d-orphan"));

        FileNotFoundException ex = await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
            ProfileEngine.ApplyDesktopProfileToLiveAsync("d-orphan"));

        StringAssert.Contains(ex.Message, "d-orphan");
        StringAssert.Contains(ex.Message, "claude_desktop_config.json");
    }

    [TestMethod]
    public async Task SyncDesktopFromLiveAsync_RoundTrips_LiveIntoProfile()
    {
        CreateDesktopProfileWithConfig("d-sync", """{"old":true}""");
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopLiveConfig)!);
        await File.WriteAllTextAsync(DesktopLiveConfig, """{"theme":"system"}""");

        await ProfileEngine.SyncDesktopFromLiveAsync("d-sync");

        string afterSync = await File.ReadAllTextAsync(DesktopProfileConfig("d-sync"));
        StringAssert.Contains(afterSync, "system",
            "Sync must overwrite the profile config with the current live content.");
        Assert.IsFalse(afterSync.Contains("\"old\""),
            "Old profile content must not survive the sync.");
    }

    [TestMethod]
    public async Task SyncDesktopFromLiveAsync_NoLiveConfig_WritesEmptyProfile()
    {
        // No live config exists — the sync should still produce a valid {}.
        Assert.IsFalse(File.Exists(DesktopLiveConfig));

        await ProfileEngine.SyncDesktopFromLiveAsync("d-blank");

        Assert.IsTrue(File.Exists(DesktopProfileConfig("d-blank")));
        Assert.AreEqual("{}", await File.ReadAllTextAsync(DesktopProfileConfig("d-blank")));
    }

    [TestMethod]
    public async Task ApplyDesktopProfileToLiveAsync_AutoSync_UpdatesPreviousActiveProfile()
    {
        CreateDesktopProfileWithConfig("a", """{"theme":"old-a"}""");
        CreateDesktopProfileWithConfig("b", """{"theme":"new-b"}""");

        Directory.CreateDirectory(Path.GetDirectoryName(DesktopLiveConfig)!);
        await File.WriteAllTextAsync(DesktopLiveConfig, """{"theme":"live-edit"}""");
        ProfileEngine.WriteCurrentDesktopProfileName("a");

        await ProfileEngine.ApplyDesktopProfileToLiveAsync("b", autoSync: true);

        // Profile a should now contain the live state because auto-sync ran.
        StringAssert.Contains(
            await File.ReadAllTextAsync(DesktopProfileConfig("a")),
            "live-edit",
            "Auto-sync must capture live edits into the previously-active Desktop profile.");
        Assert.AreEqual("b", ProfileEngine.ReadCurrentDesktopProfileName());
    }

    // ── CreateFromLiveAsync happy paths (B.3 coverage gap #4) ─────────

    [TestMethod]
    public async Task CreateFromLiveAsync_NoLiveSettings_WritesEmptyObject()
    {
        // No ~/.claude/settings.json exists.  The fallback path should
        // write "{}" into the profile's settings.json so the new profile
        // is at least valid (empty config) rather than missing the file.
        bool created = await ProfileEngine.CreateFromLiveAsync("fresh");

        Assert.IsTrue(created);
        string content = await File.ReadAllTextAsync(ProfileSettings("fresh"));
        Assert.AreEqual("{}", content);
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_WithLiveSettings_CopiesContent()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"model":"haiku"}""");

        bool created = await ProfileEngine.CreateFromLiveAsync("seed");

        Assert.IsTrue(created);
        string content = await File.ReadAllTextAsync(ProfileSettings("seed"));
        StringAssert.Contains(content, "haiku");
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_WithClaudeMd_CopiesIt()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        await File.WriteAllTextAsync(PlatformPaths.ClaudeMdPath, "# Project memory\nSome notes.");

        await ProfileEngine.CreateFromLiveAsync("withmd");

        string profileMd = Path.Combine(ProfileDir("withmd"), "CLAUDE.md");
        Assert.IsTrue(File.Exists(profileMd),
            "When live CLAUDE.md exists, it must be copied into the new profile dir.");
        StringAssert.Contains(await File.ReadAllTextAsync(profileMd), "Project memory");
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_NoClaudeMd_DoesNotCreateOne()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        // Deliberately no CLAUDE.md.

        await ProfileEngine.CreateFromLiveAsync("nomd");

        string profileMd = Path.Combine(ProfileDir("nomd"), "CLAUDE.md");
        Assert.IsFalse(File.Exists(profileMd),
            "When live has no CLAUDE.md, the profile dir must not contain a stray file.");
    }

    [TestMethod]
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

        await ProfileEngine.CreateFromLiveAsync("hasmcp");

        string profileMcp = ProfileMcp("hasmcp");
        Assert.IsTrue(File.Exists(profileMcp),
            "When live ~/.claude.json has mcpServers, profile/mcp.json must be written.");
        StringAssert.Contains(await File.ReadAllTextAsync(profileMcp), "echo");
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_AlreadyExists_ReturnsFalseWithoutOverwrite()
    {
        // Pre-existing profile dir with custom content — Create should
        // refuse to overwrite and return false.
        CreateProfileWithSettings("dup", """{"existing":"keep"}""");

        bool result = await ProfileEngine.CreateFromLiveAsync("dup");

        Assert.IsFalse(result);
        string preserved = await File.ReadAllTextAsync(ProfileSettings("dup"));
        StringAssert.Contains(preserved, "keep",
            "Pre-existing profile content must NOT be overwritten when Create returns false.");
    }

    // ── SyncFromLiveAsync direct coverage (B.3 coverage gap #4) ──────

    [TestMethod]
    public async Task SyncFromLiveAsync_CopiesLiveSettingsIntoProfile()
    {
        CreateProfileWithSettings("p", """{"old":"value"}""");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"new":"value"}""");

        await ProfileEngine.SyncFromLiveAsync("p");

        string profileSettings = await File.ReadAllTextAsync(ProfileSettings("p"));
        StringAssert.Contains(profileSettings, "new",
            "Live settings.json must overwrite the profile's settings.json on sync.");
        Assert.IsFalse(profileSettings.Contains("old"),
            "The old profile content is replaced wholesale.");
    }

    [TestMethod]
    public async Task SyncFromLiveAsync_NoLiveSettings_WritesEmptyObject()
    {
        // Edge case: profile exists, but live has been deleted (e.g. user
        // ran Clear App Data).  Sync should reset the profile to "{}"
        // rather than fail or leave the old content stale.
        CreateProfileWithSettings("p", """{"old":"value"}""");

        await ProfileEngine.SyncFromLiveAsync("p");

        string profileSettings = await File.ReadAllTextAsync(ProfileSettings("p"));
        Assert.AreEqual("{}", profileSettings);
    }

    [TestMethod]
    public async Task SyncFromLiveAsync_WithLiveClaudeMd_CopiesIt()
    {
        CreateProfileWithSettings("p");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        await File.WriteAllTextAsync(PlatformPaths.ClaudeMdPath, "# updated");

        await ProfileEngine.SyncFromLiveAsync("p");

        string profileMd = Path.Combine(ProfileDir("p"), "CLAUDE.md");
        Assert.IsTrue(File.Exists(profileMd));
        StringAssert.Contains(await File.ReadAllTextAsync(profileMd), "updated");
    }

    [TestMethod]
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

        await ProfileEngine.SyncFromLiveAsync("p");

        Assert.IsFalse(File.Exists(profileMd),
            "When live has no CLAUDE.md, sync must remove the profile's stale copy.");
    }
}