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
[Collection("DoNotParallelize")]
public sealed class ManagedSettingsLocationTests : IDisposable
{
    public ManagedSettingsLocationTests() => Init();

    private void Init() => PlatformInfo.ResetForTesting();

    private void Cleanup() => PlatformInfo.ResetForTesting();

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OnMacOS_PolicyLivesUnderLibraryApplicationSupport()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));

        OrdinalAssert.EndsWith(
            "/Library/Application Support/ClaudeCode",
            Normalize(PlatformPaths.ManagedSettingsRoot));
    }

    [Fact]
    public void OnLinux_PolicyLivesUnderEtcClaudeCode()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));

        OrdinalAssert.EndsWith("/etc/claude-code", Normalize(PlatformPaths.ManagedSettingsRoot));
    }

    /// <summary>
    /// ⚠ <b>Split in two, because only half of this is platform-independent.</b> Emulation flips
    /// which BRANCH runs, but the host's <see cref="Environment.SpecialFolder"/> lookups still
    /// answer for the real operating system — which <see cref="PlatformPaths.ManagedSettingsRoot"/>
    /// says in its own remarks, and which the first version of this test then ignored.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>It passed on Windows and Linux and failed on macOS, which is the worst distribution.</b>
    /// Windows returns a real Program Files; Linux returns empty, so the literal fallback fires and
    /// happens to contain the expected text; macOS returns something non-empty and unrelated. Two
    /// green platforms out of three is exactly enough to look deliberate.
    /// </remarks>
    [Fact]
    public void OnWindows_PolicyIsAClaudeCodeFolder()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));

        // True on every host: the branch selected is the Windows one, whatever root it resolves.
        OrdinalAssert.EndsWith("/ClaudeCode", Normalize(PlatformPaths.ManagedSettingsRoot));
    }

    /// <summary>
    /// The Program Files half, asserted only where <see cref="Environment.SpecialFolder"/> can
    /// answer for it — which is the same host the claim is about.
    /// </summary>
    [Fact]
    public void OnARealWindowsHost_PolicyLivesUnderProgramFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip(
                "Only meaningful on Windows: SpecialFolder.ProgramFiles answers for the HOST, not "
                + "for the emulated platform, so this says nothing when run elsewhere.");
        }

        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));

        MessageAssert.Contains(
            "Program Files",
            Normalize(PlatformPaths.ManagedSettingsRoot),
            "Windows policy must resolve under Program Files.");
    }

    /// <summary>
    /// ⛔ <b>The negative, and it is the half a new-location-only test would pass without.</b>
    /// Adding the system directory while still reading the old one leaves the confidently-wrong
    /// display in place for exactly the users who already have such a file.
    /// </summary>
    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    [InlineData("linux")]
    public void NoManagedPath_ResolvesUnderTheUserHome(string platformId)
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId(platformId));

        string home = Normalize(PlatformPaths.ClaudeHome(ClaudeEnvironment.Empty));

        foreach (string managed in new[]
                 {
                     PlatformPaths.ManagedSettingsRoot,
                     PlatformPaths.ManagedSettingsPath,
                     PlatformPaths.ManagedSettingsDropInDir,
                     PlatformPaths.ManagedMcpPath,
                 })
        {
            Assert.False(
                Normalize(managed).StartsWith(home, StringComparison.OrdinalIgnoreCase),
                $"'{managed}' resolves under the user home '{home}'. Managed policy lives in a " +
                "system directory; a path under the home is the original defect.");
        }
    }

    /// <summary>
    /// ⚠ The legacy Windows location is explicitly NOT one Claude Code consults, so reading it
    /// would put the confidently-wrong display back in a new place.
    /// </summary>
    [Fact]
    public void OnWindows_TheLegacyProgramDataLocationIsNotUsed()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("windows"));

        Assert.DoesNotMatch(
            new System.Text.RegularExpressions.Regex("ProgramData", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            Normalize(PlatformPaths.ManagedSettingsRoot));
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    [InlineData("linux")]
    public void TheThreeManagedFiles_SitDirectlyInThePolicyRoot(string platformId)
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId(platformId));

        string root = Normalize(PlatformPaths.ManagedSettingsRoot);

        Assert.Equal(root + "/managed-settings.json", Normalize(PlatformPaths.ManagedSettingsPath));
        Assert.Equal(root + "/managed-settings.d", Normalize(PlatformPaths.ManagedSettingsDropInDir));
        Assert.Equal(root + "/managed-mcp.json", Normalize(PlatformPaths.ManagedMcpPath));
    }

    /// <summary>
    /// Separator-insensitive comparison: these paths are built with
    /// <see cref="Path.Combine(string, string)"/>, so the host's separator appears no matter which
    /// platform is being simulated. Comparing raw strings would make every assertion here pass or
    /// fail on the runner's OS rather than on the behaviour under test.
    /// </summary>
    private static string Normalize(string path) => path.Replace('\\', '/');
}
