using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;

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
    protected override IBackupClient CreateBackupClient()
        => new BackupClient(BackupEngine.Default, [Product]);
}
