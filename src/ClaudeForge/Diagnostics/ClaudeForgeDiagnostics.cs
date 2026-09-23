using Bennewitz.Ninja.AppServices.AvaloniaUI;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Diagnostics;

/// <summary>
/// The F12 live-log window and the Shift+F12 config-file-event window, wired by hand.
/// </summary>
/// <remarks>
/// <para>
/// Both windows stayed in this repository when the diagnostics library moved to
/// <c>Bennewitz.Ninja.AppServices</c>, and that package's <see cref="AvaloniaDiagnostics"/> ships
/// without the wiring that fed them: no live-log sink in its pipeline, no window construction,
/// and an <see cref="AvaloniaDiagnostics.EnqueueEvent"/> that writes the event FILE only. Moved
/// as they were, both windows would open and stay empty — with nothing failing. This class puts
/// back exactly what was removed, and nothing else.
/// </para>
/// <para>
/// ⛔ <b>Call <see cref="EnqueueEvent"/>, never <see cref="AvaloniaDiagnostics.EnqueueEvent"/>.</b>
/// The package method still compiles and still writes the file, so calling it directly empties the
/// Shift+F12 window silently. <c>ClaudeForgeDiagnosticsWiringTests</c> fails if anything else calls it.
/// </para>
/// </remarks>
internal static class ClaudeForgeDiagnostics
{
    // Moved verbatim from Program.cs, where they were AvaloniaDiagnosticsOptions values.
    private const string EventTailWindowTitle = "Live Config-File Events — Shift+F12 to hide";
    private const string EventTailLaunchLabel = "Config-file events ▸";

    private static ILogger? _packageLogger;
    private static string? _logsDirectory;
    private static LiveTailWindow? _eventWindow;

    /// <summary>
    /// Feeds the F12 window from the logging pipeline. Call IMMEDIATELY after
    /// <see cref="AvaloniaDiagnostics.ConfigureLogging"/>, before any other code runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The package publishes its pipeline as <see cref="Log.Logger"/>; this wraps it in a
    /// sub-logger that also writes to the window, the same arrangement the pre-extraction
    /// library built internally. Idempotent.
    /// </para>
    /// <para>
    /// ⚠ <b>Order matters.</b> Anything that captured <see cref="Log.Logger"/> before this call —
    /// a <c>static readonly</c> <c>ForContext</c> field initialised early — keeps the package's
    /// logger, and its events never reach F12.
    /// </para>
    /// <para>
    /// ⚠ <b>The wrapped logger is not owned by the wrapper</b>: Serilog does not dispose a logger
    /// passed to <c>WriteTo.Logger</c>. <see cref="CloseAndFlush"/> disposes it explicitly, or the
    /// rolling file could lose the lines written last — the part a post-mortem needs most.
    /// </para>
    /// </remarks>
    /// <param name="options">The options passed to <see cref="AvaloniaDiagnostics.ConfigureLogging"/>.</param>
    public static void AttachLiveLogWindow(AvaloniaDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_packageLogger is not null)
        {
            return;
        }

        _packageLogger = Log.Logger;
        _logsDirectory = options.LogsDirectory;

        Log.Logger = new LoggerConfiguration()
                     .MinimumLevel.Is(options.MinimumLevel)
                     .Enrich.FromLogContext()
                     .WriteTo.Logger(_packageLogger)
                     .WriteTo.Sink(new LiveLogWindowSink())
                     .CreateLogger();
    }

    /// <summary>
    /// Builds both windows. Call from <c>App.OnFrameworkInitializationCompleted</c>, after
    /// <see cref="AvaloniaDiagnostics.InstallAvaloniaHooks"/> — earlier and the windows would
    /// touch the dispatcher before it exists.
    /// </summary>
    public static void InstallWindows()
    {
        _eventWindow ??= new LiveTailWindow(EventTailWindowTitle);

        LiveLogWindow.Initialize(
            logFilePath: static () => AvaloniaDiagnostics.CurrentLogFilePath,
            logsDirectory: _logsDirectory,
            extraActionLabel: EventTailLaunchLabel,
            extraAction: _eventWindow.Toggle);
    }

    /// <summary>
    /// Appends one line to the Shift+F12 window AND to the event log file. Thread-safe and
    /// non-blocking; before <see cref="InstallWindows"/> has run, the line reaches the file only.
    /// </summary>
    public static void EnqueueEvent(string line)
    {
        _eventWindow?.Enqueue(line);
        AvaloniaDiagnostics.EnqueueEvent(line);
    }

    /// <summary>Shows or hides the F12 live-log window. UI thread only.</summary>
    public static void ToggleLiveLogWindow() => LiveLogWindow.ToggleWindow();

    /// <summary>Shows or hides the Shift+F12 event window. UI thread only.</summary>
    public static void ToggleEventTailWindow() => _eventWindow?.Toggle();

    /// <summary>
    /// Flushes and closes logging. Use in place of <see cref="Log.CloseAndFlush"/>: it also
    /// disposes the package's pipeline, which the wrapper does not own.
    /// </summary>
    public static void CloseAndFlush()
    {
        ILogger? packageLogger = _packageLogger;
        _packageLogger = null;
        Log.CloseAndFlush();
        (packageLogger as IDisposable)?.Dispose();
    }
}
