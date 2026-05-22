using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Platform;

/// <summary>
/// smoke-test the composed Claude Desktop version probe.
/// The internal probes hit real OS resources (Registry, filesystem), so these
/// tests only assert the narrow platform-gating contract and the fallback
/// behavior when nothing is installed. Full per-probe unit coverage would
/// require injecting fake registry/file readers — out of scope for this polish
/// round.
/// </summary>
[TestClass]
public sealed class ClaudeDesktopVersionProbeTests
{
    [TestInitialize]
    public void Init()
    {
        // TryGetVersion is process-lifetime cached; reset before each test so
        // a previous test (or a previous test class) cannot leak a result.
        ClaudeDesktopVersionProbe.ResetCache();
    }

    [TestCleanup]
    public void Cleanup()
    {
        ClaudeDesktopVersionProbe.ResetCache();
    }

    [TestMethod]
    public void TryGetVersion_OnNonWindows_ReturnsNull()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Test is only meaningful on macOS/Linux.");
            return;
        }

        string? v = ClaudeDesktopVersionProbe.TryGetVersion();
        Assert.IsNull(v,
            "On non-Windows platforms the probe must return null so macOS/Linux fall through to the plist reader.");
    }

    [TestMethod]
    public void TryGetVersion_OnWindows_DoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Test only runs on Windows.");
            return;
        }

        // The result is environment-dependent — we just require the probe to
        // return cleanly (no exceptions escape its try/catch guards) whether
        // or not Claude Desktop is installed on the test host.
        _ = ClaudeDesktopVersionProbe.TryGetVersion();
    }

    // ── WindowsApps / MSIX folder-name parser ────────────────────────────────
    //
    // Pure-function tests exercise the regex without touching the filesystem.
    // These run on every platform because the helper is OS-agnostic.

    [TestMethod]
    public void TryExtractWindowsAppsVersion_PicksHighestVersion()
    {
        string[] folders =
        [
            "Claude_1.3108.0.0_x64__pzs8sxrjxfjjc",
            "Claude_1.3109.0.0_x64__pzs8sxrjxfjjc",
            "Claude_1.3107.5.0_x64__pzs8sxrjxfjjc",
        ];

        string? v = ClaudeDesktopVersionProbe.TryExtractWindowsAppsVersion(folders);
        Assert.AreEqual("1.3109.0.0", v);
    }

    [TestMethod]
    public void TryExtractWindowsAppsVersion_SingleEntry_ReturnsItsVersion()
    {
        string[] folders = ["Claude_1.3109.0.0_x64__pzs8sxrjxfjjc"];
        string? v = ClaudeDesktopVersionProbe.TryExtractWindowsAppsVersion(folders);
        Assert.AreEqual("1.3109.0.0", v);
    }

    [TestMethod]
    public void TryExtractWindowsAppsVersion_ReturnsNull_WhenNoMatches()
    {
        string[] folders =
        [
            "Microsoft.WindowsStore_12106.1001.15.0_x64__8wekyb3d8bbwe",
            "some-other-folder",
            "",
        ];
        string? v = ClaudeDesktopVersionProbe.TryExtractWindowsAppsVersion(folders);
        Assert.IsNull(v);
    }

    [TestMethod]
    public void TryExtractWindowsAppsVersion_EmptyInput_ReturnsNull()
    {
        string? v = ClaudeDesktopVersionProbe.TryExtractWindowsAppsVersion([]);
        Assert.IsNull(v);
    }

    [TestMethod]
    public void TryGetVersionFromSquirrel_ReturnsNull_WhenNotInstalled()
    {
        // The Squirrel probe is platform-agnostic (it just reads a LOCALAPPDATA
        // directory name) so it's safe to call on any OS. On a build host that
        // has neither LOCALAPPDATA nor an AnthropicClaude subdirectory it
        // should return null rather than throwing.
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string claudeDir = Path.Combine(localAppData, "AnthropicClaude");
        if (Directory.Exists(claudeDir))
        {
            Assert.Inconclusive("AnthropicClaude directory exists on this host; test is inconclusive.");
            return;
        }

        string? v = ClaudeDesktopVersionProbe.TryGetVersionFromSquirrel();
        Assert.IsNull(v);
    }
}