namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// The parsed representation of a file's YAML front-matter block plus its
/// markdown body.  Produced by <see cref="YamlFrontMatter.Parse"/> and
/// rendered back by <see cref="YamlFrontMatter.Compose"/>.
///
/// <para>
/// <see cref="Nodes"/> preserves the front-matter's contents in source
/// order — fields, comments, and blank lines — so a Parse → Compose
/// round-trip keeps comments and key ordering intact.  Unknown / un-modelled
/// keys survive as ordinary <see cref="FrontMatterField"/> nodes carrying
/// their verbatim <see cref="FrontMatterField.RawText"/>.
/// </para>
/// </summary>
/// <param name="Present">
/// <see langword="true"/> when a well-formed <c>---</c>…<c>---</c> block was
/// found at the top of the file.  <see langword="false"/> for files with no
/// front-matter (plain <c>CLAUDE.md</c>-style content) or a malformed /
/// unterminated block — in those cases <see cref="Body"/> holds the entire
/// original text and <see cref="Nodes"/> is empty.
/// </param>
/// <param name="Nodes">Front-matter contents in source order.</param>
/// <param name="Body">Everything after the closing delimiter (or the whole
/// file when <paramref name="Present"/> is <see langword="false"/>).</param>
public sealed record FrontMatter(
    bool Present,
    IReadOnlyList<FrontMatterNode> Nodes,
    string Body)
{
    /// <summary>A front-matter-less result wrapping the whole text as body.</summary>
    public static FrontMatter None(string body)
    {
        return new FrontMatter(Present: false, Nodes: [], Body: body);
    }

    /// <summary>The field nodes only, in source order (skips comments/blanks).</summary>
    public IEnumerable<FrontMatterField> Fields => Nodes.OfType<FrontMatterField>();

    /// <summary>
    /// Find a field by key (case-insensitive — YAML keys are conventionally
    /// lower-case but we match tolerantly).  Returns <see langword="null"/>
    /// when absent.
    /// </summary>
    public FrontMatterValue? Find(string key)
    {
        return Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>Scalar value for <paramref name="key"/>, or null if absent / list-valued.</summary>
    public string? FindScalar(string key)
    {
        FrontMatterValue? v = Find(key);
        return v is { IsList: false } ? v.Scalar : null;
    }

    /// <summary>List value for <paramref name="key"/>, or null if absent / scalar-valued.</summary>
    public IReadOnlyList<string>? FindList(string key)
    {
        FrontMatterValue? v = Find(key);
        return v is { IsList: true } ? v.List : null;
    }

    /// <summary>
    /// Return a copy with <paramref name="key"/> set to a scalar value.
    /// Replaces an existing field in place (preserving its position) or
    /// appends a new one immediately after the last existing field.  The
    /// touched field's <see cref="FrontMatterField.RawText"/> is cleared so
    /// <see cref="YamlFrontMatter.Compose"/> re-renders just that line.
    /// </summary>
    public FrontMatter WithScalar(string key, string value)
    {
        return WithField(key, FrontMatterValue.OfScalar(value));
    }

    /// <summary>List-valued counterpart of <see cref="WithScalar"/>.</summary>
    public FrontMatter WithList(string key, IReadOnlyList<string> items)
    {
        return WithField(key, FrontMatterValue.OfList(items));
    }

    /// <summary>
    /// Return a copy with <paramref name="key"/> removed (all matching field
    /// nodes dropped).  No-op when the key is absent.
    /// </summary>
    public FrontMatter Without(string key)
    {
        var kept = Nodes
                   .Where(n => n is not FrontMatterField f
                               || !string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase))
                   .ToList();
        return this with { Nodes = kept };
    }

    private FrontMatter WithField(string key, FrontMatterValue value)
    {
        var updated = new List<FrontMatterNode>(Nodes);

        int existing = updated.FindIndex(n => n is FrontMatterField f
                                              && string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        // RawText cleared → Compose re-renders this field canonically.
        var field = new FrontMatterField(key, value, RawText: null);

        if (existing >= 0)
        {
            updated[existing] = field;
        }
        else
        {
            int lastField = updated.FindLastIndex(n => n is FrontMatterField);
            updated.Insert(lastField + 1, field);
        }

        // If this was a front-matter-less doc, adding a field promotes it to
        // present so Compose emits a real block.
        return this with { Present = true, Nodes = updated };
    }
}

/// <summary>One entry inside a front-matter block: a field, a comment, or a blank line.</summary>
public abstract record FrontMatterNode;

/// <summary>A key/value field (scalar or list).</summary>
/// <param name="Key">The field name (left of the colon), trimmed.</param>
/// <param name="Value">The parsed scalar or list value.</param>
/// <param name="RawText">
/// The verbatim original source text for this field (including any block-list
/// continuation lines), preserved so an untouched field round-trips byte-for-
/// byte.  <see langword="null"/> for fields synthesised or edited in memory —
/// <see cref="YamlFrontMatter.Compose"/> re-renders those canonically.
/// </param>
public sealed record FrontMatterField(string Key, FrontMatterValue Value, string? RawText) : FrontMatterNode;

/// <summary>A comment line (begins with <c>#</c>), preserved verbatim including the marker.</summary>
public sealed record FrontMatterCommentNode(string RawText) : FrontMatterNode;

/// <summary>A blank line inside the front-matter block.</summary>
public sealed record FrontMatterBlankNode : FrontMatterNode;

/// <summary>
/// A front-matter field value: either a scalar string or a list of strings.
/// Exactly one of <see cref="Scalar"/> / <see cref="List"/> is non-null.
/// </summary>
public sealed class FrontMatterValue : IEquatable<FrontMatterValue>
{
    private FrontMatterValue(string? scalar, IReadOnlyList<string>? list)
    {
        Scalar = scalar;
        List = list;
    }

    /// <summary>The scalar text, or null when this value is a list.</summary>
    public string? Scalar { get; }

    /// <summary>The list items, or null when this value is a scalar.</summary>
    public IReadOnlyList<string>? List { get; }

    /// <summary><see langword="true"/> when this value is a list.</summary>
    public bool IsList => List is not null;

    /// <summary>Create a scalar value.</summary>
    public static FrontMatterValue OfScalar(string value)
    {
        return new FrontMatterValue(value, null);
    }

    /// <summary>Create a list value (a defensive copy is taken).</summary>
    public static FrontMatterValue OfList(IReadOnlyList<string> items)
    {
        return new FrontMatterValue(null, items.ToList());
    }

    public bool Equals(FrontMatterValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (IsList != other.IsList)
        {
            return false;
        }

        return IsList
            ? List!.SequenceEqual(other.List!, StringComparer.Ordinal)
            : string.Equals(Scalar, other.Scalar, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as FrontMatterValue);
    }

    public override int GetHashCode()
    {
        if (IsList)
        {
            var hash = new HashCode();
            foreach (string item in List!)
            {
                hash.Add(item, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        return Scalar is null ? 0 : StringComparer.Ordinal.GetHashCode(Scalar);
    }
}