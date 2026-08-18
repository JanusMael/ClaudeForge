using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// Applies path-level changes to JSONC text by replacing the minimal span for each
/// change, so everything else — comments, blank lines, key order, indentation,
/// line endings — is preserved because it is never rewritten.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refuses to edit a document it could not fully parse.</b> If
/// <see cref="JsoncDocument.IsEditable"/> is <see langword="false"/>, every entry point
/// throws <see cref="InvalidOperationException"/>. Editing text we misunderstood is how
/// config files get corrupted, and a caller that catches this can fall back to a
/// whole-document rewrite as a deliberate choice rather than by accident.
/// </para>
/// <para>
/// Path semantics match the SDK's existing traversal exactly: split on <c>'.'</c>,
/// objects only, missing intermediate objects created on set. See
/// <see cref="JsoncDocument.FindMember"/>.
/// </para>
/// </remarks>
public static class JsoncEditor
{
    /// <summary>
    /// Return <paramref name="text"/> with <paramref name="path"/> set to
    /// <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// Replacing an existing member rewrites only its value span, so its key, its
    /// position among siblings, and any comment attached to it all survive. Adding a new
    /// member appends it to the innermost existing object on the path, indented to match
    /// that object's own members.
    /// </remarks>
    public static string SetValue(string text, string path, JsonNode? value)
    {
        JsoncDocument document = JsoncDocument.Parse(text);
        return TextEdit.Apply(text, SetValueEdits(document, path, value));
    }

    /// <summary>
    /// Return <paramref name="text"/> with <paramref name="path"/> removed. A path that
    /// is already absent produces no edits and the original text back.
    /// </summary>
    public static string Remove(string text, string path)
    {
        JsoncDocument document = JsoncDocument.Parse(text);
        return TextEdit.Apply(text, RemoveEdits(document, path));
    }

    /// <summary>
    /// The edits that would set <paramref name="path"/>, without applying them. Exposed
    /// so a caller batching several changes can collect edits and apply once — applying
    /// one at a time would invalidate the spans of the rest.
    /// </summary>
    public static IReadOnlyList<TextEdit> SetValueEdits(JsoncDocument document, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ThrowIfNotEditable(document);

        // Existing member: swap its value span and nothing else.
        if (document.FindMember(path) is { } existing)
        {
            string replacement = Render(value, document.Style, IndentDepthOf(document, existing.Value.Start));
            return [new TextEdit(existing.Value.Start, existing.Value.End - existing.Value.Start, replacement)];
        }

        return [document.Root is null
                    ? CreateRootEdit(document, path, value)
                    : InsertEdit(document, path, value)];
    }

    /// <summary>
    /// The edits that would remove <paramref name="path"/>, without applying them.
    /// Empty when the path is absent.
    /// </summary>
    public static IReadOnlyList<TextEdit> RemoveEdits(JsoncDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(path);
        ThrowIfNotEditable(document);

        if (document.FindMember(path) is not { } member)
        {
            return [];
        }

        JsoncValue owner = OwnerOf(document, path)
                           ?? throw new InvalidOperationException(
                               $"Found member '{path}' but not its containing object.");

        int start = RemovalStart(document, owner, member);
        int end = RemovalEnd(document, owner, member);
        return [new TextEdit(start, end - start, string.Empty)];
    }

    private static void ThrowIfNotEditable(JsoncDocument document)
    {
        if (document.IsEditable)
        {
            return;
        }

        throw new InvalidOperationException(
            "Refusing to edit a JSONC document that did not parse cleanly, because the "
            + "edit would be placed against a structure we misread. Parse errors: "
            + string.Join("; ", document.Errors));
    }

    // ── Removal extents ──────────────────────────────────────────────────────
    //
    // Removing "b" from { "a": 1, "b": 2 } has to take a comma with it, and which
    // comma depends on position. Taking the preceding one when the member is last
    // keeps the result valid; taking the following one otherwise keeps the remaining
    // members' own formatting intact.

    private static int RemovalStart(JsoncDocument document, JsoncValue owner, JsoncMember member)
    {
        int index = IndexOfMember(owner, member);
        bool isLast = index == owner.Members.Count - 1;

        if (!isLast)
        {
            // Take the whitespace that precedes the member so the surviving members keep
            // their own leading indentation rather than inheriting this one's.
            return BackUpOverInlineWhitespaceAndNewline(document.Text, member.Start);
        }

        // Last member: absorb the comma that precedes it, if any, or its leading blank run.
        int scan = member.Start - 1;
        while (scan >= 0 && IsInlineOrNewline(document.Text[scan]))
        {
            scan--;
        }

        if (scan >= 0 && document.Text[scan] == ',')
        {
            return scan;
        }

        return BackUpOverInlineWhitespaceAndNewline(document.Text, member.Start);
    }

    private static int RemovalEnd(JsoncDocument document, JsoncValue owner, JsoncMember member)
    {
        int index = IndexOfMember(owner, member);
        bool isLast = index == owner.Members.Count - 1;

        if (isLast)
        {
            return member.End;
        }

        // Consume the separating comma so the next member does not start with one.
        int scan = member.End;
        while (scan < document.Text.Length && IsInlineOrNewline(document.Text[scan]))
        {
            scan++;
        }

        return scan < document.Text.Length && document.Text[scan] == ',' ? scan + 1 : member.End;
    }

    private static int BackUpOverInlineWhitespaceAndNewline(string text, int from)
    {
        int scan = from;
        while (scan > 0 && (text[scan - 1] == ' ' || text[scan - 1] == '\t'))
        {
            scan--;
        }

        // Take one line break with it so removing a member does not leave a blank line.
        if (scan > 0 && text[scan - 1] == '\n')
        {
            scan--;
            if (scan > 0 && text[scan - 1] == '\r')
            {
                scan--;
            }
        }

        return scan;
    }

    private static int IndexOfMember(JsoncValue owner, JsoncMember member)
    {
        for (int i = 0; i < owner.Members.Count; i++)
        {
            if (ReferenceEquals(owner.Members[i], member))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsInlineOrNewline(char c) => c is ' ' or '\t' or '\r' or '\n';

    /// <summary>
    /// The object that directly contains the member at <paramref name="path"/>, or
    /// <see langword="null"/> when the path does not resolve.
    /// </summary>
    private static JsoncValue? OwnerOf(JsoncDocument document, string path)
    {
        if (document.Root is not { Kind: JsoncValueKind.Object } owner)
        {
            return null;
        }

        string[] segments = path.Split('.');
        for (int i = 0; i < segments.Length - 1; i++)
        {
            JsoncMember? next = owner.FindMember(segments[i]);
            if (next is null || next.Value.Kind != JsoncValueKind.Object)
            {
                return null;
            }

            owner = next.Value;
        }

        return owner;
    }

    // ── Insertion ────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the edit that creates a root object in a document that has none — a new or
    /// empty config file, or one holding only comments.
    /// </summary>
    /// <remarks>
    /// Appends after whatever is already there rather than replacing the whole text, so a
    /// file that is nothing but a licence header or a note to self keeps it.
    /// </remarks>
    private static TextEdit CreateRootEdit(JsoncDocument document, string path, JsonNode? value)
    {
        string[] segments = path.Split('.');

        JsonNode? payload = value;
        for (int i = segments.Length - 1; i > 0; i--)
        {
            payload = new JsonObject { [segments[i]] = payload?.DeepClone() };
        }

        JsonObject root = new() { [segments[0]] = payload };

        StringBuilder sb = new();
        bool hasContent = document.Text.AsSpan().Trim().Length > 0;
        if (hasContent && !EndsWithNewLine(document.Text))
        {
            sb.Append(document.Style.NewLine);
        }

        sb.Append(Render(root, document.Style, depth: 0));
        sb.Append(document.Style.NewLine);

        return new TextEdit(document.Text.Length, 0, sb.ToString());
    }

    private static bool EndsWithNewLine(string text) =>
        text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r');

    /// <summary>
    /// Build the edit that adds a missing path. Walks as far down the existing structure
    /// as it can, then synthesizes the remaining nesting as part of the inserted text.
    /// </summary>
    private static TextEdit InsertEdit(JsoncDocument document, string path, JsonNode? value)
    {
        string[] segments = path.Split('.');

        if (document.Root is not { Kind: JsoncValueKind.Object } container)
        {
            throw new InvalidOperationException(
                $"Cannot insert into a root of kind {document.Root!.Kind}; an object is required.");
        }

        int consumed = 0;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            JsoncMember? next = container.FindMember(segments[i]);
            if (next is null || next.Value.Kind != JsoncValueKind.Object)
            {
                break;
            }

            container = next.Value;
            consumed = i + 1;
        }

        // Nest whatever is left of the path inside the value.
        JsonNode? payload = value;
        for (int i = segments.Length - 1; i > consumed; i--)
        {
            payload = new JsonObject { [segments[i]] = payload?.DeepClone() };
        }

        int depth = IndentDepthOf(document, container.Start) + 1;
        string indent = Repeat(document.Style.IndentUnit, depth);
        string rendered = Render(payload, document.Style, depth);
        string member = $"{Quote(segments[consumed])}: {rendered}";

        bool empty = container.Members.Count == 0;
        int insertAt = empty
            ? container.Start + 1                        // just after '{'
            : container.Members[^1].End;                 // after the current last member

        StringBuilder sb = new();
        if (!empty)
        {
            sb.Append(',');
        }

        sb.Append(document.Style.NewLine).Append(indent).Append(member);

        if (empty)
        {
            // An empty object is written on one line ("{}"), so closing it needs its own
            // line at the container's own depth.
            sb.Append(document.Style.NewLine)
              .Append(Repeat(document.Style.IndentUnit, depth - 1));
        }

        return new TextEdit(insertAt, 0, sb.ToString());
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialize <paramref name="value"/> and re-indent it to sit at
    /// <paramref name="depth"/> levels using the document's own indent unit and newline.
    /// </summary>
    /// <remarks>
    /// <c>System.Text.Json</c> always writes two-space indentation and <c>\n</c>, so its
    /// output is normalized here rather than trusted. Without this step a tab-indented,
    /// CRLF document would grow space-indented LF islands wherever a value was inserted.
    /// </remarks>
    private static string Render(JsonNode? value, JsoncStyle style, int depth)
    {
        if (value is null)
        {
            return "null";
        }

        string json = value.ToJsonString(RenderOptions);

        // Scalars and empty containers are single-line; nothing to re-indent.
        if (!json.Contains('\n'))
        {
            return json;
        }

        string[] lines = json.Replace("\r\n", "\n").Split('\n');
        StringBuilder sb = new();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int spaces = 0;
            while (spaces < line.Length && line[spaces] == ' ')
            {
                spaces++;
            }

            if (i > 0)
            {
                sb.Append(style.NewLine);

                // STJ emits exactly two spaces per level; convert that count into levels
                // and re-emit with the document's unit, offset to the insertion depth.
                int levels = (spaces / 2) + depth;
                sb.Append(Repeat(style.IndentUnit, levels));
            }

            sb.Append(line, spaces, line.Length - spaces);
        }

        return sb.ToString();
    }

    private static readonly JsonSerializerOptions RenderOptions = new() { WriteIndented = true };

    /// <summary>
    /// How many indent levels deep the line containing <paramref name="offset"/> is,
    /// measured against the document's own indent unit.
    /// </summary>
    private static int IndentDepthOf(JsoncDocument document, int offset)
    {
        string text = document.Text;
        int lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        int p = lineStart;
        while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
        {
            p++;
        }

        string unit = document.Style.IndentUnit;
        if (unit.Length == 0)
        {
            return 0;
        }

        return (p - lineStart) / unit.Length;
    }

    private static string Repeat(string unit, int count) =>
        count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(unit, count));

    /// <summary>Quote and escape a member name using the same rules as the serializer.</summary>
    /// <remarks>
    /// ⚠ <b>Not <c>JsonSerializer.Serialize(name)</c>, and not for style.</b> That overload is
    /// the reflection-based one, so it carries <c>RequiresUnreferencedCode</c> and fails the
    /// Release publish with <c>IL2026</c> → <c>NETSDK1144</c> under
    /// <c>PublishTrimmed=true</c>. A Debug build cannot see it; the CI trim check is what
    /// catches it.
    /// <para>
    /// <see cref="Utf8JsonWriter.WriteStringValue(string?)"/> is the same code path the
    /// serializer itself uses for a string, minus the reflection — so the escaping is
    /// identical by construction rather than by resemblance. Pinned against
    /// <c>JsonSerializer.Serialize</c> as an oracle in <c>JsoncEditorQuoteTests</c>, which is
    /// free to use the reflection overload because test assemblies are not trimmed.
    /// </para>
    /// </remarks>
    internal static string Quote(string name)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStringValue(name);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
