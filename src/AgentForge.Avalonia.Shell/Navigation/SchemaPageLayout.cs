using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;

/// <summary>
/// One page's worth of schema properties, in display order.
/// </summary>
/// <param name="Title">Page title, also the navigation node's label.</param>
/// <param name="Description">
/// One-line "what is on this page" text, or empty when the layout declares none.
/// </param>
/// <param name="Nodes">
/// The properties filed under this page, in the order they arrived from the schema.
/// </param>
public sealed record SchemaPage(
    string Title,
    string Description,
    IReadOnlyList<SchemaNode> Nodes);

/// <summary>
/// A product's statement about how its flat schema is split into editor pages, and
/// the neutral arrangement that applies it.
///
/// <para>
/// Every layered-config product faces the same problem — a schema is a flat bag of
/// properties, a usable editor is a small ordered set of themed pages — and solves
/// it the same way: a property-to-page map, a declared page order, and a bucket for
/// everything unclaimed. Only the words differ, and the words are the product's.
/// </para>
/// <para>
/// Note what deliberately stays outside: this produces <em>groupings</em>, not view
/// models. Which editor type hosts a page, which factory builds its property
/// editors, and which tab customiser it gets are all the host's business.
/// </para>
/// </summary>
public sealed class SchemaPageLayout
{
    /// <summary>
    /// Schema property name (<see cref="SchemaNode.Name"/>) to the page it belongs on.
    /// A property absent from this map lands on <see cref="FallbackPage"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, string> PropertyToPage { get; init; }

    /// <summary>
    /// Pages in display order. A page listed here but with no properties is skipped;
    /// a page with properties but not listed here is appended after these, sorted by
    /// title — so a schema gaining a property nobody has filed yet still shows up
    /// rather than vanishing.
    /// </summary>
    public required IReadOnlyList<string> PageOrder { get; init; }

    /// <summary>
    /// Where an unmapped property goes. Usually the product's catch-all page, and it
    /// usually appears in <see cref="PageOrder"/> too so it keeps its declared
    /// position rather than being appended.
    /// </summary>
    public required string FallbackPage { get; init; }

    /// <summary>
    /// Optional one-line descriptions keyed by page title. A page with no entry gets
    /// an empty description.
    /// </summary>
    public IReadOnlyDictionary<string, string> PageDescriptions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Bucket <paramref name="nodes"/> into pages and return them in display order.
    /// </summary>
    public IReadOnlyList<SchemaPage> Arrange(IReadOnlyList<SchemaNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        Dictionary<string, List<SchemaNode>> buckets = new(StringComparer.Ordinal);
        foreach (SchemaNode node in nodes)
        {
            string page = PropertyToPage.TryGetValue(node.Name, out string? p) ? p : FallbackPage;
            if (!buckets.TryGetValue(page, out List<SchemaNode>? list))
            {
                list = [];
                buckets[page] = list;
            }

            list.Add(node);
        }

        List<SchemaPage> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string title in PageOrder)
        {
            if (!buckets.TryGetValue(title, out List<SchemaNode>? pageNodes))
            {
                continue;
            }

            seen.Add(title);
            result.Add(Build(title, pageNodes));
        }

        foreach ((string title, List<SchemaNode> pageNodes) in buckets.OrderBy(kv => kv.Key))
        {
            if (seen.Contains(title))
            {
                continue;
            }

            result.Add(Build(title, pageNodes));
        }

        return result;
    }

    private SchemaPage Build(string title, IReadOnlyList<SchemaNode> nodes)
    {
        string description = PageDescriptions.TryGetValue(title, out string? d) ? d : string.Empty;
        return new SchemaPage(title, description, nodes);
    }
}
