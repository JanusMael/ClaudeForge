using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Profile;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Profile;

[TestClass]
public sealed class ProfileEngineTests
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
            /* best effort */
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string ClaudeHome => Path.Combine(_sandbox, ".claude");
    private string ProfilesDir => Path.Combine(ClaudeHome, "profiles");
    private string CurrentFile => Path.Combine(ClaudeHome, ".claudectx-current");
    private string LiveSettings => Path.Combine(ClaudeHome, "settings.json");
    private string LiveClaudeMd => Path.Combine(ClaudeHome, "CLAUDE.md");
    private string ClaudeJsonPath => Path.Combine(_sandbox, ".claude.json");

    private string ProfileDir(string name)
    {
        return Path.Combine(ProfilesDir, name);
    }

    private string ProfileSettings(string name)
    {
        return Path.Combine(ProfileDir(name), "settings.json");
    }

    private string ProfileMd(string name)
    {
        return Path.Combine(ProfileDir(name), "CLAUDE.md");
    }

    private string ProfileMcp(string name)
    {
        return Path.Combine(ProfileDir(name), "mcp.json");
    }

    private void CreateProfileWithSettings(string name, string json = "{}")
    {
        Directory.CreateDirectory(ProfileDir(name));
        File.WriteAllText(ProfileSettings(name), json);
    }

    // ── DiscoverProfiles ─────────────────────────────────────────────────────

    [TestMethod]
    public void DiscoverProfiles_NoDirExists_ReturnsEmpty()
    {
        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void DiscoverProfiles_ProfileWithoutSettingsJson_IsFilteredOut()
    {
        Directory.CreateDirectory(ProfileDir("empty-profile"));

        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void DiscoverProfiles_ValidProfile_IsReturned()
    {
        CreateProfileWithSettings("myprofile");

        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("myprofile", result[0].Name);
        Assert.IsTrue(result[0].HasSettings);
    }

    [TestMethod]
    public void DiscoverProfiles_MarksIsCliActive_ForCurrentProfile()
    {
        CreateProfileWithSettings("alpha");
        CreateProfileWithSettings("beta");
        Directory.CreateDirectory(ClaudeHome);
        File.WriteAllText(CurrentFile, "beta");

        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles();

        ProfileInfo alpha = result.Single(p => p.Name == "alpha");
        ProfileInfo beta = result.Single(p => p.Name == "beta");
        Assert.IsFalse(alpha.IsCliActive);
        Assert.IsTrue(beta.IsCliActive);
    }

    // ── ReadCurrentProfileName ───────────────────────────────────────────────

    [TestMethod]
    public void ReadCurrentProfileName_FileAbsent_ReturnsNull()
    {
        string? result = ProfileEngine.ReadCurrentProfileName();

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ReadCurrentProfileName_FilePresent_ReturnsName()
    {
        Directory.CreateDirectory(ClaudeHome);
        File.WriteAllText(CurrentFile, "work");

        string? result = ProfileEngine.ReadCurrentProfileName();

        Assert.AreEqual("work", result);
    }

    // ── WriteCurrentProfileName ──────────────────────────────────────────────

    [TestMethod]
    public void WriteCurrentProfileName_NullValue_DeletesFile()
    {
        Directory.CreateDirectory(ClaudeHome);
        File.WriteAllText(CurrentFile, "old");

        ProfileEngine.WriteCurrentProfileName(null);

        Assert.IsFalse(File.Exists(CurrentFile));
    }

    [TestMethod]
    public void WriteCurrentProfileName_StringValue_CreatesFileWithName()
    {
        ProfileEngine.WriteCurrentProfileName("foo");

        Assert.IsTrue(File.Exists(CurrentFile));
        Assert.AreEqual("foo", File.ReadAllText(CurrentFile).Trim());
    }

    // ── CreateFromLiveAsync ──────────────────────────────────────────────────

    [TestMethod]
    public async Task CreateFromLiveAsync_CopiesLiveSettingsIntoProfile()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"theme":"dark"}""");

        bool created = await ProfileEngine.CreateFromLiveAsync("snap");

        Assert.IsTrue(created);
        Assert.IsTrue(File.Exists(ProfileSettings("snap")));
        Assert.AreEqual("""{"theme":"dark"}""", await File.ReadAllTextAsync(ProfileSettings("snap")));
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_NoLiveSettings_WritesEmptyObject()
    {
        bool created = await ProfileEngine.CreateFromLiveAsync("blank");

        Assert.IsTrue(created);
        Assert.IsTrue(File.Exists(ProfileSettings("blank")));
        Assert.AreEqual("{}", (await File.ReadAllTextAsync(ProfileSettings("blank"))).Trim());
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_ProfileAlreadyExists_ReturnsFalse()
    {
        CreateProfileWithSettings("existing");

        bool created = await ProfileEngine.CreateFromLiveAsync("existing");

        Assert.IsFalse(created);
    }

    [TestMethod]
    public async Task CreateFromLiveAsync_ExtractsMcpServersFromClaudeJson()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        await File.WriteAllTextAsync(ClaudeJsonPath, """{"mcpServers":{"myserver":{"command":"npx"}}}""");

        await ProfileEngine.CreateFromLiveAsync("withMcp");

        Assert.IsTrue(File.Exists(ProfileMcp("withMcp")));
        JsonObject? mcp = JsonNode.Parse(await File.ReadAllTextAsync(ProfileMcp("withMcp"))) as JsonObject;
        Assert.IsNotNull(mcp);
        Assert.IsTrue(mcp.ContainsKey("myserver"));
    }

    // ── ApplyProfileToLiveAsync ──────────────────────────────────────────────

    [TestMethod]
    public async Task ApplyProfileToLiveAsync_CopiesSettingsToLiveAndUpdatesCurrentFile()
    {
        CreateProfileWithSettings("prod", """{"env":"prod"}""");

        await ProfileEngine.ApplyProfileToLiveAsync("prod", autoSync: false);

        Assert.IsTrue(File.Exists(LiveSettings));
        Assert.AreEqual("""{"env":"prod"}""", await File.ReadAllTextAsync(LiveSettings));
        Assert.AreEqual("prod", (await File.ReadAllTextAsync(CurrentFile)).Trim());
    }

    [TestMethod]
    public async Task ApplyProfileToLiveAsync_NoProfileClaudeMd_DeletesLiveClaudeMd()
    {
        CreateProfileWithSettings("minimal");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveClaudeMd, "# old instructions");

        await ProfileEngine.ApplyProfileToLiveAsync("minimal", autoSync: false);

        Assert.IsFalse(File.Exists(LiveClaudeMd));
    }

    // ── SyncFromLiveAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task SyncFromLiveAsync_CopiesLiveSettingsBackIntoProfile_RoundTrip()
    {
        // Create a profile, apply it, then externally modify live settings, then sync.
        CreateProfileWithSettings("dev", """{"theme":"light"}""");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"theme":"dark","newKey":true}""");

        await ProfileEngine.SyncFromLiveAsync("dev");

        string synced = await File.ReadAllTextAsync(ProfileSettings("dev"));
        StringAssert.Contains(synced, "dark");
        StringAssert.Contains(synced, "newKey");
    }
}