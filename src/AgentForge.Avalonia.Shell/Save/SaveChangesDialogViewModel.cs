using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk.Diagnostics;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Save;

/// <summary>Controls the terminology shown in the save/restore confirmation dialog.</summary>
public enum SaveDialogMode
{
    Save,
    Restore
}

/// <summary>
/// Data model for the save-confirmation dialog.
/// Contains one <see cref="SaveChangeSectionViewModel"/> for each settings file that
/// has pending changes, each of which holds the list of individual property diffs.
/// </summary>
public sealed class SaveChangesDialogViewModel : ISaveChangesPrompt
{
    public IReadOnlyList<SaveChangeSectionViewModel> Sections { get; init; } = [];

    /// <summary>
    /// Controls the dialog title and button labels.
    /// Use <see cref="SaveDialogMode.Restore"/> when the dialog is shown as part of a
    /// pre-restore save so the terminology matches the action being taken.
    /// </summary>
    public SaveDialogMode Mode { get; init; } = SaveDialogMode.Save;

    /// <summary>
    /// The host's wording. <c>required</c> on purpose — a dialog with no words is not
    /// a sensible default, and inheriting another product's would be worse.
    /// </summary>
    public required SaveDialogText Text { get; init; }

    // ── Computed labels bound in AXAML ──────────────────────────────────────

    /// <summary>Window title — the host's save or restore title.</summary>
    public string WindowTitle => Text.TitleFor(Mode);

    /// <summary>Primary confirm button label.</summary>
    public string ConfirmButtonLabel => Text.ConfirmButtonFor(Mode);

    /// <summary>
    /// Dismiss button label — the same in both Save and Restore contexts.
    /// <para>
    /// This was "Discard Changes" in Save mode (with the intent that pressing it
    /// discarded the in-memory edits via a reload), but user feedback ("I wasn't
    /// expecting a reload to occur when pressing it and I wouldn't expect my changes
    /// to disappear") established that the natural expectation is "Cancel returns me
    /// to my edits". The save flow now matches: pressing Cancel dismisses the dialog
    /// without touching the in-memory workspace state.
    /// </para>
    /// </summary>
    public string CancelButtonLabel => Text.CancelButton;

    /// <summary>
    /// Whether to show this dialog the next time the user saves.
    /// Bound to the "Show this dialog on save" checkbox; defaults to <c>true</c>.
    /// The caller reads this after the dialog closes and persists the preference.
    /// </summary>
    public bool ShowDialogAgain { get; set; } = true;

    /// <summary>
    /// One-line summary at the top of the dialog telling the user how many individual
    /// changes are about to be applied and how many distinct files will be touched.
    /// The phrasing (and whether the two modes differ at all) is the host's.
    /// </summary>
    public string SummaryLine
    {
        get
        {
            int changeCount = Sections.Sum(s => s.Entries.Count);
            int fileCount = Sections.Count;
            return string.Format(Text.SummaryFormatFor(Mode), changeCount, fileCount);
        }
    }

    /// <summary>
    /// Verb phrase shown above each section's destination path. Sections read this
    /// from their own <see cref="SaveChangeSectionViewModel.ActionVerb"/>, which the
    /// builder wires from this dialog's <see cref="Mode"/>.
    /// </summary>
    public string ActionVerb => Text.ActionVerbFor(Mode);

    /// <summary>
    /// How many pending changes are writing a value the product calls unsafe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Counts <see cref="DangerAssessment.IsDangerNow"/>, not the tier.</b> Most of a real
    /// save touches settings that merely *could* matter — a per-entry dot says that, and it is the
    /// right density for a list. A headline is only worth interrupting someone with when the value
    /// being written is actually the boundary-weakening one, and this is the last screen before it
    /// reaches disk.
    /// </para>
    /// <para>
    /// ⚠ Deliberately not a max-severity: "2 changes weaken a boundary" is actionable, whereas
    /// "highest severity: Critical" restates what the dots already show.
    /// </para>
    /// </remarks>
    public int UnsafeChangeCount =>
        Sections.Sum(s => s.Entries.Count(e => e.Danger.IsDangerNow));

    /// <summary>Whether to show the unsafe-change headline at all.</summary>
    public bool HasUnsafeChanges => UnsafeChangeCount > 0;

    /// <summary>
    /// The headline itself, or the empty string when nothing pending is unsafe.
    /// </summary>
    public string UnsafeChangeWarning =>
        HasUnsafeChanges ? string.Format(Text.UnsafeChangeWarningFormat, UnsafeChangeCount) : string.Empty;

    /// <summary>
    /// Plain-text representation of all changes — one line per entry, no descriptions.
    /// Used by the "Copy changes to clipboard" button.
    /// </summary>
    public string ChangesOnlyText =>
        string.Join("\n\n", Sections.Select(s =>
            $"{s.Title}:\n" + string.Join("\n", s.Entries.Select(e => e.ChangeLine))));
}

/// <summary>One group of changes from a single settings document (workspace + scope).</summary>
public sealed class SaveChangeSectionViewModel
{
    public string WorkspaceName { get; init; } = string.Empty;
    public string ScopeText { get; init; } = string.Empty;
    public ConfigScope? Scope { get; init; }
    public string Title => $"{WorkspaceName}  —  {ScopeText} scope";
    public IReadOnlyList<SaveChangeEntryViewModel> Entries { get; init; } = [];

    /// <summary>
    /// Absolute (or <c>~</c>-prefixed display) path of the file that this section's
    /// changes will be written to in Save mode, or restored to in Restore mode.
    /// Empty string when the path was not provided. Bound directly into the section
    /// header in AXAML.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Verb phrase shown above the destination path, so the section can render the
    /// path label without a reference back to its parent dialog.
    /// <para>
    /// <c>required</c> rather than defaulted to the save wording: a section that
    /// silently claims "will be written to" inside a <em>restore</em> preview is
    /// exactly the mistake a default invites, and the builder always knows the mode.
    /// </para>
    /// </summary>
    public required string ActionVerb { get; init; }
}

/// <summary>One changed property within a section.</summary>
public sealed class SaveChangeEntryViewModel
{
    public ChangeKind Kind { get; init; }
    public string Key { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }

    /// <summary>
    /// Full, untruncated old value — used as the hover-tooltip source so the user can
    /// read the complete JSON when <see cref="OldValue"/> is truncated with "…".
    /// <c>null</c> when the value was not present before the change.
    /// </summary>
    public string? FullOldValue { get; init; }

    /// <summary>
    /// Full, untruncated new value — used as the hover-tooltip source so the user can
    /// read the complete JSON when <see cref="NewValue"/> is truncated with "…".
    /// <c>null</c> when the value is being removed by the change.
    /// </summary>
    public string? FullNewValue { get; init; }

    /// <summary>
    /// Tooltip + screen-reader label for the kind pill, from
    /// <see cref="SaveDialogText.AccessibleNameFor"/>.
    /// <para>
    /// <c>required</c> because the alternative failure is silent: the pill renders a
    /// bare glyph, and an empty automation name reads to a screen reader as nothing
    /// at all. A compile error is the only thing that reliably prevents that.
    /// </para>
    /// </summary>
    public required string KindAccessibleName { get; init; }

    /// <summary>
    /// How much the setting this change touches matters, assessed at the scope of the file being
    /// written and over the value that will be on disk once it is.
    /// </summary>
    /// <remarks>
    /// ⭐ Defaults to <see cref="DangerAssessment.Unremarkable"/> — no dot — so a caller with no
    /// policy (tests, a product with no table) renders exactly as this dialog always has.
    /// </remarks>
    public DangerAssessment Danger { get; init; } = DangerAssessment.Unremarkable;

    /// <summary>
    /// Whether to render a severity dot. Named to match
    /// <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel.HasDangerSeverity"/>
    /// so the markup is the same shape on every danger surface.
    /// </summary>
    public bool HasDangerSeverity => Danger.Explanation is not null;

    /// <summary>Tier plus consequence, for the tooltip and the screen reader.</summary>
    /// <remarks>
    /// ⛔ Goes on <c>AutomationProperties.HelpText</c>, never <c>Name</c>: on a <c>TextBlock</c>
    /// the <c>Text</c> wins and an explicit <c>Name</c> is ignored outright.
    /// </remarks>
    public string DangerAccessibleText =>
        Danger.Explanation is null ? string.Empty : $"{Danger.Severity}: {Danger.Explanation}";

    /// <summary>Human-readable one-liner shown in the bordered textbox.</summary>
    public string FormattedText => Kind switch
    {
        ChangeKind.Added => $"+ {Key}: {NewValue ?? "(null)"}",
        ChangeKind.Removed => $"− {Key}  (removed)",
        ChangeKind.Modified => $"~ {Key}:  {OldValue ?? "(null)"}  →  {NewValue ?? "(null)"}",
        var _ => Key,
    };

    /// <summary>
    /// One-liner used by "Copy All" clipboard output.
    /// Includes old/new values for every change kind so the clipboard text is
    /// self-contained — the Removed case in particular carries the old JSON blob.
    /// </summary>
    public string ChangeLine => Kind switch
    {
        ChangeKind.Added => $"+ {Key}: {NewValue ?? "(null)"}",
        ChangeKind.Removed => $"− {Key}: {OldValue ?? "(null)"}  (removed)",
        ChangeKind.Modified => $"~ {Key}:  {OldValue ?? "(null)"}  →  {NewValue ?? "(null)"}",
        var _ => Key,
    };

    /// <summary>Prefix character for the coloured kind pill in the UI.</summary>
    public string KindLabel => Kind switch
    {
        ChangeKind.Added => "+",
        ChangeKind.Removed => "-", // plain ASCII so all fonts render it correctly
        var _ => "~",
    };

    /// <summary>Background colour for the kind pill — green/red/orange matching the change type.</summary>
    public string KindBackground => Kind switch
    {
        ChangeKind.Added => "#2E7D32",
        ChangeKind.Removed => "#C62828",
        // Modified — Material Orange 700.  Was #E65100 (Orange 900) which is
        // named "orange" but R230/G81/B0 visually reads as red — easy to
        // confuse with the #C62828 "removed" pill.  #F57C00 keeps the
        // Material palette + has a clearly more orange hue.
        var _ => "#F57C00",
    };
}
