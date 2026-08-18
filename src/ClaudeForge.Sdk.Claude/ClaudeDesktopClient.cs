using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

/// <summary>
/// <see cref="IClaudeConfigClient"/> for the Claude Desktop GUI app. Loads
/// <c>claude_desktop_config.json</c> from the user's Desktop config directory.
/// </summary>
/// <remarks>
/// Most of the implementation lives on <see cref="AgentConfigClientCore"/> (the
/// product-neutral machinery) and <see cref="ClaudeConfigClientBase"/> (the
/// Claude-domain accessors). This subclass only supplies the file-discovery
/// strategy and the schema discriminator.
/// </remarks>
public sealed class ClaudeDesktopClient : ClaudeConfigClientBase
{
    /// <inheritdoc cref="ClaudeCodeClient()"/>
    public ClaudeDesktopClient()
        : this(ConfigScope.User)
    {
    }

    /// <inheritdoc cref="ClaudeCodeClient(ConfigScope)"/>
    public ClaudeDesktopClient(ConfigScope defaultScope)
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
        SchemaRegistry schemaRegistry,
        IConfigWriter? configWriter = null)
    {
        return new ClaudeDesktopClient(defaultScope, schemaRegistry, workspace, configWriter);
    }

    private ClaudeDesktopClient(ConfigScope defaultScope, SchemaRegistry schemaRegistry,
                                SettingsWorkspace preLoaded, IConfigWriter? configWriter)
        : base(defaultScope, schemaRegistry, preLoaded, configWriter)
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