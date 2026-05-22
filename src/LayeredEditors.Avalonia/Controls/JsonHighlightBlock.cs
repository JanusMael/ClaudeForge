using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Controls;

/// <summary>
/// A read-only, selectable control that renders a JSON string with syntax
/// highlighting. Supports light and dark themes by querying
/// <see cref="Control.ActualThemeVariant"/> and swapping colour sets on theme change.
/// </summary>
/// <remarks>
/// Uses Avalonia's native <see cref="SelectableTextBlock"/> + <see cref="InlineCollection"/>
/// so there are no extra NuGet dependencies and the control is fully trimming-safe.
/// <para>
/// Colour scheme mirrors VS Code's built-in Light+ and Dark+ themes so the JSON
/// looks immediately familiar to most developers.
/// </para>
/// </remarks>
public sealed class JsonHighlightBlock : UserControl
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Styled property
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>JSON string to display with syntax highlighting.</summary>
    public static readonly StyledProperty<string?> JsonProperty =
        AvaloniaProperty.Register<JsonHighlightBlock, string?>(nameof(Json));

    /// <summary>JSON string to display with syntax highlighting.</summary>
    public string? Json
    {
        get => GetValue(JsonProperty);
        set => SetValue(JsonProperty, value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Colour palettes — cached; never re-allocated
    // ─────────────────────────────────────────────────────────────────────────

    // VS Code "Light+" colours
    private static readonly ISolidColorBrush LightKey = new SolidColorBrush(Color.Parse("#0070C0")); // steel-blue
    private static readonly ISolidColorBrush LightString = new SolidColorBrush(Color.Parse("#A31515")); // dark-red
    private static readonly ISolidColorBrush LightNumber = new SolidColorBrush(Color.Parse("#098658")); // forest-green
    private static readonly ISolidColorBrush LightKeyword = new SolidColorBrush(Color.Parse("#7B00D4")); // violet

    // VS Code "Dark+" colours
    private static readonly ISolidColorBrush DarkKey = new SolidColorBrush(Color.Parse("#569CD6")); // sky-blue
    private static readonly ISolidColorBrush DarkString = new SolidColorBrush(Color.Parse("#CE9178")); // orange-tan
    private static readonly ISolidColorBrush DarkNumber = new SolidColorBrush(Color.Parse("#B5CEA8")); // sage-green
    private static readonly ISolidColorBrush DarkKeyword = new SolidColorBrush(Color.Parse("#C586C0")); // lavender

    // ─────────────────────────────────────────────────────────────────────────
    //  Internal controls
    // ─────────────────────────────────────────────────────────────────────────

    private readonly SelectableTextBlock _textBlock;

    public JsonHighlightBlock()
    {
        _textBlock = new SelectableTextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Courier New,monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Content = new ScrollViewer
        {
            Content = _textBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Re-colour whenever the user or OS switches light ↔ dark.
        ActualThemeVariantChanged += (_, _) => RebuildInlines();
        RebuildInlines();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == JsonProperty)
        {
            RebuildInlines();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Inline builder
    // ─────────────────────────────────────────────────────────────────────────

    private void RebuildInlines()
    {
        string json = Json ?? string.Empty;
        bool isDark = ActualThemeVariant == ThemeVariant.Dark;
        InlineCollection inlines = _textBlock.Inlines ??= new InlineCollection();
        inlines.Clear();

        foreach (JsonToken token in JsonTokenizer.Tokenize(json))
        {
            Run run = new() { Text = token.Text };
            ISolidColorBrush? brush = ResolveColor(token.Type, isDark);
            if (brush != null)
            {
                run.Foreground = brush;
            }

            inlines.Add(run);
        }
    }

    private static ISolidColorBrush? ResolveColor(JsonTokenType type, bool dark)
    {
        return type switch
        {
            JsonTokenType.Key => dark ? DarkKey : LightKey,
            JsonTokenType.String => dark ? DarkString : LightString,
            JsonTokenType.Number => dark ? DarkNumber : LightNumber,
            JsonTokenType.Keyword => dark ? DarkKeyword : LightKeyword,
            var _ => null, // Default + Punctuation inherit the theme foreground
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  JSON tokeniser
// ─────────────────────────────────────────────────────────────────────────────

internal enum JsonTokenType
{
    Default,
    Key,
    String,
    Number,
    Keyword,
    Punctuation
}

internal sealed record JsonToken(string Text, JsonTokenType Type);

/// <summary>
/// Minimal JSON tokeniser that produces <see cref="JsonToken"/> values whose
/// <see cref="JsonToken.Text"/> concatenation equals the original input.
/// String keys are identified by looking ahead for a colon after the closing quote.
/// </summary>
internal static class JsonTokenizer
{
    public static IEnumerable<JsonToken> Tokenize(string json)
    {
        int i = 0;
        while (i < json.Length)
        {
            char c = json[i];

            // ── String (key or value) ─────────────────────────────────────────
            if (c == '"')
            {
                int start = i++; // i now points to char after opening quote
                while (i < json.Length)
                {
                    if (json[i] == '\\')
                    {
                        i += 2;
                        continue;
                    } // escape sequence

                    if (json[i] == '"')
                    {
                        i++;
                        break;
                    } // closing quote

                    i++;
                }

                string text = json[start..i];
                bool isKey = NextNonWs(json, i) == ':';
                yield return new JsonToken(text, isKey ? JsonTokenType.Key : JsonTokenType.String);
                continue;
            }

            // ── Number ────────────────────────────────────────────────────────
            if (c == '-' || char.IsAsciiDigit(c))
            {
                int start = i;
                if (c == '-')
                {
                    i++;
                }

                while (i < json.Length && IsNumericBodyChar(json[i]))
                {
                    i++;
                }

                yield return new JsonToken(json[start..i], JsonTokenType.Number);
                continue;
            }

            // ── Keyword: true / false / null ──────────────────────────────────
            if (char.IsAsciiLetter(c))
            {
                int start = i;
                while (i < json.Length && char.IsAsciiLetter(json[i]))
                {
                    i++;
                }

                yield return new JsonToken(json[start..i], JsonTokenType.Keyword);
                continue;
            }

            // ── Structural punctuation ────────────────────────────────────────
            if (c is '{' or '}' or '[' or ']' or ':' or ',')
            {
                yield return new JsonToken(json[i..(i + 1)], JsonTokenType.Punctuation);
                i++;
                continue;
            }

            // ── Whitespace / other (newlines, spaces, tabs) ───────────────────
            {
                int start = i;
                while (i < json.Length && !IsTokenStart(json[i]))
                {
                    i++;
                }

                if (i == start)
                {
                    i++; // safety: always advance on unexpected char
                }

                yield return new JsonToken(json[start..i], JsonTokenType.Default);
            }
        }
    }

    /// <summary>Peek at the next non-whitespace character after position <paramref name="from"/>.</summary>
    private static char? NextNonWs(string s, int from)
    {
        while (from < s.Length)
        {
            if (!char.IsWhiteSpace(s[from]))
            {
                return s[from];
            }

            from++;
        }

        return null;
    }

    private static bool IsNumericBodyChar(char c)
    {
        return char.IsAsciiDigit(c) || c is '.' or 'e' or 'E' or '+' or '-';
    }

    /// <summary>Returns true for characters that begin a new token (not whitespace).</summary>
    private static bool IsTokenStart(char c)
    {
        return c == '"'
               || char.IsAsciiDigit(c)
               || char.IsAsciiLetter(c)
               || c is '{' or '}' or '[' or ']' or ':' or ','
               || c == '-';
    }
}