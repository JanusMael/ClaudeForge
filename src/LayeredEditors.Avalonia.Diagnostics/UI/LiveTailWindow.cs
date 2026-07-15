using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.UI;

/// <summary>
/// A floating, instantiable "live tail" window: an auto-scrolling, selectable
/// text view of short lines fed from any thread via <see cref="Enqueue"/>.
/// <para>
/// Shares <see cref="LiveLogWindow"/>'s ingest model — a bounded
/// <see cref="Channel{T}"/> feeds a thread-pool drain loop that coalesces bursts
/// over <see cref="CoalesceMs"/> ms into a single batched UI update — but renders
/// into a single <see cref="SelectableTextBlock"/> (a natural "tail of text" with
/// free selection + Ctrl+C copy) rather than a virtualized ListBox. That is the
/// right trade for LOW-VOLUME, EPHEMERAL streams (e.g. debounced file-watcher
/// hits): there is no backing file to open, so in-window copy matters, and the
/// buffer is capped at <see cref="MaxLines"/> so the non-virtualized text layout
/// stays cheap. For high-throughput logs prefer <see cref="LiveLogWindow"/>'s
/// virtualized list. Constructed lazily on the UI thread after platform init.
/// </para>
/// </summary>
public sealed class LiveTailWindow
{
    private const int ChannelCapacity = 5000;

    /// <summary>Max lines retained. Capped low because the text view is not virtualized.</summary>
    private const int MaxLines = 500;

    /// <summary>Coalescing window (ms). Caps UI updates to ≤5/s under bursts.</summary>
    private const int CoalesceMs = 200;

    private const uint WindowBackgroundArgb = 0xFF_1E_1E_1E;
    private const uint HeaderBackgroundArgb = 0xFF_28_28_28;
    private const uint HeaderBorderArgb = 0xFF_3E_3E_3E;
    private const uint HeaderLabelArgb = 0xFF_9E_9E_9E;
    private const uint HeaderLinkArgb = 0xFF_6A_B0_F3;

    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        });

    // Rolling buffer of the last <= MaxLines lines; the SelectableTextBlock shows
    // them joined. Touched only on the UI thread (Append + the Clear link).
    private readonly List<string> _buffer = new();
    private readonly Window _window;
    private readonly ScrollViewer _scroller;
    private readonly SelectableTextBlock _text;

    /// <summary>
    /// Builds the (hidden) window. <b>Must be called on the UI thread after
    /// Avalonia platform init</b> (e.g. from <c>OnFrameworkInitializationCompleted</c>).
    /// </summary>
    public LiveTailWindow(string title)
    {
        SolidColorBrush windowBackground = new(Color.FromUInt32(WindowBackgroundArgb));

        // A single selectable text surface: native selection + Ctrl+C, no virtualization.
        // NoWrap so long paths scroll horizontally rather than reflow.
        _text = new SelectableTextBlock
        {
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(6, 2),
        };

        _scroller = new ScrollViewer
        {
            Content = _text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = windowBackground,
        };

        Border header = BuildHeader(title);

        DockPanel root = new() { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(_scroller);

        _window = new Window
        {
            Title = title,
            Width = 820,
            Height = 460,
            MinWidth = 380,
            MinHeight = 180,
            Content = root,
            Background = windowBackground,
            ShowInTaskbar = true,
            ShowActivated = false,
        };

        // Hide-on-close so Toggle() can re-show it. Closing via the OS ✕ button
        // DESTROYS the window; the next Show() then throws "Cannot re-show a closed
        // window." Intercept only a user/OS window close and hide instead — let
        // ApplicationShutdown / OSShutdown close it for real so app exit isn't
        // blocked by a cancelled close.
        _window.Closing += OnWindowClosing;

        _window.Hide();
        _ = DrainAsync();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            _window.Hide();
        }
    }

    /// <summary>
    /// Enqueues one line for display. Thread-safe, non-blocking, never throws.
    /// The oldest line is dropped when the channel is full.
    /// </summary>
    public void Enqueue(string line)
    {
        _channel.Writer.TryWrite(line);
    }

    /// <summary>Shows the window if hidden, hides it if visible. UI thread only.</summary>
    public void Toggle()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            _window.Show();
            _window.Activate();
        }
    }

    private Border BuildHeader(string title)
    {
        SolidColorBrush labelBrush = new(Color.FromUInt32(HeaderLabelArgb));
        SolidColorBrush linkBrush = new(Color.FromUInt32(HeaderLinkArgb));

        StackPanel stack = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 6),
            Spacing = 10,
        };

        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = labelBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        });

        stack.Children.Add(MakeLink(
            "Clear",
            "Remove all lines currently shown",
            linkBrush,
            (_, _) =>
            {
                _buffer.Clear();
                _text.Text = string.Empty;
            }));

        return new Border
        {
            Background = new SolidColorBrush(Color.FromUInt32(HeaderBackgroundArgb)),
            BorderBrush = new SolidColorBrush(Color.FromUInt32(HeaderBorderArgb)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = stack,
        };
    }

    private static TextBlock MakeLink(
        string text, string tooltip, IBrush foreground, EventHandler<PointerPressedEventArgs> onClick)
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

    private async Task DrainAsync()
    {
        ChannelReader<string> reader = _channel.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            // Coalesce bursts into one UI update (≤5 layout passes/second).
            await Task.Delay(CoalesceMs).ConfigureAwait(false);

            List<string> batch = new();
            while (reader.TryRead(out string? line))
            {
                batch.Add(line);
            }

            if (batch.Count == 0)
            {
                continue;
            }

            List<string> captured = batch;
            Dispatcher.UIThread.Post(() => Append(captured), DispatcherPriority.Background);
        }
    }

    private void Append(List<string> batch)
    {
        try
        {
            // "Stick to bottom" only when the user is already at the bottom, so a
            // scrolled-up reader (mid-selection) isn't yanked back down by new lines.
            bool wasAtBottom = IsAtBottom();

            _buffer.AddRange(batch);
            if (_buffer.Count > MaxLines)
            {
                _buffer.RemoveRange(0, _buffer.Count - MaxLines);
            }

            // Each entry is one line; join verbatim (no newline-splitting — file-event
            // lines are single-line).
            _text.Text = string.Join('\n', _buffer);

            if (wasAtBottom)
            {
                // Extent is only correct after the new text re-layouts; scroll on a
                // later dispatcher pass (Background runs after the layout pass). The
                // ScrollViewer clamps the offset to the valid range, so an over-large
                // Y lands exactly at the bottom.
                Dispatcher.UIThread.Post(
                    () => _scroller.Offset = new Vector(_scroller.Offset.X, _scroller.Extent.Height),
                    DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            // Never feed an error back into the logger (avoids a log→window→log loop).
            Console.Error.WriteLine("[LiveTailWindow] Append error: " + ex.Message);
        }
    }

    private bool IsAtBottom()
    {
        double max = _scroller.Extent.Height - _scroller.Viewport.Height;
        return max <= 0 || _scroller.Offset.Y >= max - 4;
    }
}
