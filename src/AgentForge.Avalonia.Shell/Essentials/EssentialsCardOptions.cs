using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;

/// <summary>
/// Everything one Essentials card needs to exist. Passed to
/// <see cref="EssentialsCardViewModel"/> in place of a positional argument list.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This replaced a 14-parameter constructor — the worst violation of the max-6-positional
/// convention in the repo</b>, and the plan's own note said to convert it "here, where the
/// signature is already being changed" rather than let two more card kinds and a scope argument
/// push it to seventeen. Named members also make the call sites readable: a curated card list is
/// dozens of lines of arguments, and `true, false, null, "", ""` tails are unreviewable.
/// </para>
/// <para>
/// ⚠ <b><see cref="Title"/>, <see cref="Body"/> and the two banner strings are the HOST's
/// words</b>, like <c>SaveDialogText</c>'s. The shell owns the card mechanism; which knobs deserve
/// a card, and how to describe them, is a statement about a specific agent product.
/// </para>
/// </remarks>
public sealed record EssentialsCardOptions
{
    /// <summary>Stable identifier, unchanged across renames — search deep-links to it.</summary>
    public required string Id { get; init; }

    /// <summary>Localised card title.</summary>
    public required string Title { get; init; }

    /// <summary>Localised "why this matters" body.</summary>
    public required string Body { get; init; }

    /// <summary>How much attention this setting deserves — drives the dot.</summary>
    public required AppSeverity Severity { get; init; }

    /// <summary>Which inline editor surface to render.</summary>
    public required EssentialsCardKind Kind { get; init; }

    /// <summary>Refresh the card's bindings from the underlying accessor.</summary>
    public required Func<EssentialsCardViewModel, Task> ReadAsync { get; init; }

    /// <summary>
    /// Persist the card's current value, or <see langword="null"/> on a
    /// <see cref="EssentialsCardKind.Derived"/> card.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Not <c>required</c>, and the pairing is enforced in the constructor instead.</b>
    /// Keeping it required would have every derived card pass a no-op lambda that reads as
    /// "persists nothing yet" rather than "has nothing to persist" — and a no-op writer on an
    /// editable card would then be indistinguishable from a correct one.
    /// </remarks>
    public Func<EssentialsCardViewModel, Task>? WriteAsync { get; init; }

    /// <summary>
    /// Title of the nav node this setting lives in, for the "View in &lt;group&gt;" deep link.
    /// Empty for a synthetic or env-only card with no group home.
    /// </summary>
    public string ViewInGroupTitle { get; init; } = string.Empty;

    /// <summary>
    /// Composite format for the deep-link button, taking <c>{0}</c> = <see cref="ViewInGroupTitle"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ The host's wording, and the only string the card view-model formats itself. Ignored when
    /// <see cref="ViewInGroupTitle"/> is empty, because then no button renders.
    /// </remarks>
    public string ViewInGroupLabelFormat { get; init; } = "{0}";

    /// <summary>
    /// When true the card renders an extra "effective source" row naming which of the settings
    /// file / OS user env / OS machine env is contributing.
    /// </summary>
    public bool IsEnvVarCard { get; init; }

    /// <summary>Options for an <see cref="EssentialsCardKind.EnumString"/> card.</summary>
    public IReadOnlyList<string>? EnumOptions { get; init; }

    /// <summary>Smallest value an <see cref="EssentialsCardKind.Int"/> card's spinner offers.</summary>
    /// <remarks>
    /// ⚠ <b>Per card, because the schema is per key.</b> A single range hardcoded in the markup
    /// serves whichever card was written first: OpenCode's <c>subagent_depth</c> admits 0, while
    /// <c>tool_output.max_lines</c> and <c>max_bytes</c> are <c>exclusiveMinimum: 0</c> — so a
    /// shared <c>Minimum="0"</c> offers those two a value OpenCode rejects at load, and the user
    /// finds out at save time instead of never being able to pick it.
    /// </remarks>
    public decimal IntMinimum { get; init; }

    /// <summary>Largest value an <see cref="EssentialsCardKind.Int"/> card's spinner offers.</summary>
    /// <remarks>
    /// Defaults to <see cref="int.MaxValue"/> — the card's value is an <c>int?</c>, so that is the
    /// real ceiling regardless of what a schema's <c>maximum</c> says.
    /// </remarks>
    public decimal IntMaximum { get; init; } = int.MaxValue;

    /// <summary>Step the spinner's arrows move an <see cref="EssentialsCardKind.Int"/> card by.</summary>
    public decimal IntIncrement { get; init; } = 1;

    /// <summary>
    /// The closed, ordered option set for an <see cref="EssentialsCardKind.LabelledEnum"/> card.
    /// Required on that kind and rejected on every other.
    /// </summary>
    public IReadOnlyList<EssentialsEnumOption>? LabelledOptions { get; init; }

    /// <summary>
    /// Evaluated on every value change to decide whether the standing danger banner shows.
    /// <see langword="null"/> on a card with no unsafe state.
    /// </summary>
    public Func<EssentialsCardViewModel, bool>? IsDangerPredicate { get; init; }

    /// <summary>Localised body of the danger banner.</summary>
    public string DangerBannerText { get; init; } = string.Empty;

    /// <summary>Localised body of the one-time amber callout.</summary>
    public string AmberCalloutText { get; init; } = string.Empty;

    /// <summary>
    /// True when the enum options are <i>suggestions</i> rather than a closed set, so the card
    /// renders an editable picker.
    /// </summary>
    public bool AllowsFreeForm { get; init; }

    /// <summary>
    /// Rich suggestions for a free-form picker — friendly label plus committed value.
    /// </summary>
    public IReadOnlyList<ModelSuggestionItem>? ModelSuggestions { get; init; }

    /// <summary>
    /// Dotted JSON path applied as a filter on the target editor when the deep link is followed.
    /// </summary>
    public string JsonPathFilter { get; init; } = string.Empty;
}
