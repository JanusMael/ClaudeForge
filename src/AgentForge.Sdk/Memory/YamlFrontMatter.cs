using System.Text;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// A narrow-spec parser + composer for the YAML front-matter block at the
/// top of Claude Code agent / skill / slash-command files.
///
/// <para>
/// We deliberately do NOT take a YamlDotNet dependency (see
/// <c>docs/SKILLS-AGENTS-COMMANDS-PLAN.md</c> §7).  The full YAML spec is
/// ~30 pages; the front-matter we edit needs ~5% of it.  This parser is
/// pure string manipulation — no reflection, no JSON — so it carries zero
/// IL2026 trim footprint.
/// </para>
///
/// <para><b>Supported constructs:</b></para>
/// <list type="bullet">
///   <item>Top-level scalar string — <c>name: foo</c></item>
///   <item>Top-level inline list — <c>tools: [Read, Grep, Bash]</c></item>
///   <item>Top-level block list —
///         <c>tools:\n  - Read\n  - Grep</c></item>
///   <item>Quoted strings (single or double) — <c>name: "with: colon"</c></item>
///   <item>Folded and literal block scalars — <c>description: &gt;-</c> /
///         <c>|</c>, with chomping (<c>-</c> / <c>+</c>) and an explicit
///         indentation indicator (<c>|2-</c>)</item>
///   <item>Comments on their own line — <c># preserved verbatim</c></item>
///   <item>Empty front-matter (<c>---\n---</c>) — valid, no fields</item>
///   <item>No front-matter at all — returns <see cref="FrontMatter.Present"/>
///         = <see langword="false"/> and the whole text as
///         <see cref="FrontMatter.Body"/></item>
/// </list>
///
/// <para><b>Deliberately unsupported</b> (such keys round-trip verbatim via
/// the field's preserved <see cref="FrontMatterField.RawText"/> but can't be
/// edited through the typed surface): nested objects, anchors/aliases,
/// multi-document streams, custom tags, and YAML implicit type coercion
/// (<c>yes</c>/<c>true</c>/numbers stay strings).</para>
///
/// <para>
/// ⛔⛔ <b>Block scalars used to be unsupported, and the way they failed is
/// the reason they no longer are.</b>  A <c>description: &gt;-</c> header
/// parsed as the plain two-character scalar <c>"&gt;-"</c>, and its
/// continuation lines were re-read as top-level entries — a line containing a
/// colon became a phantom field whose key was a sentence.  Because
/// <c>"&gt;-"</c> is non-empty, every downstream "does this declare a
/// description?" check passed, so a skill whose description was unreadable
/// was reported as healthy.  Measured 2026-08-27: 14 of the skills under
/// <c>~/.claude/skills</c> are written this way.  Silence, not breakage, is
/// what made it worth supporting rather than rejecting.
/// </para>
///
/// <para><b>Round-trip contract:</b> a field parsed from disk keeps its
/// original <see cref="FrontMatterField.RawText"/>.  <see cref="Compose"/>
/// emits that verbatim, so a file passed through Parse → Compose unchanged
/// is byte-identical (modulo a single trailing newline normalisation).  A
/// field the editor mutates (via <see cref="FrontMatter.WithScalar"/> etc.)
/// drops its RawText, so Compose re-renders it canonically — keeping the
/// on-disk diff minimal to exactly the edited lines.</para>
/// </summary>
public static class YamlFrontMatter
{
    private const string OpenDelimiter = "---";

    /// <summary>
    /// Parse <paramref name="text"/> into a <see cref="FrontMatter"/>.  Never
    /// throws — malformed or absent front-matter yields
    /// <c>Present = false</c> with the full text as the body.
    /// </summary>
    public static FrontMatter Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return FrontMatter.None(text ?? string.Empty);
        }

        // Preserve original line endings: split on '\n' but keep any trailing
        // '\r' on each element.  The body is later reconstructed by re-joining
        // the untouched (still-'\r'-bearing) tail with '\n', which restores the
        // original CRLF / LF exactly.
        string[] rawLines = text.Split('\n');

        // The opening delimiter must be the very first line (after an optional
        // UTF-8 BOM).  Anything else → no front-matter.
        string first = rawLines[0].TrimEnd('\r');
        if (first.Length > 0 && first[0] == '﻿')
        {
            first = first[1..];
        }

        if (first.Trim() != OpenDelimiter)
        {
            return FrontMatter.None(text);
        }

        var nodes = new List<FrontMatterNode>();
        int closingIndex = -1;

        for (int i = 1; i < rawLines.Length; i++)
        {
            string raw = rawLines[i].TrimEnd('\r');
            string trimmed = raw.Trim();

            // Closing delimiter — both "---" and "..." are valid YAML block ends.
            if (trimmed is OpenDelimiter or "...")
            {
                closingIndex = i;
                break;
            }

            if (trimmed.Length == 0)
            {
                nodes.Add(new FrontMatterBlankNode());
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                nodes.Add(new FrontMatterCommentNode(raw));
                continue;
            }

            int colon = raw.IndexOf(':');
            if (colon < 0)
            {
                // Not a key line and not a comment — preserve verbatim so we
                // never silently drop a hand-written construct we don't model.
                nodes.Add(new FrontMatterCommentNode(raw));
                continue;
            }

            string key = raw[..colon].Trim();
            string valuePart = raw[(colon + 1)..].Trim();

            if (valuePart.Length == 0)
            {
                // Either an empty scalar OR the header of a block list whose
                // items follow on subsequent "  - x" lines.  Peek ahead.
                var blockItems = new List<string>();
                int blockEnd = i;
                for (int j = i + 1; j < rawLines.Length; j++)
                {
                    string peek = rawLines[j].TrimEnd('\r').Trim();
                    if (peek == "-")
                    {
                        blockItems.Add(string.Empty);
                        blockEnd = j;
                    }
                    else if (peek.StartsWith("- "))
                    {
                        blockItems.Add(StripQuotes(peek[2..].Trim()));
                        blockEnd = j;
                    }
                    else
                    {
                        break;
                    }
                }

                if (blockItems.Count > 0)
                {
                    string rawBlock = string.Join('\n',
                        rawLines[i..(blockEnd + 1)].Select(l => l.TrimEnd('\r')));
                    nodes.Add(new FrontMatterField(key, FrontMatterValue.OfList(blockItems), rawBlock));
                    i = blockEnd;
                }
                else
                {
                    nodes.Add(new FrontMatterField(key, FrontMatterValue.OfScalar(string.Empty), raw));
                }

                continue;
            }

            if (TryReadBlockScalarHeader(valuePart, out bool literal, out char chomping, out int explicitIndent))
            {
                // The header owns every following line that is blank or
                // indented deeper than the key itself.  Consuming them here is
                // what stops a continuation line from being re-read as a field
                // — and what lets the whole block share one RawText, so the
                // round-trip contract survives.
                int keyIndent = CountIndent(raw);
                var blockLines = new List<string>();
                int blockEnd = i;
                for (int j = i + 1; j < rawLines.Length; j++)
                {
                    string peek = rawLines[j].TrimEnd('\r');
                    if (peek.Trim().Length != 0 && CountIndent(peek) <= keyIndent)
                    {
                        break;
                    }

                    blockLines.Add(peek);
                    blockEnd = j;
                }

                // Trailing blank lines only carry meaning under "keep" (+).
                // Leaving them outside the block otherwise keeps them as their
                // own nodes, so editing the field doesn't also delete the blank
                // line that separated it from the next one.
                if (chomping != '+')
                {
                    while (blockLines.Count > 0 && blockLines[^1].Trim().Length == 0)
                    {
                        blockLines.RemoveAt(blockLines.Count - 1);
                        blockEnd--;
                    }
                }

                string folded = ReadBlockScalar(blockLines, literal, chomping, keyIndent, explicitIndent);
                string rawBlock = string.Join('\n',
                    rawLines[i..(blockEnd + 1)].Select(l => l.TrimEnd('\r')));
                nodes.Add(new FrontMatterField(key, FrontMatterValue.OfScalar(folded), rawBlock));
                i = blockEnd;
                continue;
            }

            if (valuePart.Length >= 2 && valuePart[0] == '[' && valuePart[^1] == ']')
            {
                // Inline list.  Empty "[]" → zero items.
                string inner = valuePart[1..^1].Trim();
                var items = inner.Length == 0
                    ? new List<string>()
                    : inner.Split(',').Select(s => StripQuotes(s.Trim())).ToList();
                nodes.Add(new FrontMatterField(key, FrontMatterValue.OfList(items), raw));
                continue;
            }

            nodes.Add(new FrontMatterField(key, FrontMatterValue.OfScalar(StripQuotes(valuePart)), raw));
        }

        // An unterminated front-matter block (no closing "---") is almost
        // certainly not real front-matter — treat the whole text as body.
        if (closingIndex < 0)
        {
            return FrontMatter.None(text);
        }

        string body = closingIndex + 1 < rawLines.Length
            ? string.Join('\n', rawLines[(closingIndex + 1)..])
            : string.Empty;

        return new FrontMatter(Present: true, Nodes: nodes, Body: body);
    }

    /// <summary>
    /// Render a <see cref="FrontMatter"/> back to a single string.  Fields
    /// that still carry their parsed <see cref="FrontMatterField.RawText"/>
    /// are emitted verbatim; fields the editor mutated (RawText = null) are
    /// re-rendered canonically.  When <see cref="FrontMatter.Present"/> is
    /// <see langword="false"/>, the body is returned unchanged.
    /// </summary>
    public static string Compose(FrontMatter frontMatter)
    {
        ArgumentNullException.ThrowIfNull(frontMatter);

        if (!frontMatter.Present)
        {
            return frontMatter.Body;
        }

        // Match the dominant line ending of the body so the composed file is
        // internally consistent; default to '\n'.
        string nl = frontMatter.Body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var sb = new StringBuilder();
        sb.Append(OpenDelimiter).Append(nl);

        foreach (FrontMatterNode node in frontMatter.Nodes)
        {
            switch (node)
            {
                case FrontMatterBlankNode:
                    sb.Append(nl);
                    break;
                case FrontMatterCommentNode comment:
                    sb.Append(comment.RawText).Append(nl);
                    break;
                case FrontMatterField field:
                    // A multi-line field (block list, block scalar) holds its
                    // interior breaks as '\n' regardless of the file's ending,
                    // so re-apply the file's own here — otherwise a CRLF file
                    // silently loses its CRLF everywhere but the first line.
                    sb.Append(WithLineEndings(field.RawText ?? RenderField(field), nl)).Append(nl);
                    break;
            }
        }

        sb.Append(OpenDelimiter).Append(nl);
        sb.Append(frontMatter.Body);
        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Render one field canonically, always with <c>'\n'</c> breaks —
    /// <see cref="WithLineEndings"/> converts them to the file's ending once,
    /// so no renderer has to know what that ending is.
    /// </summary>
    private static string RenderField(FrontMatterField field)
    {
        if (field.Value.IsList)
        {
            var sb = new StringBuilder();
            sb.Append(field.Key).Append(':');
            foreach (string item in field.Value.List!)
            {
                sb.Append('\n').Append("  - ").Append(QuoteIfNeeded(item));
            }

            return sb.ToString();
        }

        string scalar = field.Value.Scalar ?? string.Empty;

        // ⛔ Reading block scalars made a multi-line scalar reachable for the
        // first time, and the editor rewrites `description` on every save.
        // Emitting one as a plain `key: value` line would put a raw newline
        // mid-scalar and corrupt the file, so it goes back as a block.
        return ContainsLineBreak(scalar)
            ? RenderBlockScalar(field.Key, scalar)
            : $"{field.Key}: {QuoteIfNeeded(scalar)}";
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.Contains('\n', StringComparison.Ordinal)
               || value.Contains('\r', StringComparison.Ordinal);
    }

    private static string WithLineEndings(string text, string nl)
    {
        return nl == "\n" ? text : text.Replace("\n", nl, StringComparison.Ordinal);
    }

    // ── Block scalars ────────────────────────────────────────────────────

    /// <summary>How many spaces a canonically re-rendered block scalar indents by.</summary>
    private const int BlockIndent = 2;

    /// <summary>
    /// Recognise a block-scalar header: an indicator (<c>|</c> literal /
    /// <c>&gt;</c> folded), then — in either order — an optional indentation
    /// digit and an optional chomping indicator (<c>-</c> strip / <c>+</c>
    /// keep), then nothing but an optional comment.
    /// </summary>
    /// <remarks>
    /// ⚠ Anything that isn't a well-formed header returns <see langword="false"/>
    /// and keeps its previous plain-scalar reading.  A plain YAML scalar can't
    /// legally begin with <c>|</c> or <c>&gt;</c> anyway, so the only values
    /// that reach here are headers and malformed ones — and guessing at the
    /// malformed ones would trade a visible oddity for a silent one.
    /// </remarks>
    private static bool TryReadBlockScalarHeader(
        string valuePart, out bool literal, out char chomping, out int explicitIndent)
    {
        literal = false;
        chomping = '\0';
        explicitIndent = 0;

        if (valuePart.Length == 0 || (valuePart[0] != '|' && valuePart[0] != '>'))
        {
            return false;
        }

        literal = valuePart[0] == '|';

        int p = 1;
        while (p < valuePart.Length)
        {
            char c = valuePart[p];
            if (c is '+' or '-' && chomping == '\0')
            {
                chomping = c;
            }
            else if (c is >= '1' and <= '9' && explicitIndent == 0)
            {
                explicitIndent = c - '0';
            }
            else
            {
                break;
            }

            p++;
        }

        string rest = valuePart[p..];
        return rest.Length == 0
               || (char.IsWhiteSpace(rest[0]) && rest.TrimStart().StartsWith('#'));
    }

    /// <summary>
    /// Turn a block scalar's continuation lines into its value: folded
    /// (<c>&gt;</c>) joins lines with spaces, literal (<c>|</c>) keeps every
    /// break, and chomping decides how many trailing newlines survive.
    /// </summary>
    private static string ReadBlockScalar(
        IReadOnlyList<string> blockLines, bool literal, char chomping, int keyIndent, int explicitIndent)
    {
        if (blockLines.Count == 0)
        {
            // `description: >-` with nothing under it declares the empty
            // string — which is what keeps "has no description" detectable.
            return string.Empty;
        }

        // YAML auto-detects the content indent from the first non-empty line
        // unless the header states it.
        int contentIndent = explicitIndent > 0
            ? keyIndent + explicitIndent
            : blockLines.Where(l => l.Trim().Length != 0)
                        .Select(CountIndent)
                        .DefaultIfEmpty(keyIndent + BlockIndent)
                        .First();

        var lines = blockLines.Select(l => StripIndent(l, contentIndent)).ToList();

        // A folded scalar's trailing whitespace is absorbed by the fold; a
        // literal one's is content.
        int lastContent = literal
            ? lines.FindLastIndex(l => l.Length != 0)
            : lines.FindLastIndex(l => l.TrimEnd().Length != 0);

        string body = lastContent < 0
            ? string.Empty
            : literal
                ? string.Join('\n', lines.Take(lastContent + 1))
                : Fold(lines.Take(lastContent + 1));

        int trailing = chomping switch
        {
            '-' => 0,
            '+' => lastContent < 0 ? lines.Count : lines.Count - lastContent,
            _ => lastContent < 0 ? 0 : 1,
        };

        return trailing == 0 ? body : body + new string('\n', trailing);
    }

    /// <summary>
    /// Fold content lines the way YAML does: a single break between two
    /// ordinary lines becomes one space, <c>n</c> breaks become <c>n-1</c>
    /// newlines, and a line indented deeper than the block keeps its breaks
    /// verbatim — so an indented example inside a description isn't silently
    /// reflowed onto one line.
    /// </summary>
    private static string Fold(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        int pending = 0;
        bool started = false;
        bool previousWasMoreIndented = false;
        bool first = true;

        foreach (string source in lines)
        {
            if (!first)
            {
                pending++;
            }

            first = false;

            string line = source.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            bool moreIndented = line[0] is ' ' or '\t';

            if (started)
            {
                if (moreIndented || previousWasMoreIndented)
                {
                    sb.Append('\n', pending);
                }
                else if (pending <= 1)
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append('\n', pending - 1);
                }
            }

            sb.Append(line);
            started = true;
            previousWasMoreIndented = moreIndented;
            pending = 0;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Render a multi-line scalar as a literal block, choosing the chomping
    /// indicator that reproduces its trailing newlines exactly so
    /// <see cref="Parse"/> reads back what was written.
    /// </summary>
    private static string RenderBlockScalar(string key, string value)
    {
        // The reader only ever yields '\n', so normalising here keeps
        // write-then-read exact for a value that arrived from elsewhere.
        string text = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        int trailing = 0;
        while (trailing < text.Length && text[^(trailing + 1)] == '\n')
        {
            trailing++;
        }

        string content = text[..(text.Length - trailing)];
        List<string> lines = content.Length == 0 ? [] : [.. content.Split('\n')];

        // Under "keep", each newline past the content needs its own line; with
        // no content at all every one of them does.
        for (int k = content.Length == 0 ? 0 : 1; k < trailing; k++)
        {
            lines.Add(string.Empty);
        }

        char chomping = content.Length == 0
            ? (trailing == 0 ? '-' : '+')
            : trailing switch { 0 => '-', 1 => '\0', _ => '+' };

        // Auto-detection would read a leading space as part of the block's own
        // indentation, so state the indent when the first line has one.
        string? firstContent = lines.Find(l => l.Length != 0);
        bool needsExplicitIndent = firstContent is not null && firstContent[0] == ' ';

        var sb = new StringBuilder();
        sb.Append(key).Append(": |");
        if (needsExplicitIndent)
        {
            sb.Append(BlockIndent);
        }

        if (chomping != '\0')
        {
            sb.Append(chomping);
        }

        foreach (string line in lines)
        {
            sb.Append('\n');
            if (line.Length != 0)
            {
                sb.Append(' ', BlockIndent).Append(line);
            }
        }

        return sb.ToString();
    }

    /// <summary>Leading spaces / tabs, counted as indentation depth.</summary>
    private static int CountIndent(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] is ' ' or '\t')
        {
            n++;
        }

        return n;
    }

    /// <summary>Drop up to <paramref name="count"/> leading whitespace characters.</summary>
    private static string StripIndent(string line, int count)
    {
        int n = 0;
        while (n < count && n < line.Length && line[n] is ' ' or '\t')
        {
            n++;
        }

        return line[n..];
    }

    /// <summary>
    /// Strip a single matching pair of surrounding single or double quotes.
    /// Leaves unquoted text untouched.
    /// </summary>
    private static string StripQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    /// <summary>
    /// Wrap a scalar in double quotes only when emitting it bare would change
    /// its YAML meaning in <b>block context</b> (where we always emit).  In
    /// block context a plain scalar may legally contain mid-string commas,
    /// brackets, and colons-not-followed-by-space — so we quote only for:
    /// <list type="bullet">
    ///   <item>the empty string;</item>
    ///   <item>leading or trailing whitespace (would be trimmed on re-parse);</item>
    ///   <item>a leading YAML indicator character (<c>- ? : , [ ] {{ }} # &amp; * ! | &gt; ' " % @ `</c>)
    ///         which would otherwise start a list / flow / comment / anchor;</item>
    ///   <item><c>": "</c> (colon-space) or a trailing <c>:</c> — the key/value split token;</item>
    ///   <item><c>" #"</c> (space-hash) — starts a trailing comment.</item>
    /// </list>
    /// This keeps the common case — e.g. <c>tools: Read, Grep, Bash</c> —
    /// unquoted, matching Claude Code's native form, while still quoting
    /// genuinely ambiguous values like <c>"Foo: bar"</c> or <c>"[literal]"</c>.
    /// </summary>
    private static string QuoteIfNeeded(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        bool needsQuote =
            value != value.Trim() ||
            IsYamlIndicatorStart(value[0]) ||
            value.Contains(": ", StringComparison.Ordinal) ||
            value.EndsWith(':') ||
            value.Contains(" #", StringComparison.Ordinal);

        if (!needsQuote)
        {
            return value;
        }

        // Escape embedded double quotes, then wrap.
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// The set of characters that, appearing as the FIRST character of a
    /// plain scalar, force quoting because YAML would otherwise read them as
    /// a structural indicator (list dash, flow open, comment, anchor, etc.).
    /// </summary>
    private static bool IsYamlIndicatorStart(char c)
    {
        return c is '-' or '?' or ':' or ',' or '[' or ']' or '{' or '}' or '#'
            or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`';
    }
}