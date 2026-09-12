using Avalonia.Controls;
using Avalonia.Interactivity;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.Views;

/// <summary>
/// Code-behind for the footprint page's per-row Reveal button.
/// </summary>
/// <remarks>
/// <para>
/// A handler rather than a command binding for the reason the Backup page's file states: an
/// ancestor binding cannot be compiled (the AXAML compiler types <c>DataContext</c> as
/// <c>object?</c>) and would resolve by reflection, tripping trim analysis.
/// </para>
/// <para>
/// ⚠ <b>The row carries everything needed, so this never reaches for the page.</b> Unlike the
/// Backup page's handlers there is no ancestor walk here — the path being revealed is the row's
/// own — which keeps the one behaviour on this page independent of where the button is nested.
/// </para>
/// </remarks>
public partial class FootprintView : UserControl
{
    public FootprintView()
    {
        InitializeComponent();
    }

    private void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: OpenCodeFootprintRowViewModel row })
        {
            return;
        }

        Log.Information(
            "[Footprint.Command] action=Reveal category={Category}", row.Category.Id);

        // ⚠ Reveals a path that may not exist: a category measuring zero is still a category the
        // walk checked, and its row stays on the page so the user knows it was. ShellLauncher
        // swallows a failed launch by design, so a missing directory is a no-op rather than a
        // crash — the alternative, hiding the button, would read as the row being broken.
        ShellLauncher.Instance.RevealInFileManager(row.AbsolutePath);
    }
}
