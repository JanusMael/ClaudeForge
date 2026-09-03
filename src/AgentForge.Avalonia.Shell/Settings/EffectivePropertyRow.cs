using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;

/// <summary>
/// One row of the effective-value view: a property, the value that wins, and where it came from.
/// </summary>
/// <remarks>
/// Extracted from the app's effective-settings view-model when the settings group editor became
/// neutral — the group editor produces these rows, so the type has to live where the producer
/// does. Every member was already product-neutral.
/// </remarks>
public sealed record EffectivePropertyRow(
    string Property,
    string DisplayValue,
    ConfigScope? Scope,
    bool IsOverridden,
    string? Description = null)
{
    /// <summary>
    /// Tooltip for the property-name cell: the schema description when known, else the
    /// raw path (so the cell always has a meaningful hover, matching the old behaviour).
    /// </summary>
    public string PropertyTooltip => string.IsNullOrWhiteSpace(Description) ? Property : Description!;

    /// <summary>
    /// How much the setting deserving this row matters, assessed at the scope that actually
    /// won and over the value that actually won.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐⭐ <b>This is deliberately assessed at the EFFECTIVE scope, not the editing scope — so it
    /// can legitimately differ from the dot on the same setting's editor row, and that difference
    /// is the entire point of the column.</b> A key whose tier escalates in a git-committed file
    /// is Caution while you edit it at User scope and Critical once a project file overrides it;
    /// the settings tree can only answer for the scope you are editing, and this view answers
    /// "what is true at runtime". Reading the two as a contradiction is the mistake — they are
    /// answers to different questions, which is why the column header says so.
    /// </para>
    /// <para>
    /// ⛔ <b>Do not "unify" this with
    /// <see cref="LayeredEditors.Avalonia.ViewModels.IDangerAnnotatedEditor.AssessDanger"/>.</b>
    /// Search asks the editor because search holds neither the value nor a scope and would have
    /// to invent both. A row here holds the real winning pair, so classifying is the accurate
    /// move and delegating would report the wrong scope's answer.
    /// </para>
    /// <para>
    /// Defaults to <see cref="DangerAssessment.Unremarkable"/> — no dot — so a producer with no
    /// classifier (tests, a product section that declares no table) renders exactly as before.
    /// </para>
    /// </remarks>
    public DangerAssessment Danger { get; init; } = DangerAssessment.Unremarkable;

    /// <summary>
    /// Whether to render a severity dot at all. Named to match
    /// <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel.HasDangerSeverity"/>
    /// so the markup is the same shape on every danger surface.
    /// </summary>
    public bool HasDangerSeverity => Danger.Explanation is not null;

    /// <summary>
    /// Tier plus consequence, as one sentence for a screen reader and the tooltip.
    /// </summary>
    /// <remarks>
    /// ⛔ Goes on <c>AutomationProperties.HelpText</c>, never <c>Name</c>: on a
    /// <c>TextBlock</c> the <c>Text</c> always wins and an explicit <c>Name</c> is ignored
    /// outright, so a glyph annotated that way announces "▲" and nothing else.
    /// </remarks>
    public string DangerAccessibleText =>
        Danger.Explanation is null ? string.Empty : $"{Danger.Severity}: {Danger.Explanation}";
}
