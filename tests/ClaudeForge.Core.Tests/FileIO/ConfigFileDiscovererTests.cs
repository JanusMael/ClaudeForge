using Bennewitz.Ninja.ClaudeForge.Core.FileIO;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.FileIO;

[TestClass]
public class ConfigFileDiscovererTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
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

    [TestMethod]
    public void DiscoverClaudeCodeSettings_NoProjectRoot_NoManagedFile_ReturnsOneUserEntry()
    {
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings();

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(ConfigScope.User, files[0].Scope);
        Assert.AreEqual(ConfigFileType.ClaudeCodeSettings, files[0].FileType);
        Assert.AreEqual(PlatformPaths.UserSettingsPath, files[0].FilePath);
        Assert.IsFalse(files[0].IsReadOnly);
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_NoProjectRoot_WithManagedFile_ReturnsTwoEntries()
    {
        Touch(PlatformPaths.ManagedSettingsPath);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings();

        Assert.AreEqual(2, files.Count);
        Assert.AreEqual(ConfigScope.Managed, files[0].Scope);
        Assert.IsTrue(files[0].IsReadOnly);
        Assert.AreEqual(ConfigScope.User, files[1].Scope);
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_WithProjectRoot_ReturnsThreeEntries()
    {
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(projectRoot: projectRoot);

        Assert.AreEqual(3, files.Count);
        Assert.AreEqual(ConfigScope.User, files[0].Scope);
        Assert.AreEqual(ConfigScope.Project, files[1].Scope);
        Assert.AreEqual(ConfigScope.Local, files[2].Scope);
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_WithProjectRoot_AndManagedFile_ReturnsFourEntries()
    {
        Touch(PlatformPaths.ManagedSettingsPath);
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(projectRoot: projectRoot);

        Assert.AreEqual(4, files.Count);
        Assert.AreEqual(ConfigScope.Managed, files[0].Scope);
        Assert.AreEqual(ConfigScope.User, files[1].Scope);
        Assert.AreEqual(ConfigScope.Project, files[2].Scope);
        Assert.AreEqual(ConfigScope.Local, files[3].Scope);
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_ProjectScopePaths_MatchPlatformPaths()
    {
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(projectRoot: projectRoot);

        DiscoveredFile project = files.Single(f => f.Scope == ConfigScope.Project);
        DiscoveredFile local = files.Single(f => f.Scope == ConfigScope.Local);
        Assert.AreEqual(PlatformPaths.ProjectSettingsPath(projectRoot), project.FilePath);
        Assert.AreEqual(PlatformPaths.LocalSettingsPath(projectRoot), local.FilePath);
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_WithProfileName_UserPathIsProfilePath()
    {
        const string profile = "work";

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(profileName: profile);

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(ConfigScope.User, files[0].Scope);
        Assert.AreEqual(PlatformPaths.ProfileSettingsPath(profile), files[0].FilePath);
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_DropInDir_AddsOneManagedEntryPerJsonFile()
    {
        string dropDir = PlatformPaths.ManagedSettingsDropInDir;
        Directory.CreateDirectory(dropDir);
        Touch(Path.Combine(dropDir, "a-policy.json"));
        Touch(Path.Combine(dropDir, "b-policy.json"));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings();

        List<DiscoveredFile> managed = files.Where(f => f.Scope == ConfigScope.Managed).ToList();
        Assert.AreEqual(2, managed.Count);
        Assert.IsTrue(managed.All(f => f.IsReadOnly));
        Assert.IsTrue(managed.All(f => f.FileType == ConfigFileType.ClaudeCodeSettings));
        // Sorted by name
        Assert.IsTrue(managed[0].FilePath.EndsWith("a-policy.json"));
        Assert.IsTrue(managed[1].FilePath.EndsWith("b-policy.json"));
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_ExistingFile_ExistsIsTrue()
    {
        Touch(PlatformPaths.UserSettingsPath);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings();

        DiscoveredFile user = files.Single(f => f.Scope == ConfigScope.User);
        Assert.IsTrue(user.Exists);
    }

    [TestMethod]
    public void DiscoverClaudeCodeSettings_MissingFile_ExistsIsFalse()
    {
        // user settings file is not created
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverClaudeCodeSettings();

        DiscoveredFile user = files.Single(f => f.Scope == ConfigScope.User);
        Assert.IsFalse(user.Exists);
    }

    // -----------------------------------------------------------------------
    // DiscoverDesktopConfig
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DiscoverDesktopConfig_ReturnsSingleUserScopeEntry()
    {
        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig();

        Assert.AreEqual(ConfigScope.User, file.Scope);
        Assert.AreEqual(ConfigFileType.ClaudeDesktopConfig, file.FileType);
        Assert.AreEqual(PlatformPaths.DesktopConfigPath, file.FilePath);
        Assert.IsFalse(file.IsReadOnly);
    }

    [TestMethod]
    public void DiscoverDesktopConfig_WhenFileExists_ExistsIsTrue()
    {
        Touch(PlatformPaths.DesktopConfigPath);

        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig();

        Assert.IsTrue(file.Exists);
    }

    [TestMethod]
    public void DiscoverDesktopConfig_WithProfileName_ReturnsProfileSpecificPath()
    {
        const string profile = "work";

        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig(profileName: profile);

        Assert.AreEqual(ConfigScope.User, file.Scope);
        Assert.AreEqual(ConfigFileType.ClaudeDesktopConfig, file.FileType);
        Assert.AreEqual(PlatformPaths.DesktopProfileConfigPath(profile), file.FilePath);
        Assert.IsFalse(file.IsReadOnly);
    }

    [TestMethod]
    public void DiscoverDesktopConfig_WithProfileName_WhenFileExists_ExistsIsTrue()
    {
        const string profile = "work";
        Touch(PlatformPaths.DesktopProfileConfigPath(profile));

        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig(profileName: profile);

        Assert.IsTrue(file.Exists);
    }

    [TestMethod]
    public void DiscoverDesktopConfig_WithNullProfileName_ReturnsLivePath()
    {
        // Explicitly passing null should behave identically to the parameterless call.
        DiscoveredFile file = ConfigFileDiscoverer.DiscoverDesktopConfig(profileName: null);

        Assert.AreEqual(PlatformPaths.DesktopConfigPath, file.FilePath);
    }

    // -----------------------------------------------------------------------
    // DiscoverMcpFiles
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DiscoverMcpFiles_NoProjectRoot_ReturnsOneUserEntry()
    {
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles();

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(ConfigScope.User, files[0].Scope);
        Assert.AreEqual(ConfigFileType.McpJson, files[0].FileType);
        Assert.AreEqual(PlatformPaths.UserMcpPath, files[0].FilePath);
        Assert.IsFalse(files[0].IsReadOnly);
    }

    [TestMethod]
    public void DiscoverMcpFiles_WithProjectRoot_ReturnsTwoEntries()
    {
        string projectRoot = Path.Combine(_sandbox, "myproject");
        Directory.CreateDirectory(projectRoot);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles(projectRoot: projectRoot);

        Assert.AreEqual(2, files.Count);
        Assert.AreEqual(ConfigScope.User, files[0].Scope);
        Assert.AreEqual(ConfigScope.Project, files[1].Scope);
        Assert.AreEqual(PlatformPaths.ProjectMcpPath(projectRoot), files[1].FilePath);
    }

    [TestMethod]
    public void DiscoverMcpFiles_WithProfileName_UserPathIsProfileMcpPath()
    {
        const string profile = "work";

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles(profileName: profile);

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(PlatformPaths.ProfileMcpPath(profile), files[0].FilePath);
    }

    [TestMethod]
    public void DiscoverMcpFiles_ExistingFile_ExistsIsTrue()
    {
        Touch(PlatformPaths.UserMcpPath);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles();

        Assert.IsTrue(files[0].Exists);
    }

    [TestMethod]
    public void DiscoverMcpFiles_MissingFile_ExistsIsFalse()
    {
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverMcpFiles();

        Assert.IsFalse(files[0].Exists);
    }

    // -----------------------------------------------------------------------
    // DiscoverProfiles
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DiscoverProfiles_NoProfilesDir_ReturnsEmptyList()
    {
        // ProfilesDirectory does not exist in the fresh sandbox
        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles();

        Assert.AreEqual(0, files.Count);
    }

    [TestMethod]
    public void DiscoverProfiles_EmptyProfilesDir_ReturnsEmptyList()
    {
        Directory.CreateDirectory(PlatformPaths.ProfilesDirectory);

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles();

        Assert.AreEqual(0, files.Count);
    }

    [TestMethod]
    public void DiscoverProfiles_SingleProfile_ReturnsTwoEntries()
    {
        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, "work");
        Touch(Path.Combine(profileDir, "settings.json"));
        Touch(Path.Combine(profileDir, "mcp.json"));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles();

        Assert.AreEqual(2, files.Count);
        DiscoveredFile settings = files.Single(f => f.FileType == ConfigFileType.ProfileSettings);
        DiscoveredFile mcp = files.Single(f => f.FileType == ConfigFileType.ProfileMcp);
        Assert.AreEqual("work", settings.ProfileName);
        Assert.AreEqual("work", mcp.ProfileName);
        Assert.AreEqual(ConfigScope.User, settings.Scope);
        Assert.AreEqual(ConfigScope.User, mcp.Scope);
        Assert.IsTrue(settings.Exists);
        Assert.IsTrue(mcp.Exists);
    }

    [TestMethod]
    public void DiscoverProfiles_ProfileWithMissingFiles_ExistsIsFalse()
    {
        // Create the profile directory but leave files absent
        Directory.CreateDirectory(Path.Combine(PlatformPaths.ProfilesDirectory, "empty-profile"));

        IReadOnlyList<DiscoveredFile> files = ConfigFileDiscoverer.DiscoverProfiles();

        Assert.AreEqual(2, files.Count);
        Assert.IsTrue(files.All(f => !f.Exists));
    }
}