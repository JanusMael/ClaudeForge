using Bennewitz.Ninja.AppServices.AvaloniaUI;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Diagnostics;

/// <summary>
/// The F12 live-log window and the Shift+F12 config-file-event window, and what feeds them.
/// </summary>
/// <remarks>
/// <para>
/// Both windows stayed in this repository when the diagnostics library moved to
/// <c>Bennewitz.Ninja.AppServices</c>. That package ships no viewer and binds no key; it offers two
/// extension points on <see cref="AvaloniaDiagnosticsOptions"/> instead, and this class supplies
/// both: <see cref="ConfigureLogger"/> adds the live-log sink to its pipeline, and
/// <see cref="OnEventLine"/> receives every <see cref="AvaloniaDiagnostics.EnqueueEvent"/> line.
/// Without them both windows would open and stay empty — with nothing failing.
/// </para>
/// <para>
/// ⓘ Hosts call <see cref="AvaloniaDiagnostics.EnqueueEvent"/> directly: the package forwards each
/// line here as well as writing it to the event file, so there is no second entry point to get wrong.
/// </para>
/// </remarks>
internal static class ClaudeForgeDiagnostics
{
    // Moved verbatim from Program.cs, where they were AvaloniaDiagnosticsOptions values before the
    // package stopped owning the windows.
    private const string EventTailWindowTitle = "Live Config-File Events — Shift+F12 to hide";
    private const string EventTailLaunchLabel = "Config-file events ▸";

    private static LiveTailWindow? _eventWindow;

    /// <summary>
    /// For <see cref="AvaloniaDiagnosticsOptions.ConfigureLogger"/>: adds the F12 window's sink to
    /// the package's pipeline, after its built-in sinks and before the logger is created — so it
    /// sees exactly what the rolling file sees.
    /// </summary>
    /// <remarks>
    /// Runs inside <see cref="AvaloniaDiagnostics.ConfigureLogging"/>, before
    /// <c>BuildAvaloniaApp</c>. That is safe: the sink only writes to a static channel, and
    /// <see cref="LiveLogWindow"/> builds nothing Avalonia-typed until <see cref="InstallWindows"/>.
    /// </remarks>
    public static void ConfigureLogger(LoggerConfiguration configuration)
        => configuration.WriteTo.Sink(new LiveLogWindowSink());

    /// <summary>
    /// For <see cref="AvaloniaDiagnosticsOptions.EventListener"/>: appends one line to the Shift+F12
    /// window. Called on the enqueuing thread, possibly concurrently —
    /// <see cref="LiveTailWindow.Enqueue"/> is thread-safe and non-blocking. Before
    /// <see cref="InstallWindows"/> has run there is no window, and the line reaches the file only.
    /// </summary>
    public static void OnEventLine(string line) => _eventWindow?.Enqueue(line);

    /// <summary>
    /// Builds both windows. Call from <c>App.OnFrameworkInitializationCompleted</c>, after
    /// <see cref="AvaloniaDiagnostics.InstallAvaloniaHooks"/> — earlier and the windows would touch
    /// the dispatcher before it exists.
    /// </summary>
    public static void InstallWindows()
    {
        _eventWindow ??= new LiveTailWindow(EventTailWindowTitle);

        LiveLogWindow.Initialize(
            logFilePath: static () => AvaloniaDiagnostics.CurrentLogFilePath,
            logsDirectory: AvaloniaDiagnostics.LogsDirectory,
            extraActionLabel: EventTailLaunchLabel,
            extraAction: _eventWindow.Toggle);
    }

    /// <summary>Shows or hides the F12 live-log window. UI thread only.</summary>
    public static void ToggleLiveLogWindow() => LiveLogWindow.ToggleWindow();

    /// <summary>Shows or hides the Shift+F12 event window. UI thread only.</summary>
    public static void ToggleEventTailWindow() => _eventWindow?.Toggle();
}
