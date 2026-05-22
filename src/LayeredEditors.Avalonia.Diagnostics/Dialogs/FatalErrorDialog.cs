using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Dialogs;

/// <summary>
/// A fully programmatic fatal-error dialog shown when an unhandled exception
/// reaches the top-level handler. No XAML required — safe to show even before
/// the resource dictionary has fully loaded.
/// <para>
/// Use <see cref="ShowSafe"/> from within a Dispatcher unhandled-exception
/// handler or a <c>TaskScheduler.UnobservedTaskException</c> handler. If the
/// Avalonia runtime is completely dead (e.g. the failure occurred before
/// <c>BuildAvaloniaApp</c> completed), fall back to
/// <see cref="NativeErrorDialog.ShowFatalError"/> instead — this dialog
/// requires the dispatcher to be alive.
/// </para>
/// <para>
/// The window shows:
/// <list type="bullet">
///   <item>A header message (caller-supplied).</item>
///   <item>A scrollable, read-only, monospaced <see cref="TextBox"/> containing
///         <see cref="Exception.ToString"/>.</item>
///   <item>A "Copy to Clipboard" button and a "Close" button.</item>
/// </list>
/// </para>
/// </summary>
public sealed class FatalErrorDialog : Window
{
    /// <summary>Default header text when the caller does not supply one.</summary>
    public const string DefaultMessage =
        "The application encountered an unexpected error.\n" +
        "You can copy the details below for bug reporting.";

    private readonly string _exceptionText;

    /// <summary>
    /// Creates a dialog that displays <paramref name="exception"/> and uses
    /// <paramref name="title"/> as the window title.
    /// </summary>
    /// <param name="title">Window title. Conventionally
    /// <c>"Application Error — &lt;AppName&gt;"</c>.</param>
    /// <param name="message">Short user-facing header shown above the exception
    /// details.</param>
    /// <param name="exception">The exception whose <see cref="Exception.ToString"/>
    /// output is rendered in the read-only text box.</param>
    public FatalErrorDialog(string title, string message, Exception exception)
    {
        _exceptionText = exception.ToString();

        Title = title;
        Width = 840;
        Height = 580;
        MinWidth = 560;
        MinHeight = 380;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildContent(message);
    }

    private DockPanel BuildContent(string message)
    {
        TextBlock header = new()
        {
            Text = message,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 8),
        };

        // Read-only monospaced TextBox inside a ScrollViewer.
        // MinHeight ≈ 20 lines × 16 px = 320 px.
        TextBox exBox = new()
        {
            Text = _exceptionText,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 320,
        };

        ScrollViewer scroll = new()
        {
            Content = exBox,
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
                await clipboard.SetTextAsync(_exceptionText);
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
    /// Creates and shows a <see cref="FatalErrorDialog"/>. Thread-safe — marshals
    /// to the UI thread automatically. Swallows every exception so this
    /// last-resort handler never throws.
    /// </summary>
    /// <param name="title">Window title (e.g. <c>"Application Error — MyApp"</c>).</param>
    /// <param name="message">Short user-facing header.</param>
    /// <param name="exception">Exception to render in the details pane.</param>
    public static void ShowSafe(string title, string message, Exception exception)
    {
        try
        {
            void Show()
            {
                new FatalErrorDialog(title, message, exception).Show();
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                Show();
            }
            else
                // The posted action may throw if Avalonia is mid-shutdown; we deliberately
                // swallow because the outer ShowSafe catch is already the last line of
                // defence and reposting would loop. Cancellation is allowed to propagate
                // (it is normal control flow during shutdown, not a crash).
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
            // Absolute fallback: write to stderr so the crash is not silently lost.
            // Filter excludes cancellation only; everything else is fair game because
            // this method runs in last-resort error display where any failure to show
            // the dialog must still leave the user with *some* trace of what happened.
            _ = ex;
            Console.Error.WriteLine($"[CRASH] {message}");
            Console.Error.WriteLine(exception);
        }
    }
}