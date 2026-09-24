using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Platform;

/// <summary>
/// Tests for the platform-emulation abstraction
/// (<see cref="IPlatformInfo"/> / <see cref="PlatformInfo"/> /
/// <see cref="EmulatedPlatformInfo"/>) used by the <c>--windows</c> /
/// <c>--macos</c> / <c>--linux</c> debug flags.
/// </summary>
// PlatformInfo.Current is a PROCESS-WIDE static (set via OverrideForDebug, which
// production debug flags also use — so it is deliberately not AsyncLocal). This
// class mutates it by design, so it must not run concurrently with anything that
// reads PlatformInfo.Current. DoNotParallelize runs it serially, isolated from the
// method-level-parallelized rest of the assembly.
[Collection("DoNotParallelize")]
public sealed class PlatformInfoTests : IDisposable
{
    private void Cleanup()
    {
        PlatformInfo.ResetForTesting();
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // EmulatedPlatformInfo: each id maps to the right flag tuple
    // -----------------------------------------------------------------------

    [Fact]
    public void Emulated_Windows_HasWindowsFlagOnly()
    {
        EmulatedPlatformInfo info = EmulatedPlatformInfo.ForId("windows");
        Assert.True(info.IsWindows);
        Assert.False(info.IsMacOS);
        Assert.False(info.IsLinux);
        Assert.Equal("windows", info.PlatformId);
        Assert.Equal("Windows", info.DisplayName);
        Assert.Equal(';', info.PathListSeparator);
        Assert.Equal(StringComparison.OrdinalIgnoreCase, info.PathComparison);
    }

    [Fact]
    public void Emulated_MacOS_HasMacOSFlagOnly()
    {
        EmulatedPlatformInfo info = EmulatedPlatformInfo.ForId("macos");
        Assert.False(info.IsWindows);
        Assert.True(info.IsMacOS);
        Assert.False(info.IsLinux);
        Assert.Equal("macos", info.PlatformId);
        Assert.Equal("macOS", info.DisplayName);
        Assert.Equal(':', info.PathListSeparator);
        Assert.Equal(StringComparison.Ordinal, info.PathComparison);
    }

    [Fact]
    public void Emulated_Linux_HasLinuxFlagOnly()
    {
        EmulatedPlatformInfo info = EmulatedPlatformInfo.ForId("linux");
        Assert.False(info.IsWindows);
        Assert.False(info.IsMacOS);
        Assert.True(info.IsLinux);
        Assert.Equal("linux", info.PlatformId);
        Assert.Equal("Linux", info.DisplayName);
        Assert.Equal(':', info.PathListSeparator);
        Assert.Equal(StringComparison.Ordinal, info.PathComparison);
    }

    [Fact]
    public void Emulated_UnknownId_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EmulatedPlatformInfo.ForId("freebsd"));
    }

    // -----------------------------------------------------------------------
    // PlatformInfo.Current override + reset
    // -----------------------------------------------------------------------

    [Fact]
    public void Current_DefaultsToRuntimePlatformInfo()
    {
        // Cleanup runs after every test, so the static state is always fresh here.
        Assert.Same(RuntimePlatformInfo.Instance, PlatformInfo.Current);
    }

    [Fact]
    public void OverrideForDebug_ReplacesCurrent()
    {
        EmulatedPlatformInfo emulated = EmulatedPlatformInfo.ForId("linux");
        PlatformInfo.OverrideForDebug(emulated);

        Assert.Same(emulated, PlatformInfo.Current);
        Assert.True(PlatformInfo.Current.IsLinux);
        Assert.Equal("linux", PlatformInfo.Current.PlatformId);
    }

    [Fact]
    public void ResetForTesting_RestoresRuntimeInstance()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));
        Assert.True(PlatformInfo.Current.IsMacOS, "Setup: emulated macOS is active.");

        PlatformInfo.ResetForTesting();

        Assert.Same(RuntimePlatformInfo.Instance, PlatformInfo.Current);
    }

    [Fact]
    public void OverrideForDebug_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PlatformInfo.OverrideForDebug(null!));
    }

    // -----------------------------------------------------------------------
    // PlatformPaths integration: PlatformId routes through PlatformInfo
    // -----------------------------------------------------------------------

    [Fact]
    public void PlatformPaths_PlatformId_ReflectsEmulation()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));
        Assert.Equal("linux", PlatformPaths.PlatformId);

        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));
        Assert.Equal("macos", PlatformPaths.PlatformId);

        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));
        Assert.Equal("windows", PlatformPaths.PlatformId);
    }

    [Fact]
    public void PlatformPaths_DesktopConfigPath_RespectsEmulatedMacOS()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));

        // macOS layout: ~/Library/Application Support/Claude/claude_desktop_config.json.
        // The host's UserProfile resolution (Windows %USERPROFILE% in CI / Unix $HOME)
        // is intentionally NOT overridden — we are testing the BRANCH selection, which
        // is what the debug flag controls.
        string path = PlatformPaths.DesktopConfigPath;
        MessageAssert.Contains(Path.Combine("Library", "Application Support", "Claude", "claude_desktop_config.json"),
            path,
            $"Expected emulated-macOS path layout, got '{path}'.");
    }

    [Fact]
    public void PlatformPaths_DesktopLogsPath_RespectsEmulatedLinux()
    {
        // Linux: Claude Desktop has no persistent log dir → DesktopLogsPath returns null.
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));
        Assert.Null(PlatformPaths.DesktopLogsPath);
    }
}