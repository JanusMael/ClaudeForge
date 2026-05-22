using System.Text;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Platform-specific dialog services (file/folder pickers, alerts).
/// Implemented by the Avalonia host.
/// </summary>
public interface IDialogService
{
    /// <summary>Show a folder picker. Returns the selected path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync(string? title = null);

    /// <summary>Show a file open picker. Returns the selected path, or null if cancelled.</summary>
    Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null);

    /// <summary>
    /// Show a file <i>save</i> picker. Returns the selected destination path, or <c>null</c> if
    /// cancelled. Platform UI typically warns the user if the chosen name already exists.
    /// </summary>
    /// <param name="title">Window title, e.g. "Save Backup As".</param>
    /// <param name="defaultFileName">Suggested initial filename including extension.</param>
    /// <param name="filters">Optional format filters; the first is preselected.</param>
    Task<string?> PickSaveFileAsync(
        string? title,
        string defaultFileName,
        IReadOnlyList<FilePickerFilter>? filters = null);

    /// <summary>Show an alert/error dialog.</summary>
    Task ShowAlertAsync(string title, string message);

    /// <summary>
    /// Rich-message overload of <see cref="ShowAlertAsync(string, string)"/>.
    /// Renders <paramref name="message"/> as inline runs (text / bold /
    /// monospace path with click-to-copy / accent hyperlink with shell-open).
    /// <paramref name="category"/> selects header colour and glyph;
    /// <see cref="DialogCategory.Error"/> renders a red error header.
    /// </summary>
    /// <remarks>
    /// Default implementation flattens the segments to plain text and
    /// delegates to <see cref="ShowAlertAsync(string, string)"/> so test
    /// doubles and other lightweight implementations stay compiling.
    /// Real renderers (e.g. <c>AvaloniaDialogService</c>) override this.
    /// </remarks>
    Task ShowAlertAsync(
        string title,
        DialogMessage message,
        DialogCategory category = DialogCategory.Information)
    {
        return ShowAlertAsync(title, FlattenSegments(message));
    }

    /// <summary>
    /// Show a single-line text input dialog.
    /// Returns the trimmed text the user entered, or <c>null</c> if cancelled or left empty.
    /// </summary>
    Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null);

    /// <summary>
    /// Show a yes/no confirmation dialog.
    /// <paramref name="confirmLabel"/> is the label on the affirmative button (default "Confirm").
    /// <paramref name="cancelLabel"/> is the label on the cancel button (default "Cancel").
    /// </summary>
    /// <returns>
    /// Three-valued result:
    /// <list type="bullet">
    ///   <item><c>true</c>  — user clicked the confirm button.</item>
    ///   <item><c>false</c> — user clicked the cancel button (or pressed
    ///         Escape, since the cancel button has <c>IsCancel = true</c>).</item>
    ///   <item><c>null</c>  — user dismissed the dialog via the window
    ///         close (X) without choosing.  Callers MUST treat this as
    ///         "abort whatever flow this dialog was a step of," NEVER
    ///         as a synonym for cancel-side proceed.  This is the load-
    ///         bearing distinction for trinary prompts (e.g. the
    ///         "include / omit / X-to-abort" credentials prompt on the
    ///         Backup tab).
    ///   </item>
    /// </list>
    /// return type changed from <c>Task&lt;bool&gt;</c> to
    /// <c>Task&lt;bool?&gt;</c> to surface the X-close case.
    /// </returns>
    Task<bool?> ShowConfirmAsync(string title, string message,
                                 string confirmLabel = "Confirm",
                                 string cancelLabel = "Cancel");

    /// <summary>
    /// Rich-message + category overload of
    /// <see cref="ShowConfirmAsync(string, string, string, string)"/>.
    /// Renders <paramref name="message"/> as inline runs.
    /// <paramref name="category"/> selects header colour, glyph, and confirm
    /// button styling — pass <see cref="DialogCategory.Destructive"/> for
    /// irreversible actions so the confirm button rendered in danger style.
    /// </summary>
    /// <remarks>
    /// Same three-valued return as the four-string overload: <c>true</c>
    /// confirm, <c>false</c> cancel button, <c>null</c> X-close (abort).
    /// Default implementation flattens the segments to plain text and
    /// delegates to the four-string overload so test doubles stay
    /// compiling.  Real renderers override this.
    /// </remarks>
    Task<bool?> ShowConfirmAsync(
        string title,
        DialogMessage message,
        DialogCategory category = DialogCategory.Confirmation,
        string confirmLabel = "Confirm",
        string cancelLabel = "Cancel")
    {
        return ShowConfirmAsync(title, FlattenSegments(message), confirmLabel, cancelLabel);
    }

    /// <summary>
    /// Flatten a <see cref="DialogMessage"/>'s segments into a plain string
    /// for the default implementation fallback.  Path and hyperlink segments
    /// keep their visible text; the URL of a hyperlink is appended in
    /// parentheses so the user can still see where the link would have gone.
    /// </summary>
    private static string FlattenSegments(DialogMessage message)
    {
        StringBuilder sb = new();
        foreach (DialogSegment seg in message.Segments)
        {
            sb.Append(seg.Value);
            if (seg.Kind == DialogSegmentKind.Hyperlink && !string.IsNullOrEmpty(seg.Url))
            {
                sb.Append(" (").Append(seg.Url).Append(')');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Show the rich "Save Changes" dialog that presents pending workspace diffs in
    /// bordered, copyable text boxes with per-entry and bulk copy-to-clipboard actions.
    /// The concrete dialog is supplied by the host application via
    /// <see cref="AvaloniaDialogService.RegisterSaveChangesDialog"/>.
    /// Returns <c>true</c> when the user clicks Save, <c>false</c> on Cancel.
    /// </summary>
    Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt);

    /// <summary>
    /// three-button modal for the "you have unsaved changes,
    /// close anyway?" prompt that fires when the user clicks the window's
    /// X (or invokes Alt+F4) with pending edits.  Distinct from
    /// <see cref="ShowSaveChangesDialogAsync"/>: that one previews the
    /// per-change diff inside a normal save flow; this one is a fast
    /// three-way decision shown from the window-close hook.
    /// </summary>
    /// <remarks>
    /// Same X-close-aborts principle as <see cref="ShowConfirmAsync(string,string,string,string)"/>:
    /// dismissing the dialog via X returns <see cref="UnsavedChangesChoice.Cancel"/>
    /// so the caller keeps the window open.  Pre-fix the OnClosing
    /// handler didn't exist at all — closing the window with unsaved
    /// edits silently exited.
    /// </remarks>
    Task<UnsavedChangesChoice> ShowUnsavedChangesAsync(string title, DialogMessage message)
    {
        return Task.FromResult(UnsavedChangesChoice.Cancel);
    }
}

/// <summary>
/// outcome of the unsaved-changes-on-close modal.
/// </summary>
public enum UnsavedChangesChoice
{
    /// <summary>User clicked Save — save then close.</summary>
    Save = 0,

    /// <summary>User clicked Don't Save — close without saving, edits lost.</summary>
    DontSave = 1,

    /// <summary>
    /// User clicked Cancel OR dismissed via X — keep the window open.
    /// Universal "X never proceeds" principle.
    /// </summary>
    Cancel = 2,
}

/// <summary>File type filter for file pickers.</summary>
public sealed record FilePickerFilter(string Name, IReadOnlyList<string> Extensions);