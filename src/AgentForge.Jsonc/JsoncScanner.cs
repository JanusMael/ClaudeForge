namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// Turns JSONC text into a flat token list with offsets. Comments and whitespace are
/// emitted, not discarded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never throws on bad input.</b> Malformed text becomes a
/// <see cref="JsoncTokenKind.Invalid"/> token and scanning continues, so a caller
/// always gets a complete token list and can decide what to do. That matters because
/// the alternative — throwing — is how the config loader this replaces ended up
/// treating an unparseable file as an empty one and overwriting it.
/// </para>
/// <para>
/// Strings are captured verbatim, quotes and escape sequences included. The scanner
/// validates escapes well enough to find the closing quote; it does not unescape.
/// Unescaping is the parser's job, and only for object keys.
/// </para>
/// </remarks>
public static class JsoncScanner
{
    /// <summary>Scan <paramref name="text"/> into tokens covering it end to end.</summary>
    /// <remarks>
    /// The returned tokens are contiguous and gapless: concatenating every token's text
    /// reproduces <paramref name="text"/> exactly. <see cref="JsoncEditor"/> depends on
    /// that, and <c>JsoncScannerTests</c> asserts it.
    /// </remarks>
    public static IReadOnlyList<JsoncToken> Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<JsoncToken> tokens = [];
        int pos = 0;

        while (pos < text.Length)
        {
            char c = text[pos];

            if (IsWhitespace(c))
            {
                int start = pos;
                while (pos < text.Length && IsWhitespace(text[pos]))
                {
                    pos++;
                }

                tokens.Add(new JsoncToken(JsoncTokenKind.Whitespace, start, pos - start));
                continue;
            }

            switch (c)
            {
                case '/':
                    tokens.Add(ScanSlash(text, ref pos));
                    continue;

                case '"':
                    tokens.Add(ScanString(text, ref pos));
                    continue;

                case '{':
                    tokens.Add(Single(JsoncTokenKind.OpenBrace, ref pos));
                    continue;
                case '}':
                    tokens.Add(Single(JsoncTokenKind.CloseBrace, ref pos));
                    continue;
                case '[':
                    tokens.Add(Single(JsoncTokenKind.OpenBracket, ref pos));
                    continue;
                case ']':
                    tokens.Add(Single(JsoncTokenKind.CloseBracket, ref pos));
                    continue;
                case ':':
                    tokens.Add(Single(JsoncTokenKind.Colon, ref pos));
                    continue;
                case ',':
                    tokens.Add(Single(JsoncTokenKind.Comma, ref pos));
                    continue;
            }

            if (c == '-' || IsDigit(c))
            {
                tokens.Add(ScanNumber(text, ref pos));
                continue;
            }

            if (IsLetter(c))
            {
                tokens.Add(ScanWord(text, ref pos));
                continue;
            }

            tokens.Add(Single(JsoncTokenKind.Invalid, ref pos));
        }

        return tokens;
    }

    private static JsoncToken Single(JsoncTokenKind kind, ref int pos)
    {
        JsoncToken token = new(kind, pos, 1);
        pos++;
        return token;
    }

    private static JsoncToken ScanSlash(string text, ref int pos)
    {
        int start = pos;

        if (pos + 1 < text.Length && text[pos + 1] == '/')
        {
            pos += 2;
            while (pos < text.Length && text[pos] != '\n' && text[pos] != '\r')
            {
                pos++;
            }

            return new JsoncToken(JsoncTokenKind.LineComment, start, pos - start);
        }

        if (pos + 1 < text.Length && text[pos + 1] == '*')
        {
            pos += 2;
            while (pos < text.Length)
            {
                if (text[pos] == '*' && pos + 1 < text.Length && text[pos + 1] == '/')
                {
                    pos += 2;
                    return new JsoncToken(JsoncTokenKind.BlockComment, start, pos - start);
                }

                pos++;
            }

            // Unterminated: consume to EOF and report it. Editing a document whose
            // comment never closes would append text inside the comment.
            return new JsoncToken(JsoncTokenKind.Invalid, start, pos - start);
        }

        pos++;
        return new JsoncToken(JsoncTokenKind.Invalid, start, 1);
    }

    private static JsoncToken ScanString(string text, ref int pos)
    {
        int start = pos;
        pos++; // opening quote

        while (pos < text.Length)
        {
            char c = text[pos];

            if (c == '\\')
            {
                // Consume the escape introducer plus one char so an escaped quote does
                // not terminate the string. \uXXXX's four hex digits are ordinary
                // characters to the scanner; the parser validates them when it unescapes.
                pos += 2;
                continue;
            }

            if (c == '"')
            {
                pos++;
                return new JsoncToken(JsoncTokenKind.String, start, pos - start);
            }

            if (c is '\n' or '\r')
            {
                // A raw newline inside a string is invalid JSON. Stop at the line break
                // rather than swallowing the rest of the file.
                return new JsoncToken(JsoncTokenKind.Invalid, start, pos - start);
            }

            pos++;
        }

        // Ran off the end, possibly because a trailing backslash pushed pos past Length.
        pos = text.Length;
        return new JsoncToken(JsoncTokenKind.Invalid, start, pos - start);
    }

    private static JsoncToken ScanNumber(string text, ref int pos)
    {
        int start = pos;

        if (pos < text.Length && text[pos] == '-')
        {
            pos++;
        }

        int intStart = pos;
        bool ok = ScanDigits(text, ref pos, out int intDigits);

        // JSON forbids leading zeros — "01" is invalid, a lone "0" is fine.
        if (ok && intDigits > 1 && text[intStart] == '0')
        {
            ok = false;
        }

        if (ok && pos < text.Length && text[pos] == '.')
        {
            pos++;
            ok = ScanDigits(text, ref pos, out _);
        }

        if (ok && pos < text.Length && (text[pos] == 'e' || text[pos] == 'E'))
        {
            pos++;
            if (pos < text.Length && (text[pos] == '+' || text[pos] == '-'))
            {
                pos++;
            }

            ok = ScanDigits(text, ref pos, out _);
        }

        // Trailing junk directly attached to the number ("1abc", "1.2.3") is invalid.
        if (ok && pos < text.Length && (IsLetter(text[pos]) || IsDigit(text[pos]) || text[pos] == '.'))
        {
            ok = false;
        }

        if (!ok)
        {
            // Consume the rest of the malformed run so the caller sees one Invalid token
            // rather than a cascade.
            while (pos < text.Length && (IsLetter(text[pos]) || IsDigit(text[pos])
                                         || text[pos] is '.' or '+' or '-'))
            {
                pos++;
            }

            return new JsoncToken(JsoncTokenKind.Invalid, start, pos - start);
        }

        return new JsoncToken(JsoncTokenKind.Number, start, pos - start);
    }

    private static bool ScanDigits(string text, ref int pos, out int count)
    {
        int start = pos;
        while (pos < text.Length && IsDigit(text[pos]))
        {
            pos++;
        }

        count = pos - start;
        return count > 0;
    }

    private static JsoncToken ScanWord(string text, ref int pos)
    {
        int start = pos;
        while (pos < text.Length && IsLetter(text[pos]))
        {
            pos++;
        }

        int length = pos - start;
        ReadOnlySpan<char> word = text.AsSpan(start, length);

        JsoncTokenKind kind = word switch
        {
            "true" => JsoncTokenKind.True,
            "false" => JsoncTokenKind.False,
            "null" => JsoncTokenKind.Null,
            _ => JsoncTokenKind.Invalid,
        };

        return new JsoncToken(kind, start, length);
    }

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
