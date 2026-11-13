using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Converters;
using Bennewitz.Ninja.ClaudeForge.Diagnostics;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Save;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.Views;
using Bennewitz.Ninja.ScopedEditors.AvaloniaUI.Controls;
using Bennewitz.Ninja.AppServices.AvaloniaUI;
using Bennewitz.Ninja.AppServices;
using Bennewitz.Ninja.AppServices.Abstractions;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Wire the env-var description provider so that env-var tokens in property
        // descriptions show a useful hover tooltip (name + description + navigate hint).
        LinkifiedTextBlock.EnvVarDescriptionProvider = EnvVarTooltipConverter.GetDescription;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Re-apply culture here in addition to Program.Main() because Semi.Avalonia
        // lazy-loads its locale resource bundle (zh-cn / en-us) on first control
        // creation, which can happen on Avalonia's internal threads after framework
        // initialization. Calling ApplyCulture() again is idempotent and ensures any
        // such thread picks up the correct culture rather than the system default.
        LocalizationService.ApplyCulture();

        // Post-framework-init bootstrap:
        //   - BindingValidationErrorLogger.Install() — logs coercion errors that
        //     land in DataValidationErrors.ErrorsProperty (bypasses Avalonia.Logging)
        //   - then the F12 and Shift+F12 windows, hidden until their key is pressed.
        //     They stayed in ClaudeForge when the library moved, so it builds them itself.
        AvaloniaDiagnostics.InstallAvaloniaHooks();
        ClaudeForgeDiagnostics.InstallWindows();


        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No exception handlers here: AppServices' own, installed by InstallAvaloniaHooks()
            // above, own the UI thread and unobserved task exceptions. See Program.Main's remarks.

            // CreateWithNetwork, not `new`: a bare registry is OFFLINE by design, and this
            // line said `new()` from the initial commit. That was harmless while the chain
            // was bundled-first — bundled won for every product, so an HttpClient would
            // never have been reached. Network-first inverted it, and the parameterless
            // constructor became the difference between fetching and not. ClaudeForge kept
            // building its pages, and validating its saves, against bundled schemas.
            //
            // ONE registry serves the whole app: it is handed to both SDK clients in
            // MainWindowViewModel, so a schema is fetched once per launch rather than once
            // per consumer. Disposal stays with MainWindowViewModel, which now also disposes
            // the HttpClient this creates.
            //
            // The --schema-source override reaches this without being passed: Program.cs
            // sets SchemaRegistry.ProcessSourceOverride in step 1, and the constructor falls
            // back to it.
            // ⛔ The cache directory is supplied BY THE APP and there is no neutral default: the
            // registry writes the resolved schema artifact here, and ~/.claude is Claude's answer
            // to that question, not OpenCode's. A null directory means no disk cache at all,
            // which is what keeps every test off a real profile.
            // ⭐ THE composition root for the environment: read from the process exactly once,
            // here, and passed down as a value. Every path the app resolves hangs off this one
            // call, so a session cannot change its mind about where the config directory is
            // half way through — which is what a re-read per accessor would allow, and what a
            // save in flight cannot survive.
            ClaudeEnvironment claudeEnvironment = ClaudeEnvironment.FromProcess();

            // ⛔ Must happen before any update check. AppUpdateService is a static whose
            // coordinator reads the auto-check preference from the persisted UI state, and it
            // throws rather than falling back to the default home if this is skipped.
            AppUpdateService.Initialize(claudeEnvironment);

            SchemaRegistry schemaRegistry = SchemaRegistry.CreateWithNetwork(
                cacheDirectory: Path.Combine(
                    PlatformPaths.ClaudeHome(claudeEnvironment), "cache", "schemas"));
            AvaloniaDialogService dialogService = new();
            // The DialogAppIcon assignment below uses SmallInstance (64-px
            // render of the simplified small SVG) instead of Instance (256-px
            // detailed master); dialog titlebars scale the icon down to
            // ~16-32 px on every platform, where the simpler design reads
            // more clearly than the detailed master scaled by the same amount.
            //
            // Why Post instead of a direct assignment:
            // accessing AppIcon.SmallInstance synchronously here triggers the
            // first call to AppIcon.EnsureLoaded, which renders the large
            // 256-px PNG + the 64-px PNG + the 16-px PNG via SKSvg, all on the
            // dispatcher. The cost is ~one to several hundred ms depending on
            // the system, and it directly blocks first paint — it shows up
            // as the AppIcon.Ens... → SKSvg.C... block under
            // App.OnFrameworkInitializationCompleted in cold-start profiles.
            //
            // The dialog service only needs DialogAppIcon populated before a
            // dialog opens, and the earliest a dialog can open is in response
            // to a user gesture, which can't happen until MainWindow has
            // painted and the dispatcher is idle. So we Post the assignment
            // at Background priority — it lands on the dispatcher's next idle
            // tick, after first paint, with the SVG render running there
            // instead of in the synchronous startup path. Window.Icon (set
            // inside MainWindow.axaml.cs from AppIcon.Instance) does still
            // trigger the render at MainWindow construction time, but at
            // least the dialog-service slot doesn't double-charge for it.
            Dispatcher.UIThread.Post(
                () => dialogService.DialogAppIcon = AppIcon.SmallInstance,
                DispatcherPriority.Background);
            dialogService.RegisterSaveChangesDialog(async (prompt, window) =>
            {
                if (window is null)
                {
                    return false;
                }

                SaveChangesDialogViewModel vm = (SaveChangesDialogViewModel)prompt;
                SaveChangesDialog dialog = new() { DataContext = vm };
                return await dialog.ShowDialog<bool>(window);
            });

            MainWindow mainWindow = new();

            // ⚠ The share service needs nothing from the window. It used to: a #if for the
            // Windows TFM passed an hwndProvider closure so MAUI Essentials could anchor its
            // share flyout. That TFM never built, so the #else branch below is the only one that
            // has ever compiled — see DefaultShareService's remarks.
            IShareService shareService = new DefaultShareService();

            MainWindowViewModel mainVm = new(
                claudeEnvironment, schemaRegistry, dialogService, shareService);

            mainWindow.DataContext = mainVm;
            desktop.MainWindow = mainWindow;

            // close every helper window (today: the F12 LiveLogWindow)
            // when the main window closes.  Default is OnLastWindowClose, which
            // keeps the app process alive as long as ANY top-level window remains
            // visible — so a user who pressed F12 and then closed the main window
            // observed the log viewer staying open and the process not exiting.
            // The LiveLogWindow has no Owner (the diagnostics library is
            // host-agnostic by design), so the Owner-based auto-close path is
            // unavailable.  OnMainWindowClose is the SDI-app-canonical fix:
            // when the main window goes, the lifetime tears down all remaining
            // windows and shuts down the process.  Modal dialogs (About, Save
            // preview, …) already open via ShowDialog(this) and close
            // automatically with their parent, so this only affects the
            // standalone LiveLogWindow today.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Kick off async initialization after the window is shown
            desktop.Startup += async (_, _) => await mainVm.InitializeCommand.ExecuteAsync(null);
        }

        base.OnFrameworkInitializationCompleted();
    }
}