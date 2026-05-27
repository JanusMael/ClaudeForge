using System.Text;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

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
/// multi-document streams, custom tags, folded/literal block scalars
/// (<c>|</c> / <c>&gt;</c>), and YAML implicit type coercion
/// (<c>yes</c>/<c>true</c>/numbers stay strings).</para>
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
                    sb.Append(field.RawText ?? RenderField(field, nl)).Append(nl);
                    break;
            }
        }

        sb.Append(OpenDelimiter).Append(nl);
        sb.Append(frontMatter.Body);
        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string RenderField(FrontMatterField field, string nl)
    {
        if (field.Value.IsList)
        {
            var sb = new StringBuilder();
            sb.Append(field.Key).Append(':');
            foreach (string item in field.Value.List!)
            {
                sb.Append(nl).Append("  - ").Append(QuoteIfNeeded(item));
            }

            return sb.ToString();
        }

        return $"{field.Key}: {QuoteIfNeeded(field.Value.Scalar ?? string.Empty)}";
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