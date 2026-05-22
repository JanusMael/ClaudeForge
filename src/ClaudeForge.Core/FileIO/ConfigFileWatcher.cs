namespace Bennewitz.Ninja.ClaudeForge.Core.FileIO;

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
/// <para>The watcher lives in <c>ClaudeForge.Core</c> which has no Avalonia
/// dependency, so marshaling cannot happen here; it is delegated to subscribers
/// by design. The canonical subscriber pattern is
/// <c>Dispatcher.UIThread.Post(() => ...)</c>.</para>
/// </remarks>
public sealed class ConfigFileWatcher : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly TimeSpan _debounce;
    private readonly Dictionary<string, CancellationTokenSource> _pending = new();
    private readonly Lock _lock = new();
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
    {
        _debounce = debounce == TimeSpan.Zero ? TimeSpan.FromMilliseconds(400) : debounce;
    }

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
                EnableRaisingEvents = true,
            };

            watcher.Changed += (_, _) => OnChanged(filePath);
            watcher.Created += (_, _) => OnChanged(filePath);
            watcher.Deleted += (_, _) => OnChanged(filePath);
            watcher.Renamed += (_, _) => OnChanged(filePath);

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

            _ = Task.Delay(_debounce, cts.Token).ContinueWith(t =>
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
            foreach (CancellationTokenSource cts in _pending.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pending.Clear();
        }
    }
}