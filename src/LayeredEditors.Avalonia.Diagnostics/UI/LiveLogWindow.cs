using System.Diagnostics;
using System.Linq;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Logging;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.UI;

/// <summary>
/// Manages the floating live-log window toggled by F12.
/// <para>
/// The window ships in both Debug and Release builds. It stays hidden until
/// the user presses F12, so the steady-state overhead is a single
/// <see cref="Channel{T}"/>.<c>Writer.TryWrite</c> call per log event.
/// </para>
/// <para>
/// <strong>Layout:</strong> a header strip at the top of the window shows the
/// current on-disk log-file path reported by the backing
/// <see cref="BucketedRollingFileSink"/>. Clicking the path opens the file in
/// the OS default text editor; clicking "Open folder" opens the logs
/// directory. The rest of the window is a virtualized <see cref="ListBox"/> of
/// log lines.
/// </para>
/// <para>
/// <strong>Rendering model:</strong> log lines are stored in an
/// <see cref="AvaloniaList{T}"/> bound to a <see cref="ListBox"/> backed by
/// Avalonia's built-in <c>VirtualizingStackPanel</c>. Only the rows currently
/// visible in the viewport are rendered — layout cost is O(visible rows),
/// typically 30–50 items, regardless of total collection size.
/// </para>
/// <para>
/// <strong>Threading model:</strong> <see cref="EnqueueLog"/> is thread-safe
/// and non-blocking. The drain loop runs permanently on the thread pool
/// (<c>ConfigureAwait(false)</c> throughout) and coalesces writes over
/// <see cref="CoalesceMs"/> milliseconds before posting a single UI
/// collection update, capping layout work to ≤4 passes/second under heavy
/// logging. UI updates are fired via
/// <see cref="Dispatcher.UIThread"/>.<c>Post</c> at
/// <see cref="DispatcherPriority.Background"/>.
/// </para>
/// </summary>
public static class LiveLogWindow
{
    private const int ChannelCapacity = 5000;
    private const int MaxLinesInWindow = 2000;

    /// <summary>
    /// Coalescing window in milliseconds. Caps UI updates to ≤4/s so the
    /// ListBox layout never starves the main render loop under burst logging.
    /// </summary>
    private const int CoalesceMs = 250;

    // -----------------------------------------------------------------------
    // CRITICAL: no Avalonia-typed fields at static-field-initializer scope.
    //
    // LiveLogWindow's type initializer runs the first time ANY static member is
    // touched — which happens via the first Log.* call that routes through
    // LiveLogWindowSink.Emit → LiveLogWindow.EnqueueLog, BEFORE
    // BuildAvaloniaApp() has had a chance to register the Win32 dispatcher impl.
    // Constructing e.g. SolidColorBrush, AvaloniaList<T>, or any AvaloniaObject
    // at that instant forces Dispatcher.UIThread to resolve to the managed
    // fallback (ManagedDispatcherImpl), which does NOT implement
    // IControlledDispatcherImpl. Avalonia's ClassicDesktopStyleApplicationLifetime
    // later calls Dispatcher.MainLoop, which throws PlatformNotSupportedException
    // against the managed impl — crashing the app on startup before MainWindow
    // is shown.
    //
    // All Avalonia-typed objects are therefore created lazily inside BuildWindow()
    // (invoked from Initialize(), i.e. after platform init). Non-Avalonia
    // primitives (Channel<string>, primitive constants) may still live at
    // static-field scope — they don't trigger type loads that touch the dispatcher.
    // -----------------------------------------------------------------------

    private static readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        });

    // Header colour constants — stored as ARGB ints; the real IBrush objects are
    // built inside BuildHeader() on first Initialize().
    private const uint HeaderBackgroundArgb = 0xFF_28_28_28;
    private const uint HeaderBorderArgb = 0xFF_3E_3E_3E;
    private const uint HeaderLabelArgb = 0xFF_9E_9E_9E;
    private const uint HeaderLinkArgb = 0xFF_6A_B0_F3;
    private const uint WindowBackgroundArgb = 0xFF_1E_1E_1E;

    // All Avalonia-typed state is null until BuildWindow() runs. Modified only
    // on the UI thread (BuildWindow and the Dispatcher.UIThread.Post callbacks).
    private static AvaloniaList<string>? _logLines;
    private static Window? _window;
    private static ListBox? _logList;
    private static TextBlock? _logPathLink;
    private static string? _currentLogFilePath;
    private static bool _initialized;

    // Decoupling from the hard-coded fixed-path logger: the file sink + logs
    // directory are supplied by the bootstrap, so this window can display any
    // app's current-file path without knowing where the app chose to put logs.
    private static BucketedRollingFileSink? _fileSink;
    private static string? _logsDirectory;
    private static string _windowTitle = "Live Debug Logs — F12 to hide";

    // Optional host-supplied launch affordance rendered in the header (e.g. a
    // link that opens a second LiveTailWindow). The link renders only when BOTH
    // the label and the action are supplied to Initialize.
    private static string? _extraActionLabel;
    private static Action? _extraAction;

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Must be called once from the UI thread (e.g. at the end of
    /// <c>App.OnFrameworkInitializationCompleted</c>) before the first
    /// <see cref="ToggleWindow"/> call. Idempotent.
    /// </summary>
    /// <param name="fileSink">Optional rolling-file sink whose
    /// <see cref="BucketedRollingFileSink.CurrentFilePath"/> is used to populate
    /// the header "log file" link. When <c>null</c> the link is hidden — the
    /// window still displays live log lines, just without the file-open
    /// affordance.</param>
    /// <param name="logsDirectory">Optional directory shown by the "Open folder"
    /// link. When <c>null</c> the link is hidden.</param>
    /// <param name="windowTitle">Optional window title. Defaults to
    /// <c>"Live Debug Logs — F12 to hide"</c>.</param>
    /// <param name="extraActionLabel">Optional text for a header launch link (e.g.
    /// opening a second live-tail window). The link renders only when this AND
    /// <paramref name="extraAction"/> are both supplied.</param>
    /// <param name="extraAction">Optional callback invoked when the
    /// <paramref name="extraActionLabel"/> link is clicked.</param>
    public static void Initialize(
        BucketedRollingFileSink? fileSink = null,
        string? logsDirectory = null,
        string? windowTitle = null,
        string? extraActionLabel = null,
        Action? extraAction = null)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _fileSink = fileSink;
        _logsDirectory = logsDirectory;
        _extraActionLabel = extraActionLabel;
        _extraAction = extraAction;
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            _windowTitle = windowTitle;
        }

        BuildWindow();
        _ = ProcessLogsAsync();
    }

    /// <summary>
    /// Shows the log window if it is hidden; hides it if it is visible.
    /// When showing, <c>Window.Activate</c> is called so the window
    /// rises above the main window rather than opening behind it.
    /// Must be called from the UI thread (typically from <c>MainWindow.OnKeyDown</c>).
    /// </summary>
    public static void ToggleWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            // Refresh the file-path link each time the window comes up — the user
            // may have left it closed across a bucket boundary.
            RefreshLogPathLink();
            _window.Show();
            _window.Activate();
        }
    }

    /// <summary>
    /// Enqueues a rendered log message for display. Thread-safe. Never blocks
    /// or throws. Messages are silently dropped when the channel is full
    /// (oldest entry removed).
    /// </summary>
    public static void EnqueueLog(string message)
    {
        _channel.Writer.TryWrite(message);
    }

    // -----------------------------------------------------------------------
    // Window construction
    // -----------------------------------------------------------------------

    private static void BuildWindow()
    {
        // Construct Avalonia-typed state here — platform detection is now complete.
        _logLines = new AvaloniaList<string>();

        // Brushes are local to the method so they don't pin AvaloniaObject
        // lifetime beyond the Window; the Window + its children hold the only
        // references.
        SolidColorBrush headerBackground = new(Color.FromUInt32(HeaderBackgroundArgb));
        SolidColorBrush headerBorder = new(Color.FromUInt32(HeaderBorderArgb));
        SolidColorBrush headerLabel = new(Color.FromUInt32(HeaderLabelArgb));
        SolidColorBrush headerLink = new(Color.FromUInt32(HeaderLinkArgb));
        SolidColorBrush windowBackground = new(Color.FromUInt32(WindowBackgroundArgb));

        _logList = BuildLogList(windowBackground);
        Border header = BuildHeader(headerBackground, headerBorder, headerLabel, headerLink);

        DockPanel root = new() { LastChildFill = true };
        root.Children.Add(header); // Dock = Top (set below)
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(_logList); // Fills remaining space

        _window = new Window
        {
            Title = _windowTitle,
            Width = 900,
            Height = 500,
            MinWidth = 400,
            MinHeight = 200,
            Content = root,
            Background = windowBackground,
            ShowInTaskbar = true,
            ShowActivated = false,
        };

        _window.Hide();
        RefreshLogPathLink();
    }

    private static ListBox BuildLogList(IBrush background)
    {
        // Each row is an independent TextBlock rendered only while in the viewport.
        // We sync TextBlock.Text to the recycled DataContext via DataContextChanged
        // rather than a reflection-based {Binding} — this keeps the template
        // trim-safe (no IL2026 warning from Avalonia.Data.Binding's reflection-
        // binding pipeline) while still updating correctly when the ListBox
        // recycles row containers during virtualized scrolling.
        ListBox list = new()
        {
            ItemsSource = _logLines,
            Background = background,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            // Multiple so a user can Shift/Ctrl-select a range of lines and copy
            // them with Ctrl+C — a virtualized list has no free-text selection.
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<string>((_, _) =>
            {
                TextBlock tb = new()
                {
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    FontSize = 11,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(4, 0),
                };
                // Initial DataContext is set by the framework before this handler
                // is attached, so seed Text from it now, then track changes.
                tb.Text = tb.DataContext as string;
                tb.DataContextChanged += static (s, _) =>
                {
                    if (s is TextBlock t)
                    {
                        t.Text = t.DataContext as string;
                    }
                };
                return tb;
            }, supportsRecycling: true),
        };

        list.KeyDown += OnLogListKeyDown;

        // Right-click → Copy. Deliberately does NOT change the selection, so a
        // Ctrl+click discontiguous selection survives the right-click (the flow is
        // "Ctrl+click to select rows, then right-click → Copy").
        ContextMenu menu = new();
        MenuItem copyItem = new() { Header = "Copy" };
        copyItem.Click += (_, _) => CopySelectedRows(list);
        menu.Items.Add(copyItem);
        list.ContextMenu = menu;

        return list;
    }

    /// <summary>Ctrl+C copies the selected rows (see <see cref="CopySelectedRows"/>).</summary>
    private static void OnLogListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && sender is ListBox list)
        {
            CopySelectedRows(list);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Copies the selected rows' text to the clipboard in row order. Shared by the
    /// Ctrl+C key handler and the right-click "Copy" menu item. A virtualized
    /// ListBox has no free-text selection, so this (plus the header "Log file"
    /// link for the full file) is the copy affordance. Indexes come from the
    /// <c>ISelectionModel</c> so duplicate log strings — and Ctrl+click
    /// discontiguous selections — resolve to the correct rows.
    /// </summary>
    private static void CopySelectedRows(ListBox list)
    {
        if (_logLines is null)
        {
            return;
        }

        var indexes = list.Selection.SelectedIndexes;
        if (indexes.Count == 0)
        {
            return;
        }

        string text = string.Join(
            Environment.NewLine,
            indexes.OrderBy(i => i)
                   .Where(i => i >= 0 && i < _logLines.Count)
                   .Select(i => _logLines[i]));

        _ = TopLevel.GetTopLevel(list)?.Clipboard?.SetTextAsync(text);
    }

    private static Border BuildHeader(
        IBrush headerBackground,
        IBrush headerBorder,
        IBrush labelBrush,
        IBrush linkBrush)
    {
        StackPanel stack = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6),
            Spacing = 8,
        };

        // The "log file" link is only useful when a rolling-file sink was wired
        // up; otherwise the window runs sink-less (live view only) and we
        // suppress the header link entirely rather than showing an empty label.
        if (_fileSink is not null)
        {
            TextBlock label = new()
            {
                Text = "Log file:",
                Foreground = labelBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _logPathLink = MakeLink(
                text: string.Empty, // filled in by RefreshLogPathLink
                tooltip: "Open the current log file in the default editor",
                foreground: linkBrush,
                onClick: OnLogFileClicked);
            _logPathLink.FontFamily = new FontFamily("Consolas, Courier New, monospace");

            stack.Children.Add(label);
            stack.Children.Add(_logPathLink);
        }

        if (!string.IsNullOrEmpty(_logsDirectory))
        {
            if (_fileSink is not null)
            {
                TextBlock separator = new()
                {
                    Text = "·",
                    Foreground = labelBrush,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                stack.Children.Add(separator);
            }

            TextBlock folderLink = MakeLink(
                text: "Open folder",
                tooltip: "Open the logs directory in the OS file browser",
                foreground: linkBrush,
                onClick: OnFolderClicked);
            stack.Children.Add(folderLink);
        }

        // Host-supplied launch affordance (e.g. "open the config-file-events tail").
        // Invoked on click; captured into a local so the delegate doesn't re-read
        // the mutable static field.
        if (!string.IsNullOrEmpty(_extraActionLabel) && _extraAction is { } extraAction)
        {
            if (stack.Children.Count > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = "·",
                    Foreground = labelBrush,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            stack.Children.Add(MakeLink(
                text: _extraActionLabel!,
                tooltip: "Open the related live view",
                foreground: linkBrush,
                onClick: (_, _) => extraAction()));
        }

        return new Border
        {
            Background = headerBackground,
            BorderBrush = headerBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = stack,
        };
    }

    private static TextBlock MakeLink(
        string text,
        string tooltip,
        IBrush foreground,
        EventHandler<PointerPressedEventArgs> onClick)
    {
        TextBlock link = new()
        {
            Text = text,
            FontSize = 11,
            Foreground = foreground,
            Cursor = new Cursor(StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(link, tooltip);
        link.PointerPressed += onClick;
        return link;
    }

    // -----------------------------------------------------------------------
    // Header link actions
    // -----------------------------------------------------------------------

    private static void OnLogFileClicked(object? sender, PointerPressedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentLogFilePath))
        {
            OpenWithShell(_currentLogFilePath);
        }
    }

    private static void OnFolderClicked(object? sender, PointerPressedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_logsDirectory))
        {
            OpenWithShell(_logsDirectory);
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> with the OS-default handler. Swallows every
    /// failure — this is a convenience affordance; it must never crash the app
    /// or feed a log event back into the pipeline.
    /// </summary>
    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LiveLogWindow] Failed to open '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Recomputes the current bucket filename and updates the header link text
    /// iff it differs from the last seen value. Safe to call from every
    /// <see cref="AppendToLog"/> invocation — a no-op when the path is
    /// unchanged.
    /// </summary>
    private static void RefreshLogPathLink()
    {
        if (_logPathLink is null || _fileSink is null)
        {
            return;
        }

        string? path = _fileSink.CurrentFilePath
                       ?? (_logsDirectory is null
                           ? null
                           : Path.Combine(_logsDirectory, _fileSink.FileName(DateTime.UtcNow)));

        if (path is null || path == _currentLogFilePath)
        {
            return;
        }

        _currentLogFilePath = path;
        _logPathLink.Text = path;
    }

    // -----------------------------------------------------------------------
    // Drain loop — stays on the thread pool throughout
    // -----------------------------------------------------------------------

    private static async Task ProcessLogsAsync()
    {
        ChannelReader<string> reader = _channel.Reader;

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            // Coalesce: wait CoalesceMs so bursts are collapsed into a single UI
            // update. Without coalescing every WaitToReadAsync → TryRead → Post
            // cycle triggers a ListBox layout pass; 250 ms caps that to ≤4/s
            // under heavy logging.
            await Task.Delay(CoalesceMs).ConfigureAwait(false);

            // Drain ALL items currently in the channel into one batch — one
            // Post call means one pair of collection-change events (Add +
            // optional Remove), not one per message.
            List<string> batch = new();
            while (reader.TryRead(out string? msg))
            {
                batch.Add(msg);
            }

            if (batch.Count == 0)
            {
                continue;
            }

            // Post to the UI thread at Background priority so the UI thread
            // handles input, animations, and render passes first, then drains
            // our batches — keeps the app responsive even when logs are
            // flowing rapidly.
            //
            // Fire-and-forget via Post (not awaited): the drain loop continues
            // reading on the thread pool without waiting for the UI thread to
            // finish the previous batch, and the loop never gets "pinned" to
            // the Avalonia dispatcher.
            List<string> captured = batch;
            Dispatcher.UIThread.Post(
                () => AppendToLog(captured),
                DispatcherPriority.Background);
        }
    }

    // -----------------------------------------------------------------------
    // UI-thread update (always called from Post callback)
    // -----------------------------------------------------------------------

    private static void AppendToLog(List<string> batch)
    {
        // Both references are set in lockstep by BuildWindow(); either being
        // null here means Initialize() hasn't completed — drop the batch silently.
        if (_logList is null || _logLines is null)
        {
            return;
        }

        try
        {
            // Flatten every batch entry into individual display lines. Exception
            // stack traces embed newlines and must become separate list items so
            // virtualization can skip the off-screen ones.
            List<string> newLines = new(batch.Count * 2);
            foreach (string entry in batch)
            {
                string[] parts = entry.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    newLines.Add(part);
                }
            }

            if (newLines.Count == 0)
            {
                return;
            }

            // AddRange fires a single CollectionChanged(Add) event — one layout pass.
            _logLines.AddRange(newLines);

            // Cap to MaxLinesInWindow by evicting the oldest lines from the
            // front. RemoveRange also fires a single CollectionChanged(Remove)
            // event.
            if (_logLines.Count > MaxLinesInWindow)
            {
                _logLines.RemoveRange(0, _logLines.Count - MaxLinesInWindow);
            }

            // Scroll to the last item by index, not by object reference, so
            // duplicate log strings don't accidentally resolve to an earlier row.
            _logList.ScrollIntoView(_logLines.Count - 1);

            // Cheap: updates the header link only when the bucket has rolled.
            RefreshLogPathLink();
        }
        catch (Exception ex)
        {
            // Never feed a log error back into the logger (avoids log→window→log loop).
            Console.Error.WriteLine("[LiveLogWindow] AppendToLog error: " + ex.Message);
        }
    }
}