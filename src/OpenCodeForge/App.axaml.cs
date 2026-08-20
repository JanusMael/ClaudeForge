using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;
using Bennewitz.Ninja.OpenCodeForge.Views;

namespace Bennewitz.Ninja.OpenCodeForge;

/// <summary>The Avalonia application.</summary>
public class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        AvaloniaDiagnostics.InstallAvaloniaHooks();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindowViewModel vm = new();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Load after the window exists so the first paint is not blocked on disk and schema
            // work. Failures surface in the view-model's status text rather than as a crash
            // before anything is on screen.
            _ = vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
