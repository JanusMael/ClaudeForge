using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Platform;

/// <summary>
/// Where managed (enterprise / MDM) policy is read from, per platform.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The defect these guard: every managed path resolved to <c>~/.claude/</c></b>, which
/// Claude Code never reads. Silent in both directions — a machine with real policy showed no
/// managed layer, and a file the user placed in <c>~/.claude/</c> displayed as enforced while
/// doing nothing.
/// </para>
/// <para>
/// ⛔ <b>A test cannot write to <c>C:\Program Files\ClaudeCode\</c> or <c>/etc/claude-code/</c></b>
/// — both need elevation. So "place a policy file there and see it discovered" is not a runnable
/// check on a normal CI agent or developer machine, and writing it as though it were would produce
/// a step that is quietly skipped or quietly run as admin. Neither is evidence. This class
/// therefore asserts the computed PATH with the platform simulated, and never touches a file.
/// </para>
/// <para>
/// ⚠ <b><see cref="PlatformInfo"/>.Current is a PROCESS-WIDE static</b>, so this class is
/// <c>DoNotParallelize</c> and resets in both directions, matching <c>PlatformInfoTests</c>.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ManagedSettingsLocationTests
{
    [TestInitialize]
    public void Init() => PlatformInfo.ResetForTesting();

    [TestCleanup]
    public void Cleanup() => PlatformInfo.ResetForTesting();

    [TestMethod]
    public void OnMacOS_PolicyLivesUnderLibraryApplicationSupport()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));

        StringAssert.EndsWith(
            Normalize(PlatformPaths.ManagedSettingsRoot),
            "/Library/Application Support/ClaudeCode");
    }

    [TestMethod]
    public void OnLinux_PolicyLivesUnderEtcClaudeCode()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));

        StringAssert.EndsWith(Normalize(PlatformPaths.ManagedSettingsRoot), "/etc/claude-code");
    }

    [TestMethod]
    public void OnWindows_PolicyLivesUnderProgramFiles()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));

        StringAssert.EndsWith(Normalize(PlatformPaths.ManagedSettingsRoot), "/ClaudeCode");
        StringAssert.Contains(
            Normalize(PlatformPaths.ManagedSettingsRoot),
            "Program Files",
            "Windows policy must resolve under Program Files.");
    }

    /// <summary>
    /// ⛔ <b>The negative, and it is the half a new-location-only test would pass without.</b>
    /// Adding the system directory while still reading the old one leaves the confidently-wrong
    /// display in place for exactly the users who already have such a file.
    /// </summary>
    [TestMethod]
    [DataRow("windows")]
    [DataRow("macos")]
    [DataRow("linux")]
    public void NoManagedPath_ResolvesUnderTheUserHome(string platformId)
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId(platformId));

        string home = Normalize(PlatformPaths.ClaudeHome);

        foreach (string managed in new[]
                 {
                     PlatformPaths.ManagedSettingsRoot,
                     PlatformPaths.ManagedSettingsPath,
                     PlatformPaths.ManagedSettingsDropInDir,
                     PlatformPaths.ManagedMcpPath,
                 })
        {
            Assert.IsFalse(
                Normalize(managed).StartsWith(home, StringComparison.OrdinalIgnoreCase),
                $"'{managed}' resolves under the user home '{home}'. Managed policy lives in a " +
                "system directory; a path under the home is the original defect.");
        }
    }

    /// <summary>
    /// ⚠ The legacy Windows location is explicitly NOT one Claude Code consults, so reading it
    /// would put the confidently-wrong display back in a new place.
    /// </summary>
    [TestMethod]
    public void OnWindows_TheLegacyProgramDataLocationIsNotUsed()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));

        StringAssert.DoesNotMatch(
            Normalize(PlatformPaths.ManagedSettingsRoot),
            new System.Text.RegularExpressions.Regex("ProgramData", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    [TestMethod]
    [DataRow("windows")]
    [DataRow("macos")]
    [DataRow("linux")]
    public void TheThreeManagedFiles_SitDirectlyInThePolicyRoot(string platformId)
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId(platformId));

        string root = Normalize(PlatformPaths.ManagedSettingsRoot);

        Assert.AreEqual(root + "/managed-settings.json", Normalize(PlatformPaths.ManagedSettingsPath));
        Assert.AreEqual(root + "/managed-settings.d", Normalize(PlatformPaths.ManagedSettingsDropInDir));
        Assert.AreEqual(root + "/managed-mcp.json", Normalize(PlatformPaths.ManagedMcpPath));
    }

    /// <summary>
    /// Separator-insensitive comparison: these paths are built with
    /// <see cref="Path.Combine(string, string)"/>, so the host's separator appears no matter which
    /// platform is being simulated. Comparing raw strings would make every assertion here pass or
    /// fail on the runner's OS rather than on the behaviour under test.
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/');
}
