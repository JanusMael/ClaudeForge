using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

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

    // ── Computed labels bound in AXAML ──────────────────────────────────────

    /// <summary>Window title — "Save Changes" or "Restore Preview" (localized).</summary>
    public string WindowTitle =>
        Mode == SaveDialogMode.Restore
            ? Strings.DialogTitleRestorePreview
            : Strings.DialogTitleSaveChanges;

    /// <summary>Primary confirm button label — "Save" or "Restore" (localized).</summary>
    public string ConfirmButtonLabel =>
        Mode == SaveDialogMode.Restore
            ? Strings.ButtonRestore
            : Strings.ButtonSaveDialog;

    /// <summary>
    /// Cancel button label — "Cancel" in both Save and Restore contexts.
    /// <para>
    /// was "Discard Changes" in Save mode (with the intent that
    /// pressing it discarded the in-memory edits via ReloadAsync), but user
    /// feedback ("I wasn't expecting a reload to occur when pressing it and
    /// I wouldn't expect my changes to disappear") established that the
    /// natural user expectation is "Cancel returns me to my edits".  The
    /// save-flow path now matches: pressing Cancel here just dismisses the
    /// dialog without touching the in-memory workspace state.
    /// </para>
    /// </summary>
    public string CancelButtonLabel => Strings.ButtonCancel;

    /// <summary>
    /// Whether to show this dialog the next time the user saves.
    /// Bound to the "Show this dialog on save" checkbox; defaults to <c>true</c>.
    /// The caller reads this after the dialog closes and persists the preference.
    /// </summary>
    public bool ShowDialogAgain { get; set; } = true;

    /// <summary>
    /// One-line summary at the top of the dialog telling the user how many
    /// individual changes are about to be applied and how many distinct files
    /// will be touched. Phrasing varies by <see cref="Mode"/>:
    /// <list type="bullet">
    ///   <item><c>Save</c>:    "Saving N change(s) across M file(s):"</item>
    ///   <item><c>Restore</c>: "Restoring N change(s) across M file(s):"</item>
    /// </list>
    /// </summary>
    public string SummaryLine
    {
        get
        {
            int changeCount = Sections.Sum(s => s.Entries.Count);
            int fileCount = Sections.Count;
            string? template = Mode == SaveDialogMode.Restore
                ? Strings.TextRestoreSummary
                : Strings.TextSaveSummary;
            return string.Format(template, changeCount, fileCount);
        }
    }

    /// <summary>
    /// Verb phrase shown above each section's destination path. Localized via
    /// <see cref="Strings.LabelWillBeWrittenTo"/> /
    /// <see cref="Strings.LabelWillBeRestoredTo"/>. Sections read this from
    /// their <see cref="SaveChangeSectionViewModel.ActionVerb"/> property,
    /// which is wired to its parent dialog's <c>Mode</c> at construction time.
    /// </summary>
    public string ActionVerb =>
        Mode == SaveDialogMode.Restore
            ? Strings.LabelWillBeRestoredTo
            : Strings.LabelWillBeWrittenTo;

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
    /// Absolute (or <c>~</c>-prefixed display) path of the file that this
    /// section's changes will be written to in Save mode, or restored to in
    /// Restore mode. Populated by <c>MainWindowViewModel</c> from each dirty
    /// <see cref="SettingsDocument.FilePath"/>; empty string if the path was
    /// not provided. Bound directly into the section header in AXAML.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Verb phrase shown above the destination path (e.g. "Will be written to:"
    /// in Save mode, "Will be restored to:" in Restore mode). Set by the
    /// builder so the section can render the path label without needing a
    /// reference back to its parent <see cref="SaveChangesDialogViewModel"/>.
    /// </summary>
    public string ActionVerb { get; init; } = Strings.LabelWillBeWrittenTo;
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

    /// <summary>
    /// Tooltip + screen-reader label for the kind pill.  The pill's visible
    /// content is a single +/-/~ glyph in a coloured square — meaningless to a
    /// blind user without context, and even sighted users may not know that
    /// '~' means "modified".  ToolTip.Tip + AutomationProperties.Name both
    /// bind to this so hover-discoverable AND screen-reader accessible.
    /// </summary>
    public string KindAccessibleName => Kind switch
    {
        ChangeKind.Added => Strings.SaveDialogKindAdded,
        ChangeKind.Removed => Strings.SaveDialogKindRemoved,
        var _ => Strings.SaveDialogKindModified,
    };
}