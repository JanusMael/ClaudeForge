using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Logging;
using Serilog.Events;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics;

/// <summary>
/// Configuration for the one-line <see cref="AvaloniaDiagnostics"/> bootstrap.
/// Every property has a safe default — the minimum required input is
/// <see cref="AppName"/>. Pass instances to
/// <see cref="AvaloniaDiagnostics.ConfigureLogging"/>.
/// </summary>
/// <remarks>
/// <para>
/// The default pipeline wires up:
/// </para>
/// <list type="bullet">
///   <item>A <see cref="BucketedRollingFileSink"/> writing to
///         <see cref="LogsDirectory"/>.</item>
///   <item><c>Serilog.Sinks.Trace</c> so the debugger Output window keeps working.</item>
///   <item>A <see cref="Logging.LiveLogWindowSink"/> feeding the F12 live-log window
///         (controlled by <see cref="EnableLiveLogWindow"/>).</item>
///   <item>A <see cref="SerilogAvaloniaSink"/> bridging Avalonia's internal
///         logger into the same pipeline (controlled by
///         <see cref="BridgeAvaloniaLogger"/> and <see cref="MutedAvaloniaAreas"/>).</item>
/// </list>
/// </remarks>
public sealed class AvaloniaDiagnosticsOptions
{
    /// <summary>
    /// Application name used in crash-dialog titles and the default live-log
    /// window title. Required — there is no sensible default, and every
    /// consumer needs a distinct value to avoid confusing dialogs.
    /// </summary>
    public required string AppName { get; init; }

    /// <summary>
    /// Directory where log files are written. Required. The directory is
    /// created on bootstrap if it does not already exist. Callers typically
    /// use a per-user OS-conventional location (LOCALAPPDATA / Library/Logs /
    /// XDG_STATE_HOME).
    /// </summary>
    public required string LogsDirectory { get; init; }

    /// <summary>
    /// Minimum Serilog level. Defaults to
    /// <see cref="LogEventLevel.Information"/>. Set lower for verbose
    /// diagnostic captures; set higher to reduce log volume in production.
    /// </summary>
    public LogEventLevel MinimumLevel { get; init; } = LogEventLevel.Information;

    /// <summary>
    /// Rolling-bucket size (must be a whole number of hours that divides 24 —
    /// 1, 2, 3, 4, 6, 8, 12, 24). Defaults to
    /// <see cref="BucketedRollingFileSink.DefaultBucketSize"/> (8 hours).
    /// </summary>
    public TimeSpan BucketSize { get; init; } = BucketedRollingFileSink.DefaultBucketSize;

    /// <summary>
    /// How long old log files are retained. Defaults to
    /// <see cref="BucketedRollingFileSink.DefaultRetention"/> (3 days).
    /// </summary>
    public TimeSpan Retention { get; init; } = BucketedRollingFileSink.DefaultRetention;

    /// <summary>
    /// Filename prefix for rolling log files. Must not contain <c>-</c>,
    /// whitespace, or path separators. Defaults to
    /// <see cref="BucketedRollingFileSink.DefaultFileNamePrefix"/> (<c>"app"</c>).
    /// </summary>
    public string FileNamePrefix { get; init; } = BucketedRollingFileSink.DefaultFileNamePrefix;

    /// <summary>
    /// Include a <c>Serilog.Sinks.Trace</c> sink in the pipeline so log events
    /// are visible in the debugger's Output window. Defaults to <c>true</c>.
    /// </summary>
    public bool EnableTraceSink { get; init; } = true;

    /// <summary>
    /// Include the F12 <see cref="UI.LiveLogWindow"/> sink in the pipeline.
    /// Defaults to <c>true</c>. Set <c>false</c> to ship a build without the
    /// live-log window affordance.
    /// </summary>
    public bool EnableLiveLogWindow { get; init; } = true;

    /// <summary>
    /// Window title used for the F12 live-log window. Defaults to
    /// <c>"Live Debug Logs — F12 to hide"</c>. Overridable so each host app
    /// can brand its own window.
    /// </summary>
    public string? LiveLogWindowTitle { get; init; }

    /// <summary>
    /// Bridge Avalonia's internal logger (<c>Avalonia.Logging.Logger.Sink</c>)
    /// into Serilog so binding errors, layout warnings, etc. reach the same
    /// sinks. Defaults to <c>true</c>. Incompatible with
    /// <c>AppBuilder.LogToTrace()</c> — do not enable both, or Avalonia events
    /// will be double-emitted to Trace.
    /// </summary>
    public bool BridgeAvaloniaLogger { get; init; } = true;

    /// <summary>
    /// Avalonia logging areas whose below-<see cref="LogEventLevel.Warning"/>
    /// events are suppressed before reaching Serilog. Defaults to
    /// <see cref="SerilogAvaloniaSink.DefaultMutedAreas"/>
    /// (<c>Layout</c>, <c>Property</c>, <c>Visual</c>) — these saturate the
    /// log under normal UI activity. Pass an empty collection to disable
    /// muting entirely.
    /// </summary>
    public IReadOnlyCollection<string>? MutedAvaloniaAreas { get; init; }

    /// <summary>
    /// Install the attached-property class handler that logs every
    /// <see cref="global::Avalonia.Controls.DataValidationErrors.ErrorsProperty"/>
    /// change (catches binding coercion errors that bypass
    /// <see cref="global::Avalonia.Data.Core.Plugins.IDataValidationPlugin"/>).
    /// Defaults to <c>true</c>. Must be called after Avalonia framework init —
    /// handled by <see cref="AvaloniaDiagnostics.InstallAvaloniaHooks"/>.
    /// </summary>
    public bool EnableBindingValidationLogger { get; init; } = true;
}