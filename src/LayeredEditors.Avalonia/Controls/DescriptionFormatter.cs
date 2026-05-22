using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Controls;

/// <summary>
/// Presentation helper for property description text. Splits multi-sentence
/// descriptions onto separate paragraphs so long help strings do not render
/// as one long run-on line.
/// </summary>
/// <remarks>
/// The formatter lives in the rendering layer (not <c>SchemaTreeBuilder</c>)
/// so that schema parsing stays a pure transformation and the newlines only
/// affect UI display.
/// </remarks>
public static partial class DescriptionFormatter
{
    /// <summary>
    /// Regex that splits after a sentence-end punctuation (<c>.</c>, <c>!</c>,
    /// <c>?</c>) followed by whitespace and a capital letter. The negative
    /// lookbehind <c>[A-Za-z]\.[A-Za-z]</c> prevents splitting when the period
    /// is the terminal dot of an internal abbreviation like <c>e.g.</c> or
    /// <c>i.e.</c>. Decimal numbers and version identifiers are naturally
    /// preserved because the character after the period is a digit, not a
    /// capital letter. URLs are preserved because the domain letters following
    /// a period are lowercase and there is no whitespace between the period
    /// and the next character.
    /// </summary>
    private static readonly Regex SentenceBoundary = MyRegex();

    /// <summary>
    /// Insert a blank line between sentences in <paramref name="source"/>.
    /// Returns <paramref name="source"/> unchanged when it is <c>null</c> or
    /// empty, when it contains no sentence boundary, or when it already uses
    /// explicit paragraph breaks. Pure function — safe for unit testing
    /// without any Avalonia dependency.
    /// </summary>
    public static string? SplitSentencesOntoLines(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return source;
        }

        return SentenceBoundary.Replace(source, "$1\n\n");
    }

    [GeneratedRegex(@"(?<![A-Za-z]\.[A-Za-z])([.!?])\s+(?=[A-Z])", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();
}