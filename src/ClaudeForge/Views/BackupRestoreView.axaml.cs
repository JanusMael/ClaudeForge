using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class BackupRestoreView : UserControl
{
    public BackupRestoreView()
    {
        InitializeComponent();
    }

    // -----------------------------------------------------------------------
    // Backup action buttons
    // Code-behind handlers for the DataGrid action buttons.
    // {Binding $parent[DataGrid].DataContext.Command} cannot be compiled because
    // DataContext is typed as object? at the AXAML compiler level, so these event
    // handlers provide a trim-safe alternative.
    // -----------------------------------------------------------------------

    private void OnRestoreBackup(object? sender, RoutedEventArgs e)
    {
        // L3 (2026-05-14): defence-in-depth re-check against
        // IsRestorable.  The Restore button's IsEnabled is bound to
        // IsRestorable so disabled rows shouldn't fire this handler,
        // but Avalonia can dispatch a click that started before the
        // IsEnabled flip lands.  Since IsRestorable is computed from
        // immutable Entry.Manifest.Mode the actual race is currently
        // unreachable in practice — but the early-return below makes
        // sure a future mutator (or a programmatic command invoke)
        // can't accidentally bypass the row-level guard.
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

    // -----------------------------------------------------------------------
    // Right-click row selection
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ensures that right-clicking a DataGrid row selects it before the context menu
    /// opens, so the "Open file location" menu item always acts on the row under the
    /// pointer rather than whatever was previously selected.
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

        // Walk up the visual tree from the event source to find the DataGridRow.
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

    // Drag-drop of a backup .zip onto the Restore tab is wired declaratively
    // in BackupRestoreView.axaml via the LayeredEditors.Avalonia.Behaviors
    // FileDrop attached behaviour:
    //
    //     behaviors:FileDrop.AllowedExtensions="zip"
    //     behaviors:FileDrop.DropCommand="{Binding RestoreFromDroppedArchiveCommand}"
    //
    // The behaviour filters payloads to .zip in DragOver (sets the OS cursor
    // to accepted/rejected) and invokes the command with the local file path
    // as parameter; no code-behind handlers needed here.
}