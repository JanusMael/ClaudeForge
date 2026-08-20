using System.Net.Http;
using Bennewitz.Ninja.AgentForge.Core.Updates;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Process-wide host for the app's GitHub update checks.
///
/// <para>
/// Owns three concerns the pure
/// <see cref="GithubReleaseChecker"/> deliberately doesn't:
/// </para>
/// <list type="bullet">
///   <item>The single <see cref="HttpClient"/> instance the checker uses
///         (static singleton — the .NET-recommended pattern for HTTP
///         clients in a long-lived process).</item>
///   <item>The "fired exactly once per process" latch — every check
///         beyond the first returns <see cref="UpdateCheckResult.NoUpdate"/>
///         without doing any work.</item>
///   <item>The branching between (a) the live GitHub call, (b) the
///         <see cref="DebugFlags.SimulateUpdate"/> short-circuit
///         used by QA / dev to test the banner flow without publishing
///         a real release, and (c) the early bail when the user has
///         disabled the auto-check via the Essentials card.</item>
/// </list>
///
/// <para>
/// Entry points: <see cref="CheckOncePerLaunchAsync"/> (launch check, latched
/// to once per process), <see cref="CheckPeriodicAsync"/> (the 4-hourly
/// re-check — unlatched, but still opt-out-gated), and
/// <see cref="CheckManualAsync"/> (the About-dialog button, which bypasses the
/// opt-out).  Callers fire and forget — the result is delivered via the returned
/// Task and every failure mode collapses to
/// <see cref="UpdateCheckResult.NoUpdate"/> (silent-skip contract).
/// </para>
/// </summary>
internal static class AppUpdateService
{
    /// <summary>
    /// 0 = not yet fired this process; 1 = fired.  Set via
    /// <see cref="Interlocked.CompareExchange(ref int, int, int)"/> so
    /// concurrent first-launch calls from a race in the VM layer can
    /// only allow ONE through.
    /// </summary>
    private static int _checkFired;

    /// <summary>
    /// HttpClient is constructed lazily on first need so unit tests that
    /// never call the live check path (every test in the current suite)
    /// don't pay the network-stack init cost.
    /// </summary>
    private static readonly Lazy<HttpClient> Http = new(() =>
        GithubReleaseChecker.CreateDefaultProductionHttpClient("ClaudeForge", GetCurrentVersionString()));

    /// <summary>
    /// ClaudeForge's releases are tagged <c>v2026.3.810</c> — no app-name prefix.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This cannot change.</b> Thirteen releases are already published in that shape
    /// and every installed copy looks for exactly it. Prefixing ClaudeForge's future tags
    /// would not fix old installs — it would blind them, because their check would stop
    /// recognising any tag as its own. A later app in this repository takes a prefix instead;
    /// see <see cref="ReleaseTagScheme.Unprefixed"/>.
    /// </remarks>
    private static readonly ReleaseTagScheme Tags = ReleaseTagScheme.Unprefixed;

    /// <summary>
    /// Explicit / manual update check, invoked by the user clicking the
    /// "Check for updates" button on the About dialog.  Bypasses BOTH
    /// the once-per-launch latch (so the user can re-click) AND the
    /// <see cref="WindowState.CheckForUpdatesOnLaunch"/> opt-out
    /// (clicking the button IS explicit consent, irrespective of the
    /// auto-check preference).  Continues to honour the
    /// <see cref="DebugFlags.SimulateUpdate"/> debug short-circuit
    /// so QA can exercise the About-dialog flow without publishing a
    /// real release.  Same silent-skip-on-failure contract as
    /// <see cref="CheckOncePerLaunchAsync"/>: every error path returns
    /// <see cref="UpdateCheckResult.NoUpdate"/>; failures log at
    /// Information level.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckManualAsync(
        CancellationToken ct = default)
    {
        Version current = GetCurrentVersion();

        // Debug-flag short-circuit honoured here too — the same QA
        // path that drives the auto-check banner ALSO drives the
        // manual button.  This means a single --simulate-update flag
        // exercises both surfaces in one test session.
        if (DebugFlags.SimulateUpdate)
        {
            return SynthesiseSimulatedNextVersion(current);
        }

        Log.Information("[UpdateCheck] Manual check triggered (current={Current}).", current);
        GithubReleaseChecker checker = new(Http.Value, Tags);
        return await checker.CheckAsync(current, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Run the auto-update check at most once per process.  Returns
    /// <see cref="UpdateCheckResult.NoUpdate"/> for every short-circuit
    /// path: already-fired, user disabled the check, simulated tag is
    /// older-or-equal, network failed, malformed response, etc.  Failure
    /// modes log at Information level (visible in the rolling log) but
    /// are never user-facing.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckOncePerLaunchAsync(
        CancellationToken ct = default)
    {
        // Atomically transition 0 → 1.  Returns the ORIGINAL value;
        // if it was already 1 we know the check has fired before and
        // can bail without any further work.  Total cost on the
        // second+ call: one interlocked compare-exchange.
        if (Interlocked.CompareExchange(ref _checkFired, 1, 0) != 0)
        {
            return UpdateCheckResult.NoUpdate();
        }

        return await RunAutoCheckAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Periodic re-check driven by <c>MainWindowViewModel</c>'s 4-hourly timer
    /// after the launch check.  Deliberately does NOT consume the once-per-launch
    /// latch — that latch guards only the single launch-time kick, and this is
    /// meant to fire repeatedly for the life of the window.  It DOES keep the
    /// same <see cref="WindowState.CheckForUpdatesOnLaunch"/> opt-out,
    /// <see cref="DebugFlags.SimulateUpdate"/> short-circuit, and
    /// silent-skip-on-failure contract as <see cref="CheckOncePerLaunchAsync"/>.
    ///
    /// <para>
    /// This is the key distinction from <see cref="CheckManualAsync"/>, which
    /// bypasses the opt-out because an explicit button click IS consent.  A
    /// background timer is not consent — a user who disabled the auto-check must
    /// get no periodic banners either — so the opt-out is honoured here.
    /// </para>
    /// </summary>
    public static async Task<UpdateCheckResult> CheckPeriodicAsync(
        CancellationToken ct = default)
    {
        return await RunAutoCheckAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared body of the automatic (non-manual) checks — the launch check and
    /// the periodic re-check.  Honours the user opt-out, then either synthesises
    /// a simulated result (QA) or makes the live GitHub call.  Reads
    /// <see cref="WindowState.CheckForUpdatesOnLaunch"/> fresh on every call, so a
    /// mid-session toggle takes effect on the next check without a restart.
    /// </summary>
    private static async Task<UpdateCheckResult> RunAutoCheckAsync(CancellationToken ct)
    {
        // User opt-out: respected even when --simulate-update is set.
        // (If a QA tester disables the check via the Essentials toggle,
        // they expect NO banner to appear — even simulated ones.)
        WindowState state = WindowStateService.Load();
        if (!state.CheckForUpdatesOnLaunch)
        {
            Log.Information(
                "[UpdateCheck] Automatic update check skipped — CheckForUpdatesOnLaunch is disabled.");
            return UpdateCheckResult.NoUpdate();
        }

        Version current = GetCurrentVersion();

        // Debug-flag short-circuit: skip the network entirely and
        // synthesise a "newer release" result by incrementing the
        // running app's version.  Always produces UpdateAvailable
        // (the synth is guaranteed greater-than-current by construction
        // — no QA pain of picking a version that's actually newer than
        // the auto-versioning generator's output).
        if (DebugFlags.SimulateUpdate)
        {
            return SynthesiseSimulatedNextVersion(current);
        }

        // Live path: real GitHub call.
        GithubReleaseChecker checker = new(Http.Value, Tags);
        return await checker.CheckAsync(current, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Synthesise a "newer release" <see cref="UpdateCheckResult"/> by
    /// taking <paramref name="currentVersion"/> and incrementing its
    /// rightmost meaningful segment by one.  Result is always strictly
    /// greater than <paramref name="currentVersion"/>, so the banner
    /// always surfaces — that's the whole point of the simulate flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bump rule: the <see cref="Version.Build"/> segment (the third
    /// part — "523" in <c>2026.2.523</c>) is incremented by 1.  This
    /// matches the auto-versioning generator's calendar-versioning
    /// shape where Build is the rightmost segment that changes every
    /// build.  When the running version has no Build segment (e.g. a
    /// hand-built <c>1.0</c>), we fall back to incrementing Minor.
    /// </para>
    /// <para>
    /// The synthesised <see cref="UpdateCheckResult.ReleaseUrl"/>
    /// points at the canonical "releases/tag/&lt;tag&gt;" URL even
    /// though no such release exists — clicking it from the banner
    /// will land the QA tester on a 404 page, which is acceptable for
    /// a debug-mode affordance.  We document the synth in the log so
    /// it's distinguishable from a real check during post-mortems.
    /// </para>
    /// </remarks>
    private static UpdateCheckResult SynthesiseSimulatedNextVersion(Version currentVersion)
    {
        Version next;
        if (currentVersion.Build >= 0)
        {
            // Standard calendar-versioning shape — bump the Build
            // segment.  The Major / Minor stay pinned so the
            // synthesised tag stays in the same series the user is
            // already on (no confusing "year jumped" surprises).
            next = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build + 1);
        }
        else if (currentVersion.Minor >= 0)
        {
            // No Build segment (rare — e.g. 1.0).  Bump Minor.
            next = new Version(currentVersion.Major, currentVersion.Minor + 1);
        }
        else
        {
            // 1-part version (very rare).  Bump Major.
            next = new Version(currentVersion.Major + 1, 0);
        }

        string tag = $"v{next}";
        Log.Information(
            "[UpdateCheck] --simulate-update active: synthesised UpdateAvailable {Tag} from current={Current}.",
            tag, currentVersion);
        return UpdateCheckResult.UpdateAvailable(
            tag,
            next,
            releaseUrl: $"https://github.com/JanusMael/ClaudeForge/releases/tag/{tag}");
    }

    /// <summary>
    /// Current app version as a <see cref="Version"/>.  Sourced from
    /// the AssemblyVersion of the entry assembly — populated at build
    /// time by the auto-versioning Roslyn source generator (see
    /// <c>Directory.Build.props</c>'s
    /// <c>GenerateAutoVersionedAssemblyInfo</c>), so this never returns
    /// <c>0.0.0.0</c> on a real build.
    /// </summary>
    private static Version GetCurrentVersion()
    {
        Version? v = typeof(AppUpdateService).Assembly.GetName().Version;
        return v ?? new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// Same version, formatted for inclusion in the User-Agent header.
    /// Stripped to the 3-part form (Major.Minor.Build) because
    /// User-Agent strings traditionally drop the trailing build-number
    /// segment.
    /// </summary>
    private static string GetCurrentVersionString()
    {
        Version v = GetCurrentVersion();
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Test seam.  Clears the once-per-process latch so a test can
    /// invoke <see cref="CheckOncePerLaunchAsync"/> in a clean state
    /// after a previous test has fired it.  Production code MUST NOT
    /// call this — the latch is load-bearing for the
    /// "fires exactly once per launch" contract.
    /// </summary>
    internal static void ResetForTesting()
    {
        Interlocked.Exchange(ref _checkFired, 0);
    }
}
