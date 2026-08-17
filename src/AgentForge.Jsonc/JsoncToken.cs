namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// One lexical token, identified by its span in the source text rather than by a
/// copied string.
/// </summary>
/// <param name="Kind">Lexical category.</param>
/// <param name="Start">Zero-based index of the token's first character.</param>
/// <param name="Length">Character count.</param>
/// <remarks>
/// Spans, not substrings: every edit this library makes is expressed as a span
/// replacement against the original text, so tokens that carry offsets are the whole
/// mechanism by which untouched bytes stay untouched.
/// </remarks>
public readonly record struct JsoncToken(JsoncTokenKind Kind, int Start, int Length)
{
    /// <summary>Index one past the token's last character.</summary>
    public int End => Start + Length;

    /// <summary>
    /// <see langword="true"/> for whitespace and comments — tokens that carry no
    /// structural meaning and are skipped by the parser.
    /// </summary>
    public bool IsTrivia =>
        Kind is JsoncTokenKind.Whitespace or JsoncTokenKind.LineComment or JsoncTokenKind.BlockComment;

    /// <summary>The token's text, sliced out of <paramref name="source"/>.</summary>
    public ReadOnlySpan<char> Text(string source) => source.AsSpan(Start, Length);
}
