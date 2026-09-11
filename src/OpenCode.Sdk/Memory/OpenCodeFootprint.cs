using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.OpenCode.Sdk.Memory;

/// <summary>
/// OpenCode's Tier-2 footprint: what it leaves on disk, where, and which of it is safe to delete.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Every category here was MEASURED, and the list this replaces was documentation that broke
/// on contact.</b> The plan enumerated <c>storage/</c> (with five children) · <c>log/</c> ·
/// <c>snapshot/</c> · <c>tool-output/</c> · <c>bin/</c> · <c>repos/</c>. Measured against a real
/// install: <c>storage/</c> does not exist at all — <c>session</c>, <c>message</c> and <c>part</c>
/// are SQLite <i>tables</i> — <c>snapshot/</c> and <c>tool-output/</c> are absent, and
/// <c>repos/</c> is empty. A page built to that list would have shown three categories that do not
/// exist and missed every large item actually on disk. See
/// <c>docs/opencode-install-probe.json</c>.
/// </para>
/// <para>
/// ⛔⛔ <b>THE PRUNE ORDER AND THE BACKUP ORDER ARE OPPOSITE, and a page sorted by size alone
/// invites the wrong click.</b> The biggest item by an order of magnitude
/// (<c>node_modules/</c>, ~52 MiB) is the most disposable — it regenerates from
/// <c>package.json</c>. The smallest meaningful one (<c>opencode.db</c>, ~530 KiB) is the ONLY
/// thing here a user cannot get back. Sorting by size puts the irreplaceable item at the bottom of
/// the list and the safest deletion at the top.
/// </para>
/// <para>
/// ⚠ <b>Sizes are STRUCTURE, not scale.</b> Every figure quoted came from an install with zero
/// sessions, so growth and retention are still unmeasured — that half of Phase 16 is open.
/// </para>
/// </remarks>
public static class OpenCodeFootprint
{
    /// <summary>Root key for <c>~/.config/opencode</c>.</summary>
    public const string ConfigRoot = "config";

    /// <summary>Root key for <c>~/.local/share/opencode</c>.</summary>
    public const string DataRoot = "data";

    /// <summary>Root key for <c>~/.local/state/opencode</c>.</summary>
    public const string StateRoot = "state";

    /// <summary>Root key for <c>~/.cache/opencode</c>.</summary>
    public const string CacheRoot = "cache";

    /// <summary>
    /// The four roots, resolved fresh on each call.
    /// </summary>
    /// <remarks>
    /// ⛔ A method rather than a property holding resolved paths: these derive from the user
    /// profile, which honours an <c>AsyncLocal</c> test override, and a footprint service is cached
    /// for the lifetime of a client.
    /// </remarks>
    public static FootprintRoots Roots() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ConfigRoot] = OpenCodePaths.DefaultGlobalDirectory(),
        [DataRoot] = OpenCodePaths.DataDirectory(),
        [StateRoot] = OpenCodePaths.StateDirectory(),
        [CacheRoot] = OpenCodePaths.CacheDirectory(),
    });

    /// <summary>
    /// OpenCode's footprint categories, ordered most-disposable first.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>The order IS the guidance.</b> Claude's catalog renders in its former enum's
    /// declaration order, which carried no meaning; this one is deliberately ordered so the safest
    /// and largest prune target sits at the top and the irreplaceable database at the bottom —
    /// the opposite of a size sort, and the opposite of backup priority.
    /// </remarks>
    public static FootprintCatalog Catalog { get; } = new(
    [
        new FootprintCategoryDefinition(
            Id: "node-modules",
            // ~52.5 MiB across 3,458 files for a SINGLE declared dependency — 99.97% of the config
            // root, materialized to resolve plugin imports, and regenerated on demand.
            Sources: [FootprintSource.Directory(ConfigRoot, "node_modules")],
            Anchor: FootprintSource.Directory(ConfigRoot, "node_modules"),
            // Excluded by the .gitignore OpenCode maintains, so a backup never carried it anyway.
            IsInStandardBackup: false),

        new FootprintCategoryDefinition(
            Id: "download-temps",
            // ⭐ Found by inspection, on a list nothing else covers: an interrupted download leaves
            // a FULL-SIZE orphan (`models.json.<pid>.<ts>.tmp`, ~4.6 MB) beside the complete file,
            // and nothing ever cleans it. A page keyed on the filename `models.json` misses it
            // entirely, while it is the second-largest single item on the install.
            //
            // ⭐ Unlike every other prune candidate this one is UNAMBIGUOUSLY safe: a temp whose
            // final file already exists cannot be needed.
            Sources:
            [
                FootprintSource.Directory(CacheRoot, string.Empty, "*.tmp", recursive: false),
                FootprintSource.Directory(CacheRoot, "bin", "*", recursive: true),
            ],
            Anchor: FootprintSource.Directory(CacheRoot, string.Empty),
            IsInStandardBackup: false),

        new FootprintCategoryDefinition(
            Id: "model-catalog",
            // The entire cache is this one file, ~4.6 MB, re-fetched on demand.
            // ⚠ `bin/` is NOT empty, despite an earlier measurement saying so: it holds a ~1.8 MB
            // ripgrep archive once anything triggers that download. It is counted under
            // download-temps above, where its extraction leftovers also land.
            Sources: [FootprintSource.File(CacheRoot, "models.json")],
            Anchor: FootprintSource.File(CacheRoot, "models.json"),
            IsInStandardBackup: false),

        new FootprintCategoryDefinition(
            Id: "logs",
            Sources: [FootprintSource.Directory(DataRoot, "log")],
            Anchor: FootprintSource.Directory(DataRoot, "log"),
            IsInStandardBackup: false),

        new FootprintCategoryDefinition(
            Id: "locks",
            // ⚠ Each lock is a DIRECTORY holding heartbeat + meta.json, not a file.
            Sources: [FootprintSource.Directory(StateRoot, "locks")],
            Anchor: FootprintSource.Directory(StateRoot, "locks"),
            IsInStandardBackup: false),

        new FootprintCategoryDefinition(
            Id: "session-database",
            // ⛔ LAST on purpose. This is the only irreplaceable item in the list — every session,
            // message and part lives here, plus the credential tables — and it is by far the
            // smallest meaningful one. All three files: a copy without the -wal is a stale
            // snapshot by construction.
            Sources:
            [
                FootprintSource.File(DataRoot, "opencode.db"),
                FootprintSource.File(DataRoot, "opencode.db-wal"),
                FootprintSource.File(DataRoot, "opencode.db-shm"),
            ],
            Anchor: FootprintSource.File(DataRoot, "opencode.db"),
            // ⛔ FALSE, and the reason matters: the database is archived only behind an explicit
            // credential opt-in, and never in the sharing-targeted mode. A user who took an
            // ordinary backup does NOT have this, and the page must not imply otherwise.
            IsInStandardBackup: false),
    ]);
}
