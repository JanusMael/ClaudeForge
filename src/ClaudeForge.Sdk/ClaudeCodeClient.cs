using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.FileIO;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Sdk;

/// <summary>
/// <see cref="IClaudeConfigClient"/> for the Claude Code CLI. Loads
/// <c>~/.claude/settings.json</c>, the matching <c>~/.claude/.mcp.json</c>,
/// and (when a project root is provided) <c>.claude/settings.json</c> +
/// <c>.claude/settings.local.json</c> + <c>.mcp.json</c>.
/// </summary>
/// <remarks>
/// Most of the implementation lives on the internal
/// <see cref="ClaudeConfigClientCore"/> base class. This subclass only supplies
/// the file-discovery strategy and the schema discriminator.
/// </remarks>
public sealed class ClaudeCodeClient : ClaudeConfigClientCore
{
    /// <summary>Construct a client whose mutations target <paramref name="defaultScope"/> by default.</summary>
    /// <param name="defaultScope">
    /// Scope used by accessor mutations and unscoped <see cref="IClaudeConfigClient.SetValue{T}(string, T)"/>
    /// calls. Per-call overrides go through the explicit-scope overload.
    /// </param>
    public ClaudeCodeClient(ConfigScope defaultScope = ConfigScope.User)
        : base(defaultScope, schemaRegistry: null)
    {
    }

    /// <summary>
    /// Test-only constructor that lets fixtures inject a shared
    /// <see cref="SchemaRegistry"/> instance (e.g. one preloaded with bundled
    /// schemas). The public constructor creates a fresh registry per client.
    /// </summary>
    internal ClaudeCodeClient(ConfigScope defaultScope, SchemaRegistry schemaRegistry)
        : base(defaultScope, schemaRegistry)
    {
    }

    /// <summary>
    /// Wraps an already-loaded <see cref="SettingsWorkspace"/>. Used during
    /// the GUI's in-flight SDK migratio so the existing
    /// <c>MainWindowViewModel._workspace</c> and the SDK client share a
    /// single underlying state object — no double-load, no divergent state.
    /// </summary>
    /// <remarks>
    /// Skip <see cref="IClaudeConfigClient.OpenAsync"/> when constructed via
    /// this overload; the workspace is already populated. Subsequent
    /// <see cref="IClaudeConfigClient.ReloadAsync"/> calls re-discover and
    /// re-load via the standard path.
    /// </remarks>
    internal static ClaudeCodeClient FromExistingWorkspace(
        SettingsWorkspace workspace,
        ConfigScope defaultScope,
        SchemaRegistry schemaRegistry)
    {
        return new ClaudeCodeClient(defaultScope, schemaRegistry, workspace);
    }

    private ClaudeCodeClient(ConfigScope defaultScope, SchemaRegistry schemaRegistry, SettingsWorkspace preLoaded)
        : base(defaultScope, schemaRegistry, preLoaded)
    {
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot)
    {
        // Match the GUI's discovery flow: settings files first (so their save
        // order takes priority over .mcp.json when both contain mcpServers).
        // profileName=null — profile-aware loading is post-v1 SDK work; for now
        // the SDK always operates against the global ~/.claude/ tree.
        IReadOnlyList<DiscoveredFile> settings = ConfigFileDiscoverer.DiscoverClaudeCodeSettings(projectRoot, profileName: null);
        IReadOnlyList<DiscoveredFile> mcp = ConfigFileDiscoverer.DiscoverMcpFiles(projectRoot, profileName: null);
        return [.. settings, .. mcp];
    }

    /// <inheritdoc/>
    protected override bool IsClaudeCode => true;

    /// <inheritdoc/>
    protected override IBackupClient CreateBackupClient()
    {
        return new BackupClient(
            engine: BackupEngine.Default,
            includeClaudeCode: true,
            includeClaudeDesktop: false);
    }
}