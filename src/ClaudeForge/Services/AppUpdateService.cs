using System.Reflection;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Updates;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// This app's GitHub update checks — a thin wrapper over the product-neutral
/// <see cref="AppUpdateCoordinator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Everything that used to live here — the once-per-process latch, the shared
/// <see cref="System.Net.Http.HttpClient"/>, the branching between a live call, a simulated
/// result and the user's opt-out, and the version arithmetic — moved to the coordinator when a
/// second app needed the same behaviour. What stays is the five things that are genuinely this
/// app's: its name, its tag scheme, where its opt-out is persisted, which debug flag simulates,
/// and its release URL.
/// </para>
/// <para>
/// ⚠ <b>The public surface is unchanged on purpose.</b> Three callers use these three methods
/// and none of them moved, so the refactor is observable only by what it deletes.
/// </para>
/// <para>
/// <b>Entry points:</b> <see cref="CheckOncePerLaunchAsync"/> (launch, latched to once per
/// process), <see cref="CheckPeriodicAsync"/> (the 4-hourly re-check — unlatched, still
/// opt-out-gated), and <see cref="CheckManualAsync"/> (the About-dialog button, which bypasses
/// the opt-out because the click IS consent). Callers fire and forget; every failure mode
/// collapses to <see cref="UpdateCheckResult.NoUpdate"/>.
/// </para>
/// </remarks>
internal static class AppUpdateService
{
    /// <summary>
    /// ClaudeForge's releases are tagged <c>v2026.3.810</c> — no app-name prefix.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>This cannot change.</b> Releases are already published in that shape and every
    /// installed copy looks for exactly it. Prefixing this app's future tags would not fix old
    /// installs — it would blind them, because their check would stop recognising any tag as its
    /// own. A later app in this repository takes a prefix instead; see
    /// <see cref="ReleaseTagScheme.Unprefixed"/>.
    /// </remarks>
    /// <summary>
    /// The resolved Claude environment, supplied once by the composition root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>An app-level holder, and it THROWS rather than defaulting.</b> This is a static
    /// service whose coordinator is built from a deferred predicate, so there is no parameter to
    /// thread and no instance to construct. The one thing it must never do is fall back to
    /// <see cref="ClaudeEnvironment.Empty"/>: that would read the auto-check preference out of
    /// the default home for a user who had relocated it, and report a plausible wrong answer.
    /// </para>
    /// <para>
    /// ⚠ Scope: the APP's composition root, the same shape as <c>DebugFlags.Initialize</c>. It is
    /// not the pattern the shared libraries use, where the environment is a required parameter.
    /// </para>
    /// </remarks>
    private static ClaudeEnvironment? _env;

    /// <summary>Supply the resolved environment. Called once, from the composition root.</summary>
    internal static void Initialize(ClaudeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;
    }

    private static ClaudeEnvironment Env =>
        _env ?? throw new InvalidOperationException(
            "AppUpdateService.Initialize has not been called. The composition root must supply " +
            "the resolved ClaudeEnvironment before any update check runs.");

    private static readonly AppUpdateCoordinator Coordinator = new(
        new AppUpdateOptions(
            ProductName: "ClaudeForge",
            Tags: ReleaseTagScheme.Unprefixed,
            ReleaseTagUrlFormat: "https://github.com/JanusMael/ClaudeForge/releases/tag/{0}",
            // Read fresh on every automatic check, so toggling the Essentials card takes effect
            // without a restart.
            // Deferred, so Env is read when a check runs rather than at type-initialisation —
            // which is what lets a static field depend on a value the composition root supplies.
            IsAutoCheckEnabled: () => WindowStateService.Load(Env).CheckForUpdatesOnLaunch,
            IsSimulatingUpdate: () => DebugFlags.SimulateUpdate),
        AppUpdateCoordinator.VersionOf(Assembly.GetExecutingAssembly()));

    /// <inheritdoc cref="AppUpdateCoordinator.CheckManualAsync"/>
    public static Task<UpdateCheckResult> CheckManualAsync(CancellationToken ct = default) =>
        Coordinator.CheckManualAsync(ct);

    /// <inheritdoc cref="AppUpdateCoordinator.CheckOncePerLaunchAsync"/>
    public static Task<UpdateCheckResult> CheckOncePerLaunchAsync(CancellationToken ct = default) =>
        Coordinator.CheckOncePerLaunchAsync(ct);

    /// <inheritdoc cref="AppUpdateCoordinator.CheckPeriodicAsync"/>
    public static Task<UpdateCheckResult> CheckPeriodicAsync(CancellationToken ct = default) =>
        Coordinator.CheckPeriodicAsync(ct);

    /// <summary>
    /// Test seam. Clears the once-per-process latch so a test can invoke
    /// <see cref="CheckOncePerLaunchAsync"/> in a clean state after a previous test fired it.
    /// </summary>
    /// <remarks>
    /// Production code MUST NOT call this — the latch is load-bearing for the "fires exactly
    /// once per launch" contract.
    /// </remarks>
    internal static void ResetForTesting() => Coordinator.ResetLatchForTesting();
}
