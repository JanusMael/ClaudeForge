using System.Text;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Fuzzy, case- and separator-insensitive matcher for the model picker. A query
/// matches when every whitespace-normalized token of the query appears (as a
/// substring) in the normalized haystack — so "Opus" matches <c>claude-opus-4-8</c>,
/// <c>opus[1m]</c>, and the brand label <c>Opus 4.8</c>, while "opus 4" narrows to the
/// 4.x family. Kept UI-free so it is directly unit-testable; the AutoCompleteBox
/// control just forwards its item's value+label here.
/// </summary>
public static class ModelSuggestionFilter
{
    /// <summary>
    /// True when <paramref name="query"/> fuzzy-matches <paramref name="haystack"/>.
    /// An empty/whitespace query matches everything (so focus shows the full list).
    /// </summary>
    public static bool Matches(string? query, string? haystack)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string normalizedHaystack = Normalize(haystack ?? string.Empty);
        foreach (string token in Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!normalizedHaystack.Contains(token, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Lower-case and collapse every non-alphanumeric run to a single space, so
    /// separators never block a match: <c>claude-opus-4-8[1m]</c> → <c>claude opus 4 8 1m</c>.
    /// </summary>
    private static string Normalize(string value)
    {
        StringBuilder sb = new(value.Length);
        bool lastWasSpace = true; // leading separators produce no leading space
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return sb.ToString();
    }
}
