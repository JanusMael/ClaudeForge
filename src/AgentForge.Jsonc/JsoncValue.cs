namespace Bennewitz.Ninja.AgentForge.Jsonc;

/// <summary>
/// A parsed JSONC value, located by span in the source text.
/// </summary>
/// <remarks>
/// Deliberately not a data-carrying tree: it stores <i>where</i> each value is, not
/// what it contains. Callers that want values read them from the source with
/// <see cref="Text"/>, or go through <c>System.Text.Json</c> separately. Keeping this
/// span-only is what lets the writer replace one value without knowing how to
/// re-serialize its siblings.
/// </remarks>
public sealed class JsoncValue
{
    internal JsoncValue(JsoncValueKind kind, int start, int end)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    /// <summary>Structural category.</summary>
    public JsoncValueKind Kind { get; }

    /// <summary>Index of the value's first character.</summary>
    public int Start { get; }

    /// <summary>Index one past the value's last character.</summary>
    public int End { get; internal set; }

    /// <summary>
    /// Members, when <see cref="Kind"/> is <see cref="JsoncValueKind.Object"/>.
    /// Source order is preserved — key order is user-visible formatting and this
    /// library never reorders it.
    /// </summary>
    public IReadOnlyList<JsoncMember> Members => _members;

    /// <summary>Items, when <see cref="Kind"/> is <see cref="JsoncValueKind.Array"/>.</summary>
    public IReadOnlyList<JsoncValue> Items => _items;

    private readonly List<JsoncMember> _members = [];
    private readonly List<JsoncValue> _items = [];

    internal void AddMember(JsoncMember member) => _members.Add(member);

    internal void AddItem(JsoncValue item) => _items.Add(item);

    /// <summary>This value's text, sliced out of <paramref name="source"/>.</summary>
    public ReadOnlySpan<char> Text(string source) => source.AsSpan(Start, End - Start);

    /// <summary>
    /// The member named <paramref name="name"/>, or <see langword="null"/>. When a
    /// malformed document repeats a key, the <b>last</b> occurrence wins, matching
    /// <c>System.Text.Json</c>'s object-model behaviour so the writer edits whichever
    /// copy a reader would have seen.
    /// </summary>
    public JsoncMember? FindMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        for (int i = _members.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_members[i].Name, name, StringComparison.Ordinal))
            {
                return _members[i];
            }
        }

        return null;
    }
}
