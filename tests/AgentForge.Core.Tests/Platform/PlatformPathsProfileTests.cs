using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Platform;

public sealed class PlatformPathsProfileTests : IDisposable
{
    private string _sandbox = null!;

    public PlatformPathsProfileTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // DiscoverDesktopProfiles
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscoverDesktopProfiles_NoDirExists_ReturnsEmpty()
    {
        // DesktopProfilesDirectory does not exist in the fresh sandbox
        Assert.False(Directory.Exists(PlatformPaths.DesktopProfilesDirectory));

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.Empty(profiles);
    }

    [Fact]
    public void DiscoverDesktopProfiles_EmptyDir_ReturnsEmpty()
    {
        Directory.CreateDirectory(PlatformPaths.DesktopProfilesDirectory);

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.Empty(profiles);
    }

    [Fact]
    public void DiscoverDesktopProfiles_SingleProfile_ReturnsProfileName()
    {
        string profileDir = Path.Combine(PlatformPaths.DesktopProfilesDirectory, "work");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "claude_desktop_config.json"), "{}");

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.Single(profiles);
        Assert.Equal("work", profiles[0]);
    }

    [Fact]
    public void DiscoverDesktopProfiles_MultipleProfiles_ReturnsSortedNames()
    {
        // Create in reverse alphabetical order to confirm sorting
        foreach (string name in new[] { "zzz", "aaa", "mmm" })
        {
            Directory.CreateDirectory(Path.Combine(PlatformPaths.DesktopProfilesDirectory, name));
        }

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.Equal(3, profiles.Count);
        Assert.Equal("aaa", profiles[0]);
        Assert.Equal("mmm", profiles[1]);
        Assert.Equal("zzz", profiles[2]);
    }

    [Fact]
    public void DiscoverDesktopProfiles_ProfileDirWithNoConfigFile_StillReturnsName()
    {
        // The method enumerates subdirectory names; it does not require the config file to exist.
        Directory.CreateDirectory(Path.Combine(PlatformPaths.DesktopProfilesDirectory, "empty"));

        IReadOnlyList<string> profiles = PlatformPaths.DiscoverDesktopProfiles();

        Assert.Single(profiles);
        Assert.Equal("empty", profiles[0]);
    }
}