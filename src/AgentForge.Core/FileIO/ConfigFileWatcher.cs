using Serilog;

namespace Bennewitz.Ninja.AgentForge.Core.FileIO;

/// <summary>
/// Watches a set of config files for external changes and raises a debounced
/// <see cref="FileChanged"/> event.
/// </summary>
/// <remarks>
/// <para><b>Threading contract.</b> The <see cref="FileChanged"/> event is raised
/// from a thread-pool thread (<see cref="TaskScheduler.Default"/>), not from any
/// UI dispatcher. Subscribers that touch UI state (Avalonia controls,
/// ObservableCollection mutation, view-model properties bound to the UI) MUST
/// marshal the work onto their UI thread before doing so — failing to do so
/// can throw <see cref="InvalidOperationException"/> in DataGrid and other
/// thread-affined controls, and produce hard-to-reproduce races elsewhere.</para>
/// <para>The watcher lives in <c>AgentForge.Core</c> which has no Avalonia
/// dependency, so marshaling cannot happen here; it is delegated to subscribers
/// by design. The canonical subscriber pattern is
/// <c>Dispatcher.UIThread.Post(() => ...)</c>.</para>
/// <para><b>A watch the operating system refuses is replaced by a poll.</b> On Linux,
/// .NET reports a <see cref="FileSystemWatcher"/> whose inotify watch the kernel refused
/// only through the watcher's <see cref="FileSystemWatcher.Error"/> event — turning it on
/// throws nothing — and every watch a user may have can be in use: one application was
/// measured holding about 63,800 of 65,536. Such a file used to stop reporting changes,
/// silently. Now it is checked every <see cref="PollInterval"/> instead, and one warning
/// per file names the limit.</para>
/// </remarks>
public sealed class ConfigFileWatcher : IDisposable
{
    /// <summary>How often a file whose watcher failed is checked instead.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, FileStamp> _polled = new();
    private readonly TimeSpan _debounce;
    private readonly TimeProvider _time;
    private readonly ILogger? _log;
    private readonly Dictionary<string, CancellationTokenSource> _pending = new();
    private readonly Lock _lock = new();
    private ITimer? _poll;
    private bool _disposed;

    /// <summary>
    /// Raised after a watched file changes and the debounce window elapses.
    /// </summary>
    /// <remarks>
    /// <b>This event is raised on a thread-pool thread.</b> See the class
    /// remarks for the marshaling contract subscribers must honour.
    /// </remarks>
    public event EventHandler<string>? FileChanged;

    public ConfigFileWatcher(TimeSpan debounce = default)
        : this(debounce, TimeProvider.System, log: null)
    {
    }

    /// <summary>
    /// A watcher on <paramref name="time"/>, which the debounce and the poll both run on, so a
    /// test advances a clock rather than sleeping, and writing to <paramref name="log"/> when one
    /// is given rather than to <see cref="Log.Logger"/>. Internal, so the published surface does
    /// not grow for a test's sake.
    /// </summary>
    internal ConfigFileWatcher(TimeSpan debounce, TimeProvider time, ILogger? log)
    {
        ArgumentNullException.ThrowIfNull(time);
        _debounce = debounce == TimeSpan.Zero ? TimeSpan.FromMilliseconds(400) : debounce;
        _time = time;
        _log = log;
    }

    // Read when used rather than when built, so a watcher made before logging is configured
    // still writes where the application's log goes.
    private ILogger Logger => _log ?? Log.Logger;

    /// <summary>Watch a specific file path for changes.</summary>
    public void Watch(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        string name = Path.GetFileName(filePath);

        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
        {
            return;
        }

        // Create directory if it doesn't exist so FSW can be set up
        // (don't create the file itself — it will be created on first save)
        if (!Directory.Exists(dir))
        {
            return;
        }

        lock (_lock)
        {
            if (_watchers.ContainsKey(filePath))
            {
                return;
            }

            FileSystemWatcher watcher = new(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };

            watcher.Changed += (_, _) => OnChanged(filePath);
            watcher.Created += (_, _) => OnChanged(filePath);
            watcher.Deleted += (_, _) => OnChanged(filePath);
            watcher.Renamed += (_, _) => OnChanged(filePath);
            watcher.Error += (_, e) => OnWatcherFailed(filePath, e.GetException());

            // Turned on once every handler is attached, so a change in between is not lost.
            watcher.EnableRaisingEvents = true;

            _watchers[filePath] = watcher;
        }
    }

    /// <summary>Stop watching a file.</summary>
    public void Unwatch(string filePath)
    {
        lock (_lock)
        {
            if (_watchers.TryGetValue(filePath, out FileSystemWatcher? w))
            {
                w.Dispose();
                _watchers.Remove(filePath);
            }

            if (_polled.Remove(filePath) && _polled.Count == 0)
            {
                _poll?.Dispose();
                _poll = null;
            }
        }
    }

    /// <summary>The watcher on <paramref name="filePath"/>, for a test to raise its <c>Error</c> as the kernel would.</summary>
    internal FileSystemWatcher? WatcherFor(string filePath)
    {
        lock (_lock)
        {
            return _watchers.GetValueOrDefault(filePath);
        }
    }

    /// <summary>
    /// <paramref name="filePath"/>'s watcher can no longer be trusted, so the file is checked on
    /// the clock from now on, and the log says why — once for the file, however often the
    /// watcher reports it.
    /// </summary>
    private void OnWatcherFailed(string filePath, Exception reason)
    {
        lock (_lock)
        {
            if (_disposed || !_watchers.ContainsKey(filePath) || _polled.ContainsKey(filePath))
            {
                return;
            }

            _polled[filePath] = FileStamp.Of(filePath);
            _poll ??= _time.CreateTimer(_ => Poll(), null, PollInterval, PollInterval);
        }

        Logger.Warning(reason,
            "[Watcher] {Path} cannot be watched any more, so it is checked every {Interval} instead. "
            + "On Linux this is usually every inotify watch the user may have being in use "
            + "(fs.inotify.max_user_watches).",
            filePath, PollInterval);
    }

    /// <summary>
    /// On the clock's thread: a polled file whose stamp has moved is reported as its watcher
    /// would have reported it, through the same debounce.
    /// </summary>
    private void Poll()
    {
        List<string> moved = [];
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            foreach ((string path, FileStamp before) in _polled.ToArray())
            {
                FileStamp now = FileStamp.Of(path);
                if (now != before)
                {
                    _polled[path] = now;
                    moved.Add(path);
                }
            }
        }

        foreach (string path in moved)
        {
            OnChanged(path);
        }
    }

    private void OnChanged(string filePath)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(filePath, out CancellationTokenSource? prev))
            {
                prev.Cancel();
                prev.Dispose();
            }

            CancellationTokenSource cts = new();
            _pending[filePath] = cts;

            _ = Task.Delay(_debounce, _time, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled)
                {
                    return;
                }

                // Capture the handler snapshot inside the lock so we hold an atomic
                // reference to the current delegate list.  Invoking outside the lock
                // avoids calling potentially-blocking subscribers while holding it.
                EventHandler<string>? handler;
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return; // watcher was disposed while the debounce was pending
                    }

                    _pending.Remove(filePath);
                    handler = FileChanged;
                }

                handler?.Invoke(this, filePath);
            }, TaskScheduler.Default);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (FileSystemWatcher w in _watchers.Values)
            {
                w.Dispose();
            }

            _watchers.Clear();
            _poll?.Dispose();
            _poll = null;
            _polled.Clear();
            foreach (CancellationTokenSource cts in _pending.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pending.Clear();
        }
    }

    /// <summary>What a poll compares: whether the file exists, when it was last written, and how long it is.</summary>
    private readonly record struct FileStamp(bool Exists, DateTime LastWriteUtc, long Length)
    {
        public static FileStamp Of(string path)
        {
            FileInfo info = new(path);
            return info.Exists ? new FileStamp(true, info.LastWriteTimeUtc, info.Length) : default;
        }
    }
}
