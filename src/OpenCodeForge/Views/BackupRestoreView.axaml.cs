using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;

namespace Bennewitz.Ninja.OpenCodeForge.Views;

/// <summary>
/// Code-behind for the Backup / Restore page's DataGrid.
/// </summary>
/// <remarks>
/// The mirror of ClaudeForge's file of the same name, and the same four handlers: three action
/// buttons plus right-click row selection. Nothing here is product-shaped — it exists because
/// <c>{Binding $parent[DataGrid].DataContext.Command}</c> cannot be compiled (the AXAML compiler
/// types <c>DataContext</c> as <c>object?</c>), and an ancestor binding would resolve by reflection
/// and trip trim analysis.
/// </remarks>
public partial class BackupRestoreView : UserControl
{
    public BackupRestoreView()
    {
        InitializeComponent();
    }

    /// <remarks>
    /// Defence-in-depth against <see cref="BackupRowViewModel.IsRestorable"/>: the button's
    /// <c>IsEnabled</c> is bound to it, so a disabled row should never reach here, but Avalonia can
    /// dispatch a click that began before the <c>IsEnabled</c> flip landed. The race is currently
    /// unreachable — <c>IsRestorable</c> is computed from an immutable manifest — and the guard is
    /// what keeps a future mutator, or a programmatic invoke, from bypassing the row-level rule.
    /// </remarks>
    private void OnRestoreBackup(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
        {
            return;
        }

        if (btn.DataContext is not BackupRowViewModel { IsRestorable: true } row)
        {
            return;
        }

        if (btn.FindAncestorOfType<DataGrid>() is { DataContext: BackupRestoreViewModel vm })
        {
            vm.RestoreCommand.Execute(row);
        }
    }

    private void OnDeleteBackup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<DataGrid>() is { DataContext: BackupRestoreViewModel vm })
        {
            vm.DeleteCommand.Execute(btn.DataContext as BackupRowViewModel);
        }
    }

    private void OnShareBackup(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<DataGrid>() is { DataContext: BackupRestoreViewModel vm })
        {
            vm.ShareBackupCommand.Execute(btn.DataContext as BackupRowViewModel);
        }
    }

    /// <summary>
    /// Select the row under the pointer before its context menu opens, so "Open file location"
    /// acts on the row that was right-clicked rather than whatever was selected before.
    /// </summary>
    private void OnBackupDataGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not DataGrid dg)
        {
            return;
        }

        Visual? source = e.Source as Visual;
        while (source != null)
        {
            if (source is DataGridRow row && row.DataContext is BackupRowViewModel item)
            {
                dg.SelectedItem = item;
                break;
            }

            source = source.GetVisualParent();
        }
    }

    // Drag-drop of a backup .zip onto the Restore tab is wired declaratively in
    // BackupRestoreView.axaml via the LayeredEditors.Avalonia.Behaviors FileDrop attached
    // behaviour — it filters payloads to .zip during DragOver and invokes the command with the
    // local path, so there are no handlers for it here.
}
