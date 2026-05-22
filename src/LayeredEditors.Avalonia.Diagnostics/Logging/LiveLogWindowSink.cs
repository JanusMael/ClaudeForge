using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.UI;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Logging;

/// <summary>
/// A Serilog <see cref="ILogEventSink"/> that enqueues rendered log messages
/// into the floating F12 <see cref="UI.LiveLogWindow"/>'s bounded channel.
/// <para>
/// Internal by design — this sink is an implementation detail of the
/// <see cref="UI.LiveLogWindow"/> pump. Consumers wire it up indirectly by
/// calling <see cref="AvaloniaDiagnostics.ConfigureLogging"/>, which adds an
/// instance to the Serilog pipeline when
/// <see cref="AvaloniaDiagnosticsOptions.EnableLiveLogWindow"/> is set.
/// </para>
/// <para>
/// The target channel is bounded and configured with
/// <see cref="System.Threading.Channels.BoundedChannelFullMode.DropOldest"/>, so
/// this sink never blocks the logging call-site and never causes memory growth
/// even if the window is not being drained. The steady-state cost when the F12
/// window is hidden is a single non-blocking channel <c>TryWrite</c>.
/// </para>
/// </summary>
internal sealed class LiveLogWindowSink : ILogEventSink
{
    private static readonly MessageTemplateTextFormatter _formatter =
        new("{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            formatProvider: null);

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
    {
        StringWriter writer = new();
        _formatter.Format(logEvent, writer);
        LiveLogWindow.EnqueueLog(writer.ToString().TrimEnd());
    }
}