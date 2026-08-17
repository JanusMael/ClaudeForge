namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// Lexical categories produced by <see cref="JsoncScanner"/>.
/// </summary>
/// <remarks>
/// Trivia (<see cref="Whitespace"/>, <see cref="LineComment"/>,
/// <see cref="BlockComment"/>) is emitted as tokens rather than skipped. The whole
/// point of this library is that trivia survives a save, and it can only survive if
/// something knows where it is.
/// </remarks>
public enum JsoncTokenKind
{
    /// <summary>A run of spaces, tabs, CR, and/or LF.</summary>
    Whitespace,

    /// <summary><c>// …</c> up to but not including the line break.</summary>
    LineComment,

    /// <summary><c>/* … */</c>, possibly spanning lines.</summary>
    BlockComment,

    /// <summary><c>{</c></summary>
    OpenBrace,

    /// <summary><c>}</c></summary>
    CloseBrace,

    /// <summary><c>[</c></summary>
    OpenBracket,

    /// <summary><c>]</c></summary>
    CloseBracket,

    /// <summary><c>:</c></summary>
    Colon,

    /// <summary><c>,</c></summary>
    Comma,

    /// <summary>A double-quoted string, including its quotes and escapes verbatim.</summary>
    String,

    /// <summary>A JSON number.</summary>
    Number,

    /// <summary><c>true</c></summary>
    True,

    /// <summary><c>false</c></summary>
    False,

    /// <summary><c>null</c></summary>
    Null,

    /// <summary>
    /// Text that is not valid JSONC — an unterminated string or block comment, a
    /// malformed number, an unknown bare word, or a stray character. Its presence
    /// makes the document unsafe to edit; see <see cref="JsoncDocument.IsEditable"/>.
    /// </summary>
    Invalid,
}
