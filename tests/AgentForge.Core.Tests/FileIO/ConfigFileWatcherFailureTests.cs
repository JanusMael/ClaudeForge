using System.Reflection;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.FileIO;

/// <summary>
/// Covers <see cref="ConfigFileWatcher"/> when the operating system refuses one of its
/// watchers.
/// <para>
/// <b>Why it exists.</b> .NET reports a refused watch only through the watcher's
/// <see cref="FileSystemWatcher.Error"/> event — turning the watcher on throws nothing — and on
/// Linux every inotify watch a user may have can be in use: one application was measured holding
/// about 63,800 of 65,536, cycling through them for minutes at a time. Nothing handled
/// <c>Error</c>, so a file watched at such a moment stopped reporting external edits, and nothing
/// said so.
/// </para>
/// <para>
/// The watcher's real <c>Error</c> event is raised here through its protected <c>OnError</c>, with
/// the watcher's own events turned off, so only the poll can see the change; and the clock is a
/// <see cref="FakeTimeProvider"/>, so nothing sleeps.
/// </para>
/// </summary>
public sealed class ConfigFileWatcherFailureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cfw_fail_" + Guid.NewGuid().ToString("N"));

    public ConfigFileWatcherFailureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a temp folder left behind costs nothing a later test can see.
        }
    }

    [Fact]
    public async Task AFailedWatcher_IsLoggedOnce_AndTheFileIsPolledInstead()
    {
        string file = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(file, "{}", TestContext.Current.CancellationToken);

        FakeTimeProvider time = new();
        CapturingSink sink = new();
        ILogger log = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        TimeSpan debounce = TimeSpan.FromMilliseconds(10);

        using ConfigFileWatcher watcher = new(debounce, time, log);
        TaskCompletionSource<string> reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.FileChanged += (_, path) => reported.TrySetResult(path);
        watcher.Watch(file);

        FileSystemWatcher inner = watcher.WatcherFor(file)
            ?? throw new InvalidOperationException("Watch made no watcher for " + file);
        inner.EnableRaisingEvents = false;
        IOException refused = new("The configured user limit (65536) on the number of inotify watches has been reached");
        RaiseError(inner, refused);
        RaiseError(inner, refused);

        await File.WriteAllTextAsync(file, """{"model": "opus"}""", TestContext.Current.CancellationToken);
        // Moved well past the old stamp, so a coarse file-system clock cannot hide the write.
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddSeconds(5));

        time.Advance(ConfigFileWatcher.PollInterval);
        time.Advance(debounce);

        await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        MessageAssert.Equal(true, reported.Task.IsCompletedSuccessfully,
            "A file whose watcher failed is still reported when it changes - by the poll that replaced "
            + "the watcher, through the same debounce - rather than going silent for the session.");
        MessageAssert.Equal(file, await reported.Task, "The change is reported for the file that moved.");

        IReadOnlyList<LogEvent> events = sink.Events;
        MessageAssert.Equal(1, events.Count,
            "The failure is logged once for the file, however often the watcher reports it.");
        MessageAssert.Equal(LogEventLevel.Warning, events[0].Level,
            "A warning: the file is still followed, by the poll, so nothing is lost.");
        MessageAssert.Contains("fs.inotify.max_user_watches", events[0].RenderMessage(),
            "The log names the limit, which is what a user on Linux can act on.");
    }

    /// <summary>Raises <paramref name="watcher"/>'s <c>Error</c> event the way it does itself.</summary>
    private static void RaiseError(FileSystemWatcher watcher, Exception reason) =>
        typeof(FileSystemWatcher)
            .GetMethod("OnError", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(watcher, [new ErrorEventArgs(reason)]);

    /// <summary>Keeps every event; a watcher may report its failure from its own thread.</summary>
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_events)
            {
                _events.Add(logEvent);
            }
        }
    }
}
