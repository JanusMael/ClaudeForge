using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Dialogs;

/// <summary>
/// A fully programmatic, NON-fatal notice dialog — a sibling of
/// <see cref="FatalErrorDialog"/> for informational (non-crash) messages that
/// carry a copyable body, e.g. the list of settings that have no structured
/// editor and are shown as raw JSON.
/// <para>
/// No XAML required — safe to show at any time. Shown modelessly
/// (<see cref="Window.Show()"/>, not <c>ShowDialog</c>) so it never blocks the
/// app. Use <see cref="ShowSafe"/>, which marshals to the UI thread and swallows
/// failures so an informational notice can never destabilise the host.
/// </para>
/// <para>
/// The window shows a wrapped header message, a scrollable read-only monospaced
/// <see cref="TextBox"/> with the body text, and "Copy to Clipboard" + "Close"
/// buttons — the same shape as <see cref="FatalErrorDialog"/> minus the exception
/// semantics.
/// </para>
/// </summary>
public sealed class NonFatalNoticeDialog : Window
{
    private readonly string _bodyText;

    /// <summary>
    /// Creates a notice dialog with <paramref name="title"/> as the window title,
    /// <paramref name="message"/> as the wrapped header, and
    /// <paramref name="bodyText"/> in the scrollable read-only box.
    /// </summary>
    public NonFatalNoticeDialog(string title, string message, string bodyText)
    {
        _bodyText = bodyText;

        Title = title;
        Width = 720;
        Height = 460;
        MinWidth = 480;
        MinHeight = 300;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildContent(message);
    }

    private DockPanel BuildContent(string message)
    {
        TextBlock header = new()
        {
            Text = message,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 8),
        };

        TextBox bodyBox = new()
        {
            Text = _bodyText,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 12,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 220,
        };

        ScrollViewer scroll = new()
        {
            Content = bodyBox,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(16, 0, 16, 8),
        };

        Button copyBtn = new()
        {
            Content = "📋 Copy to Clipboard",
            Margin = new Thickness(0, 0, 8, 0),
        };
        copyBtn.Click += async (_, _) =>
        {
            IClipboard? clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(_bodyText);
            }
        };

        Button closeBtn = new()
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        closeBtn.Click += (_, _) => Close();

        DockPanel btnRow = new() { Margin = new Thickness(16, 0, 16, 16) };
        DockPanel.SetDock(closeBtn, Dock.Right);
        btnRow.Children.Add(closeBtn);
        btnRow.Children.Add(copyBtn);

        DockPanel root = new();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(btnRow, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(btnRow);
        root.Children.Add(scroll);

        return root;
    }

    /// <summary>
    /// Creates and shows (modelessly) a <see cref="NonFatalNoticeDialog"/>.
    /// Thread-safe — marshals to the UI thread automatically. Swallows every
    /// exception except cancellation so an informational notice never throws
    /// into the caller.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="message">Short user-facing header.</param>
    /// <param name="bodyText">Copyable body (e.g. a newline-separated path list).</param>
    public static void ShowSafe(string title, string message, string bodyText)
    {
        try
        {
            void Show()
            {
                new NonFatalNoticeDialog(title, message, bodyText).Show();
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Show();
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        Show();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                    }
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Informational notice — never let a failure to show it surface.
            _ = ex;
        }
    }
}
