using System.Text;
using System.Text.Json;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Diagnostics;

/// <summary>
/// Pure-function helpers for rendering JSON values into bounded display
/// strings.  Backs UI tooltips, audit messages, MCP server response
/// previews, and any other surface where a long JSON blob needs to be
/// shaped into a "fits-on-screen" representation without truncating
/// silently.
/// </summary>
/// <remarks>
/// The IValueConverter wrapper stays
/// App-side because <see cref="System.Globalization.CultureInfo"/>-typed
/// signatures are an Avalonia data-binding concern, not a domain one.
/// </remarks>
public static class JsonFormatting
{
    /// <summary>Default cap on rendered lines before truncation.</summary>
    public const int DefaultMaxLines = 30;

    /// <summary>Default cap on rendered characters before truncation.</summary>
    public const int DefaultMaxChars = 2_000;

    /// <summary>
    /// Footer appended when output is truncated, pointing the user at the
    /// canonical right-click → Copy escape hatch for the full value.
    /// </summary>
    public const string TruncationFooter =
        "\n…\n(content truncated — right-click → Copy for the full value)";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> looks
    /// shaped like a JSON object or array (after trimming whitespace).
    /// Cheap structural check; does not attempt to parse.
    /// </summary>
    public static bool LooksLikeJson(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
        return trimmed.Length >= 2 &&
               ((trimmed[0] == '{' && trimmed[^1] == '}') ||
                (trimmed[0] == '[' && trimmed[^1] == ']'));
    }

    /// <summary>
    /// Pretty-print <paramref name="rawJson"/> with indentation.  Returns
    /// the indented string on success, or <see langword="null"/> when the
    /// input is null/empty or fails to parse as JSON (caller decides
    /// whether to fall back to the raw value).
    /// </summary>
    public static string? TryPrettyPrint(string? rawJson)
    {
        if (string.IsNullOrEmpty(rawJson))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(rawJson.Trim());
            using MemoryStream ms = new();
            using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
            {
                doc.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cap <paramref name="text"/> at <paramref name="maxLines"/> lines and
    /// <paramref name="maxChars"/> characters, appending
    /// <see cref="TruncationFooter"/> when truncation occurs.
    /// </summary>
    /// <remarks>
    /// Truncation is line-first (preserves whole lines, more readable than
    /// a mid-line cut), then char-cap on top.  When the input fits within
    /// both caps it is returned unchanged.
    /// </remarks>
    public static string Cap(
        string text,
        int maxLines = DefaultMaxLines,
        int maxChars = DefaultMaxChars)
    {
        if (text.Length <= maxChars && CountLines(text) <= maxLines)
        {
            return text;
        }

        string[] lines = text.Split('\n');
        int keep = Math.Min(lines.Length, maxLines);
        string head = string.Join('\n', lines, 0, keep).TrimEnd('\r');
        if (head.Length > maxChars)
        {
            head = head[..maxChars];
        }

        return head + TruncationFooter;
    }

    /// <summary>Count <c>\n</c> occurrences + 1 to get a line count (cheaper than Split).</summary>
    private static int CountLines(string text)
    {
        int n = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                n++;
            }
        }

        return n;
    }
}