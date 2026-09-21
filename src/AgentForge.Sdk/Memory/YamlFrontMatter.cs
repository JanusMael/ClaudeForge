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
///   <item>Comments on their own line — <c># preserved verbatim</c></item>
///   <item>Empty front-matter (<c>---\n---</c>) — valid, no fields</item>
///   <item>No front-matter at all — returns <see cref="FrontMatter.Present"/>
///         = <see langword="false"/> and the whole text as
///         <see cref="FrontMatter.Body"/></item>
///   <item>Block scalars — <c>description: &gt;-</c> followed by indented
///         lines.  Both folded (<c>&gt;</c>) and literal (<c>|</c>), all three
///         chomping indicators, and explicit indentation indicators.  Every
///         skill Claude Code ships writes its <c>description</c> this way,
///         because the prose contains quotes and colons that a plain scalar
///         would have to escape.</item>
///   <item>Multi-line flow scalars — a quoted or plain value carried across
///         indented lines with no block indicator, opening either on the key
///         line or on the line below it.  The lines fold with spaces, and the
///         surrounding quotes come off the joined value, since the opening and
///         closing quote sit on different lines.</item>
/// </list>
///
/// <para><b>Deliberately unsupported</b>: nested mappings, anchors/aliases,
/// multi-document streams, custom tags, and YAML implicit type coercion
/// (<c>yes</c>/<c>true</c>/numbers stay strings).  A nested mapping is consumed
/// whole and re-emitted verbatim, so it round-trips byte-for-byte while staying
/// invisible to <see cref="FrontMatter.FindScalar"/> and
/// <see cref="FrontMatter.WithScalar"/>.  That invisibility is the point: a
/// nested key surfaced as a top-level field would be re-rendered at column 0 on
/// edit, silently lifting it out of its parent.</para>
///
/// <para>See <c>docs/YAML-FRONT-MATTER.md</c> for the full token reference.</para>
///
/// <para><b>Block-scalar round-trip:</b> a field that arrived as a block scalar
/// records its style on <see cref="FrontMatterField.Block"/>, and
/// <see cref="FrontMatter.WithScalar"/> carries that style onto the edited
/// field — so editing a folded <c>description</c> writes a folded
/// <c>description</c> back rather than collapsing it into one very long plain
/// line and churning the whole file on first edit.  A value containing a
/// newline can never be a plain scalar, so it is emitted as a literal block
/// even when it did not arrive as one.</para>
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

            // A top-level key sits at column 0, so an indented key line belongs
            // to a nested mapping — which the typed surface deliberately does
            // not model.  Emitting it as a field would invent a phantom
            // top-level key AND let an edit re-render it at column 0, silently
            // lifting it out of its parent.  Pass it through verbatim instead.
            // A well-formed mapping is consumed whole by its parent below; this
            // is the backstop for an indented line that arrives some other way.
            if (char.IsWhiteSpace(raw[0]))
            {
                nodes.Add(new FrontMatterCommentNode(raw));
                continue;
            }

            string key = raw[..colon].Trim();
            string valuePart = raw[(colon + 1)..].Trim();

            if (valuePart.Length == 0)
            {
                // The value is not on this line, so it is one of: a block list
                // ("  - x"), a nested mapping ("  k: v"), a multi-line flow
                // scalar (indented prose with no block indicator), or a
                // genuinely empty value.  Peek ahead to tell them apart.
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
                    continue;
                }

                int indentedEnd = ScanIndentedRun(rawLines, i);
                if (indentedEnd > i)
                {
                    // The first continuation line decides, the way YAML itself
                    // decides: "k: v" makes this a mapping, anything else makes
                    // it a scalar whose value simply starts on the next line.
                    if (IsKeyShaped(rawLines[i + 1].TrimEnd('\r').Trim()))
                    {
                        nodes.Add(new FrontMatterCommentNode(string.Join('\n',
                            rawLines[i..(indentedEnd + 1)].Select(l => l.TrimEnd('\r')))));
                    }
                    else
                    {
                        nodes.Add(FlowScalarField(key, rawLines, i, firstFragment: null, indentedEnd));
                    }

                    i = indentedEnd;
                    continue;
                }

                nodes.Add(new FrontMatterField(key, FrontMatterValue.OfScalar(string.Empty), raw));
                continue;
            }

            if (TryParseBlockHeader(valuePart, out BlockScalarStyle? style, out int explicitIndent))
            {
                // Block scalar: the value lives on the following, more-indented
                // lines. Consume them, decode per the header, and keep the whole
                // span as RawText so an untouched field still round-trips exactly.
                int blockEnd = ScanBlockEnd(rawLines, i, explicitIndent, style!.Chomping, out int indent);

                string[] contentLines = rawLines[(i + 1)..(blockEnd + 1)]
                                        .Select(l => l.TrimEnd('\r'))
                                        .ToArray();

                string decoded = DecodeBlockScalar(contentLines, indent, style!);

                string rawBlock = string.Join('\n',
                    rawLines[i..(blockEnd + 1)].Select(l => l.TrimEnd('\r')));

                nodes.Add(new FrontMatterField(
                    key, FrontMatterValue.OfScalar(decoded), rawBlock, style));
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

            // A quoted scalar may open on the key line and run on over the
            // following indented lines.  Without this the value would be
            // truncated at the first line, keeping a stray opening quote.
            int flowEnd = ScanIndentedRun(rawLines, i);
            if (flowEnd > i)
            {
                nodes.Add(FlowScalarField(key, rawLines, i, valuePart, flowEnd));
                i = flowEnd;
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
                    sb.Append(WithLineEnding(comment.RawText, nl)).Append(nl);
                    break;
                case FrontMatterField field:
                    sb.Append(field.RawText is { } raw
                                  ? WithLineEnding(raw, nl)
                                  : RenderField(field, nl))
                      .Append(nl);
                    break;
            }
        }

        sb.Append(OpenDelimiter).Append(nl);
        sb.Append(frontMatter.Body);
        return sb.ToString();
    }

    /// <summary>
    /// Re-emit preserved source text with the composed file's line ending.
    /// </summary>
    /// <remarks>
    /// <see cref="Parse"/> strips the <c>\r</c> from each line and joins multi-line
    /// <see cref="FrontMatterField.RawText"/> with <c>\n</c>, so a CRLF file whose
    /// block scalar, block list or nested mapping came back verbatim would end up
    /// with LF inside that construct and CRLF everywhere else — mixed line endings
    /// written into a file the user only opened, and not the byte-for-byte round
    /// trip the contract promises.
    /// </remarks>
    private static string WithLineEnding(string rawText, string nl)
    {
        string lf = rawText.Replace("\r\n", "\n", StringComparison.Ordinal);
        return nl == "\n" ? lf : lf.Replace("\n", nl, StringComparison.Ordinal);
    }

    // ── Block scalars ────────────────────────────────────────────────────

    /// <summary>
    /// Recognise a block-scalar header — the text after the colon on a key line.
    /// Accepts <c>&gt;</c> / <c>|</c> with optional chomping (<c>-</c> / <c>+</c>),
    /// an optional explicit indentation digit, and an optional trailing comment.
    /// The two indicators may appear in either order (<c>&gt;2-</c> and <c>&gt;-2</c>
    /// are both legal YAML).
    /// </summary>
    /// <param name="valuePart">The trimmed text following the key's colon.</param>
    /// <param name="style">The decoded header on success; <see langword="null"/> otherwise.</param>
    /// <param name="explicitIndent">
    /// The explicit indentation indicator, or 0 when the block auto-detects its
    /// indentation from the first non-empty content line.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="valuePart"/> is a block-scalar header.</returns>
    private static bool TryParseBlockHeader(string valuePart, out BlockScalarStyle? style, out int explicitIndent)
    {
        style = null;
        explicitIndent = 0;

        if (valuePart.Length == 0 || (valuePart[0] != '>' && valuePart[0] != '|'))
        {
            return false;
        }

        BlockScalarKind kind = valuePart[0] == '>' ? BlockScalarKind.Folded : BlockScalarKind.Literal;
        BlockChomping chomping = BlockChomping.Clip;

        int p = 1;
        for (; p < valuePart.Length; p++)
        {
            char c = valuePart[p];
            if (c == '-')
            {
                chomping = BlockChomping.Strip;
            }
            else if (c == '+')
            {
                chomping = BlockChomping.Keep;
            }
            else if (c is >= '1' and <= '9')
            {
                explicitIndent = c - '0';
            }
            else
            {
                break;
            }
        }

        // Anything left must be whitespace or a comment; otherwise this was not a
        // block header at all (e.g. a plain scalar that merely starts with '>').
        string rest = valuePart[p..].TrimStart();
        if (rest.Length > 0 && rest[0] != '#')
        {
            return false;
        }

        style = new BlockScalarStyle(kind, chomping);
        return true;
    }

    /// <summary>
    /// Find the last line belonging to the block that opens at
    /// <paramref name="headerIndex"/>, and report the block's content indentation.
    /// The block continues over blank lines and any line indented at least as far
    /// as the first non-empty content line; it ends at the first line indented
    /// less than that (the next key, or the closing delimiter).
    /// </summary>
    /// <remarks>
    /// ⛔ <b>The chomping indicator is load-bearing here, and taking it into account is what
    /// fixes a silent data loss.</b> Under clip and strip a trailing blank line is not content,
    /// so refusing to extend the block over one is right. Under <c>+</c> those blank lines ARE
    /// the value — that is the entire meaning of the indicator — so dropping them means a file
    /// that round-trips through the editor comes back with its trailing newlines gone, with
    /// nothing anywhere reporting it.
    /// </remarks>
    private static int ScanBlockEnd(
        string[] rawLines, int headerIndex, int explicitIndent, BlockChomping chomping, out int indent)
    {
        int headerIndent = IndentOf(rawLines[headerIndex].TrimEnd('\r'));
        indent = explicitIndent > 0 ? headerIndent + explicitIndent : -1;

        int end = headerIndex;
        for (int j = headerIndex + 1; j < rawLines.Length; j++)
        {
            string line = rawLines[j].TrimEnd('\r');

            if (line.Trim().Length == 0)
            {
                // A blank line may be interior to the block; only a later
                // content line proves it was — unless the header said `+`, where
                // a trailing blank line is exactly what the author asked to keep.
                if (chomping == BlockChomping.Keep)
                {
                    end = j;
                }

                continue;
            }

            int lineIndent = IndentOf(line);

            // The first non-empty line fixes the block's indentation when the
            // header carried no explicit indicator.
            if (indent < 0)
            {
                if (lineIndent <= headerIndent)
                {
                    break;      // nothing is indented under the header — empty block
                }

                indent = lineIndent;
            }

            if (lineIndent < indent)
            {
                break;
            }

            end = j;
        }

        if (indent < 0)
        {
            indent = headerIndent + 2;
        }

        return end;
    }

    /// <summary>
    /// Turn a block scalar's raw content lines into its string value, applying
    /// folding (for <c>&gt;</c>) and the chomping indicator.
    /// </summary>
    private static string DecodeBlockScalar(string[] contentLines, int indent, BlockScalarStyle style)
    {
        // Strip the block indentation. Lines indented FURTHER keep the extra
        // indentation — in a folded block that also makes them literal.
        var stripped = contentLines
                       .Select(l => l.Trim().Length == 0 ? string.Empty : StripIndent(l, indent))
                       .ToList();

        string joined = style.Kind == BlockScalarKind.Literal
            ? string.Join('\n', stripped)
            : Fold(stripped);

        return Chomp(joined, style.Chomping);
    }

    /// <summary>
    /// Fold a folded-block's lines: equally-indented lines join with a space, a
    /// blank line becomes a newline, and a more-indented line stays on its own
    /// line (YAML keeps such lines literal).
    /// </summary>
    private static string Fold(List<string> lines)
    {
        var sb = new StringBuilder();
        bool atLineStart = true;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];

            if (line.Length == 0)
            {
                // Blank line → a hard break. Consecutive blanks each add one.
                sb.Append('\n');
                atLineStart = true;
                continue;
            }

            bool moreIndented = char.IsWhiteSpace(line[0]);

            if (!atLineStart)
            {
                sb.Append(moreIndented || EndsMoreIndented(lines, i - 1) ? '\n' : ' ');
            }

            sb.Append(line);
            atLineStart = false;
        }

        return sb.ToString();
    }

    private static bool EndsMoreIndented(List<string> lines, int index)
    {
        return index >= 0 && lines[index].Length > 0 && char.IsWhiteSpace(lines[index][0]);
    }

    private static string Chomp(string value, BlockChomping chomping)
    {
        return chomping switch
        {
            BlockChomping.Strip => value.TrimEnd('\n'),
            BlockChomping.Keep => value.Length == 0 ? value : value + "\n",
            _ => value.TrimEnd('\n') + (value.Length == 0 ? string.Empty : "\n"),
        };
    }

    private static int IndentOf(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ')
        {
            n++;
        }

        return n;
    }

    private static string StripIndent(string line, int indent)
    {
        int take = 0;
        while (take < indent && take < line.Length && line[take] == ' ')
        {
            take++;
        }

        return line[take..];
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

        string scalar = field.Value.Scalar ?? string.Empty;

        // A field that arrived as a block scalar goes back as one, in the same
        // shape — otherwise a GUI edit of a folded `description` would rewrite it
        // as a single enormous line and churn the file. A value that contains a
        // newline CANNOT be a plain scalar at all, so it gets a literal block
        // even if it was never one on disk.
        // ⛔ The synthesised style used to be Literal+Strip unconditionally, which DISCARDS every
        // trailing newline the value carries — a value edited to end in a blank line came back
        // without it, silently. The indicator has to be chosen from the content: strip when there
        // is no trailing newline, clip for exactly one (clip's whole meaning), keep for more.
        BlockScalarStyle? style = field.Block
                                  ?? (scalar.Contains('\n')
                                      ? new BlockScalarStyle(BlockScalarKind.Literal, ChompingFor(scalar))
                                      : null);

        if (style is not null && scalar.Length > 0)
        {
            return RenderBlockScalar(field.Key, scalar, style, nl);
        }

        return $"{field.Key}: {QuoteIfNeeded(scalar)}";
    }

    /// <summary>Indentation used for re-rendered block-scalar content.</summary>
    private const string BlockIndent = "  ";

    /// <summary>
    /// Width at which re-folded block content wraps.  Chosen to match the skill
    /// files Claude Code itself ships, so an edit through the GUI produces a diff
    /// of only the changed prose rather than a re-flow of the whole block.
    /// </summary>
    private const int FoldWidth = 96;

    /// <summary>
    /// The chomping indicator a value's own trailing newlines require, so that composing and
    /// re-parsing returns what went in.
    /// </summary>
    private static BlockChomping ChompingFor(string scalar)
    {
        int trailing = 0;
        while (trailing < scalar.Length && scalar[^(trailing + 1)] == '\n')
        {
            trailing++;
        }

        return trailing switch
        {
            0 => BlockChomping.Strip,
            1 => BlockChomping.Clip,
            _ => BlockChomping.Keep,
        };
    }

    private static string RenderBlockScalar(string key, string scalar, BlockScalarStyle style, string nl)
    {
        // ⚠ Trailing newlines are NOT content lines. A block's last content line always carries
        // one line break implicitly, so `keep` needs only the newlines BEYOND that first one
        // written out as blank lines — emitting one per newline renders three where the value
        // had two, and the round-trip grows the file every time it is saved.
        int trailing = 0;
        while (trailing < scalar.Length && scalar[^(trailing + 1)] == '\n')
        {
            trailing++;
        }

        string content = scalar[..(scalar.Length - trailing)];

        // A literal block must keep the author's own line breaks; a folded block
        // is re-wrapped, because folding makes the source line breaks invisible
        // in the value anyway.
        List<string> lines = content.Length == 0
            ? []
            : style.Kind == BlockScalarKind.Literal
                ? [.. content.Split('\n')]
                : [.. content.Split('\n').SelectMany(paragraph => WrapToWidth(paragraph, FoldWidth))];

        if (style.Chomping == BlockChomping.Keep)
        {
            for (int k = content.Length == 0 ? 0 : 1; k < trailing; k++)
            {
                lines.Add(string.Empty);
            }
        }

        // ⛔ Auto-detection reads a leading space as part of the block's OWN indentation and eats
        // it, so a value whose first content line starts with one is unrepresentable without
        // stating the indent. `|2-` says "content begins two columns in", which is what the
        // renderer indents by.
        string? firstContent = lines.Find(l => l.Length != 0);
        bool needsExplicitIndent = firstContent is not null && firstContent[0] == ' ';

        var sb = new StringBuilder();
        sb.Append(key).Append(": ").Append(style.Kind == BlockScalarKind.Literal ? '|' : '>');
        if (needsExplicitIndent)
        {
            sb.Append(BlockIndent.Length);
        }

        sb.Append(style.Chomping switch
        {
            BlockChomping.Strip => "-",
            BlockChomping.Keep => "+",
            _ => string.Empty,
        });

        foreach (string line in lines)
        {
            sb.Append(nl);
            if (line.Length > 0)
            {
                sb.Append(BlockIndent).Append(line);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Greedy word wrap.  A single word longer than <paramref name="width"/> is
    /// emitted on its own over-long line rather than being broken — splitting a
    /// URL or an identifier would change the folded value.
    /// </summary>
    private static IEnumerable<string> WrapToWidth(string paragraph, int width)
    {
        if (paragraph.Trim().Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var current = new StringBuilder();
        foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    // ── Flow scalars and nested mappings ─────────────────────────────────

    /// <summary>
    /// Find the last line of the more-indented run that follows
    /// <paramref name="headerIndex"/> — the lines belonging to a nested mapping
    /// or continuing a multi-line flow scalar.
    /// </summary>
    /// <remarks>
    /// A blank line ends the run.  Inside a mapping that is merely spacing, and
    /// inside a flow scalar it is a paragraph break; stopping at it keeps this
    /// conservative, and the remaining lines still round-trip verbatim through
    /// the indented-key backstop in <see cref="Parse"/>.
    /// </remarks>
    /// <param name="rawLines">All lines of the source text.</param>
    /// <param name="headerIndex">Index of the key line the run follows.</param>
    /// <returns>
    /// <paramref name="headerIndex"/> itself when no indented run follows.
    /// </returns>
    private static int ScanIndentedRun(string[] rawLines, int headerIndex)
    {
        int headerIndent = IndentOf(rawLines[headerIndex].TrimEnd('\r'));
        int end = headerIndex;

        for (int j = headerIndex + 1; j < rawLines.Length; j++)
        {
            string line = rawLines[j].TrimEnd('\r');
            string trimmed = line.Trim();

            if (trimmed.Length == 0
                || trimmed is OpenDelimiter or "..."
                || IndentOf(line) <= headerIndent)
            {
                break;
            }

            end = j;
        }

        return end;
    }

    /// <summary>
    /// Does this line read as <c>key: value</c> (or a bare <c>key:</c>)?  Used
    /// on the first continuation line to tell a nested mapping from prose that
    /// merely happens to be indented.  A key never contains a space or a quote,
    /// and its colon is always followed by whitespace or the end of the line —
    /// so "Solve competition math problems (IMO, Putnam)" is not key-shaped, and
    /// neither is a sentence containing "for example: this".
    /// </summary>
    private static bool IsKeyShaped(string trimmed)
    {
        if (trimmed.Length == 0 || trimmed[0] is '"' or '\'' or '-' or '#')
        {
            return false;
        }

        int colon = trimmed.IndexOf(':');
        if (colon <= 0 || trimmed.AsSpan(0, colon).ContainsAny(' ', '"', '\''))
        {
            return false;
        }

        return colon == trimmed.Length - 1 || char.IsWhiteSpace(trimmed[colon + 1]);
    }

    /// <summary>
    /// Build the field for a multi-line flow scalar — a value written across
    /// several indented lines with no <c>&gt;</c> / <c>|</c> indicator, either
    /// opening on the key line or starting on the line below it.
    /// </summary>
    /// <param name="key">The field's key.</param>
    /// <param name="rawLines">All lines of the source text.</param>
    /// <param name="headerIndex">Index of the key line.</param>
    /// <param name="firstFragment">
    /// The text after the colon on the key line, or <see langword="null"/> when
    /// the value starts on the following line.
    /// </param>
    /// <param name="end">Index of the run's last line, from <see cref="ScanIndentedRun"/>.</param>
    /// <remarks>
    /// The lines join with single spaces, which is how YAML folds them, and the
    /// surrounding quotes come off the joined result rather than any one line —
    /// the opening and closing quotes sit on different lines.
    /// <para>
    /// The field is tagged folded/strip so that editing it re-renders as a
    /// <c>&gt;-</c> block.  It arrived spread over several lines; collapsing it
    /// into one very long plain line on first edit is exactly the churn
    /// <see cref="BlockScalarStyle"/> exists to prevent.
    /// </para>
    /// </remarks>
    private static FrontMatterField FlowScalarField(
        string key, string[] rawLines, int headerIndex, string? firstFragment, int end)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(firstFragment))
        {
            parts.Add(firstFragment);
        }

        for (int j = headerIndex + 1; j <= end; j++)
        {
            string piece = rawLines[j].TrimEnd('\r').Trim();
            if (piece.Length > 0)
            {
                parts.Add(piece);
            }
        }

        string rawBlock = string.Join('\n',
            rawLines[headerIndex..(end + 1)].Select(l => l.TrimEnd('\r')));

        return new FrontMatterField(
            key,
            FrontMatterValue.OfScalar(StripQuotes(string.Join(' ', parts))),
            rawBlock,
            new BlockScalarStyle(BlockScalarKind.Folded, BlockChomping.Strip));
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