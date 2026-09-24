using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.FileIO;

public class ConfigFileDiscovererTests : IDisposable
{
    private string _sandbox = null!;

    /// <summary>
    /// A scratch stand-in for the per-OS system policy directory.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>These tests used to write to <c>PlatformPaths.ManagedSettingsPath</c> directly, and
    /// that stopped being safe the moment managed policy moved where Claude Code actually reads
    /// it.</b> That path is now <c>C:\Program Files\ClaudeCode\</c> or <c>/etc/claude-code/</c> —
    /// so the old line either throws <see cref="UnauthorizedAccessException"/>, or, on an elevated
    /// run, <b>writes a policy file into the machine's real system directory</b>. The injected
    /// root exists so the check stays runnable without either outcome.
    /// </remarks>
    private string _managedRoot = null!;

    public ConfigFileDiscovererTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        // Deliberately OUTSIDE the fake home: managed policy is a system location, and putting it
        // under the home here would let a regression that reads the home go on passing.
        _managedRoot = Path.Combine(_sandbox, "..", "policy-" + Guid.NewGuid().ToString("N"));
        _managedRoot = Path.GetFullPath(_managedRoot);
        Directory.CreateDirectory(_managedRoot);
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
        if (Directory.Exists(_managedRoot))
        {
            Directory.Delete(_managedRoot, recursive: true);
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
    }

    // -----------------------------------------------------------------------
    // DiscoverClaudeCodeSettings
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscoverClaudeCodeSettings_NoProjectRoot_NoManagedFile_ReturnsOneUserEntry()
    {
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty);

        Assert.Single(files);
        Assert.Equal(ConfigScope.User, files[0].Scope);
        Assert.Equal(ConfigFileType.ClaudeCodeSettings, files[0].FileType);
        Assert.Equal(PlatformPaths.UserSettingsPath(ClaudeEnvironment.Empty), files[0].FilePath);
        Assert.False(files[0].IsReadOnly);
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_NoProjectRoot_WithManagedFile_ReturnsTwoEntries()
    {
        Touch(Path.Combine(_managedRoot, "managed-settings.json"));

        IReadOnlyList<DiscoveredFile> files =
            ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, managedRoot: _managedRoot);

        Assert.Equal(2, files.Count);
        Assert.Equal(ConfigScope.Managed, files[0].Scope);
        Assert.True(files[0].IsReadOnly);
        Assert.Equal(ConfigScope.User, files[1].Scope);
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_WithProjectRoot_ReturnsThreeEntries()
    {
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, projectRoot: projectRoot);

        Assert.Equal(3, files.Count);
        Assert.Equal(ConfigScope.User, files[0].Scope);
        Assert.Equal(ConfigScope.Project, files[1].Scope);
        Assert.Equal(ConfigScope.Local, files[2].Scope);
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_WithProjectRoot_AndManagedFile_ReturnsFourEntries()
    {
        Touch(Path.Combine(_managedRoot, "managed-settings.json"));
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, 
            projectRoot: projectRoot, managedRoot: _managedRoot);

        Assert.Equal(4, files.Count);
        Assert.Equal(ConfigScope.Managed, files[0].Scope);
        Assert.Equal(ConfigScope.User, files[1].Scope);
        Assert.Equal(ConfigScope.Project, files[2].Scope);
        Assert.Equal(ConfigScope.Local, files[3].Scope);
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_ProjectScopePaths_MatchPlatformPaths()
    {
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, projectRoot: projectRoot);

        DiscoveredFile project = files.Single(f => f.Scope == ConfigScope.Project);
        DiscoveredFile local = files.Single(f => f.Scope == ConfigScope.Local);
        Assert.Equal(PlatformPaths.ProjectSettingsPath(projectRoot), project.FilePath);
        Assert.Equal(PlatformPaths.LocalSettingsPath(projectRoot), local.FilePath);
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_WithProfileName_UserPathIsProfilePath()
    {
        const string profile = "work";

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, profileName: profile);

        Assert.Single(files);
        Assert.Equal(ConfigScope.User, files[0].Scope);
        Assert.Equal(PlatformPaths.ProfileSettingsPath(ClaudeEnvironment.Empty, profile), files[0].FilePath);
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_DropInDir_AddsOneManagedEntryPerJsonFile()
    {
        string dropDir = Path.Combine(_managedRoot, "managed-settings.d");
        Directory.CreateDirectory(dropDir);
        Touch(Path.Combine(dropDir, "a-policy.json"));
        Touch(Path.Combine(dropDir, "b-policy.json"));

        IReadOnlyList<DiscoveredFile> files =
            ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, managedRoot: _managedRoot);

        List<DiscoveredFile> managed = files.Where(f => f.Scope == ConfigScope.Managed).ToList();
        Assert.Equal(2, managed.Count);
        Assert.True(managed.All(f => f.IsReadOnly));
        Assert.True(managed.All(f => f.FileType == ConfigFileType.ClaudeCodeSettings));
        // Sorted by name
        Assert.EndsWith("a-policy.json", managed[0].FilePath);
        Assert.EndsWith("b-policy.json", managed[1].FilePath);
    }

    /// <summary>
    /// ⛔⛔ <b>The negative, and it is the half that a new-location-only test passes without.</b>
    /// Adding the system directory while still reading <c>~/.claude/managed-settings.json</c>
    /// would leave the confidently-wrong display in place for exactly the users who already have
    /// such a file — it shows as enforced policy and does nothing.
    /// </summary>
    [Fact]
    public void AManagedFileInTheOldHomeLocation_IsNotDiscovered()
    {
        string legacy = Path.Combine(_sandbox, ".claude", "managed-settings.json");
        Touch(legacy);

        // Assert the premise, or the claim below is about a file that was never written.
        Assert.True(File.Exists(legacy), "Precondition: the legacy file must exist.");

        IReadOnlyList<DiscoveredFile> files =
            ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, managedRoot: _managedRoot);

        Assert.False(
            files.Any(f => f.Scope == ConfigScope.Managed),
            "A managed-settings.json under the user home was discovered as policy. Claude Code " +
            "never reads that location, so showing it as enforced is the original defect.");
    }

    /// <summary>
    /// The drop-in directory has the same failure mode as the file beside it, and a sweep that
    /// fixed only the file would leave this one reading the home.
    /// </summary>
    [Fact]
    public void ADropInDirectoryInTheOldHomeLocation_IsNotDiscovered()
    {
        string legacyDropIn = Path.Combine(_sandbox, ".claude", "managed-settings.d");
        Directory.CreateDirectory(legacyDropIn);
        Touch(Path.Combine(legacyDropIn, "policy.json"));

        IReadOnlyList<DiscoveredFile> files =
            ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty, managedRoot: _managedRoot);

        Assert.False(
            files.Any(f => f.Scope == ConfigScope.Managed),
            "A managed-settings.d under the user home was discovered as policy.");
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_ExistingFile_ExistsIsTrue()
    {
        Touch(PlatformPaths.UserSettingsPath(ClaudeEnvironment.Empty));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty);

        DiscoveredFile user = files.Single(f => f.Scope == ConfigScope.User);
        Assert.True(user.Exists);
    }

    [Fact]
    public void DiscoverClaudeCodeSettings_MissingFile_ExistsIsFalse()
    {
        // user settings file is not created
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(ClaudeEnvironment.Empty);

        DiscoveredFile user = files.Single(f => f.Scope == ConfigScope.User);
        Assert.False(user.Exists);
    }

    // -----------------------------------------------------------------------
    // DiscoverDesktopConfig
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscoverDesktopConfig_ReturnsSingleUserScopeEntry()
    {
        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig();

        Assert.Equal(ConfigScope.User, file.Scope);
        Assert.Equal(ConfigFileType.ClaudeDesktopConfig, file.FileType);
        Assert.Equal(PlatformPaths.DesktopConfigPath, file.FilePath);
        Assert.False(file.IsReadOnly);
    }

    [Fact]
    public void DiscoverDesktopConfig_WhenFileExists_ExistsIsTrue()
    {
        Touch(PlatformPaths.DesktopConfigPath);

        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig();

        Assert.True(file.Exists);
    }

    [Fact]
    public void DiscoverDesktopConfig_WithProfileName_ReturnsProfileSpecificPath()
    {
        const string profile = "work";

        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig(profileName: profile);

        Assert.Equal(ConfigScope.User, file.Scope);
        Assert.Equal(ConfigFileType.ClaudeDesktopConfig, file.FileType);
        Assert.Equal(PlatformPaths.DesktopProfileConfigPath(profile), file.FilePath);
        Assert.False(file.IsReadOnly);
    }

    [Fact]
    public void DiscoverDesktopConfig_WithProfileName_WhenFileExists_ExistsIsTrue()
    {
        const string profile = "work";
        Touch(PlatformPaths.DesktopProfileConfigPath(profile));

        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig(profileName: profile);

        Assert.True(file.Exists);
    }

    [Fact]
    public void DiscoverDesktopConfig_WithNullProfileName_ReturnsLivePath()
    {
        // Explicitly passing null should behave identically to the parameterless call.
        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig(profileName: null);

        Assert.Equal(PlatformPaths.DesktopConfigPath, file.FilePath);
    }

    // -----------------------------------------------------------------------
    // DiscoverMcpFiles
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscoverMcpFiles_NoProjectRoot_ReturnsOneUserEntry()
    {
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles(ClaudeEnvironment.Empty);

        Assert.Single(files);
        Assert.Equal(ConfigScope.User, files[0].Scope);
        Assert.Equal(ConfigFileType.McpJson, files[0].FileType);
        Assert.Equal(PlatformPaths.UserMcpPath(ClaudeEnvironment.Empty), files[0].FilePath);
        Assert.False(files[0].IsReadOnly);
    }

    [Fact]
    public void DiscoverMcpFiles_WithProjectRoot_ReturnsTwoEntries()
    {
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles(ClaudeEnvironment.Empty, projectRoot: projectRoot);

        Assert.Equal(2, files.Count);
        Assert.Equal(ConfigScope.User, files[0].Scope);
        Assert.Equal(ConfigScope.Project, files[1].Scope);
        Assert.Equal(PlatformPaths.ProjectMcpPath(projectRoot), files[1].FilePath);
    }

    [Fact]
    public void DiscoverMcpFiles_WithProfileName_UserPathIsProfileMcpPath()
    {
        const string profile = "work";

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles(ClaudeEnvironment.Empty, profileName: profile);

        Assert.Single(files);
        Assert.Equal(PlatformPaths.ProfileMcpPath(ClaudeEnvironment.Empty, profile), files[0].FilePath);
    }

    [Fact]
    public void DiscoverMcpFiles_ExistingFile_ExistsIsTrue()
    {
        Touch(PlatformPaths.UserMcpPath(ClaudeEnvironment.Empty));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles(ClaudeEnvironment.Empty);

        Assert.True(files[0].Exists);
    }

    [Fact]
    public void DiscoverMcpFiles_MissingFile_ExistsIsFalse()
    {
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles(ClaudeEnvironment.Empty);

        Assert.False(files[0].Exists);
    }

    // -----------------------------------------------------------------------
    // DiscoverProfiles
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscoverProfiles_NoProfilesDir_ReturnsEmptyList()
    {
        // ProfilesDirectory does not exist in the fresh sandbox
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles(ClaudeEnvironment.Empty);

        Assert.Empty(files);
    }

    [Fact]
    public void DiscoverProfiles_EmptyProfilesDir_ReturnsEmptyList()
    {
        Directory.CreateDirectory(PlatformPaths.ProfilesDirectory(ClaudeEnvironment.Empty));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles(ClaudeEnvironment.Empty);

        Assert.Empty(files);
    }

    [Fact]
    public void DiscoverProfiles_SingleProfile_ReturnsTwoEntries()
    {
        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory(ClaudeEnvironment.Empty), "work");
        Touch(Path.Combine(profileDir, "settings.json"));
        Touch(Path.Combine(profileDir, "mcp.json"));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles(ClaudeEnvironment.Empty);

        Assert.Equal(2, files.Count);
        DiscoveredFile settings = files.Single(f => f.FileType == ConfigFileType.ProfileSettings);
        DiscoveredFile mcp = files.Single(f => f.FileType == ConfigFileType.ProfileMcp);
        Assert.Equal("work", settings.ProfileName);
        Assert.Equal("work", mcp.ProfileName);
        Assert.Equal(ConfigScope.User, settings.Scope);
        Assert.Equal(ConfigScope.User, mcp.Scope);
        Assert.True(settings.Exists);
        Assert.True(mcp.Exists);
    }

    [Fact]
    public void DiscoverProfiles_ProfileWithMissingFiles_ExistsIsFalse()
    {
        // Create the profile directory but leave files absent
        Directory.CreateDirectory(Path.Combine(PlatformPaths.ProfilesDirectory(ClaudeEnvironment.Empty), "empty-profile"));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles(ClaudeEnvironment.Empty);

        Assert.Equal(2, files.Count);
        Assert.True(files.All(f => !f.Exists));
    }
}