namespace Bennewitz.Ninja.LayeredEditors.Abstractions;

/// <summary>
/// What a product says about one setting, at one scope, holding one value.
/// </summary>
/// <param name="Severity">
/// How much attention the setting deserves *in principle* — its tier. Present even when the
/// current value is perfectly safe, because that is what lets a settings tree render a dot beside
/// a knob the user has not touched yet.
/// </param>
/// <param name="IsDangerNow">
/// Whether the value actually held right now is the unsafe one. <see cref="Severity"/> says
/// "this knob can weaken a boundary"; this says "it currently does".
/// </param>
/// <param name="Explanation">
/// One sentence a user can act on, or <see langword="null"/> for an unremarkable setting.
/// Phrased as the consequence, not the mechanism — "auto-approves every tool" rather than
/// "sets permission to allow".
/// </param>
/// <remarks>
/// <para>
/// ⛔ <b>Two fields, not one, because collapsing them loses the case that matters.</b> A single
/// "is dangerous" flag cannot distinguish a permission knob sitting at its safe default from an
/// ordinary cosmetic setting — both would read false — so a tree built on it can only warn
/// *after* someone has already made the change. Keeping the tier separate is what lets the UI
/// label a knob before it is turned.
/// </para>
/// <para>
/// ⚠ <b><paramref name="Severity"/> is not a function of the value alone, and neither is it
/// static.</b> The same path can be <see cref="AppSeverity.Caution"/> at one scope and
/// <see cref="AppSeverity.Critical"/> at another — an API key in a user-global file is a local
/// secret, and the identical key in a project file is a secret published to everyone with repo
/// access. That is why this is produced by <see cref="IDangerClassifier"/> rather than annotated
/// once per schema property.
/// </para>
/// </remarks>
public sealed record DangerAssessment(AppSeverity Severity, bool IsDangerNow, string? Explanation)
{
    /// <summary>
    /// The ordinary case: nothing notable, nothing to explain.
    /// </summary>
    /// <remarks>
    /// A shared instance rather than a fresh allocation per call. Classification runs on every
    /// read and write of every visible property — the settings tree recomputes a whole page's
    /// worth on load — and the overwhelming majority of paths land here.
    /// </remarks>
    public static DangerAssessment Unremarkable { get; } = new(AppSeverity.Neutral, false, null);
}
