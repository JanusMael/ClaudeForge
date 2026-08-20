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
}
