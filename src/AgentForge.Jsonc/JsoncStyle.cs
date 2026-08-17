namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// The formatting conventions detected in a document, so newly inserted text matches
/// what is already there instead of imposing a house style.
/// </summary>
/// <param name="IndentUnit">
/// One level of indentation — e.g. <c>"  "</c>, <c>"    "</c>, or <c>"\t"</c>.
/// </param>
/// <param name="NewLine">The document's line ending: <c>"\n"</c> or <c>"\r\n"</c>.</param>
/// <remarks>
/// Detection over configuration on purpose. A user who indents with tabs and saves
/// through this tool should not find spaces in their file, and should not have to
/// discover a setting to prevent it.
/// </remarks>
public readonly record struct JsoncStyle(string IndentUnit, string NewLine)
{
    /// <summary>
    /// What an empty or single-line document gets: two spaces and the platform's
    /// newline. Two rather than four because it is what both products' own tooling
    /// emits, so a file this tool creates from scratch looks native.
    /// </summary>
    public static JsoncStyle Default => new("  ", Environment.NewLine);

    /// <summary>
    /// Infer style from <paramref name="text"/>. Falls back to <see cref="Default"/>
    /// for whichever aspect cannot be observed.
    /// </summary>
    /// <remarks>
    /// The newline is taken from the <b>first</b> line break, not a majority vote: a
    /// mixed-ending file is already inconsistent, and matching the first occurrence is
    /// predictable rather than clever.
    /// </remarks>
    public static JsoncStyle Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new JsoncStyle(DetectIndentUnit(text), DetectNewLine(text));
    }

    private static string DetectNewLine(string text)
    {
        int lf = text.IndexOf('\n');
        if (lf < 0)
        {
            return Default.NewLine;
        }

        return lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>
    /// Takes the indentation of the first line that has any, which in a JSON document
    /// is a first-level member. Deeper lines would report a multiple of the unit, and
    /// dividing back out would guess wrong on any hand-formatted file.
    /// </summary>
    private static string DetectIndentUnit(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            // Advance to the start of the next line.
            int lineStart = i;
            int lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            int p = lineStart;
            while (p < lineEnd && (text[p] == ' ' || text[p] == '\t'))
            {
                p++;
            }

            // Ignore blank / whitespace-only lines: their leading run is not indentation
            // of anything, and trailing whitespace on an empty line is common.
            bool blank = p >= lineEnd || text[p] == '\r';
            if (!blank && p > lineStart)
            {
                return text[lineStart..p];
            }

            i = lineEnd + 1;
        }

        return Default.IndentUnit;
    }
}
