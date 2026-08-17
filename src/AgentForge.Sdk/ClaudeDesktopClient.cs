using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk;

/// <summary>
/// <see cref="IAgentConfigClient"/> for the Claude Desktop GUI app. Loads
/// <c>claude_desktop_config.json</c> from the user's Desktop config directory.
/// </summary>
/// <remarks>
/// Most of the implementation lives on the internal
/// <see cref="AgentConfigClientCore"/> base class. This subclass only supplies
/// the file-discovery strategy and the schema discriminator.
/// </remarks>
public sealed class ClaudeDesktopClient : AgentConfigClientCore
{
    /// <inheritdoc cref="ClaudeCodeClient(ConfigScope)"/>
    public ClaudeDesktopClient(ConfigScope defaultScope = ConfigScope.User)
        : base(defaultScope, schemaRegistry: null)
    {
    }

    /// <summary>
    /// Test-only constructor that lets fixtures inject a shared
    /// <see cref="SchemaRegistry"/> instance.
    /// </summary>
    internal ClaudeDesktopClient(ConfigScope defaultScope, SchemaRegistry schemaRegistry)
        : base(defaultScope, schemaRegistry)
    {
    }

    /// <inheritdoc cref="ClaudeCodeClient.FromExistingWorkspace"/>
    internal static ClaudeDesktopClient FromExistingWorkspace(
        SettingsWorkspace workspace,
        ConfigScope defaultScope,
        SchemaRegistry schemaRegistry)
    {
        return new ClaudeDesktopClient(defaultScope, schemaRegistry, workspace);
    }

    private ClaudeDesktopClient(ConfigScope defaultScope, SchemaRegistry schemaRegistry, SettingsWorkspace preLoaded)
        : base(defaultScope, schemaRegistry, preLoaded)
    {
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot)
    {
        // Desktop has a single user-scope config file. The projectRoot argument
        // is ignored — Desktop is not project-aware. profileName=null for the
        // same reason ClaudeCodeClient passes null: profile-aware loading is
        // post-v1 SDK work.
        _ = projectRoot;
        return [ConfigFileDiscoverer.DiscoverDesktopConfig(profileName: null)];
    }

    /// <inheritdoc/>
    protected override bool IsClaudeCode => false;

    /// <inheritdoc/>
    protected override IBackupClient CreateBackupClient()
    {
        return new BackupClient(
            engine: BackupEngine.Default,
            includeClaudeCode: false,
            includeClaudeDesktop: true);
    }

    /// <summary>
    /// Claude Desktop has no <c>CLAUDE.md</c>-equivalent; conversation
    /// history sits inside <c>%APPDATA%/Claude/IndexedDB</c> as opaque
    /// Chromium-managed binary blobs. The Memory page renders a static
    /// explainer panel for the Desktop section and never queries the Tier
    /// 1 inventory, but we override here defensively so a hypothetical
    /// future caller (MCP server, CLI dump) gets an empty list rather
    /// than the Code-side inventory.
    /// </summary>
    public override IReadOnlyList<UserMemoryFile> SnapshotUserMemoryFiles(string? projectRoot = null)
    {
        return [];
    }
}