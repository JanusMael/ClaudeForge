using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>.
/// </summary>
/// <remarks>
/// Call <see cref="RegisterSaveChangesDialog"/> once at application startup to wire up the
/// host-specific Save Changes dialog.  All other dialog methods are self-contained.
/// </remarks>
public sealed class AvaloniaDialogService : IDialogService
{
    // Factory registered by the host application so this service never depends on
    // application-specific ViewModels or Views.  The factory receives the prompt and the
    // current main window (may be null when no window is open) and is responsible for
    // creating and showing the concrete dialog.
    private Func<ISaveChangesPrompt, Window?, Task<bool>>? _saveChangesFactory;

    /// <summary>
    /// Optional application icon applied to all programmatic dialogs (alert, input,
    /// confirm).  Set once at startup from the host application's icon so every dialog
    /// shows the correct task-bar / title-bar icon without requiring callers to pass it
    /// on each call.
    /// </summary>
    public WindowIcon? DialogAppIcon { get; set; }

    /// <summary>
    /// Registers the factory that will be called by <see cref="ShowSaveChangesDialogAsync"/>.
    /// Call this once during application startup before any dialogs can be shown.
    /// </summary>
    /// <param name="factory">
    /// A delegate that receives the <see cref="ISaveChangesPrompt"/> and the nullable owner
    /// <see cref="Window"/>, shows the host-specific dialog, and returns <c>true</c> if the
    /// user chose Save.
    /// </param>
    public void RegisterSaveChangesDialog(Func<ISaveChangesPrompt, Window?, Task<bool>> factory)
    {
        _saveChangesFactory = factory;
    }

    // -----------------------------------------------------------------------
    // Window access
    // -----------------------------------------------------------------------

    private Window? Window => (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow;

    // -----------------------------------------------------------------------
    // IDialogService — file / folder pickers
    // -----------------------------------------------------------------------

    public async Task<string?> PickFolderAsync(string? title = null)
    {
        Window? window = Window;
        if (window == null)
        {
            return null;
        }

        FolderPickerOpenOptions options = new()
        {
            Title = title ?? "Select Folder",
            AllowMultiple = false,
        };

        IReadOnlyList<IStorageFolder> result = await window.StorageProvider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null)
    {
        Window? window = Window;
        if (window == null)
        {
            return null;
        }

        FilePickerOpenOptions options = new()
        {
            Title = title ?? "Select File",
            AllowMultiple = false,
            FileTypeFilter = filters?.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => $"*.{e}").ToList()
            }).ToList(),
        };

        IReadOnlyList<IStorageFile> result = await window.StorageProvider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(
        string? title,
        string defaultFileName,
        IReadOnlyList<FilePickerFilter>? filters = null)
    {
        Window? window = Window;
        if (window == null)
        {
            return null;
        }

        FilePickerSaveOptions options = new()
        {
            Title = title ?? "Save As",
            SuggestedFileName = defaultFileName,
            FileTypeChoices = filters?.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => $"*.{e}").ToList(),
            }).ToList(),
            ShowOverwritePrompt = true,
        };

        IStorageFile? result = await window.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }

    // -----------------------------------------------------------------------
    // IDialogService — simple dialogs
    // -----------------------------------------------------------------------

    public async Task ShowAlertAsync(string title, string message)
    {
        await ShowAlertCoreAsync(title, message, icon: null, isError: false);
    }

    /// <summary>
    /// Extended overload that attaches an application icon and optionally renders a
    /// styled error header.  Called by ClaudeForge for the "Cannot Save" alert.
    /// </summary>
    public async Task ShowAlertAsync(string title, string message,
                                     WindowIcon? icon, bool isError = false)
    {
        await ShowAlertCoreAsync(title, message, icon, isError);
    }

    private async Task ShowAlertCoreAsync(string title, string message,
                                          WindowIcon? icon, bool isError)
    {
        Window? window = Window;
        if (window == null)
        {
            return;
        }

        Button okButton = new()
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 70,
            Margin = new Thickness(0, 12, 0, 0),
        };

        // Use a SelectableTextBlock so the user can copy the text (e.g. error details).
        // Wrap it in a ScrollViewer so a very long message (e.g. many schema-validation
        // violations) stays scrollable.  NO MaxHeight on the ScrollViewer itself — the
        // DockPanel layout below lets it stretch to fill the window's available height
        // (the Window MaxHeight caps the outer dialog, not the inner viewport).
        SelectableTextBlock textBlock = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
        };

        ScrollViewer scrollViewer = new()
        {
            Content = textBlock,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // Switched from StackPanel to DockPanel so OK button docks to the
        // bottom and ScrollViewer fills the remaining area.  Pre-fix: StackPanel +
        // ScrollViewer with MaxHeight=440 meant resizing the dialog vertically left an
        // empty band between the content and the OK button, and the OK button drifted
        // away from the bottom edge.  Pre-fix the horizontal resize also didn't stretch
        // the SelectableTextBlock because StackPanel doesn't constrain child width
        // beyond the longest child's preferred size.
        DockPanel body = new()
        {
            Margin = new Thickness(16),
            LastChildFill = true,
        };

        if (isError)
        {
            // Styled header with a ⚠ icon so error alerts look distinct from info alerts.
            StackPanel header = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 0, 0, 12),
            };
            header.Children.Add(new TextBlock
            {
                Text = "⚠",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.Parse("#C62828")),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            });
            DockPanel.SetDock(header, Dock.Top);
            body.Children.Add(header);
        }

        // Dock the OK button to the bottom BEFORE the ScrollViewer so LastChildFill
        // assigns the remaining vertical space to the ScrollViewer.  Order matters.
        DockPanel.SetDock(okButton, Dock.Bottom);
        body.Children.Add(okButton);
        body.Children.Add(scrollViewer);

        Window dialog = new()
        {
            Title = isError ? $"⚠ {title}" : title,
            Width = 520,
            // bumped from 560 → 720 to give users more elbow room when
            // resizing.  Combined with the DockPanel fix above, the ScrollViewer now
            // stretches to fill the available height as the user drags the bottom edge.
            MaxHeight = 720,
            MinHeight = 160,
            MinWidth = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body,
        };

        WindowIcon? effectiveIcon = icon ?? DialogAppIcon;
        if (effectiveIcon is not null)
        {
            dialog.Icon = effectiveIcon;
        }

        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(window);
    }

    // -----------------------------------------------------------------------
    // IDialogService — rich-message + category overloads
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task ShowAlertAsync(string title, DialogMessage message,
                                     DialogCategory category = DialogCategory.Information)
    {
        Window? window = Window;
        if (window == null)
        {
            return;
        }

        Button okButton = new()
        {
            Content = "OK",
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 70,
            Margin = new Thickness(0, 12, 0, 0),
        };

        Window dialog = new()
        {
            Title = TitleWithGlyph(title, category),
            Width = 520,
            // bumped from 560 → 720 + introduced MinHeight/MinWidth so
            // the resize handles have a sensible operating range.  Combined with the
            // DockPanel below, the ScrollViewer stretches to fill the available
            // height as the user drags the bottom edge.
            MaxHeight = 720,
            MinHeight = 160,
            MinWidth = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        // Use the segment-grouping content helper so Code segments
        // get a framed bordered pane while the explanatory Text segments around
        // them render as plain inline runs.  Pre-fix attempt wrapped the WHOLE
        // content (including the "These errors are in the loaded files..."
        // explanatory paragraph) in the framed pane, which the user pointed out
        // was wrong: only the actual code block should be inside the frame.
        Control messageView = BuildSegmentedDialogContent(message, dialog);

        // No MaxHeight on the ScrollViewer — the DockPanel layout lets it stretch to
        // fill the window's available height (Window MaxHeight caps the outer dialog).
        ScrollViewer scrollViewer = new()
        {
            Content = messageView,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // Switched from StackPanel to DockPanel so OK button docks to
        // the bottom and ScrollViewer fills the remaining area.  Pre-fix: StackPanel
        // + ScrollViewer with fixed MaxHeight=440 meant the schema-validation dialog
        // left an empty band between the error list and the OK button when resized
        // vertically, and the SelectableTextBlock didn't stretch horizontally on a
        // bottom-corner drag.
        DockPanel body = new()
        {
            Margin = new Thickness(16),
            LastChildFill = true,
        };

        // Category header docks Top; OK button docks Bottom; ScrollViewer is the
        // last child and fills remaining space.  Order matters for DockPanel.
        AppendCategoryHeader(body, title, category); // appends with Dock=Top below
        DockPanel.SetDock(okButton, Dock.Bottom);
        body.Children.Add(okButton);
        body.Children.Add(scrollViewer);

        dialog.Content = body;
        if (DialogAppIcon is not null)
        {
            dialog.Icon = DialogAppIcon;
        }

        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(window);
    }

    /// <inheritdoc />
    public async Task<bool?> ShowConfirmAsync(
        string title,
        DialogMessage message,
        DialogCategory category = DialogCategory.Confirmation,
        string confirmLabel = "Confirm",
        string cancelLabel = "Cancel")
    {
        Window? window = Window;
        if (window == null)
        {
            return null;
        }

        // three-valued result: null when the user dismisses
        // via the window-close (X) without clicking either button.
        // Callers MUST treat null as "abort whatever flow this dialog
        // was a step of," not as a synonym for the cancel-side action.
        // Concrete state machine:
        //   confirmButton.Click → result = true   → close
        //   cancelButton.Click  → result = false  → close   (also Escape, via IsCancel)
        //   X (window-manager)  → result stays null → close
        bool? result = null;

        Button confirmButton = new()
        {
            Content = confirmLabel,
            // Enter triggers Confirm on safe categories
            // (Information, Confirmation).  Destructive prompts keep
            // IsDefault=false so Enter doesn't accidentally Delete /
            // Save-anyway / etc. — the user must explicitly click.
            // Escape always triggers Cancel via the cancel button's
            // IsCancel=true.
            IsDefault = category != DialogCategory.Destructive,
            MinWidth = 70,
        };
        ApplyDestructiveStyle(confirmButton, category);

        Button cancelButton = new() { Content = cancelLabel, IsCancel = true, MinWidth = 70 };

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(confirmButton);

        Window dialog = new()
        {
            Title = TitleWithGlyph(title, category),
            Width = 480,
            MinHeight = 120,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        StackPanel body = new() { Margin = new Thickness(16), Spacing = 12 };
        AppendCategoryHeader(body, title, category);
        // Use the segment-grouping helper so a Code segment
        // (e.g. schema-validation error list in the save-time "Save anyway?"
        // confirm) renders inside a framed pane while explanatory Text
        // around it stays as plain inline runs.  See rich ShowAlertAsync
        // comment for the rationale.
        body.Children.Add(BuildSegmentedDialogContent(message, dialog));
        body.Children.Add(buttonRow);
        dialog.Content = body;

        if (DialogAppIcon is not null)
        {
            dialog.Icon = DialogAppIcon;
        }

        confirmButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        await dialog.ShowDialog(window);
        return result;
    }

    // -----------------------------------------------------------------------
    // Rich-message rendering helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Render a <see cref="DialogMessage"/>'s segments into a
    /// <see cref="SelectableTextBlock"/>'s inline collection.  Path segments
    /// are rendered monospace + accent and followed by an inline 📋 glyph
    /// that copies the path to the clipboard on click; hyperlink segments
    /// render accent + underline and are followed by a ↗ glyph that opens
    /// the URL in the user's default browser.
    /// </summary>
    private static SelectableTextBlock BuildSegmentedTextBlock(DialogMessage message, Window dialogWindow)
    {
        return BuildSegmentedTextBlockFor(message.Segments, dialogWindow);
    }

    /// <summary>
    /// Render an arbitrary <see cref="DialogSegment"/> sequence into a
    /// SelectableTextBlock.  Extracted from <see cref="BuildSegmentedTextBlock"/>
    /// so the segment-grouping helper (<see cref="BuildSegmentedDialogContent"/>)
    /// can call it per-group without having to construct a synthetic
    /// <see cref="DialogMessage"/>.
    /// </summary>
    private static SelectableTextBlock BuildSegmentedTextBlockFor(
        IEnumerable<DialogSegment> segments, Window dialogWindow)
    {
        SelectableTextBlock tb = new()
        {
            TextWrapping = TextWrapping.Wrap,
        };

        foreach (DialogSegment seg in segments)
        {
            switch (seg.Kind)
            {
                case DialogSegmentKind.Text:
                    tb.Inlines!.Add(new Run(seg.Value));
                    break;

                case DialogSegmentKind.Bold:
                    tb.Inlines!.Add(new Run(seg.Value) { FontWeight = FontWeight.SemiBold });
                    break;

                case DialogSegmentKind.Path:
                    AppendPathRun(tb, seg.Value, dialogWindow);
                    break;

                case DialogSegmentKind.Hyperlink:
                    AppendHyperlinkRun(tb, seg.Value, seg.Url ?? string.Empty);
                    break;

                case DialogSegmentKind.Code:
                    // Monospace family matches the Path segment's family so
                    // the dialog has visual consistency between inline paths
                    // and embedded code/JSON blocks.  No accent colour — the
                    // monospace shift alone signals "this is code".
                    tb.Inlines!.Add(new Run(seg.Value)
                    {
                        FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                    });
                    break;
            }
        }

        return tb;
    }

    /// <summary>
    /// Build dialog content where consecutive <see cref="DialogSegmentKind.Code"/>
    /// segments render inside a framed bordered pane (theme-aware bg via
    /// <c>AppCodeBlockBackgroundBrush</c> / <c>AppCodeBlockBorderBrush</c>) and
    /// all other segments stay as plain inline runs.  Two cases:
    /// <list type="bullet">
    ///   <item>Message has NO Code segments → returns a single
    ///   <see cref="SelectableTextBlock"/> with all segments inlined (same as
    ///   the pre-fix <see cref="BuildSegmentedTextBlock"/> behaviour, so dialogs
    ///   that don't use <c>Code(...)</c> render identically).</item>
    ///   <item>Message has at least one Code segment → returns a vertical
    ///   <see cref="StackPanel"/> with one element per consecutive same-kind
    ///   group: framed <see cref="Border"/> for Code groups, plain
    ///   <see cref="SelectableTextBlock"/> for everything else.  This keeps
    ///   explanatory text ABOVE and BELOW the framed code OUTSIDE the frame,
    ///   matching the user-feedback contract: "only the errors should be in
    ///   their own border."</item>
    /// </list>
    /// </summary>
    private static Control BuildSegmentedDialogContent(DialogMessage message, Window dialogWindow)
    {
        // Fast-path: no Code segments → identical to the legacy inline renderer.
        // Keeps every other dialog visually unchanged.
        if (!message.Segments.Any(s => s.Kind == DialogSegmentKind.Code))
        {
            return BuildSegmentedTextBlockFor(message.Segments, dialogWindow);
        }

        List<(bool IsCode, List<DialogSegment> Segments)> groups = GroupSegmentsByKind(message.Segments);
        StackPanel stack = new() { Spacing = 12 };
        for (int i = 0; i < groups.Count; i++)
        {
            (bool isCode, List<DialogSegment> segs) = groups[i];
            if (isCode)
            {
                stack.Children.Add(BuildCodePane(segs, dialogWindow));
            }
            else
            {
                bool precededByCode = i > 0 && groups[i - 1].IsCode;
                List<DialogSegment> effectiveSegs = precededByCode ? TrimLeadingWhitespaceAfterCode(segs) : segs;
                stack.Children.Add(BuildSegmentedTextBlockFor(effectiveSegs, dialogWindow));
            }
        }

        return stack;
    }

    /// <summary>
    /// Group consecutive <see cref="DialogSegment"/>s by whether they're
    /// <see cref="DialogSegmentKind.Code"/>.  A single <c>.Code()</c> call
    /// yields one segment; multiple consecutive <c>.Code()</c> calls are
    /// rare but treated as one group.  Non-Code groups can mix
    /// Text / Bold / Path / Hyperlink and render inline as before.
    /// </summary>
    private static List<(bool IsCode, List<DialogSegment> Segments)> GroupSegmentsByKind(
        IReadOnlyList<DialogSegment> segments)
    {
        List<(bool IsCode, List<DialogSegment> Segments)> groups = new();
        foreach (DialogSegment seg in segments)
        {
            bool isCode = seg.Kind == DialogSegmentKind.Code;
            if (groups.Count == 0 || groups[^1].IsCode != isCode)
            {
                groups.Add((isCode, new List<DialogSegment>()));
            }

            groups[^1].Segments.Add(seg);
        }

        return groups;
    }

    /// <summary>
    /// Trim leading "\n" from non-Code groups when the previous
    /// group was a Code group.  The schema-validation message template is
    /// <c>.Code(errors) .Text("\n\n") .Text("...")</c> — the "\n\n" was there
    /// to vertically separate the code block from the explanatory paragraph
    /// when they shared one TextBlock.  Now that Code lives in its own framed
    /// pane and Text lives in a separate <see cref="SelectableTextBlock"/>
    /// below, the explicit newlines would render as a tall empty gap above
    /// the explanatory paragraph.  StackPanel <c>Spacing=12</c> handles the
    /// separation visually, so strip leading whitespace from the first
    /// Text segment when its group follows a Code group.
    /// </summary>
    private static List<DialogSegment> TrimLeadingWhitespaceAfterCode(List<DialogSegment> segs)
    {
        if (segs.Count == 0 || segs[0].Kind != DialogSegmentKind.Text)
        {
            return segs;
        }

        string trimmed = segs[0].Value.TrimStart('\r', '\n', ' ', '\t');
        if (trimmed.Length == segs[0].Value.Length)
        {
            return segs;
        }

        // Replace the first segment with a trimmed copy.  DialogSegment is a
        // record so we use `with`.
        List<DialogSegment> copy = new(segs);
        copy[0] = segs[0] with { Value = trimmed };
        return copy;
    }

    /// <summary>
    /// Build the framed Border that wraps a Code-segment group.  Uses
    /// <c>AppCodeBlockBackgroundBrush</c> / <c>AppCodeBlockBorderBrush</c> via
    /// <see cref="DynamicResourceExtension"/> so the pane re-themes when the
    /// user toggles light / dark.
    /// </summary>
    private static Border BuildCodePane(List<DialogSegment> segs, Window dialogWindow)
    {
        Border pane = new()
        {
            Child = BuildSegmentedTextBlockFor(segs, dialogWindow),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 8),
        };
        pane[!Border.BackgroundProperty] = new DynamicResourceExtension("AppCodeBlockBackgroundBrush");
        pane[!Border.BorderBrushProperty] = new DynamicResourceExtension("AppCodeBlockBorderBrush");
        return pane;
    }

    /// <summary>
    /// Append a path segment: monospace + accent text run, then an inline
    /// clipboard glyph that copies the path on click.  The glyph carries a
    /// tooltip showing the full path so very long paths remain inspectable
    /// without selecting and copying the run.
    /// </summary>
    private static void AppendPathRun(SelectableTextBlock tb, string path, Window dialogWindow)
    {
        Run pathRun = new(path)
        {
            FontFamily = new FontFamily("Consolas,Menlo,monospace"),
            Foreground = new SolidColorBrush(Color.Parse("#1F6FEB")),
            FontWeight = FontWeight.SemiBold,
        };
        tb.Inlines!.Add(pathRun);

        TextBlock copyGlyph = new()
        {
            Text = "📋",
            FontSize = 12,
            Margin = new Thickness(2, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(copyGlyph, $"Copy {path}");

        copyGlyph.PointerPressed += async (_, _) =>
        {
            TopLevel? top = TopLevel.GetTopLevel(dialogWindow);
            if (top?.Clipboard is { } clip)
            {
                await clip.SetTextAsync(path);
                ToolTip.SetTip(copyGlyph, "Copied!");
            }
        };

        // BaselineAlignment.Center centres the glyph on the surrounding
        // text's vertical centre. Default `Baseline` aligns the glyph's
        // bottom to the text baseline, which leaves the emoji floating
        // above the path text — visually mis-aligned. (Smoke-reported)
        // Setting VerticalAlignment on the inner TextBlock
        // alone has no effect across the inline boundary.
        tb.Inlines.Add(new InlineUIContainer
        {
            Child = copyGlyph,
            BaselineAlignment = BaselineAlignment.Center,
        });
    }

    /// <summary>
    /// Append a hyperlink segment: accent + underlined label, then a small
    /// ↗ glyph that opens the URL in the user's default browser via the
    /// platform shell.  The visible label is the segment's <see cref="DialogSegment.Value"/>;
    /// the URL is from <see cref="DialogSegment.Url"/>.
    /// </summary>
    private static void AppendHyperlinkRun(SelectableTextBlock tb, string label, string url)
    {
        Run linkRun = new(label)
        {
            Foreground = new SolidColorBrush(Color.Parse("#1F6FEB")),
            TextDecorations = TextDecorations.Underline,
        };
        tb.Inlines!.Add(linkRun);

        TextBlock openGlyph = new()
        {
            Text = "↗",
            FontSize = 11,
            Margin = new Thickness(2, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Foreground = new SolidColorBrush(Color.Parse("#1F6FEB")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(openGlyph, $"Open {url}");

        openGlyph.PointerPressed += (_, _) =>
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                });
            }
            catch
            {
                // Shell-open failed — leave silent. The URL is still selectable
                // in the SelectableTextBlock so the user can copy it manually.
            }
        };

        tb.Inlines.Add(new InlineUIContainer { Child = openGlyph });
    }

    /// <summary>
    /// Prepend the title with a category-appropriate glyph for the window
    /// chrome.  Returns the title unchanged for neutral categories.
    /// </summary>
    private static string TitleWithGlyph(string title, DialogCategory category)
    {
        return category switch
        {
            DialogCategory.Destructive => $"⚠ {title}",
            DialogCategory.Error => $"⛔ {title}",
            var _ => title,
        };
    }

    /// <summary>
    /// Add a styled header strip for categories that warrant visual
    /// elevation (Destructive, Error).  No-ops for neutral categories so
    /// confirm/info dialogs keep their existing minimal look.
    /// </summary>
    private static void AppendCategoryHeader(Panel body, string title, DialogCategory category)
    {
        if (category is not (DialogCategory.Destructive or DialogCategory.Error))
        {
            return;
        }

        (string glyph, string color) = category switch
        {
            DialogCategory.Destructive => ("⚠", "#C77700"),
            DialogCategory.Error => ("⛔", "#C62828"),
            var _ => ("", "#888888"),
        };

        StackPanel header = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            // Bottom margin so the header doesn't kiss the ScrollViewer.  Read by
            // both StackPanel (vertical stack) and DockPanel (Top-docked child).
            Margin = new Thickness(0, 0, 0, 12),
        };
        header.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.Parse(color)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        // Set Dock.Top so the header docks correctly when `body` is a
        // DockPanel (rich ShowAlertAsync).  StackPanel ignores the Dock attached
        // property, so this is harmless on the legacy stack-based callers.
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
    }

    /// <summary>
    /// Apply danger styling to the confirm button when the category is
    /// <see cref="DialogCategory.Destructive"/>.  Other categories leave the
    /// button at the default style.
    /// </summary>
    private static void ApplyDestructiveStyle(Button confirmButton, DialogCategory category)
    {
        if (category != DialogCategory.Destructive)
        {
            return;
        }

        confirmButton.Background = new SolidColorBrush(Color.Parse("#C62828"));
        confirmButton.Foreground = Brushes.White;
        confirmButton.FontWeight = FontWeight.SemiBold;
    }

    /// <inheritdoc />
    public async Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
    {
        return await ShowInputAsync(title, prompt, placeholder, icon: null);
    }

    /// <summary>
    /// Extended overload that attaches an application icon to the input dialog.
    /// Called by ClaudeForge for the "New Profile" dialog.
    /// </summary>
    public async Task<string?> ShowInputAsync(string title, string prompt,
                                              string? placeholder, WindowIcon? icon)
    {
        Window? window = Window;
        if (window == null)
        {
            return null;
        }

        string? result = null;

        TextBox inputBox = new()
        {
            PlaceholderText = placeholder,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Button okButton = new() { Content = "OK", IsDefault = true, MinWidth = 70 };
        Button cancelButton = new() { Content = "Cancel", IsCancel = true, MinWidth = 70 };

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(okButton);

        StackPanel body = new() { Margin = new Thickness(16), Spacing = 10 };
        body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(inputBox);
        body.Children.Add(buttonRow);

        Window dialog = new()
        {
            Title = title,
            Width = 420,
            MinHeight = 120,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = body,
        };

        WindowIcon? effectiveIcon = icon ?? DialogAppIcon;
        if (effectiveIcon is not null)
        {
            dialog.Icon = effectiveIcon;
        }

        okButton.Click += (_, _) =>
        {
            result = inputBox.Text?.Trim();
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        inputBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Return or Key.Enter)
            {
                result = inputBox.Text?.Trim();
                dialog.Close();
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
            }
        };

        await dialog.ShowDialog(window);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    /// <inheritdoc />
    public async Task<bool?> ShowConfirmAsync(string title, string message,
                                              string confirmLabel = "Confirm",
                                              string cancelLabel = "Cancel")
    {
        Window? window = Window;
        if (window == null)
        {
            return null;
        }

        // same three-valued semantics as the rich-message
        // overload: null when the user dismisses via X without clicking
        // either button.  See that overload's comment for the full
        // state-machine description.
        bool? result = null;

        Button confirmButton = new() { Content = confirmLabel, IsDefault = false, MinWidth = 70 };
        Button cancelButton = new() { Content = cancelLabel, IsCancel = true, MinWidth = 70 };

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(confirmButton);

        StackPanel body = new() { Margin = new Thickness(16), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(buttonRow);

        Window dialog = new()
        {
            Title = title,
            Width = 420,
            MinHeight = 120,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = body,
        };

        WindowIcon? confirmIcon = DialogAppIcon;
        if (confirmIcon is not null)
        {
            dialog.Icon = confirmIcon;
        }

        confirmButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        await dialog.ShowDialog(window);
        return result;
    }

    // -----------------------------------------------------------------------
    // IDialogService — Save Changes (host-supplied factory)
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
    {
        if (_saveChangesFactory is null)
        {
            return false;
        }

        return await _saveChangesFactory(prompt, Window);
    }

    /// <inheritdoc />
    public async Task<UnsavedChangesChoice> ShowUnsavedChangesAsync(
        string title, DialogMessage message)
    {
        Window? window = Window;
        if (window == null)
        {
            return UnsavedChangesChoice.Cancel;
        }

        // three-button modal for the window-close-with-edits
        // prompt.  X-close (no button click) collapses to Cancel = "keep
        // window open" per the X-never-proceeds principle.
        UnsavedChangesChoice result = UnsavedChangesChoice.Cancel;

        // Save is the safe default (Enter triggers it): users who reflexively
        // hit Enter after clicking close should save, not lose work.
        Button saveButton = new() { Content = "Save", IsDefault = true, MinWidth = 90 };
        // Don't Save is destructive — explicitly NOT default so Enter doesn't
        // accidentally discard work.  No special key binding.
        Button dontSaveButton = new() { Content = "Don't Save", MinWidth = 90 };
        ApplyDestructiveStyle(dontSaveButton, DialogCategory.Destructive);
        // Cancel keeps the window open.  IsCancel = true wires Escape.
        Button cancelButton = new() { Content = "Cancel", IsCancel = true, MinWidth = 90 };

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        // Left-to-right reading order: destructive on the far left so the
        // user has to deliberately track past Cancel and Save to reach it.
        buttonRow.Children.Add(dontSaveButton);
        buttonRow.Children.Add(cancelButton);
        buttonRow.Children.Add(saveButton);

        Window dialog = new()
        {
            Title = TitleWithGlyph(title, DialogCategory.Confirmation),
            Width = 480,
            MinHeight = 140,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        StackPanel body = new() { Margin = new Thickness(16), Spacing = 12 };
        AppendCategoryHeader(body, title, DialogCategory.Confirmation);
        body.Children.Add(BuildSegmentedTextBlock(message, dialog));
        body.Children.Add(buttonRow);
        dialog.Content = body;

        if (DialogAppIcon is not null)
        {
            dialog.Icon = DialogAppIcon;
        }

        saveButton.Click += (_, _) =>
        {
            result = UnsavedChangesChoice.Save;
            dialog.Close();
        };
        dontSaveButton.Click += (_, _) =>
        {
            result = UnsavedChangesChoice.DontSave;
            dialog.Close();
        };
        cancelButton.Click += (_, _) =>
        {
            result = UnsavedChangesChoice.Cancel;
            dialog.Close();
        };

        await dialog.ShowDialog(window);
        // X-close path: no button click fired, result stays Cancel — keep
        // the window open per the X-never-proceeds contract.
        return result;
    }
}