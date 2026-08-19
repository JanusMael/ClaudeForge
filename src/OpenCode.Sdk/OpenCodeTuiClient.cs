using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// Client for OpenCode's terminal-UI configuration — <c>tui.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// A separate product rather than a second file of <see cref="OpenCodeClient"/>'s, because it
/// is validated by its own schema and shares no keys with the main config. It is the same
/// relationship Claude Code and Claude Desktop have, which is why the shell can host it with
/// no new machinery.
/// </para>
/// <para>
/// It is global-only: there is no project <c>tui.json</c> and no environment variable that
/// relocates it independently — it simply sits beside the main config, so
/// <c>$OPENCODE_CONFIG_DIR</c> moves both together. The ladder is still OpenCode's full five
/// rungs, since a ladder is the vocabulary of possible rungs rather than a claim that each is
/// populated, and discovery is what decides that a single scope is editable here.
/// </para>
/// </remarks>
public sealed class OpenCodeTuiClient : AgentConfigClientCore
{
    private readonly OpenCodeEnvironment _env;

    /// <inheritdoc cref="OpenCodeClient()"/>
    public OpenCodeTuiClient()
        : this(OpenCodeClient.GlobalScope, OpenCodeEnvironment.FromProcess())
    {
    }

    /// <inheritdoc cref="OpenCodeClient(ConfigScope, OpenCodeEnvironment)"/>
    public OpenCodeTuiClient(ConfigScope defaultScope, OpenCodeEnvironment env)
        : base(defaultScope, schemaRegistry: null)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="projectRoot"/> is accepted and ignored: <c>tui.json</c> has no project
    /// form, so a project context changes nothing about where it is found.
    /// </remarks>
    protected override IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot)
    {
        _ = projectRoot;
        return OpenCodeDiscovery.DiscoverTui(_env);
    }

    /// <inheritdoc/>
    protected override ProductDescriptor Product => OpenCodeProducts.Tui;

    /// <inheritdoc/>
    /// <remarks>
    /// The same policy as the main config. It costs nothing to share — <c>tui.json</c>
    /// declares none of the union paths — and supplying a second policy would mean two places
    /// to update when OpenCode's merge rules change.
    /// </remarks>
    protected override IMergePolicy MergePolicy => OpenCodeMergePolicy.Instance;

    /// <inheritdoc/>
    protected override ScopeLadder Scopes => OpenCodeScopes.Ladder;

    /// <inheritdoc/>
    protected override IBackupClient CreateBackupClient()
        => new BackupClient(BackupEngine.Default, [Product]);
}
