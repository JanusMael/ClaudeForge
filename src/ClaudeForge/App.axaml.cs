using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Bennewitz.Ninja.ClaudeForge.Converters;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ClaudeForge.Views;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Controls;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
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

        // One-line post-framework-init bootstrap:
        //   - LiveLogWindow.Initialize() — F12 window, hidden until F12 is pressed
        //   - BindingValidationErrorLogger.Install() — logs coercion errors that
        //     land in DataValidationErrors.ErrorsProperty (bypasses Avalonia.Logging)
        AvaloniaDiagnostics.InstallAvaloniaHooks();


        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Wire up global exception handlers before showing any UI so that
            // crashes during startup are also captured.
            Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            SchemaRegistry schemaRegistry = new();
            AvaloniaDialogService dialogService = new();
            // Propagate the app icon to all programmatic dialogs (alert, input, confirm)
            // so they all show the correct title-bar / taskbar icon without per-call changes.
            // uses SmallInstance (64-px render of the simplified
            // small SVG) instead of Instance (256-px detailed master); dialog
            // titlebars scale the icon down to ~16-32 px on every platform,
            // where the simpler design reads more clearly than the detailed
            // master scaled by the same amount.
            dialogService.DialogAppIcon = AppIcon.SmallInstance;
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

            // Create the main window first so its handle can be captured lazily
            // by DefaultShareService for the Windows HWND injection requirement.
            MainWindow mainWindow = new();

            // Build the share service. On Windows 10+ builds, the DefaultShareService
            // uses MAUI Essentials and requires the native HWND before each request —
            // captured via a closure so the handle is fetched at call time rather
            // than construction time (avoiding any ordering issues during startup).
#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER
            IShareService shareService = new DefaultShareService(
                hwndProvider: () => mainWindow.TryGetPlatformHandle()?.Handle ?? default);
#else
            IShareService shareService = new DefaultShareService();
#endif

            MainWindowViewModel mainVm = new(schemaRegistry, dialogService, shareService);

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

    // -----------------------------------------------------------------------
    // Global exception handlers
    // -----------------------------------------------------------------------

    private void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Mark as handled so Avalonia does not terminate the process immediately,
        // giving us a chance to show the fatal-error dialog.
        e.Handled = true;

        // Unwrap so the benign-check works the same way for both handler paths.
        AggregateException? inner = e.Exception is AggregateException agg ? agg.Flatten() : null;

        if (IsBenignLinuxInfrastructureException(e.Exception) ||
            (inner is not null && inner.InnerExceptions.All(IsBenignLinuxInfrastructureException)))
        {
            Log.Debug(e.Exception, "DBus/portal unavailable ({Source}) — ignored", "UIThread");
            return;
        }

        Log.Error(e.Exception, "Unhandled exception on {Source}", "UIThread");
        ShowCrashDialog(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Prevent the runtime from re-throwing on the finalizer thread (which would crash).
        e.SetObserved();
        AggregateException ex = e.Exception.Flatten();

        // OperationCanceledException (and its subclass TaskCanceledException) is normal
        // control flow for user-initiated cancellations — e.g., the user cancels a backup
        // or restore via the Cancel button.  If every inner exception is a cancellation,
        // log at Verbose and skip the crash dialog entirely to avoid alarming the user.
        if (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            Log.Verbose(ex, "Task cancelled silently ({Source})", "TaskScheduler");
            return;
        }

        // Avalonia's XDG portal layer (Tmds.DBus) throws when a portal service is not
        // present or not running on this desktop environment (e.g. Cosmic, minimal GNOME).
        // This is an infrastructure capability miss, not an app error — log at Debug and
        // skip the crash dialog so the user is not alarmed.
        if (ex.InnerExceptions.All(IsBenignLinuxInfrastructureException))
        {
            Log.Debug(ex, "DBus/portal unavailable ({Source}) — ignored", "TaskScheduler");
            return;
        }

        Log.Error(ex, "Unhandled exception on {Source}", "TaskScheduler");
        // Marshal to the UI thread so the dialog can open a window.
        Dispatcher.UIThread.Post(() => ShowCrashDialog(ex));
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ex"/> is a known benign
    /// Linux infrastructure miss — the XDG portal or DBus session service is absent
    /// or not running on this desktop environment.  These are capability-detection
    /// failures (the app tried to use a portal that is not installed), not bugs.
    /// </summary>
    /// <remarks>
    /// Detection strategy:
    /// <list type="bullet">
    ///   <item><c>Tmds.DBus.Protocol.DBusException</c> type name — the concrete exception
    ///         Avalonia's portal layer throws; type-name comparison avoids a hard package
    ///         reference to Tmds.DBus.</item>
    ///   <item>Message prefix <c>org.freedesktop.DBus.Error.*</c> — well-known DBus error
    ///         namespace (ServiceUnknown, NoReply, AccessDenied, …).</item>
    ///   <item>Message prefix <c>org.freedesktop.portal.*</c> — XDG portal error namespace
    ///         (e.g. org.freedesktop.portal.Error.NotAllowed on sandboxed desktops).</item>
    /// </list>
    /// The <see cref="OperatingSystem.IsLinux"/> guard on the message-prefix clauses is
    /// intentional: the type-name check is inherently Linux-specific, but the prefix
    /// checks are purely textual — the guard prevents a coincidentally named exception
    /// on Windows or macOS from being silently downgraded.
    /// </remarks>
    private static bool IsBenignLinuxInfrastructureException(Exception ex)
    {
        // Type check — catches the Tmds.DBus concrete type without a hard reference.
        if (ex.GetType().FullName == "Tmds.DBus.Protocol.DBusException")
        {
            return true;
        }

        // Message-prefix checks are textual → Linux-only guard.
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        return ex.Message.StartsWith("org.freedesktop.DBus.Error.", StringComparison.Ordinal) ||
               ex.Message.StartsWith("org.freedesktop.portal.", StringComparison.Ordinal);
    }

    private static void ShowCrashDialog(Exception exception)
    {
        const string message =
            "The application encountered an unexpected error.\n" +
            "You can copy the details below for bug reporting.";

        AvaloniaDiagnostics.ShowFatalErrorDialog(message, exception);
    }
}