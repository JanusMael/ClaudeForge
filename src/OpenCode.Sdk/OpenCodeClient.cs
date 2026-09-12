using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.OpenCode.Sdk.Memory;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// Client for OpenCode's main configuration — <c>opencode.json</c> / <c>opencode.jsonc</c>
/// across the global, custom and project scopes.
/// </summary>
/// <remarks>
/// <para>
/// Everything except the four members below comes from
/// <see cref="AgentConfigClientCore"/>. That is the whole point of Phases 3 through 6: the
/// scope model, the merge engine, the writer selection, the workspace and the save path were
/// generalized one at a time, each with its own seam, so the second product is a small class
/// rather than a parallel implementation.
/// </para>
/// <para>
/// ⚠ Only three of the ladder's five rungs are ever discovered. See
/// <see cref="OpenCodeDiscovery"/> for why Inline and Managed are deliberately absent rather
/// than guessed.
/// </para>
/// </remarks>
public sealed class OpenCodeClient : AgentConfigClientCore
{
    private readonly OpenCodeEnvironment _env;

    /// <summary>
    /// Construct a client whose mutations target the global scope, reading the environment
    /// from the current process.
    /// </summary>
    /// <remarks>
    /// An overload rather than a defaulted parameter, for the same reason Claude's client
    /// carries the same pair: <see cref="ConfigScope"/> is a struct as of Phase 3, and a
    /// default parameter value must be a compile-time constant, which a static property is
    /// not. This cannot be "simplified" into one constructor — it will not compile.
    /// </remarks>
    public OpenCodeClient()
        : this(GlobalScope, OpenCodeEnvironment.FromProcess())
    {
    }

    /// <summary>
    /// Construct a client with an explicit default scope and environment.
    /// </summary>
    /// <param name="defaultScope">Scope that unscoped mutations write to.</param>
    /// <param name="env">
    /// The environment overrides in effect. Passed rather than read, so a test can exercise
    /// every discovery permutation without mutating process-global state that would leak into
    /// whatever runs alongside it.
    /// </param>
    public OpenCodeClient(ConfigScope defaultScope, OpenCodeEnvironment env)
        : base(defaultScope, schemaRegistry: null)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;

        // ⛔⛔ WITHOUT THIS LINE AN OPENCODE CLIENT REPORTS CLAUDE'S FOOTPRINT.
        //
        // AgentConfigClientCore builds `new FootprintService()`, whose catalog defaults to
        // FootprintCatalog.Default — Claude's seven ~/.claude categories — so every
        // GetFootprintStatsAsync / DeleteFootprintCategoryAsync call on this client walked
        // ~/.claude/projects, history.jsonl and todos/ and called the result OpenCode's. It
        // never threw and never logged: the rows are real, they are just the wrong product's,
        // and a delete would have removed the OTHER agent's transcripts.
        //
        // FootprintCatalog.All's own docstring names this failure exactly ("Code belonging to
        // a product must enumerate that product's catalog, or it silently renders Claude's
        // seven rows for a product whose footprint looks nothing like them"), and the catalog
        // to enumerate has existed and been tested since Phase 14 — nothing connected it.
        //
        // ⭐ The fourth instance of one shape: a path resolving through the neutral default
        // rather than the product's own data. The other three were writes (the credentials
        // prompt, the backup's config root, the footprint's config root); this is a READ, which
        // is why no archive and no measurement caught it.
        //
        // ⚠ `roots` is the METHOD GROUP, not `Roots()` — the service calls it per use because
        // the paths derive from the user profile, which honours an AsyncLocal test override.
        FootprintService = new FootprintService(
            catalog: OpenCodeFootprint.Catalog,
            roots: OpenCodeFootprint.Roots);
    }

    /// <summary>The lowest rung, and the one that exists on every installation.</summary>
    public static ConfigScope GlobalScope => OpenCodeScopes.Ladder.All[^1];

    /// <inheritdoc/>
    protected override IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot)
        => OpenCodeDiscovery.DiscoverConfig(projectRoot, _env);

    /// <inheritdoc/>
    protected override ProductDescriptor Product => OpenCodeProducts.Config;

    /// <inheritdoc/>
    protected override IMergePolicy MergePolicy => OpenCodeMergePolicy.Instance;

    /// <inheritdoc/>
    protected override ScopeLadder Scopes => OpenCodeScopes.Ladder;

    /// <inheritdoc/>
    /// <remarks>
    /// ⛔ <see cref="OpenCodeBackup.Engine"/>, never <c>BackupEngine.Default</c> — the default
    /// engine writes OpenCode archives quite happily and restores nothing from them. See that
    /// type's remarks.
    /// </remarks>
    protected override IBackupClient CreateBackupClient()
        => new BackupClient(OpenCodeBackup.Engine, [Product]);
}
