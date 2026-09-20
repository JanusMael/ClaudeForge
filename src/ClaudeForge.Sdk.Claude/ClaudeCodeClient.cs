using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

/// <summary>
/// <see cref="IClaudeConfigClient"/> for the Claude Code CLI. Loads
/// <c>~/.claude/settings.json</c>, the matching <c>~/.claude/.mcp.json</c>,
/// and (when a project root is provided) <c>.claude/settings.json</c> +
/// <c>.claude/settings.local.json</c> + <c>.mcp.json</c>.
/// </summary>
/// <remarks>
/// Most of the implementation lives on <see cref="AgentConfigClientCore"/> (the
/// product-neutral machinery) and <see cref="ClaudeConfigClientBase"/> (the
/// Claude-domain accessors). This subclass only supplies the file-discovery
/// strategy and the schema discriminator.
/// </remarks>
public sealed class ClaudeCodeClient : ClaudeConfigClientBase
{
    /// <summary>
    /// The resolved Claude environment every path this client reads is relative to.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Required on every constructor rather than defaulted.</b> This client decides which
    /// <c>settings.json</c> the user is editing; one built without the environment would edit the
    /// default tree while the agent read a relocated one, and nothing would report the mismatch.
    /// </remarks>
    private readonly ClaudeEnvironment _env;

    /// <summary>Construct a client whose mutations target <see cref="ConfigScope.User"/>.</summary>
    /// <remarks>
    /// An overload rather than a defaulted parameter: <see cref="ConfigScope"/> became a
    /// struct in Phase 3, and a default parameter value must be a compile-time constant,
    /// which a static property never is. Do not "simplify" this pair back into
    /// <c>ConfigScope defaultScope = ConfigScope.User</c> — it cannot compile.
    /// </remarks>
    public ClaudeCodeClient(ClaudeEnvironment env)
        : this(env, ConfigScope.User)
    {
    }

    /// <summary>Construct a client whose mutations target <paramref name="defaultScope"/> by default.</summary>
    /// <param name="env">
    /// The resolved Claude environment: which config directory this client reads and writes.
    /// Production passes <see cref="ClaudeEnvironment.FromProcess"/>'s result from the
    /// composition root; a test that does not care passes <see cref="ClaudeEnvironment.Empty"/>.
    /// </param>
    /// <param name="defaultScope">
    /// Scope used by accessor mutations and unscoped <see cref="IAgentConfigClient.SetValue{T}(string, T)"/>
    /// calls. Per-call overrides go through the explicit-scope overload.
    /// </param>
    public ClaudeCodeClient(ClaudeEnvironment env, ConfigScope defaultScope)
        : base(defaultScope, schemaRegistry: null)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;
    }

    /// <summary>
    /// Test-only constructor that lets fixtures inject a shared
    /// <see cref="SchemaRegistry"/> instance (e.g. one preloaded with bundled
    /// schemas). The public constructor creates a fresh registry per client.
    /// </summary>
    internal ClaudeCodeClient(ClaudeEnvironment env, ConfigScope defaultScope, SchemaRegistry schemaRegistry)
        : base(defaultScope, schemaRegistry)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;
    }

    /// <summary>
    /// Wraps an already-loaded <see cref="SettingsWorkspace"/>. Used during
    /// the GUI's in-flight SDK migratio so the existing
    /// <c>MainWindowViewModel._workspace</c> and the SDK client share a
    /// single underlying state object — no double-load, no divergent state.
    /// </summary>
    /// <remarks>
    /// Skip <see cref="IAgentConfigClient.OpenAsync"/> when constructed via
    /// this overload; the workspace is already populated. Subsequent
    /// <see cref="IAgentConfigClient.ReloadAsync"/> calls re-discover and
    /// re-load via the standard path.
    /// </remarks>
    internal static ClaudeCodeClient FromExistingWorkspace(
        ClaudeEnvironment env,
        SettingsWorkspace workspace,
        ConfigScope defaultScope,
        SchemaRegistry schemaRegistry,
        IConfigWriter? configWriter = null)
    {
        return new ClaudeCodeClient(env, defaultScope, schemaRegistry, workspace, configWriter);
    }

    private ClaudeCodeClient(ClaudeEnvironment env, ConfigScope defaultScope, SchemaRegistry schemaRegistry,
                             SettingsWorkspace preLoaded, IConfigWriter? configWriter)
        : base(defaultScope, schemaRegistry, preLoaded, configWriter)
    {
        ArgumentNullException.ThrowIfNull(env);
        _env = env;
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot)
    {
        // Match the GUI's discovery flow: settings files first (so their save
        // order takes priority over .mcp.json when both contain mcpServers).
        // profileName=null — profile-aware loading is post-v1 SDK work; for now
        // the SDK always operates against the global ~/.claude/ tree.
        IReadOnlyList<DiscoveredFile> settings =
            ConfigFileDiscoverer.DiscoverClaudeCodeSettings(_env, projectRoot, profileName: null);
        IReadOnlyList<DiscoveredFile> mcp = ConfigFileDiscoverer.DiscoverMcpFiles(_env, projectRoot, profileName: null);
        return [.. settings, .. mcp];
    }

    /// <inheritdoc/>
    protected override ProductDescriptor Product => SchemaRegistry.ClaudeCodeProductFor(_env);

    /// <inheritdoc/>
    protected override IBackupClient CreateBackupClient()
    {
        // This client's own descriptor — the product it already declares as
        // Product, rather than a boolean pair restating it.
        //
        // The engine is constructed here rather than shared: an engine must name the environment
        // its destinations resolve against, and this client is the thing that knows it.
        return new BackupClient(new BackupEngine(_env), [Product]);
    }
}