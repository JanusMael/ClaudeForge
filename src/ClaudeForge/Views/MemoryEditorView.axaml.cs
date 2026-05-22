using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using ColorTextBlock.Avalonia;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// Code-behind for <c>MemoryEditorView</c>.  Two responsibilities:
///
/// <list type="number">
///   <item>
///     <b>Clipboard bridge</b> — keeps the VM free of any direct dependency
///     on <see cref="TopLevel"/>.
///   </item>
///   <item>
///     <b>Force-restyle of the markdown viewer's rendered tree</b>.  Every
///     Style-based attempt (GithubLike + overrides, FluentTheme + overrides,
///     full custom <c>MarkdownStyle</c>) failed to fix dark-theme black-on-
///     black text because Markdown.Avalonia writes <c>Foreground</c> inline
///     at render time — at <c>LocalValue</c> binding priority, which beats
///     every Style setter regardless of selector specificity.  FontSize /
///     FontWeight / Margin landed (the package leaves those to Styles) but
///     Foreground never did.
///
///     This code-behind walks the rendered tree on every layout pass and
///     writes the theme-aware brushes inline ourselves — same priority as
///     the package (LocalValue) but later, so we win on source order.
///   </item>
/// </list>
/// </summary>
public partial class MemoryEditorView : UserControl
{
    private MemoryEditorViewModel? _vm;

    /// <summary>
    /// Coalesces multiple restyle requests in a single dispatch tick.  The
    /// package's <see cref="Visual.LayoutUpdated"/> fires repeatedly during
    /// rendering; without this flag we'd queue dozens of identical tree
    /// walks.  Reset inside the deferred callback so the next layout pass
    /// can schedule another walk.
    /// </summary>
    private bool _restylePending;

    public MemoryEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Re-style on theme switch so a Light <-> Dark toggle while a file
        // is open immediately recolors the rendered tree.
        ActualThemeVariantChanged += (_, _) => SchedulePackageTreeRestyle();

        // Belt-and-suspenders restyle hooks on the MarkdownScrollViewer
        // itself.  Loaded fires once after the View attaches; LayoutUpdated
        // fires after every layout pass (covers every package render
        // including the first file open).  Both go through the
        // _restylePending coalescing flag so we don't burn cycles.
        Loaded += (_, _) =>
        {
            if (MarkdownViewer is not null)
            {
                MarkdownViewer.LayoutUpdated += (_, _) => SchedulePackageTreeRestyle();
                MarkdownViewer.AttachedToVisualTree += (_, _) => SchedulePackageTreeRestyle();
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CopyMarkdownRequested -= OnCopyMarkdownRequested;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as MemoryEditorViewModel;

        if (_vm is not null)
        {
            _vm.CopyMarkdownRequested += OnCopyMarkdownRequested;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private async void OnCopyMarkdownRequested(object? sender, string markdown)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(markdown);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemoryEditorViewModel.ViewerContent))
        {
            SchedulePackageTreeRestyle();
        }
    }

    /// <summary>
    /// Coalesces restyle requests through a single deferred dispatch.  The
    /// pending flag prevents queueing more than one walk per tick — the
    /// next layout pass simply re-schedules.
    /// </summary>
    private void SchedulePackageTreeRestyle()
    {
        if (_restylePending)
        {
            return;
        }

        _restylePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _restylePending = false;
            RestyleMarkdownTree();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Walk the rendered MarkdownScrollViewer subtree and force the
    /// theme-aware foreground / background on the elements where the
    /// package writes its own inline values.
    /// </summary>
    private void RestyleMarkdownTree()
    {
        if (MarkdownViewer is null)
        {
            return;
        }

        ThemeVariant theme = MarkdownViewer.ActualThemeVariant ?? ThemeVariant.Default;

        // App-owned brushes defined per theme variant in App.axaml's
        // ThemeDictionaries.  This app uses Semi.Avalonia (not
        // Avalonia.Themes.Fluent), and Semi does NOT reliably define
        // the SystemControl* family — App.axaml comments document
        // Semi maps SystemControl* to "dark-ish in BOTH themes",
        // producing dark-on-dark on the dark variant.  App-owned
        // tokens are the only reliable cross-theme path.  The
        // fallback hardcoded colors here mirror App.axaml so behaviour
        // is identical even if resource resolution fails.
        IBrush fg = ResolveBrush("AppPrimaryTextBrush", theme,
            theme == ThemeVariant.Dark ? Brushes.WhiteSmoke : Brushes.Black);
        IBrush bg = ResolveBrush("AppCodeBlockBackgroundBrush", theme, theme == ThemeVariant.Dark
            ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F))
            : new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4)));
        IBrush border = ResolveBrush("AppCodeBlockBorderBrush", theme, theme == ThemeVariant.Dark
            ? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A))
            : new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)));

        foreach (Visual visual in MarkdownViewer.GetVisualDescendants())
        {
            switch (visual)
            {
                case CTextBlock ctb:
                    // CTextBlock is the BLOCK-level container — covers
                    // body paragraphs, headings 1-6, list markers,
                    // plain-text code blocks, blockquote text, table cells.
                    // Setting Foreground here only colors the BLOCK's own
                    // text — the inline-level children (CRun / CCode /
                    // CHyperlink / CSpan) have their OWN Foreground that
                    // the package writes inline, so we walk Content too.
                    ctb.Foreground = fg;
                    foreach (CInline? inline in ctb.Content)
                    {
                        inline.Foreground = fg;
                        // Inline `code` (CCode) has both fg + bg set inline
                        // by the package — give it a theme-aware tint so
                        // it reads as "code" without blending into the
                        // body.
                        if (inline is CCode code)
                        {
                            code.Background = bg;
                        }
                    }

                    break;

                case TextBlock tb:
                    // Plain TextBlocks — covers fenced code blocks
                    // rendered through the no-language path
                    // (TextBlock.CodeBlock), language watermarks,
                    // copy-button labels, anything else the package
                    // emits as a non-CTextBlock text element.
                    tb.Foreground = fg;
                    break;

                case Border b when b.Classes.Contains("CodeBlock"):
                    // The fenced-code-block container.  Without a forced
                    // theme-aware background, the package's near-
                    // transparent overlay vanishes — code blocks lose
                    // their visible boundaries on both themes (smoke
                    // report: "no background").
                    b.Background = bg;
                    b.BorderBrush = border;
                    b.BorderThickness = new Thickness(1);
                    b.CornerRadius = new CornerRadius(4);
                    if (b.Padding == default)
                    {
                        b.Padding = new Thickness(8);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Resolve a theme-aware brush via the visual tree's resource chain,
    /// or fall back to the supplied hardcoded default if the lookup
    /// fails.  TryFindResource walks: control → ancestors → app → theme
    /// → fluent theme — same semantics as AXAML <c>{DynamicResource}</c>.
    /// </summary>
    private IBrush ResolveBrush(string key, ThemeVariant theme, IBrush fallback)
    {
        if (MarkdownViewer is not null
            && MarkdownViewer.TryFindResource(key, theme, out object? raw)
            && raw is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }
}