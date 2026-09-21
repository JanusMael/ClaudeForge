namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// One Tier 2 footprint category — files an agent has SEEN but does not actively read every
/// session. The Memory page surfaces these for audit / privacy / cleanup; deletion is per-category
/// and goes through <see cref="FootprintService.DeleteAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This was an <c>enum</c> of seven Claude categories.</b> Its members named
/// <c>~/.claude</c> paths, and <c>FootprintService</c> iterated
/// <c>Enum.GetValues&lt;FootprintCategory&gt;()</c> — so a product-neutral assembly returned
/// Claude's categories to every caller. OpenCode's footprint shares none of those names and spans
/// four unrelated roots. What the category set IS became <see cref="FootprintCatalog"/>, product
/// data; what a category DOES is unchanged.
/// </para>
/// <para>
/// ⭐ <b>The ordinal stays the identity, and <see cref="FootprintCatalog.Default"/> is encoded as a
/// <see langword="null"/> field</b> — the same two decisions <c>ConfigScope</c> documents, for the
/// same measured reason. A struct whose fields are reference types has an all-zero
/// <c>default</c> whose id is <see langword="null"/>; here that would make
/// <c>default(FootprintCategory)</c> stop being <see cref="SessionTranscripts"/>, and would make
/// every call site naming a static compare unequal to the category a Claude client hands out.
/// Plain struct equality gives <c>default(FootprintCategory) == SessionTranscripts</c> only
/// because of the null encoding.
/// </para>
/// <para>
/// ⛔ <b>A struct cannot appear in a constant pattern, so <c>switch</c> arms had to change shape.</b>
/// <c>case FootprintCategory.Todos =&gt;</c> no longer compiles; the idiom this repo already uses
/// for <c>ConfigScope</c> is <c>_ when category == FootprintCategory.Todos =&gt;</c>. That is a
/// compile error rather than a silent behaviour change, which is the one thing that makes this
/// conversion safe to do in bulk.
/// </para>
/// </remarks>
public readonly record struct FootprintCategory
{
    /// <summary>The ordinal into <see cref="Catalog"/>. Mirrors the former enum's underlying value.</summary>
    private readonly int _value;

    /// <summary>
    /// The catalog this category belongs to, or <see langword="null"/> for
    /// <see cref="FootprintCatalog.Default"/>. See the null-encoding note on the type.
    /// </summary>
    private readonly FootprintCatalog? _catalog;

    internal FootprintCategory(int value, FootprintCatalog? catalog)
    {
        _value = value;
        _catalog = catalog;
    }

    /// <summary>The catalog this category came from.</summary>
    public FootprintCatalog Catalog => _catalog ?? FootprintCatalog.Default;

    /// <summary>The ordinal, exposed so ordering code need not cast.</summary>
    public int Ordinal => _value;

    /// <summary>What this category covers — sources, anchor, and the Standard-backup flag.</summary>
    public FootprintCategoryDefinition Definition => Catalog.DefinitionAt(_value);

    /// <summary>
    /// Stable machine key — <c>"session-transcripts"</c>. Data rather than presentation: the app
    /// layer keys its localised label and tooltip lookups by it.
    /// </summary>
    public string Id => Definition.Id;

    /// <summary>Whether the Standard backup mode preserves this category.</summary>
    public bool IsInStandardBackup => Definition.IsInStandardBackup;

    // ── Claude's seven, on the default catalog ─────────────────────────────

    /// <summary><c>~/.claude/projects/&lt;mangled&gt;/*.jsonl</c> — every session transcript.</summary>
    public static FootprintCategory SessionTranscripts => FootprintCatalog.Default.CategoryAt(0);

    /// <summary><c>~/.claude/sessions/</c>, <c>session-data/</c>, <c>session-env/</c> — session metadata.</summary>
    public static FootprintCategory SessionMetadata => FootprintCatalog.Default.CategoryAt(1);

    /// <summary><c>~/.claude/history.jsonl</c> — interactive prompt history.</summary>
    public static FootprintCategory PromptHistory => FootprintCatalog.Default.CategoryAt(2);

    /// <summary><c>~/.claude/bash-commands.log</c> — log of bash invocations.</summary>
    public static FootprintCategory BashCommandLog => FootprintCatalog.Default.CategoryAt(3);

    /// <summary><c>~/.claude/cost-tracker.log</c> — token-cost telemetry.</summary>
    public static FootprintCategory CostTrackerLog => FootprintCatalog.Default.CategoryAt(4);

    /// <summary><c>~/.claude/todos/</c> — todo-list snapshots.</summary>
    public static FootprintCategory Todos => FootprintCatalog.Default.CategoryAt(5);

    /// <summary><c>~/.claude/file-history/</c> — file-edit before/after snapshots.</summary>
    public static FootprintCategory FileEditHistory => FootprintCatalog.Default.CategoryAt(6);

    /// <summary>
    /// Every category on the default catalog, in declaration order — the replacement for
    /// <c>Enum.GetValues&lt;FootprintCategory&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Default catalog only.</b> Product code must enumerate its own
    /// <see cref="FootprintCatalog.All"/>; this property exists for the Claude call sites that
    /// already said <c>Enum.GetValues</c> and meant exactly this.
    /// </remarks>
    public static IReadOnlyList<FootprintCategory> All => FootprintCatalog.Default.All;

    /// <summary>
    /// The former enum member name — <c>"SessionTranscripts"</c>, not the id.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>Kept PascalCase deliberately.</b> <c>ConfigScope.ToString()</c> is consumed as data in
    /// this codebase, and the same trap applies here: test assertions and diagnostic output
    /// compare against the old member names. The id is the lower-case machine key; this is the
    /// display-ish name the enum used to produce.
    /// </remarks>
    public override string ToString() => ToPascalCase(Id);

    private static string ToPascalCase(string id)
    {
        // "session-transcripts" -> "SessionTranscripts". The ids are ASCII machine keys written in
        // the catalog beside this code, so invariant casing is right and culture cannot bite.
        string[] parts = id.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(static p =>
            char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
