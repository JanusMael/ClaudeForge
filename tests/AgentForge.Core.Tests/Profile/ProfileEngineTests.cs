using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Profile;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Profile;

public sealed class ProfileEngineTests : IDisposable
{
    private string _sandbox = string.Empty;

    public ProfileEngineTests() => Setup();

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
            /* best effort */
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
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

    [Fact]
    public void DiscoverProfiles_NoDirExists_ReturnsEmpty()
    {
        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles(ClaudeEnvironment.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void DiscoverProfiles_ProfileWithoutSettingsJson_IsFilteredOut()
    {
        Directory.CreateDirectory(ProfileDir("empty-profile"));

        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles(ClaudeEnvironment.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void DiscoverProfiles_ValidProfile_IsReturned()
    {
        CreateProfileWithSettings("myprofile");

        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles(ClaudeEnvironment.Empty);

        Assert.Single(result);
        Assert.Equal("myprofile", result[0].Name);
        Assert.True(result[0].HasSettings);
    }

    [Fact]
    public void DiscoverProfiles_MarksIsCliActive_ForCurrentProfile()
    {
        CreateProfileWithSettings("alpha");
        CreateProfileWithSettings("beta");
        Directory.CreateDirectory(ClaudeHome);
        File.WriteAllText(CurrentFile, "beta");

        IReadOnlyList<ProfileInfo> result = ProfileEngine.DiscoverProfiles(ClaudeEnvironment.Empty);

        ProfileInfo alpha = result.Single(p => p.Name == "alpha");
        ProfileInfo beta = result.Single(p => p.Name == "beta");
        Assert.False(alpha.IsCliActive);
        Assert.True(beta.IsCliActive);
    }

    // ── ReadCurrentProfileName ───────────────────────────────────────────────

    [Fact]
    public void ReadCurrentProfileName_FileAbsent_ReturnsNull()
    {
        string? result = ProfileEngine.ReadCurrentProfileName(ClaudeEnvironment.Empty);

        Assert.Null(result);
    }

    [Fact]
    public void ReadCurrentProfileName_FilePresent_ReturnsName()
    {
        Directory.CreateDirectory(ClaudeHome);
        File.WriteAllText(CurrentFile, "work");

        string? result = ProfileEngine.ReadCurrentProfileName(ClaudeEnvironment.Empty);

        Assert.Equal("work", result);
    }

    // ── WriteCurrentProfileName ──────────────────────────────────────────────

    [Fact]
    public void WriteCurrentProfileName_NullValue_DeletesFile()
    {
        Directory.CreateDirectory(ClaudeHome);
        File.WriteAllText(CurrentFile, "old");

        ProfileEngine.WriteCurrentProfileName(ClaudeEnvironment.Empty, null);

        Assert.False(File.Exists(CurrentFile));
    }

    [Fact]
    public void WriteCurrentProfileName_StringValue_CreatesFileWithName()
    {
        ProfileEngine.WriteCurrentProfileName(ClaudeEnvironment.Empty, "foo");

        Assert.True(File.Exists(CurrentFile));
        Assert.Equal("foo", File.ReadAllText(CurrentFile).Trim());
    }

    // ── CreateFromLiveAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateFromLiveAsync_CopiesLiveSettingsIntoProfile()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"theme":"dark"}""");

        bool created = await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "snap");

        Assert.True(created);
        Assert.True(File.Exists(ProfileSettings("snap")));
        Assert.Equal("""{"theme":"dark"}""", await File.ReadAllTextAsync(ProfileSettings("snap")));
    }

    [Fact]
    public async Task CreateFromLiveAsync_NoLiveSettings_WritesEmptyObject()
    {
        bool created = await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "blank");

        Assert.True(created);
        Assert.True(File.Exists(ProfileSettings("blank")));
        Assert.Equal("{}", (await File.ReadAllTextAsync(ProfileSettings("blank"))).Trim());
    }

    [Fact]
    public async Task CreateFromLiveAsync_ProfileAlreadyExists_ReturnsFalse()
    {
        CreateProfileWithSettings("existing");

        bool created = await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "existing");

        Assert.False(created);
    }

    [Fact]
    public async Task CreateFromLiveAsync_ExtractsMcpServersFromClaudeJson()
    {
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, "{}");
        await File.WriteAllTextAsync(ClaudeJsonPath, """{"mcpServers":{"myserver":{"command":"npx"}}}""");

        await ProfileEngine.CreateFromLiveAsync(ClaudeEnvironment.Empty, "withMcp");

        Assert.True(File.Exists(ProfileMcp("withMcp")));
        JsonObject? mcp = JsonNode.Parse(await File.ReadAllTextAsync(ProfileMcp("withMcp"))) as JsonObject;
        Assert.NotNull(mcp);
        Assert.True(mcp.ContainsKey("myserver"));
    }

    // ── ApplyProfileToLiveAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ApplyProfileToLiveAsync_CopiesSettingsToLiveAndUpdatesCurrentFile()
    {
        CreateProfileWithSettings("prod", """{"env":"prod"}""");

        await ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "prod", autoSync: false);

        Assert.True(File.Exists(LiveSettings));
        Assert.Equal("""{"env":"prod"}""", await File.ReadAllTextAsync(LiveSettings));
        Assert.Equal("prod", (await File.ReadAllTextAsync(CurrentFile)).Trim());
    }

    [Fact]
    public async Task ApplyProfileToLiveAsync_NoProfileClaudeMd_DeletesLiveClaudeMd()
    {
        CreateProfileWithSettings("minimal");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveClaudeMd, "# old instructions");

        await ProfileEngine.ApplyProfileToLiveAsync(ClaudeEnvironment.Empty, "minimal", autoSync: false);

        Assert.False(File.Exists(LiveClaudeMd));
    }

    // ── SyncFromLiveAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SyncFromLiveAsync_CopiesLiveSettingsBackIntoProfile_RoundTrip()
    {
        // Create a profile, apply it, then externally modify live settings, then sync.
        CreateProfileWithSettings("dev", """{"theme":"light"}""");
        Directory.CreateDirectory(ClaudeHome);
        await File.WriteAllTextAsync(LiveSettings, """{"theme":"dark","newKey":true}""");

        await ProfileEngine.SyncFromLiveAsync(ClaudeEnvironment.Empty, "dev");

        string synced = await File.ReadAllTextAsync(ProfileSettings("dev"));
        OrdinalAssert.Contains("dark", synced);
        OrdinalAssert.Contains("newKey", synced);
    }
}