using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Helpers;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Controls;

/// <summary>
/// A <see cref="TextBlock"/> that renders a description string with any embedded
/// URLs as inline, clickable hyperlinks. Hyperlinks live inside
/// <c>TextBlock.Inlines</c> so they wrap with the surrounding prose — the
/// previous <see cref="HyperlinkButton"/> / <see cref="WrapPanel"/> combination
/// broke onto its own line whenever a URL appeared.
/// <para>
/// Unvisited links render in <c>#1B6EC2</c> (distinct blue); once clicked, the
/// URL is recorded in a session-scoped <see cref="HashSet{T}"/> and re-rendered
/// in <c>#6B3FA0</c> (purple). Both colours were chosen to read well on both
/// Semi Light and Dark variants.
/// </para>
/// <para>
/// Avalonia's <see cref="Run"/> inlines do not expose pointer events, so URL
/// segments are wrapped in <see cref="InlineUIContainer"/> around a nested
/// <see cref="TextBlock"/> — those do raise <see cref="InputElement.PointerPressedEvent"/>.
/// </para>
/// </summary>
public sealed partial class LinkifiedTextBlock : TextBlock
{
    // #006B8A = teal-blue, clearly distinct from env-var blue (#1565C0) and visited purple (#6B3FA0).
    // Instance properties so BrushHelper can read Application.Current resources at render-time
    // rather than at static-init time (before the host's resource dictionary is loaded).
    // Hosts can override any LE.* key in their Application.Resources to retheme without forking.
    private IBrush UnvisitedBrush => BrushHelper.Resolve("LE.LinkBrush", "#006B8A");
    private IBrush VisitedBrush => BrushHelper.Resolve("LE.VisitedBrush", "#6B3FA0");
    private IBrush EnvVarBrush => BrushHelper.Resolve("LE.EnvVarBrush", "#1565C0");

    /// <summary>Session-scoped visited set. Cleared when the process exits.</summary>
    private static readonly HashSet<Uri> VisitedUris = new();

    /// <summary>
    /// Optional host-provided lookup for environment-variable descriptions.
    /// When set, hover tooltips on env-var tokens show the description followed by
    /// a "click to navigate" hint. Set once at application startup.
    /// </summary>
    public static Func<string, string?>? EnvVarDescriptionProvider { get; set; }

    /// <summary>
    /// The source text to linkify. Separate from <see cref="TextBlock.Text"/>
    /// because TextBlock treats <c>Text</c> and <c>Inlines</c> as mutually
    /// exclusive — setting Text clears Inlines on the next layout pass.
    /// </summary>
    public static readonly StyledProperty<string?> SourceTextProperty =
        AvaloniaProperty.Register<LinkifiedTextBlock, string?>(nameof(SourceText));

    public string? SourceText
    {
        get => GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    static LinkifiedTextBlock()
    {
        SourceTextProperty.Changed.AddClassHandler<LinkifiedTextBlock>((x, _) => x.Rebuild());
    }

    public LinkifiedTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
    }

    // Matches http:// and https:// URLs, stopping at whitespace.
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // Matches env-var names: ALL_CAPS_WITH_UNDERSCORES or digits after leading letter.
    [GeneratedRegex(@"\b([A-Z][A-Z0-9_]*[_0-9][A-Z0-9_]*)\b")]
    private static partial Regex EnvVarPattern();

    private void Rebuild()
    {
        // Reset both — Text must be null for Inlines to render.
        Inlines?.Clear();
        SetCurrentValue(TextProperty, null);

        // Split multi-sentence descriptions onto separate paragraphs *before*
        // linkification runs so the blank-line boundary survives into the
        // rendered Inlines. See DescriptionFormatter for the regex contract.
        string? source = DescriptionFormatter.SplitSentencesOntoLines(SourceText);
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        Inlines ??= new InlineCollection();

        MatchCollection matches = UrlPattern().Matches(source);
        if (matches.Count == 0)
        {
            AddTextWithEnvVars(source);
            return;
        }

        int pos = 0;
        foreach (Match m in matches)
        {
            if (m.Index > pos)
            {
                AddTextWithEnvVars(source[pos..m.Index]);
            }

            Inlines.Add(MakeLinkInline(m.Value));
            pos = m.Index + m.Length;
        }

        if (pos < source.Length)
        {
            AddTextWithEnvVars(source[pos..]);
        }
    }

    private void AddTextWithEnvVars(string text)
    {
        string[] parts = EnvVarPattern().Split(text);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
            {
                continue;
            }

            if (i % 2 == 1)
            {
                Inlines!.Add(MakeEnvVarInline(parts[i]));
            }
            else
            {
                Inlines!.Add(new Run { Text = parts[i] });
            }
        }
    }

    /// <summary>
    /// Creates a clickable inline for an environment-variable token.
    /// Left-clicking sends a <see cref="NavigateToEnvVarMessage"/> via
    /// <see cref="WeakReferenceMessenger.Default"/> so the main window can
    /// navigate to the Environment section and filter to this variable.
    /// </summary>
    private InlineUIContainer MakeEnvVarInline(string varName)
    {
        // No underline — env-var tokens are in-app deep links, not external URLs.
        // The distinct teal-blue colour (EnvVarBrush) is enough to signal interactivity.
        TextBlock tb = new()
        {
            Text = varName,
            Foreground = EnvVarBrush,
            TextDecorations = null,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = FontSize,
            FontFamily = FontFamily,
        };
        string tipText = BuildEnvVarTooltip(varName);
        ToolTip.SetTip(tb, tipText);
        tb.PointerPressed += (_, e) => OnEnvVarPressed(varName, e);
        return new InlineUIContainer { Child = tb, BaselineAlignment = BaselineAlignment.Center };
    }

    private static string BuildEnvVarTooltip(string varName)
    {
        string? desc = EnvVarDescriptionProvider?.Invoke(varName);
        return desc is null
            ? $"{varName} — click to navigate to the Environment section"
            : $"{varName}\n{desc}\n\nClick to navigate to the Environment section.";
    }

    private static void OnEnvVarPressed(string varName, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            return;
        }

        WeakReferenceMessenger.Default.Send(new NavigateToEnvVarMessage(varName));
        e.Handled = true;
    }

    private InlineUIContainer MakeLinkInline(string url)
    {
        TextBlock link = new()
        {
            Text = url,
            TextDecorations = global::Avalonia.Media.TextDecorations.Underline,
            Foreground = IsVisited(url) ? VisitedBrush : UnvisitedBrush,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            // Inherit enclosing TextBlock's font properties where possible.
            FontSize = FontSize,
            FontFamily = FontFamily,
        };

        link.PointerPressed += (_, e) => OnLinkPressed(link, url, e);

        return new InlineUIContainer { Child = link, BaselineAlignment = BaselineAlignment.Center };
    }

    private static bool IsVisited(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && VisitedUris.Contains(uri);
    }

    private void OnLinkPressed(TextBlock linkBlock, string url, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        VisitedUris.Add(uri);
        linkBlock.Foreground = VisitedBrush;

        TopLevel? top = TopLevel.GetTopLevel(this);
        _ = top?.Launcher.LaunchUriAsync(uri);

        e.Handled = true;
    }
}