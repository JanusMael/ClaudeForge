using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

[TestClass]
public sealed class PlatformPathsProfileTests
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
    // DiscoverDesktopProfiles
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DiscoverDesktopProfiles_NoDirExists_ReturnsEmpty()
    {
        // DesktopProfilesDirectory does not exist in the fresh sandbox
        Assert.IsFalse(Directory.Exists(PlatformPaths.DesktopProfilesDirectory));

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.AreEqual(0, profiles.Count);
    }

    [TestMethod]
    public void DiscoverDesktopProfiles_EmptyDir_ReturnsEmpty()
    {
        Directory.CreateDirectory(PlatformPaths.DesktopProfilesDirectory);

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.AreEqual(0, profiles.Count);
    }

    [TestMethod]
    public void DiscoverDesktopProfiles_SingleProfile_ReturnsProfileName()
    {
        string profileDir = Path.Combine(PlatformPaths.DesktopProfilesDirectory, "work");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "claude_desktop_config.json"), "{}");

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual("work", profiles[0]);
    }

    [TestMethod]
    public void DiscoverDesktopProfiles_MultipleProfiles_ReturnsSortedNames()
    {
        // Create in reverse alphabetical order to confirm sorting
        foreach (string name in new[] { "zzz", "aaa", "mmm" })
        {
            Directory.CreateDirectory(Path.Combine(PlatformPaths.DesktopProfilesDirectory, name));
        }

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.AreEqual(3, profiles.Count);
        Assert.AreEqual("aaa", profiles[0]);
        Assert.AreEqual("mmm", profiles[1]);
        Assert.AreEqual("zzz", profiles[2]);
    }

    [TestMethod]
    public void DiscoverDesktopProfiles_ProfileDirWithNoConfigFile_StillReturnsName()
    {
        // The method enumerates subdirectory names; it does not require the config file to exist.
        Directory.CreateDirectory(Path.Combine(PlatformPaths.DesktopProfilesDirectory, "empty"));

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual("empty", profiles[0]);
    }
}