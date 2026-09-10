using System.Net.Http;
using System.Reflection;
using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.Updates;

/// <summary>
/// What one app needs to supply for <see cref="AppUpdateCoordinator"/> to check its updates.
/// </summary>
/// <remarks>
/// <para>
/// A record rather than five positional parameters: the repository's standard caps constructors
/// at six positional arguments, and — more usefully here — every one of these is a
/// <see langword="string"/> or a delegate, so a call site passing them positionally would
/// silently accept them in the wrong order.
/// </para>
/// <para>
/// ⚠ <b><see cref="Tags"/> has no default, deliberately.</b> A defaulted tag scheme is exactly
/// how a second app silently adopts the first's releases and starts offering its users the
/// wrong download — see <see cref="ReleaseTagScheme"/>.
/// </para>
/// </remarks>
/// <param name="ProductName">
/// The app's name, sent in the User-Agent so GitHub's API traffic is attributable.
/// </param>
/// <param name="Tags">Which releases in the repository belong to this app.</param>
/// <param name="ReleaseTagUrlFormat">
/// A format string with one placeholder for the tag, e.g.
/// <c>"https://github.com/owner/repo/releases/tag/{0}"</c>. Used only to give the simulated
/// result somewhere to point.
/// </param>
/// <param name="IsAutoCheckEnabled">
/// Whether the user has left the automatic check on. Read fresh on every automatic check, so a
/// mid-session toggle takes effect without a restart. Manual checks ignore it — clicking the
/// button IS consent.
/// </param>
/// <param name="IsSimulatingUpdate">
/// Whether a debug flag is forcing a synthesised "update available". Kept as a delegate rather
/// than a bool so the coordinator sees the flag's value at check time, not at construction.
/// </param>
public sealed record AppUpdateOptions(
    string ProductName,
    ReleaseTagScheme Tags,
    string ReleaseTagUrlFormat,
    Func<bool> IsAutoCheckEnabled,
    Func<bool> IsSimulatingUpdate);

/// <summary>
/// Hosts one app's GitHub update checks: the shared <see cref="HttpClient"/>, the
/// once-per-process latch, and the branching between a live call, a simulated result, and the
/// user's opt-out.
/// </summary>
/// <remarks>
/// <para>
/// Product-neutral by construction — everything app-shaped arrives through
/// <see cref="AppUpdateOptions"/>. Each app owns one instance behind a thin static of its own,
/// which is where its resx strings, debug flags and persisted preferences live.
/// </para>
/// <para>
/// ⭐ <b>Why this is shared and the wrappers are not.</b> Of the original per-app service, only
/// five things were ever product-specific: the User-Agent name, the tag scheme, where the
/// opt-out is persisted, which debug flag simulates, and the release URL. Everything else — the
/// latch, the three entry points, the version arithmetic, the silent-skip contract — was
/// identical, and a second copy of it would have drifted from the first the first time either
/// was touched.
/// </para>
/// <para>
/// <b>Silent-skip contract.</b> Every failure mode collapses to
/// <see cref="UpdateCheckResult.NoUpdate"/> and logs at Information. An update check that
/// interrupts the user because GitHub was unreachable is worse than one that says nothing.
/// </para>
/// </remarks>
public sealed class AppUpdateCoordinator
{
    private readonly AppUpdateOptions _options;
    private readonly Lazy<HttpClient> _http;
    private readonly Version _currentVersion;

    /// <summary>0 = the launch check has not fired this process; 1 = it has.</summary>
    private int _checkFired;

    /// <param name="options">This app's identity and preferences.</param>
    /// <param name="currentVersion">
    /// The running version. Passed in rather than read from the entry assembly so a test can
    /// drive the comparison, and so the coordinator does not have to guess which assembly is
    /// "the app" from inside a shared library.
    /// </param>
    /// <param name="httpClientFactory">
    /// Optional override for the HTTP client. The default builds the canonical production
    /// client lazily, so a process that never checks for updates does not pay the network
    /// stack's initialisation.
    /// </param>
    public AppUpdateCoordinator(
        AppUpdateOptions options,
        Version currentVersion,
        Func<HttpClient>? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(currentVersion);

        _options = options;
        _currentVersion = currentVersion;
        _http = new Lazy<HttpClient>(httpClientFactory ?? (() =>
            GithubReleaseChecker.CreateDefaultProductionHttpClient(
                options.ProductName, FormatForUserAgent(currentVersion))));
    }

    /// <summary>The version this coordinator compares releases against.</summary>
    public Version CurrentVersion => _currentVersion;

    /// <summary>
    /// The user asked, explicitly, by clicking a button.
    /// </summary>
    /// <remarks>
    /// Bypasses BOTH the once-per-launch latch (so a second click re-checks) and the auto-check
    /// opt-out (the click is consent, whatever the preference says). Still honours the simulate
    /// flag, so one debug flag exercises the button and the banner in a single session.
    /// </remarks>
    public async Task<UpdateCheckResult> CheckManualAsync(CancellationToken ct = default)
    {
        if (_options.IsSimulatingUpdate())
        {
            return SynthesiseNextVersion();
        }

        Log.Information(
            "[UpdateCheck] Manual check triggered for {Product} (current={Current}).",
            _options.ProductName, _currentVersion);

        return await RunLiveCheckAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The launch check, which runs at most once per process.
    /// </summary>
    /// <remarks>
    /// The latch is an <see cref="Interlocked.CompareExchange(ref int, int, int)"/> so a race in
    /// the view-model layer can only let one call through. Second and later calls cost one
    /// compare-exchange.
    /// </remarks>
    public async Task<UpdateCheckResult> CheckOncePerLaunchAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _checkFired, 1, 0) != 0)
        {
            return UpdateCheckResult.NoUpdate();
        }

        return await RunAutoCheckAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The periodic re-check, driven by a timer for the life of the window.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT consume the launch latch — that latch guards the single launch-time
    /// kick, and this is meant to fire repeatedly. It DOES honour the opt-out: a background
    /// timer is not consent, so a user who turned the check off must get no periodic banners.
    /// </remarks>
    public async Task<UpdateCheckResult> CheckPeriodicAsync(CancellationToken ct = default)
    {
        return await RunAutoCheckAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Shared body of the two automatic checks.</summary>
    private async Task<UpdateCheckResult> RunAutoCheckAsync(CancellationToken ct)
    {
        // Opt-out first, and it outranks the simulate flag: someone who turned the check off
        // expects NO banner, including a simulated one.
        if (!_options.IsAutoCheckEnabled())
        {
            Log.Information(
                "[UpdateCheck] Automatic check skipped for {Product} — the user disabled it.",
                _options.ProductName);
            return UpdateCheckResult.NoUpdate();
        }

        if (_options.IsSimulatingUpdate())
        {
            return SynthesiseNextVersion();
        }

        return await RunLiveCheckAsync(ct).ConfigureAwait(false);
    }

    private async Task<UpdateCheckResult> RunLiveCheckAsync(CancellationToken ct)
    {
        GithubReleaseChecker checker = new(_http.Value, _options.Tags);
        return await checker.CheckAsync(_currentVersion, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Build an "update available" result one version above the running one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Always strictly greater than the current version by construction, so the banner always
    /// surfaces — which is the whole point of the flag. Bumping the Build segment matches the
    /// calendar-versioning shape the generator produces (<c>2026.3.810</c>); an app on a
    /// hand-written <c>1.0</c> falls back to Minor, then Major.
    /// </para>
    /// <para>
    /// ⚠ The URL it points at is a real format applied to a tag that does not exist, so
    /// following it lands on a 404. That is acceptable for a debug affordance, and the log line
    /// says the result was synthesised so a post-mortem can tell it from a real one.
    /// </para>
    /// </remarks>
    private UpdateCheckResult SynthesiseNextVersion()
    {
        Version current = _currentVersion;
        Version next = current.Build >= 0
            ? new Version(current.Major, current.Minor, current.Build + 1)
            : current.Minor >= 0
                ? new Version(current.Major, current.Minor + 1)
                : new Version(current.Major + 1, 0);

        // The published shape is the app's prefix followed by the conventional 'v' — the same
        // spelling the release workflows and winget manifests build.
        string tag = $"{_options.Tags.PublishPrefix}v{next}";

        Log.Information(
            "[UpdateCheck] Simulated update active for {Product}: synthesised {Tag} from current={Current}.",
            _options.ProductName, tag, current);

        return UpdateCheckResult.UpdateAvailable(
            tag,
            next,
            releaseUrl: string.Format(
                System.Globalization.CultureInfo.InvariantCulture, _options.ReleaseTagUrlFormat, tag));
    }

    /// <summary>
    /// Three-part form for the User-Agent, which traditionally omits the revision.
    /// </summary>
    private static string FormatForUserAgent(Version version) =>
        $"{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";

    /// <summary>
    /// The version stamped into <paramref name="assembly"/> by the auto-versioning generator.
    /// </summary>
    /// <remarks>
    /// Falls back to <c>0.0.0.0</c> rather than throwing: a missing version makes every release
    /// look newer, which is a wrong banner — where throwing would be a failed launch.
    /// </remarks>
    public static Version VersionOf(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    /// <summary>Clear the once-per-process latch so a test can observe a fresh launch.</summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>Production code must not call this</b> — the latch is what makes
    /// <see cref="CheckOncePerLaunchAsync"/> mean what its name says.
    /// </para>
    /// <para>
    /// Public rather than internal because the apps that own a coordinator each expose their own
    /// <c>ResetForTesting</c> over it, and those wrappers live in the app assemblies rather than
    /// in a test assembly. Granting every app access to all of this library's internals to reach
    /// one seam would be the broader change.
    /// </para>
    /// </remarks>
    public void ResetLatchForTesting() => Interlocked.Exchange(ref _checkFired, 0);
}
