using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Updates;
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
public sealed class AppUpdateServiceTests : IDisposable
{
    private string _sandbox = null!;

    public AppUpdateServiceTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_appupdate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
        // Latch is process-static; reset for clean isolation per test.
        AppUpdateService.ResetForTesting();
        // The service reads the auto-check preference out of a resolved home and THROWS rather
        // than defaulting, so the composition root's Initialize has to be stood in for here.
        // Empty is right because the sandbox arrives via TestUserProfileOverride, which wins
        // over the environment — every path these tests touch is _sandbox either way.
        AppUpdateService.Initialize(ClaudeEnvironment.Empty);
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // ── Once-per-process latch ──────────────────────────────────────────

    [Fact]
    public async Task CheckOncePerLaunchAsync_SecondCall_ReturnsNoUpdateWithoutWork()
    {
        // First call exercises the real path (or a flag-driven one);
        // either way it consumes the latch.  Second call must short-
        // circuit before reaching any decision logic.
        DebugFlags.Initialize(["--simulate-update"]);
        // Default WindowState (no override file) → CheckForUpdatesOnLaunch=true.

        UpdateCheckResult first = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.True(first.IsUpdateAvailable,
            "Setup: first call must produce an UpdateAvailable (so we know the second isn't trivially false).");

        UpdateCheckResult second = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.False(second.IsUpdateAvailable,
            "Second call to CheckOncePerLaunchAsync MUST collapse to NoUpdate — the latch is load-bearing for " +
            "the 'fires exactly once per launch' contract.");
    }

    // ── User-toggle gate ────────────────────────────────────────────────

    [Fact]
    public async Task CheckOncePerLaunchAsync_CheckDisabledByUser_ReturnsNoUpdate()
    {
        // User toggled the Essentials card off — even with simulate-update
        // set, the check must be skipped.  This protects the contract
        // "if I turned it off, I get no banner".
        WindowStateService.Save(ClaudeEnvironment.Empty, new WindowState { CheckForUpdatesOnLaunch = false });
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult result = await AppUpdateService.CheckOncePerLaunchAsync();

        Assert.False(result.IsUpdateAvailable,
            "User opt-out (CheckForUpdatesOnLaunch=false) must override the simulate-update path.");
    }

    // ── --simulate-update branch ────────────────────────────────────────

    [Fact]
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

        Assert.True(result.IsUpdateAvailable,
            "Simulate flag must always produce UpdateAvailable — the synth is guaranteed " +
            "greater-than-current by construction.");
        Assert.NotNull(result.LatestVersion);
        Assert.True(result.LatestVersion! > current,
            $"Synthesised version {result.LatestVersion} must be strictly greater than " +
            $"current {current} — the whole point of the simulate flag is a visible update.");
        Assert.NotNull(result.LatestTagName);
        MessageAssert.StartsWith("v", result.LatestTagName!,
            "Synthesised tag carries the canonical 'v' prefix used in release tags.");
        Assert.NotNull(result.ReleaseUrl);
        MessageAssert.Contains("JanusMael/ClaudeForge", result.ReleaseUrl!,
            "Synthesised URL must point at the canonical repo path.");
        MessageAssert.Contains(result.LatestTagName!, result.ReleaseUrl!,
            "Synthesised URL must include the synthesised tag for the QA tester to click.");
    }

    [Fact]
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

        Assert.NotNull(result.LatestVersion);
        MessageAssert.Equal(current.Major, result.LatestVersion!.Major,
            "Major segment must be preserved — synth stays in the same major series.");
        MessageAssert.Equal(current.Minor, result.LatestVersion.Minor,
            "Minor segment must be preserved — synth stays in the same minor series.");
        if (current.Build >= 0)
        {
            MessageAssert.Equal(current.Build + 1, result.LatestVersion.Build,
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

    [Fact]
    public async Task CheckManualAsync_BypassesOncePerLaunchLatch()
    {
        // Consume the latch first via the auto path.
        DebugFlags.Initialize(["--simulate-update"]);
        UpdateCheckResult auto = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.True(auto.IsUpdateAvailable, "Setup: auto check must produce a result.");

        // Auto path is now latched — a second auto call would NoUpdate.
        UpdateCheckResult autoAgain = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.False(autoAgain.IsUpdateAvailable, "Setup: auto latch confirmed.");

        // Manual must still produce a real result.
        UpdateCheckResult manual = await AppUpdateService.CheckManualAsync();
        Assert.True(manual.IsUpdateAvailable,
            "CheckManualAsync MUST bypass the once-per-launch latch — the user " +
            "explicitly re-clicked, and is entitled to a fresh answer.");
        Assert.NotNull(manual.LatestTagName);
        MessageAssert.StartsWith("v", manual.LatestTagName!,
            "Synthesised tag has the canonical 'v' prefix.");
    }

    [Fact]
    public async Task CheckManualAsync_BypassesUserToggleOptOut()
    {
        // User has the Essentials toggle OFF.  The auto path would skip;
        // the manual path must still run.
        WindowStateService.Save(ClaudeEnvironment.Empty, new WindowState { CheckForUpdatesOnLaunch = false });
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult auto = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.False(auto.IsUpdateAvailable,
            "Setup: auto check must respect the user opt-out (CheckForUpdatesOnLaunch=false).");

        UpdateCheckResult manual = await AppUpdateService.CheckManualAsync();
        Assert.True(manual.IsUpdateAvailable,
            "CheckManualAsync MUST bypass the user-toggle opt-out — clicking the " +
            "button is explicit consent that overrides the auto-check preference.");
    }

    [Fact]
    public async Task CheckManualAsync_HonoursSimulateUpdateFlag()
    {
        DebugFlags.Initialize(["--simulate-update"]);
        Version current = typeof(AppUpdateService).Assembly.GetName().Version!;

        UpdateCheckResult result = await AppUpdateService.CheckManualAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.LatestVersion);
        Assert.True(result.LatestVersion! > current,
            "Manual + simulate must synthesise a version strictly greater than current — " +
            "same contract as the auto path's synth.");
        MessageAssert.StartsWith("v", result.LatestTagName!,
            "Synthesised tag has the canonical 'v' prefix.");
    }

    [Fact]
    public async Task CheckManualAsync_CanFireMultipleTimes()
    {
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult first = await AppUpdateService.CheckManualAsync();
        UpdateCheckResult second = await AppUpdateService.CheckManualAsync();
        UpdateCheckResult third = await AppUpdateService.CheckManualAsync();

        Assert.True(first.IsUpdateAvailable);
        Assert.True(second.IsUpdateAvailable,
            "Manual checks must not consume a latch — every call is independent.");
        Assert.True(third.IsUpdateAvailable,
            "A third manual check must still produce a result (re-click-friendly).");
    }

    // ── CheckPeriodicAsync ──────────────────────────────────────────────
    //
    // The periodic re-check (MainWindowViewModel's 4-hourly timer) must:
    //   - Bypass the once-per-launch latch (it fires repeatedly by design).
    //   - RESPECT the user-toggle opt-out — a background timer is NOT explicit
    //     consent, so this is the key behavioural difference from CheckManual.
    //   - Still honour --simulate-update for QA.

    [Fact]
    public async Task CheckPeriodicAsync_BypassesOncePerLaunchLatch()
    {
        // Consume the launch latch first via the auto path.
        DebugFlags.Initialize(["--simulate-update"]);
        UpdateCheckResult auto = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.True(auto.IsUpdateAvailable, "Setup: launch check must produce a result.");

        UpdateCheckResult autoAgain = await AppUpdateService.CheckOncePerLaunchAsync();
        Assert.False(autoAgain.IsUpdateAvailable, "Setup: launch latch confirmed.");

        // Periodic must still produce a real result despite the consumed latch.
        UpdateCheckResult periodic = await AppUpdateService.CheckPeriodicAsync();
        Assert.True(periodic.IsUpdateAvailable,
            "CheckPeriodicAsync MUST bypass the once-per-launch latch — it re-checks on a timer, " +
            "so the launch latch (which guards only the single launch kick) must not gate it.");
    }

    [Fact]
    public async Task CheckPeriodicAsync_RespectsUserToggleOptOut()
    {
        // User has the Essentials toggle OFF.  Unlike the manual button, the
        // periodic timer is not explicit consent — it must skip, like the launch
        // check does.
        WindowStateService.Save(ClaudeEnvironment.Empty, new WindowState { CheckForUpdatesOnLaunch = false });
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult periodic = await AppUpdateService.CheckPeriodicAsync();

        Assert.False(periodic.IsUpdateAvailable,
            "CheckPeriodicAsync MUST respect the opt-out (CheckForUpdatesOnLaunch=false) — a background " +
            "re-check is not consent. This is the key distinction from CheckManualAsync, which bypasses it.");
    }

    [Fact]
    public async Task CheckPeriodicAsync_CanFireMultipleTimes()
    {
        // Each timer tick is independent — no latch is consumed.
        DebugFlags.Initialize(["--simulate-update"]);

        UpdateCheckResult first = await AppUpdateService.CheckPeriodicAsync();
        UpdateCheckResult second = await AppUpdateService.CheckPeriodicAsync();

        Assert.True(first.IsUpdateAvailable);
        Assert.True(second.IsUpdateAvailable,
            "Periodic checks must not consume a latch — every 4-hourly tick is independent.");
    }

    [Fact]
    public async Task CheckPeriodicAsync_HonoursSimulateUpdateFlag()
    {
        DebugFlags.Initialize(["--simulate-update"]);
        Version current = typeof(AppUpdateService).Assembly.GetName().Version!;

        UpdateCheckResult result = await AppUpdateService.CheckPeriodicAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.NotNull(result.LatestVersion);
        Assert.True(result.LatestVersion! > current,
            "Periodic + simulate must synthesise a version strictly greater than current — " +
            "same synth contract as the launch path.");
    }
}
