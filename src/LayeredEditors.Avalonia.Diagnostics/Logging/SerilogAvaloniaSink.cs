using Avalonia.Logging;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;
using SerilogLevel = Serilog.Events.LogEventLevel;
using SerilogLog = Serilog.Log;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Logging;

/// <summary>
/// Bridges Avalonia's internal logging (<see cref="global::Avalonia.Logging.Logger"/>)
/// into the Serilog pipeline.
/// <para>
/// Avalonia exposes <see cref="global::Avalonia.Logging.Logger.Sink"/> as a single
/// pluggable entry point. Pointing it at an instance of this class causes every
/// Avalonia log event (binding errors, layout warnings, rendering diagnostics, etc.)
/// to be re-emitted on <see cref="SerilogLog.Logger"/>. From there the normal
/// Serilog sinks (file, Trace, F12 window, …) pick the events up.
/// </para>
/// <para>
/// Typical usage replaces the default <c>.LogToTrace()</c> call in the Avalonia
/// bootstrap — events still end up on <see cref="System.Diagnostics.Trace"/> if
/// the Serilog pipeline includes <c>WriteTo.Trace()</c>, so the debugger's Output
/// window remains populated.
/// </para>
/// <para>
/// <strong>Area muting.</strong> Avalonia's <c>Layout</c>, <c>Property</c>, and
/// <c>Visual</c> areas fire per measure/arrange/property-change pass at Information
/// level and saturate any log view during normal UI activity. By default this sink
/// suppresses those three areas below <see cref="AvaloniaLevel.Warning"/>. Warnings
/// and above still pass through so genuine problems aren't hidden. Pass a different
/// collection to the constructor to override.
/// </para>
/// </summary>
public sealed class SerilogAvaloniaSink : ILogSink
{
    /// <summary>
    /// Default set of Avalonia areas whose sub-Warning events are suppressed:
    /// <c>Layout</c>, <c>Property</c>, <c>Visual</c>. Rationale — each one fires
    /// at Information level on every layout/property/rendering pass and
    /// saturates log viewers during normal UI activity.
    /// </summary>
    public static IReadOnlyCollection<string> DefaultMutedAreas { get; } =
        ["Layout", "Property", "Visual"];

    private readonly HashSet<string> _mutedBelowWarning;

    /// <summary>
    /// Creates the sink. Pass <paramref name="mutedAreas"/> to override the
    /// default set of areas whose sub-Warning events are suppressed.
    /// </summary>
    /// <param name="mutedAreas">Areas whose events below
    /// <see cref="AvaloniaLevel.Warning"/> are dropped before reaching Serilog.
    /// Passing <c>null</c> (the default) uses <see cref="DefaultMutedAreas"/>.
    /// Pass an empty collection to disable muting entirely.</param>
    public SerilogAvaloniaSink(IReadOnlyCollection<string>? mutedAreas = null)
    {
        _mutedBelowWarning = new HashSet<string>(
            mutedAreas ?? DefaultMutedAreas,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns <c>false</c> for muted Avalonia areas below
    /// <see cref="AvaloniaLevel.Warning"/>, so they never even reach the Serilog
    /// pipeline. Warnings and errors always pass. All other areas defer to
    /// Serilog's own minimum-level filter.
    /// </summary>
    public bool IsEnabled(AvaloniaLevel level, string area)
    {
        if (level < AvaloniaLevel.Warning && _mutedBelowWarning.Contains(area))
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        SerilogLevel sLevel = Map(level);
        if (!SerilogLog.Logger.IsEnabled(sLevel))
        {
            return;
        }

        SerilogLog.Logger.Write(sLevel, "[Avalonia:{Area}] {Message}", area, messageTemplate);
    }

    /// <inheritdoc/>
    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate,
                    params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        SerilogLevel sLevel = Map(level);
        if (!SerilogLog.Logger.IsEnabled(sLevel))
        {
            return;
        }

        // Prepend Area to the template so Serilog's structured logging keeps the
        // {Area} property; then pass the caller's property values through.
        string combinedTemplate = "[Avalonia:{Area}] " + messageTemplate;
        object?[] combinedProps = new object?[propertyValues.Length + 1];
        combinedProps[0] = area;
        Array.Copy(propertyValues, 0, combinedProps, 1, propertyValues.Length);
        SerilogLog.Logger.Write(sLevel, combinedTemplate, combinedProps);
    }

    /// <summary>
    /// Maps Avalonia's <see cref="AvaloniaLevel"/> to Serilog's
    /// <see cref="SerilogLevel"/> (same names, different namespaces).
    /// Exposed as <c>internal static</c> so unit tests can exercise every
    /// branch without spinning up a real log sink.
    /// </summary>
    internal static SerilogLevel Map(AvaloniaLevel level)
    {
        return level switch
        {
            AvaloniaLevel.Verbose => SerilogLevel.Verbose,
            AvaloniaLevel.Debug => SerilogLevel.Debug,
            AvaloniaLevel.Information => SerilogLevel.Information,
            AvaloniaLevel.Warning => SerilogLevel.Warning,
            AvaloniaLevel.Error => SerilogLevel.Error,
            AvaloniaLevel.Fatal => SerilogLevel.Fatal,
            var _ => SerilogLevel.Information,
        };
    }
}