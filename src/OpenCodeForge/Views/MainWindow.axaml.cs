using Avalonia.Controls;
using AvWindowState = Avalonia.Controls.WindowState;
using SavedWindowState = Bennewitz.Ninja.OpenCodeForge.Services.WindowState;
using Bennewitz.Ninja.OpenCodeForge.Services;

namespace Bennewitz.Ninja.OpenCodeForge.Views;

/// <summary>The application window.</summary>
/// <remarks>
/// ⚠ Both names are aliased. Avalonia has its own <c>WindowState</c> enum, and this app has a
/// record of the same name for persisted geometry; unaliased, whichever using came last would win
/// and the resulting error points at the property rather than at the collision.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>Construct the window, restoring its remembered geometry.</summary>
    public MainWindow()
    {
        InitializeComponent();

        // Set in code rather than AXAML: AppIcon caches one WindowIcon for every window, and a
        // null (the asset failed to load) has to leave Icon alone rather than clear it.
        if (AppIcon.Instance is { } icon)
        {
            Icon = icon;
        }

        SavedWindowState remembered = WindowStateService.Load();
        Width = remembered.Width;
        Height = remembered.Height;
        if (remembered.IsMaximized)
        {
            WindowState = AvWindowState.Maximized;
        }

        Closing += (_, _) => WindowStateService.Save(
            new SavedWindowState(Width, Height, WindowState == AvWindowState.Maximized));
    }

    /// <summary>
    /// Opens the About dialog from the status-bar version button.
    /// </summary>
    /// <remarks>
    /// The view-model owns the registry and the nav tree, so it — not the dialog — runs the
    /// schema check and re-badges. A null DataContext (design-time, or a harness showing the
    /// window without a view-model) simply hides that row.
    /// </remarks>
    private async void OnVersionLabelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AboutDialog about = DataContext is ViewModels.MainWindowViewModel vm
            ? new AboutDialog(vm.CheckForSchemaUpdatesAsync)
            : new AboutDialog();

        await about.ShowDialog(this);
    }
}
