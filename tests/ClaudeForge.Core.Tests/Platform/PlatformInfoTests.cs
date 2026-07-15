using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

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
[DoNotParallelize]
[TestClass]
public sealed class PlatformInfoTests
{
    [TestCleanup]
    public void Cleanup()
    {
        PlatformInfo.ResetForTesting();
    }

    // -----------------------------------------------------------------------
    // EmulatedPlatformInfo: each id maps to the right flag tuple
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Emulated_Windows_HasWindowsFlagOnly()
    {
        EmulatedPlatformInfo info = EmulatedPlatformInfo.ForId("windows");
        Assert.IsTrue(info.IsWindows);
        Assert.IsFalse(info.IsMacOS);
        Assert.IsFalse(info.IsLinux);
        Assert.AreEqual("windows", info.PlatformId);
        Assert.AreEqual("Windows", info.DisplayName);
        Assert.AreEqual(';', info.PathListSeparator);
        Assert.AreEqual(StringComparison.OrdinalIgnoreCase, info.PathComparison);
    }

    [TestMethod]
    public void Emulated_MacOS_HasMacOSFlagOnly()
    {
        EmulatedPlatformInfo info = EmulatedPlatformInfo.ForId("macos");
        Assert.IsFalse(info.IsWindows);
        Assert.IsTrue(info.IsMacOS);
        Assert.IsFalse(info.IsLinux);
        Assert.AreEqual("macos", info.PlatformId);
        Assert.AreEqual("macOS", info.DisplayName);
        Assert.AreEqual(':', info.PathListSeparator);
        Assert.AreEqual(StringComparison.Ordinal, info.PathComparison);
    }

    [TestMethod]
    public void Emulated_Linux_HasLinuxFlagOnly()
    {
        EmulatedPlatformInfo info = EmulatedPlatformInfo.ForId("linux");
        Assert.IsFalse(info.IsWindows);
        Assert.IsFalse(info.IsMacOS);
        Assert.IsTrue(info.IsLinux);
        Assert.AreEqual("linux", info.PlatformId);
        Assert.AreEqual("Linux", info.DisplayName);
        Assert.AreEqual(':', info.PathListSeparator);
        Assert.AreEqual(StringComparison.Ordinal, info.PathComparison);
    }

    [TestMethod]
    public void Emulated_UnknownId_Throws()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => EmulatedPlatformInfo.ForId("freebsd"));
    }

    // -----------------------------------------------------------------------
    // PlatformInfo.Current override + reset
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Current_DefaultsToRuntimePlatformInfo()
    {
        // Cleanup runs after every test, so the static state is always fresh here.
        Assert.AreSame(RuntimePlatformInfo.Instance, PlatformInfo.Current);
    }

    [TestMethod]
    public void OverrideForDebug_ReplacesCurrent()
    {
        EmulatedPlatformInfo emulated = EmulatedPlatformInfo.ForId("linux");
        PlatformInfo.OverrideForDebug(emulated);

        Assert.AreSame(emulated, PlatformInfo.Current);
        Assert.IsTrue(PlatformInfo.Current.IsLinux);
        Assert.AreEqual("linux", PlatformInfo.Current.PlatformId);
    }

    [TestMethod]
    public void ResetForTesting_RestoresRuntimeInstance()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));
        Assert.IsTrue(PlatformInfo.Current.IsMacOS, "Setup: emulated macOS is active.");

        PlatformInfo.ResetForTesting();

        Assert.AreSame(RuntimePlatformInfo.Instance, PlatformInfo.Current);
    }

    [TestMethod]
    public void OverrideForDebug_NullArgument_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => PlatformInfo.OverrideForDebug(null!));
    }

    // -----------------------------------------------------------------------
    // PlatformPaths integration: PlatformId routes through PlatformInfo
    // -----------------------------------------------------------------------

    [TestMethod]
    public void PlatformPaths_PlatformId_ReflectsEmulation()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));
        Assert.AreEqual("linux", PlatformPaths.PlatformId);

        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));
        Assert.AreEqual("macos", PlatformPaths.PlatformId);

        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));
        Assert.AreEqual("windows", PlatformPaths.PlatformId);
    }

    [TestMethod]
    public void PlatformPaths_DesktopConfigPath_RespectsEmulatedMacOS()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));

        // macOS layout: ~/Library/Application Support/Claude/claude_desktop_config.json.
        // The host's UserProfile resolution (Windows %USERPROFILE% in CI / Unix $HOME)
        // is intentionally NOT overridden — we are testing the BRANCH selection, which
        // is what the debug flag controls.
        string path = PlatformPaths.DesktopConfigPath;
        StringAssert.Contains(path,
            Path.Combine("Library", "Application Support", "Claude", "claude_desktop_config.json"),
            $"Expected emulated-macOS path layout, got '{path}'.");
    }

    [TestMethod]
    public void PlatformPaths_DesktopLogsPath_RespectsEmulatedLinux()
    {
        // Linux: Claude Desktop has no persistent log dir → DesktopLogsPath returns null.
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));
        Assert.IsNull(PlatformPaths.DesktopLogsPath);
    }
}