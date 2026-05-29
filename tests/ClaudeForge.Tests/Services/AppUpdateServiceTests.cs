using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Core.Updates;
using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Tests for <see cref="AppUpdateService"/>'s decision paths.
///
/// <para>
/// The service owns three concerns:
/// </para>
/// <list type="number">
///   <item>Once-per-process latch — second+ calls collapse to NoUpdate.</item>
///   <item>User-toggle gate — when WindowState.CheckForUpdatesOnLaunch
///         is false, the check is skipped (even with --simulate-update set).</item>
///   <item>--simulate-update branch — when the flag is set, GitHub is
///         NOT contacted and the result is synthesised by incrementing
///         the running app's assembly-version's rightmost segment.</item>
/// </list>
///
/// <para>
/// We deliberately do NOT test the live-GitHub path here — that's
/// covered by <c>GithubReleaseCheckerTests</c> in the Core suite,
/// which injects fake HttpMessageHandlers.  These tests cover only
/// the AppUpdateService-level orchestration.
/// </para>
/// </summary>
[TestClass]
public sealed class AppUpdateServiceTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_appupdate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
        // Latch is process-static; reset for clean isolation per test.
        AppUpdateService.ResetForTesting();
    }

    [TestCleanup]
    public void Cleanup()
    {
        DebugFlags.ResetForTesting();
        AppUpdateService.ResetForTesting();
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            try { Directory.Delete(_sandbox, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = ex; }
        }
    }

    // ── Once-per-process latch ──────────────────────────────────────────

    [TestMethod]
    public async Task CheckOncePerLaunchAsync_SecondCall_ReturnsNoUpdateWithoutWork()
    {
        // First call exercises the real path (or a flag-driven one);
        // either way it consumes the latch.  Second call must short-
        // circuit before reaching any decision logic.
        DebugFlags.Initialize(["--simulate-update"]);
        // Default WindowState (no override file) → CheckForUpdatesOnLaunch=true.

        UpdateCheckResult first = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.IsTrue(first.IsUpdateAvailable,
            "Setup: first call must produce an UpdateAvailable (so we know the second isn't trivially false).");

        UpdateCheckResult second = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.IsFalse(second.IsUpdateAvailable,
            "Second call to CheckOncePerLaunchAsync MUST collapse to NoUpdate — the latch is load-bearing for " +
            "the 'fires exactly once per launch' contract.");
    }

    // ── User-toggle gate ────────────────────────────────────────────────

    [TestMethod]
    public async Task CheckOncePerLaunchAsync_CheckDisabledByUser_ReturnsNoUpdate()
    {
        // User toggled the Essentials card off — even with simulate-update
        // set, the check must be skipped.  This protects the contract
        // "if I turned it off, I get no banner".
        WindowStateService.Save(new WindowState { CheckForUpdatesOnLaunch = false });
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult result = await AppUpdateService.CheckOncePerLaunchAsync();

        Assert.IsFalse(result.IsUpdateAvailable,
            "User opt-out (CheckForUpdatesOnLaunch=false) must override the simulate-update path.");
    }

    // ── --simulate-update branch ────────────────────────────────────────

    [TestMethod]
    public async Task CheckOncePerLaunchAsync_SimulateUpdate_ReturnsUpdateAvailable_WithIncrementedVersion()
    {
        // The synth path takes the running assembly version and
        // increments its rightmost segment by 1.  The resulting tag
        // must compare greater than the current version — and the
        // ReleaseUrl must point at the canonical repo + the synthesised
        // tag (404 on a real fetch, but that's fine for QA).
        DebugFlags.Initialize(["--simulate-update"]);
        Version current = typeof(AppUpdateService).Assembly.GetName().Version
            ?? throw new InvalidOperationException("AppUpdateService assembly has no Version.");

        UpdateCheckResult result = await AppUpdateService.CheckOncePerLaunchAsync();

        Assert.IsTrue(result.IsUpdateAvailable,
            "Simulate flag must always produce UpdateAvailable — the synth is guaranteed " +
            "greater-than-current by construction.");
        Assert.IsNotNull(result.LatestVersion);
        Assert.IsTrue(result.LatestVersion! > current,
            $"Synthesised version {result.LatestVersion} must be strictly greater than " +
            $"current {current} — the whole point of the simulate flag is a visible update.");
        Assert.IsNotNull(result.LatestTagName);
        StringAssert.StartsWith(result.LatestTagName!, "v",
            "Synthesised tag carries the canonical 'v' prefix used in release tags.");
        Assert.IsNotNull(result.ReleaseUrl);
        StringAssert.Contains(result.ReleaseUrl!, "JanusMael/ClaudeForge",
            "Synthesised URL must point at the canonical repo path.");
        StringAssert.Contains(result.ReleaseUrl!, result.LatestTagName!,
            "Synthesised URL must include the synthesised tag for the QA tester to click.");
    }

    [TestMethod]
    public async Task CheckOncePerLaunchAsync_SimulateUpdate_BumpsBuildSegmentOnly()
    {
        // Concrete shape contract: the increment lands on the Build
        // segment (the third part of "Major.Minor.Build.Revision"),
        // leaving Major / Minor pinned so the synthesised tag stays in
        // the same series the user is already running.  No confusing
        // "year jumped" surprises.
        DebugFlags.Initialize(["--simulate-update"]);
        Version current = typeof(AppUpdateService).Assembly.GetName().Version!;

        UpdateCheckResult result = await AppUpdateService.CheckOncePerLaunchAsync();

        Assert.IsNotNull(result.LatestVersion);
        Assert.AreEqual(current.Major, result.LatestVersion!.Major,
            "Major segment must be preserved — synth stays in the same major series.");
        Assert.AreEqual(current.Minor, result.LatestVersion.Minor,
            "Minor segment must be preserved — synth stays in the same minor series.");
        if (current.Build >= 0)
        {
            Assert.AreEqual(current.Build + 1, result.LatestVersion.Build,
                "Build segment must be incremented by exactly one — that's the synth contract.");
        }
    }

    // ── CheckManualAsync ────────────────────────────────────────────────
    //
    // The manual check (About-dialog "Check for updates" button) must:
    //   - Bypass the once-per-launch latch (user can re-click).
    //   - Bypass the user-toggle gate (click IS consent — overrides the
    //     Essentials "Check for updates on launch" opt-out).
    //   - Still honour --simulate-update for QA.

    [TestMethod]
    public async Task CheckManualAsync_BypassesOncePerLaunchLatch()
    {
        // Consume the latch first via the auto path.
        DebugFlags.Initialize(["--simulate-update"]);
        UpdateCheckResult auto = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.IsTrue(auto.IsUpdateAvailable, "Setup: auto check must produce a result.");

        // Auto path is now latched — a second auto call would NoUpdate.
        UpdateCheckResult autoAgain = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.IsFalse(autoAgain.IsUpdateAvailable, "Setup: auto latch confirmed.");

        // Manual must still produce a real result.
        UpdateCheckResult manual = await AppUpdateService.CheckManualAsync();
        Assert.IsTrue(manual.IsUpdateAvailable,
            "CheckManualAsync MUST bypass the once-per-launch latch — the user " +
            "explicitly re-clicked, and is entitled to a fresh answer.");
        Assert.IsNotNull(manual.LatestTagName);
        StringAssert.StartsWith(manual.LatestTagName!, "v",
            "Synthesised tag has the canonical 'v' prefix.");
    }

    [TestMethod]
    public async Task CheckManualAsync_BypassesUserToggleOptOut()
    {
        // User has the Essentials toggle OFF.  The auto path would skip;
        // the manual path must still run.
        WindowStateService.Save(new WindowState { CheckForUpdatesOnLaunch = false });
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult auto = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.IsFalse(auto.IsUpdateAvailable,
            "Setup: auto check must respect the user opt-out (CheckForUpdatesOnLaunch=false).");

        UpdateCheckResult manual = await AppUpdateService.CheckManualAsync();
        Assert.IsTrue(manual.IsUpdateAvailable,
            "CheckManualAsync MUST bypass the user-toggle opt-out — clicking the " +
            "button is explicit consent that overrides the auto-check preference.");
    }

    [TestMethod]
    public async Task CheckManualAsync_HonoursSimulateUpdateFlag()
    {
        DebugFlags.Initialize(["--simulate-update"]);
        Version current = typeof(AppUpdateService).Assembly.GetName().Version!;

        UpdateCheckResult result = await AppUpdateService.CheckManualAsync();

        Assert.IsTrue(result.IsUpdateAvailable);
        Assert.IsNotNull(result.LatestVersion);
        Assert.IsTrue(result.LatestVersion! > current,
            "Manual + simulate must synthesise a version strictly greater than current — " +
            "same contract as the auto path's synth.");
        StringAssert.StartsWith(result.LatestTagName!, "v",
            "Synthesised tag has the canonical 'v' prefix.");
    }

    [TestMethod]
    public async Task CheckManualAsync_CanFireMultipleTimes()
    {
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult first = await AppUpdateService.CheckManualAsync();
        UpdateCheckResult second = await AppUpdateService.CheckManualAsync();
        UpdateCheckResult third = await AppUpdateService.CheckManualAsync();

        Assert.IsTrue(first.IsUpdateAvailable);
        Assert.IsTrue(second.IsUpdateAvailable,
            "Manual checks must not consume a latch — every call is independent.");
        Assert.IsTrue(third.IsUpdateAvailable,
            "A third manual check must still produce a result (re-click-friendly).");
    }
}
