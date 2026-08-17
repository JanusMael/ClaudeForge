namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// A parsed JSONC document: the original text, a span-carrying tree, and the
/// formatting style detected from it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parsing never throws.</b> A malformed document parses to whatever structure was
/// recoverable plus a non-empty <see cref="Errors"/>, and <see cref="IsEditable"/> goes
/// <see langword="false"/>. Callers must check that before writing — see
/// <see cref="JsoncEditor"/>, which refuses outright.
/// </para>
/// <para>
/// That refusal is the single most important safety property in this library. The
/// loader it replaces did the opposite: it caught the parse exception, substituted an
/// <i>empty</i> document, and the next save then wrote that empty document over the
/// user's file. Editing text we did not fully understand is how config gets corrupted,
/// so a document we could not parse is one we decline to edit.
/// </para>
/// </remarks>
public sealed class JsoncDocument
{
    private JsoncDocument(string text, JsoncValue? root, IReadOnlyList<string> errors, JsoncStyle style)
    {
        Text = text;
        Root = root;
        Errors = errors;
        Style = style;
    }

    /// <summary>The original, unmodified source text.</summary>
    public string Text { get; }

    /// <summary>
    /// The root value, or <see langword="null"/> when the document held no value at all
    /// (empty, whitespace-only, or comments-only).
    /// </summary>
    public JsoncValue? Root { get; }

    /// <summary>Human-readable parse problems. Empty for a well-formed document.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Formatting conventions detected from <see cref="Text"/>.</summary>
    public JsoncStyle Style { get; }

    /// <summary>
    /// <see langword="true"/> when the document parsed cleanly enough to edit safely.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> <see cref="Root"/> is still editable: an empty file is
    /// perfectly understood, and setting a path in one should produce a new object
    /// rather than an error.
    /// </remarks>
    public bool IsEditable => Errors.Count == 0;

    /// <summary>
    /// Parse <paramref name="text"/>. Comments and trailing commas are accepted;
    /// anything else that is not JSON is reported through <see cref="Errors"/>.
    /// </summary>
    /// <remarks>
    /// Trailing commas are tolerated rather than flagged because JSONC in the wild has
    /// them, and rejecting the document would route the caller onto a lossy fallback for
    /// something every JSONC parser accepts.
    /// </remarks>
    public static JsoncDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<JsoncToken> tokens = JsoncScanner.Scan(text);
        Parser parser = new(text, tokens);
        JsoncValue? root = parser.ParseDocument();

        return new JsoncDocument(text, root, parser.Errors, JsoncStyle.Detect(text));
    }

    /// <summary>
    /// Resolve a dotted path to its member, or <see langword="null"/> when any segment
    /// is missing or a non-object is encountered on the way down.
    /// </summary>
    /// <remarks>
    /// Path semantics deliberately mirror the SDK's existing traversal
    /// (<c>AgentConfigClientCore.SetNested</c> / <c>ResolveByPath</c>): split on
    /// <c>'.'</c>, objects only, no array indexing, and a key containing a dot is
    /// therefore unreachable. Matching the established convention exactly is what makes
    /// this writer a drop-in — divergence here would show up as paths that write to one
    /// place and read from another.
    /// </remarks>
    public JsoncMember? FindMember(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (Root is not { Kind: JsoncValueKind.Object } current)
        {
            return null;
        }

        string[] segments = path.Split('.');
        JsoncMember? member = null;

        for (int i = 0; i < segments.Length; i++)
        {
            member = current.FindMember(segments[i]);
            if (member is null)
            {
                return null;
            }

            bool isLast = i == segments.Length - 1;
            if (isLast)
            {
                return member;
            }

            // A non-object mid-path means the remaining segments cannot exist.
            if (member.Value.Kind != JsoncValueKind.Object)
            {
                return null;
            }

            current = member.Value;
        }

        return member;
    }

    // ── Recursive-descent parser over the token list ─────────────────────────

    private sealed class Parser(string text, IReadOnlyList<JsoncToken> tokens)
    {
        private readonly List<string> _errors = [];
        private int _index;

        public IReadOnlyList<string> Errors => _errors;

        public JsoncValue? ParseDocument()
        {
            SkipTrivia();

            if (AtEnd)
            {
                return null; // empty / comments-only: valid and editable
            }

            JsoncValue? root = ParseValue();

            SkipTrivia();
            if (!AtEnd)
            {
                _errors.Add($"Unexpected trailing content at offset {Current.Start}.");
            }

            return root;
        }

        private bool AtEnd => _index >= tokens.Count;

        private JsoncToken Current => tokens[_index];

        private void SkipTrivia()
        {
            while (_index < tokens.Count && tokens[_index].IsTrivia)
            {
                _index++;
            }
        }

        private JsoncValue? ParseValue()
        {
            SkipTrivia();
            if (AtEnd)
            {
                _errors.Add("Unexpected end of document; a value was expected.");
                return null;
            }

            JsoncToken token = Current;

            switch (token.Kind)
            {
                case JsoncTokenKind.OpenBrace:
                    return ParseObject();

                case JsoncTokenKind.OpenBracket:
                    return ParseArray();

                case JsoncTokenKind.String:
                    _index++;
                    return new JsoncValue(JsoncValueKind.String, token.Start, token.End);

                case JsoncTokenKind.Number:
                    _index++;
                    return new JsoncValue(JsoncValueKind.Number, token.Start, token.End);

                case JsoncTokenKind.True:
                    _index++;
                    return new JsoncValue(JsoncValueKind.True, token.Start, token.End);

                case JsoncTokenKind.False:
                    _index++;
                    return new JsoncValue(JsoncValueKind.False, token.Start, token.End);

                case JsoncTokenKind.Null:
                    _index++;
                    return new JsoncValue(JsoncValueKind.Null, token.Start, token.End);

                default:
                    _errors.Add($"Unexpected {token.Kind} at offset {token.Start}; a value was expected.");
                    _index++;
                    return null;
            }
        }

        private JsoncValue ParseObject()
        {
            JsoncToken open = Current;
            _index++; // '{'

            JsoncValue obj = new(JsoncValueKind.Object, open.Start, open.End);

            while (true)
            {
                SkipTrivia();
                if (AtEnd)
                {
                    _errors.Add($"Unterminated object opened at offset {open.Start}.");
                    obj.End = text.Length;
                    return obj;
                }

                if (Current.Kind == JsoncTokenKind.CloseBrace)
                {
                    obj.End = Current.End;
                    _index++;
                    return obj;
                }

                if (Current.Kind != JsoncTokenKind.String)
                {
                    _errors.Add($"Expected a quoted member name at offset {Current.Start}, "
                                + $"found {Current.Kind}.");
                    obj.End = Current.End;
                    return obj;
                }

                JsoncToken keyToken = Current;
                string? name = TryUnescape(keyToken);
                _index++;

                SkipTrivia();
                if (AtEnd || Current.Kind != JsoncTokenKind.Colon)
                {
                    _errors.Add($"Expected ':' after the member name at offset {keyToken.Start}.");
                    obj.End = AtEnd ? text.Length : Current.End;
                    return obj;
                }

                _index++; // ':'

                JsoncValue? value = ParseValue();
                if (value is null)
                {
                    obj.End = AtEnd ? text.Length : Current.Start;
                    return obj;
                }

                if (name is not null)
                {
                    obj.AddMember(new JsoncMember(name, keyToken.Start, keyToken.End, value));
                }

                SkipTrivia();
                if (AtEnd)
                {
                    _errors.Add($"Unterminated object opened at offset {open.Start}.");
                    obj.End = text.Length;
                    return obj;
                }

                if (Current.Kind == JsoncTokenKind.Comma)
                {
                    _index++;
                    continue; // a following '}' is a tolerated trailing comma
                }

                if (Current.Kind == JsoncTokenKind.CloseBrace)
                {
                    obj.End = Current.End;
                    _index++;
                    return obj;
                }

                _errors.Add($"Expected ',' or '}}' at offset {Current.Start}, found {Current.Kind}.");
                obj.End = Current.End;
                return obj;
            }
        }

        private JsoncValue ParseArray()
        {
            JsoncToken open = Current;
            _index++; // '['

            JsoncValue arr = new(JsoncValueKind.Array, open.Start, open.End);

            while (true)
            {
                SkipTrivia();
                if (AtEnd)
                {
                    _errors.Add($"Unterminated array opened at offset {open.Start}.");
                    arr.End = text.Length;
                    return arr;
                }

                if (Current.Kind == JsoncTokenKind.CloseBracket)
                {
                    arr.End = Current.End;
                    _index++;
                    return arr;
                }

                JsoncValue? item = ParseValue();
                if (item is null)
                {
                    arr.End = AtEnd ? text.Length : Current.Start;
                    return arr;
                }

                arr.AddItem(item);

                SkipTrivia();
                if (AtEnd)
                {
                    _errors.Add($"Unterminated array opened at offset {open.Start}.");
                    arr.End = text.Length;
                    return arr;
                }

                if (Current.Kind == JsoncTokenKind.Comma)
                {
                    _index++;
                    continue; // trailing comma tolerated
                }

                if (Current.Kind == JsoncTokenKind.CloseBracket)
                {
                    arr.End = Current.End;
                    _index++;
                    return arr;
                }

                _errors.Add($"Expected ',' or ']' at offset {Current.Start}, found {Current.Kind}.");
                arr.End = Current.End;
                return arr;
            }
        }

        /// <summary>
        /// Unescape a string token's contents. Returns <see langword="null"/> and records
        /// an error for a malformed escape — a key we cannot read is a key we must not
        /// silently mismatch against a caller's path.
        /// </summary>
        private string? TryUnescape(JsoncToken token)
        {
            // Strip the surrounding quotes.
            ReadOnlySpan<char> body = text.AsSpan(token.Start + 1, token.Length - 2);

            if (body.IndexOf('\\') < 0)
            {
                return new string(body);
            }

            System.Text.StringBuilder sb = new(body.Length);
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] != '\\')
                {
                    sb.Append(body[i]);
                    continue;
                }

                i++;
                if (i >= body.Length)
                {
                    _errors.Add($"Trailing backslash in the string at offset {token.Start}.");
                    return null;
                }

                switch (body[i])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= body.Length
                            || !ushort.TryParse(body.Slice(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                                System.Globalization.CultureInfo.InvariantCulture, out ushort code))
                        {
                            _errors.Add($"Malformed \\u escape in the string at offset {token.Start}.");
                            return null;
                        }

                        sb.Append((char)code);
                        i += 4;
                        break;

                    default:
                        _errors.Add($"Unknown escape '\\{body[i]}' in the string at offset {token.Start}.");
                        return null;
                }
            }

            return sb.ToString();
        }
    }
}
