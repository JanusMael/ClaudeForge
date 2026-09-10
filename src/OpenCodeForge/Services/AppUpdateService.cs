using System.Reflection;
using Bennewitz.Ninja.AgentForge.Core.Updates;

namespace Bennewitz.Ninja.OpenCodeForge.Services;

/// <summary>
/// This app's GitHub update checks — a thin wrapper over the product-neutral
/// <see cref="AppUpdateCoordinator"/>, matching the sibling app's.
/// </summary>
/// <remarks>
/// <para>
/// Five things here are this app's; everything else — the once-per-launch latch, the shared
/// <see cref="System.Net.Http.HttpClient"/>, the opt-out and simulate branching, the version
/// arithmetic — lives in the coordinator, so the two apps cannot drift.
/// </para>
/// <para>
/// ⛔ <b>The tag scheme is PREFIXED, and that is the whole reason this app can have an update
/// check at all.</b> GitHub releases are repository-level and this repository hosts two apps, so
/// an unprefixed check here would find whichever app shipped most recently and offer its users
/// the other app's download. The prefix must stay equal to this app's <c>TagPrefix</c> in
/// <c>src/publish/PublishApps.ps1</c> and the tag trigger in
/// <c>.github/workflows/release-opencodeforge.yml</c> — <c>ReleaseWorkflowTests</c> asserts the
/// second pair, and <c>AppUpdateWiringTests</c> the first.
/// </para>
/// <para>
/// ⚠ <b>The repository is <c>JanusMael/ClaudeForge</c> and that is not a mistake.</b> It keeps
/// its original name while hosting both apps; this app's releases live there under a prefixed
/// tag.
/// </para>
/// </remarks>
internal static class AppUpdateService
{
    /// <summary>The prefix this app's release tags carry.</summary>
    /// <remarks>
    /// Exposed so a test can assert it against the publish table and the release workflow rather
    /// than against a second copy of the string.
    /// </remarks>
    internal const string TagPrefix = "opencodeforge-";

    /// <summary>The repository whose releases this app reads.</summary>
    internal const string ReleaseRepository = "JanusMael/ClaudeForge";

    private static readonly AppUpdateCoordinator Coordinator = new(
        new AppUpdateOptions(
            ProductName: "OpenCodeForge",
            Tags: new ReleaseTagScheme(TagPrefix),
            ReleaseTagUrlFormat: $"https://github.com/{ReleaseRepository}/releases/tag/{{0}}",
            // Read fresh on every automatic check, so toggling the Essentials card takes effect
            // without a restart.
            IsAutoCheckEnabled: () => WindowStateService.Load().CheckForUpdatesOnLaunch,
            IsSimulatingUpdate: () => DebugFlags.SimulateUpdate),
        AppUpdateCoordinator.VersionOf(Assembly.GetExecutingAssembly()));

    /// <summary>The running version, as the update check compares it.</summary>
    internal static Version CurrentVersion => Coordinator.CurrentVersion;

    /// <inheritdoc cref="AppUpdateCoordinator.CheckManualAsync"/>
    public static Task<UpdateCheckResult> CheckManualAsync(CancellationToken ct = default) =>
        Coordinator.CheckManualAsync(ct);

    /// <inheritdoc cref="AppUpdateCoordinator.CheckOncePerLaunchAsync"/>
    public static Task<UpdateCheckResult> CheckOncePerLaunchAsync(CancellationToken ct = default) =>
        Coordinator.CheckOncePerLaunchAsync(ct);

    /// <inheritdoc cref="AppUpdateCoordinator.CheckPeriodicAsync"/>
    public static Task<UpdateCheckResult> CheckPeriodicAsync(CancellationToken ct = default) =>
        Coordinator.CheckPeriodicAsync(ct);

    /// <summary>Test seam. Clears the once-per-process latch.</summary>
    /// <remarks>Production code MUST NOT call this.</remarks>
    internal static void ResetForTesting() => Coordinator.ResetLatchForTesting();
}
