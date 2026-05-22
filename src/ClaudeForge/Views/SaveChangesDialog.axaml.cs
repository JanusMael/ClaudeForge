using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// Code-behind for the save-confirmation dialog.
/// Handles clipboard access (which requires a TopLevel reference not available in AXAML)
/// and routes the user's Save / Cancel decision back to the caller via <see cref="ShowDialog{T}"/>.
/// </summary>
public partial class SaveChangesDialog : Window
{
    public SaveChangesDialog()
    {
        InitializeComponent();
        // switched from AppIcon.Instance (256-px detailed
        // master) to AppIcon.SmallInstance (64-px render of the
        // simplified small SVG).  Dialog titlebars scale the icon down
        // to ~16-32 px on every platform, where the simpler small-SVG
        // design reads more clearly than the detailed master scaled by
        // the same amount.  Main window keeps Instance because the
        // taskbar/dock icon there is rendered larger.
        Icon = AppIcon.SmallInstance;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnCopyAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SaveChangesDialogViewModel vm)
        {
            return;
        }

        IClipboard? clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(vm.ChangesOnlyText);
        }
    }

    /// <summary>
    /// Per-cell "Copy" context-menu handler shared by both Old-value and
    /// New-value cells.  The MenuItem's <c>Tag</c> picks which side to copy:
    /// <c>"FullOld"</c> → <see cref="SaveChangeEntryViewModel.FullOldValue"/>,
    /// <c>"FullNew"</c> → <see cref="SaveChangeEntryViewModel.FullNewValue"/>.
    /// </summary>
    /// <remarks>
    /// We always copy the un-truncated full value because the displayed text
    /// is capped at 80 chars by <c>SaveDialogBuilder.TruncateJson</c> — a
    /// "Copy" that returned the truncated form would silently lose data and
    /// surprise the user.  Mouse-drag selection + Ctrl+C still copies just
    /// the displayed (truncated) text via <see cref="SelectableTextBlock"/>'s
    /// built-in handling, so users who genuinely want a partial copy still
    /// have that path.
    /// </remarks>
    private async void OnCopyCellValue(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        if (menuItem.DataContext is not SaveChangeEntryViewModel entry)
        {
            return;
        }

        IClipboard? clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        string? text = (menuItem.Tag as string) switch
        {
            "FullOld" => entry.FullOldValue,
            "FullNew" => entry.FullNewValue,
            var _ => null,
        };

        if (!string.IsNullOrEmpty(text))
        {
            await clipboard.SetTextAsync(text);
        }
    }
}