using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;

namespace Bennewitz.Ninja.ClaudeForge.Controls;

/// <summary>
/// Reusable read-only markdown body renderer (Markdown.Avalonia) with the app's
/// dark-theme styling baked in. Set <see cref="Markdown"/> and it renders with
/// correct light/dark colours:
/// <c>&lt;ctrl:MarkdownBodyView Markdown="{Binding Body}"/&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Encapsulates BOTH the large <c>MarkdownStyle</c> (see the .axaml) and the
/// force-restyle tree-walk below. The tree-walk is required because
/// Markdown.Avalonia writes <c>Foreground</c> inline at <c>LocalValue</c> binding
/// priority — which beats every Style setter regardless of selector specificity —
/// so Style setters land FontSize/FontWeight/Margin but never colour. We walk the
/// rendered tree on every layout pass and write the theme-aware brushes inline
/// ourselves (same priority, later in source order, so we win).
/// </para>
/// <para>
/// Extracted from <c>MemoryEditorView</c> so Memory + Agents &amp; Skills share one
/// source. Consumers still own the clipboard bridge for any "copy markdown" action
/// (clipboard needs <see cref="TopLevel"/>, a view concern).
/// </para>
/// </remarks>
public partial class MarkdownBodyView : UserControl
{
    /// <summary>The markdown source to render.</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownBodyView, string?>(nameof(Markdown));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>
    /// Vertical scroll-bar visibility of the inner viewer. Default <see cref="ScrollBarVisibility.Auto"/>
    /// (the viewer scrolls itself). Set to <see cref="ScrollBarVisibility.Disabled"/> when hosting this
    /// control inside an OUTER ScrollViewer that should scroll the body together with sibling content
    /// (e.g. Agents &amp; Skills, so the front-matter card scrolls away with the markdown) — Disabled
    /// makes the body expand to its full height instead of scrolling internally.
    /// </summary>
    public static readonly StyledProperty<ScrollBarVisibility> VerticalScrollBarVisibilityProperty =
        AvaloniaProperty.Register<MarkdownBodyView, ScrollBarVisibility>(
            nameof(VerticalScrollBarVisibility), ScrollBarVisibility.Auto);

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Coalesces multiple restyle requests into a single dispatch tick — the
    /// package's <see cref="Visual.LayoutUpdated"/> fires repeatedly during
    /// rendering; without this we'd queue dozens of identical tree walks.
    /// </summary>
    private bool _restylePending;

    public MarkdownBodyView()
    {
        InitializeComponent();

        // Re-style on theme switch so a Light <-> Dark toggle recolours the tree.
        ActualThemeVariantChanged += (_, _) => SchedulePackageTreeRestyle();

        Loaded += (_, _) =>
        {
            if (Viewer is null)
            {
                return;
            }

            Viewer.Markdown = Markdown ?? string.Empty;
            Viewer.LayoutUpdated += (_, _) => SchedulePackageTreeRestyle();
            Viewer.AttachedToVisualTree += (_, _) => SchedulePackageTreeRestyle();
            SchedulePackageTreeRestyle();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Viewer is null)
        {
            return;
        }

        if (change.Property == MarkdownProperty)
        {
            Viewer.Markdown = Markdown ?? string.Empty;
            SchedulePackageTreeRestyle();
        }
        else if (change.Property == VerticalScrollBarVisibilityProperty)
        {
            // MarkdownScrollViewer isn't a ScrollViewer and exposes no scroll knob;
            // its inner ScrollViewer is set during the restyle walk below.
            SchedulePackageTreeRestyle();
        }
    }

    /// <summary>Coalesces restyle requests through a single deferred dispatch.</summary>
    private void SchedulePackageTreeRestyle()
    {
        if (_restylePending)
        {
            return;
        }

        _restylePending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _restylePending = false;
                RestyleMarkdownTree();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// Walk the rendered viewer subtree and force theme-aware foreground /
    /// background on the elements where the package writes its own inline values.
    /// </summary>
    private void RestyleMarkdownTree()
    {
        if (Viewer is null)
        {
            return;
        }

        // MarkdownScrollViewer isn't a ScrollViewer and exposes no scroll knob, so
        // reach its inner ScrollViewer (the first ScrollViewer descendant) and apply
        // the requested vertical scroll behaviour. Disabled lets the body expand to
        // full height so a host's OUTER ScrollViewer scrolls it together with sibling
        // content (Agents & Skills). Guarded against re-setting an unchanged value so
        // this layout-pass callback doesn't spin.
        ScrollViewer? innerScroll = Viewer.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (innerScroll is not null && innerScroll.VerticalScrollBarVisibility != VerticalScrollBarVisibility)
        {
            innerScroll.VerticalScrollBarVisibility = VerticalScrollBarVisibility;
        }

        ThemeVariant theme = Viewer.ActualThemeVariant ?? ThemeVariant.Default;

        // App-owned brushes (per-theme in App.axaml). Semi.Avalonia doesn't
        // reliably define the SystemControl* family, so these tokens are the only
        // reliable cross-theme path; the hardcoded fallbacks mirror App.axaml.
        IBrush fg = ResolveBrush("AppPrimaryTextBrush", theme,
            theme == ThemeVariant.Dark ? Brushes.WhiteSmoke : Brushes.Black);
        IBrush bg = ResolveBrush("AppCodeBlockBackgroundBrush", theme, theme == ThemeVariant.Dark
            ? new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F))
            : new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4)));
        IBrush border = ResolveBrush("AppCodeBlockBorderBrush", theme, theme == ThemeVariant.Dark
            ? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A))
            : new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)));

        foreach (Visual visual in Viewer.GetVisualDescendants())
        {
            switch (visual)
            {
                case CTextBlock ctb:
                    // Block-level container (paragraphs, headings, list markers,
                    // plain-text code blocks, blockquote text, table cells). Its
                    // inline children (CRun / CCode / CHyperlink) carry their OWN
                    // inline Foreground, so walk Content too.
                    ctb.Foreground = fg;
                    foreach (CInline? inline in ctb.Content)
                    {
                        inline.Foreground = fg;
                        if (inline is CCode code)
                        {
                            code.Background = bg;
                        }
                    }

                    break;

                case TextBlock tb:
                    // Plain TextBlocks — fenced code blocks (no-language path),
                    // language watermarks, copy-button labels, etc.
                    tb.Foreground = fg;
                    break;

                case Border b when b.Classes.Contains("CodeBlock"):
                    // Fenced-code-block container — force a visible theme-aware
                    // background + outline (the package's overlay is near-invisible).
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
    /// Resolve a theme-aware brush via the visual tree's resource chain, or fall
    /// back to <paramref name="fallback"/>. Same semantics as <c>{DynamicResource}</c>.
    /// </summary>
    private IBrush ResolveBrush(string key, ThemeVariant theme, IBrush fallback)
    {
        if (Viewer is not null
            && Viewer.TryFindResource(key, theme, out object? raw)
            && raw is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }
}
