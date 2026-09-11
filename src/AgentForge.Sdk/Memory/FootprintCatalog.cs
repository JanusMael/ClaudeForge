namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// A product's ordered set of footprint categories. The table
/// <see cref="FootprintCategory"/> reads its identity from, and the direct analogue of
/// <c>ScopeLadder</c> for <c>ConfigScope</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This replaces <c>Enum.GetValues&lt;FootprintCategory&gt;()</c>.</b> That call returned
/// Claude's seven categories to every caller, including a product whose categories share none of
/// those names — a closed enum in a product-neutral assembly. The shape of the work (walk
/// categories, compute size and count, delete per category) transfers; the members do not.
/// </para>
/// <para>
/// ⭐ <b><see cref="Default"/> is encoded as <see langword="null"/> inside a
/// <see cref="FootprintCategory"/>,</b> for the reason <c>ConfigScope</c> documents at length:
/// it keeps <c>default(FootprintCategory)</c> equal to <see cref="FootprintCategory.SessionTranscripts"/>
/// and keeps the statics equal to the categories a Claude client hands out. Without it, every
/// existing call site naming <c>FootprintCategory.Todos</c> would compare unequal to the service's
/// own <c>Todos</c>, and the change would read as a hundred unrelated failures rather than one
/// design decision.
/// </para>
/// </remarks>
public sealed class FootprintCatalog
{
    private readonly IReadOnlyList<FootprintCategoryDefinition> _definitions;

    /// <param name="definitions">
    /// The categories, in the order the UI renders them. Declaration order was load-bearing for
    /// the former enum — the footprint table renders in it — so it stays load-bearing here.
    /// </param>
    public FootprintCatalog(IReadOnlyList<FootprintCategoryDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (definitions.Count == 0)
        {
            throw new ArgumentException("A footprint catalog needs at least one category.", nameof(definitions));
        }

        _definitions = definitions;
    }

    /// <summary>
    /// Claude Code's seven Tier-2 categories, in the order the former enum declared them.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>The ids and the order are a compatibility surface, not a style choice.</b> The ids key
    /// the app's localised labels and tooltips; the order is what the footprint table renders in.
    /// <c>FootprintCatalogTests</c> pins both against the former enum's member names.
    /// </remarks>
    public static FootprintCatalog Default { get; } = new(
    [
        new FootprintCategoryDefinition(
            Id: "session-transcripts",
            Sources: [FootprintSource.Directory(FootprintRoots.Home, "projects", "*.jsonl")],
            Anchor: FootprintSource.Directory(FootprintRoots.Home, "projects"),
            // ~/.claude/projects is the one subdirectory Standard mode skips; Full includes it.
            IsInStandardBackup: false),

        new FootprintCategoryDefinition(
            Id: "session-metadata",
            Sources:
            [
                FootprintSource.Directory(FootprintRoots.Home, "sessions"),
                FootprintSource.Directory(FootprintRoots.Home, "session-data"),
                FootprintSource.Directory(FootprintRoots.Home, "session-env"),
            ],
            // The parent, so a reveal shows all three siblings at once.
            Anchor: FootprintSource.Directory(FootprintRoots.Home, string.Empty),
            IsInStandardBackup: true),

        new FootprintCategoryDefinition(
            Id: "prompt-history",
            Sources: [FootprintSource.File(FootprintRoots.Home, "history.jsonl")],
            Anchor: FootprintSource.File(FootprintRoots.Home, "history.jsonl"),
            IsInStandardBackup: true),

        new FootprintCategoryDefinition(
            Id: "bash-command-log",
            Sources: [FootprintSource.File(FootprintRoots.Home, "bash-commands.log")],
            Anchor: FootprintSource.File(FootprintRoots.Home, "bash-commands.log"),
            IsInStandardBackup: true),

        new FootprintCategoryDefinition(
            Id: "cost-tracker-log",
            Sources: [FootprintSource.File(FootprintRoots.Home, "cost-tracker.log")],
            Anchor: FootprintSource.File(FootprintRoots.Home, "cost-tracker.log"),
            IsInStandardBackup: true),

        new FootprintCategoryDefinition(
            Id: "todos",
            Sources: [FootprintSource.Directory(FootprintRoots.Home, "todos")],
            Anchor: FootprintSource.Directory(FootprintRoots.Home, "todos"),
            IsInStandardBackup: true),

        new FootprintCategoryDefinition(
            Id: "file-edit-history",
            Sources: [FootprintSource.Directory(FootprintRoots.Home, "file-history")],
            Anchor: FootprintSource.Directory(FootprintRoots.Home, "file-history"),
            IsInStandardBackup: true),
    ]);

    /// <summary>How many categories this catalog holds.</summary>
    public int Count => _definitions.Count;

    /// <summary>Every category in this catalog, in render order.</summary>
    /// <remarks>
    /// ⚠ <b>This is one catalog's set, not "every category in the process".</b> Code belonging to
    /// a product must enumerate that product's catalog, or it silently renders Claude's seven rows
    /// for a product whose footprint looks nothing like them.
    /// </remarks>
    public IReadOnlyList<FootprintCategory> All =>
        [.. Enumerable.Range(0, _definitions.Count).Select(CategoryAt)];

    /// <summary>The category at <paramref name="ordinal"/>.</summary>
    /// <remarks>
    /// Normalises <see cref="Default"/> to the <see langword="null"/> encoding so a category this
    /// method hands out compares equal to the corresponding <see cref="FootprintCategory"/> static.
    /// Nothing outside this class needs to know that.
    /// </remarks>
    public FootprintCategory CategoryAt(int ordinal)
    {
        if ((uint)ordinal >= (uint)_definitions.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal),
                ordinal,
                $"This catalog has {_definitions.Count} categories.");
        }

        return new FootprintCategory(ordinal, ReferenceEquals(this, Default) ? null : this);
    }

    /// <summary>The definition at <paramref name="ordinal"/>.</summary>
    public FootprintCategoryDefinition DefinitionAt(int ordinal)
    {
        if ((uint)ordinal >= (uint)_definitions.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordinal),
                ordinal,
                $"This catalog has {_definitions.Count} categories.");
        }

        return _definitions[ordinal];
    }

    /// <summary>
    /// The category whose <see cref="FootprintCategoryDefinition.Id"/> matches, or
    /// <see langword="null"/>. Ids are compared ordinally — they are machine keys.
    /// </summary>
    public FootprintCategory? TryGetById(string id)
    {
        for (int i = 0; i < _definitions.Count; i++)
        {
            if (string.Equals(_definitions[i].Id, id, StringComparison.Ordinal))
            {
                return CategoryAt(i);
            }
        }

        return null;
    }
}
