namespace Bennewitz.Ninja.AgentForge.Artifacts;

/// <summary>
/// Groups the declarations an ordered source list produces into one chain per artifact name.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>This type ORDERS AND GROUPS. It does not merge, and it does not decide what "the" agent
/// named <c>build</c> is.</b> That restraint is the design: Spike S7 measured that OpenCode's inline
/// JSON and markdown file of the same name <b>deep-merge with the file winning per field</b>, so an
/// inline-only <c>temperature</c> is still live. A resolver that returned one winner and a list of
/// discards would be structurally incapable of expressing that, and any UI built on it would say
/// "the file shadows the inline definition" — which is false. So the chain comes out complete and
/// ordered, and folding it into one artifact is per-kind policy in the consumer.
/// </para>
/// <para>
/// ⚠ <b>A source that throws is not allowed to take the others down.</b> Enumeration is defensive
/// here as well as in each source, because a source is supplied by a caller and this type cannot
/// audit it. One bad source costs its own entries and nothing else — the same fail-soft rule the
/// memory inventory has always promised.
/// </para>
/// </remarks>
public static class ArtifactResolver
{
    /// <summary>
    /// Resolve every artifact the given sources declare.
    /// </summary>
    /// <param name="sources">
    /// The sources to read, in listing order. Listing order breaks precedence ties and nothing else.
    /// </param>
    /// <returns>
    /// One <see cref="ResolvedArtifact"/> per (kind, name), each carrying its full chain ordered
    /// highest-precedence first. Grouping is per KIND as well as name: a skill and an agent may
    /// legitimately share a name and are not each other's shadow.
    /// </returns>
    /// <remarks>
    /// ⚠ <b>Names are compared with <see cref="StringComparer.Ordinal"/>.</b> Case matters: the two
    /// products run on case-sensitive filesystems as well as Windows, and folding case here would
    /// invent a shadowing relationship on Linux that does not exist — claiming <c>Build.md</c> and
    /// <c>build.md</c> are one artifact when the tool reads them as two.
    /// </remarks>
    public static IReadOnlyList<ResolvedArtifact> Resolve(IEnumerable<IArtifactSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        // Insertion-ordered so the result is stable and reviewable: artifacts come out in the order
        // their first declaration was seen, not in hash order.
        Dictionary<(ArtifactKind Kind, string Name), List<ArtifactRef>> groups = [];
        List<(ArtifactKind Kind, string Name)> order = [];

        foreach (IArtifactSource source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (ArtifactRef entry in SafeEnumerate(source))
            {
                (ArtifactKind, string) key = (entry.Kind, entry.Name);
                if (!groups.TryGetValue(key, out List<ArtifactRef>? chain))
                {
                    chain = [];
                    groups[key] = chain;
                    order.Add(key);
                }

                chain.Add(entry);
            }
        }

        List<ResolvedArtifact> resolved = new(order.Count);
        foreach ((ArtifactKind Kind, string Name) key in order)
        {
            resolved.Add(new ResolvedArtifact
            {
                Name = key.Name,
                Kind = key.Kind,

                // ⚠ Ties keep listing order because entries were appended in source order and
                // `OrderByDescending` is a **documented stable sort** — not because of any extra
                // bookkeeping. An earlier draft carried a sequence number and a `ThenBy` for this;
                // a canary showed removing the tie-break reddened nothing, so it was dead weight
                // and its comment ("without relying on the sort's own stability guarantees") was
                // false. `EqualPrecedenceKeepsListingOrder` still pins the requirement, and it is
                // the test that would catch a future switch to an unstable sort.
                Entries = [.. groups[key].OrderByDescending(e => e.Scope.Precedence)],
            });
        }

        return resolved;
    }

    /// <summary>
    /// Enumerate one source, swallowing anything it throws.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Materialised inside the try on purpose.</b> <see cref="IArtifactSource.Enumerate"/>
    /// returns a lazy sequence in every real implementation, so a directory walk throws while the
    /// CALLER iterates, not when the method is called — a try around a bare <c>return</c> of the
    /// sequence would catch nothing at all. This is the same lazy-throw trap that makes
    /// <c>yield return</c> plus a caller-side <c>catch</c> look safe when it is not.
    /// </remarks>
    private static IReadOnlyList<ArtifactRef> SafeEnumerate(IArtifactSource source)
    {
        try
        {
            return [.. source.Enumerate().Where(e => e is not null)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            return [];
        }
    }
}
