namespace Bennewitz.Ninja.AgentForge.Core.Settings;

/// <summary>
/// The type of configuration file being managed.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>This is a descriptive label, not a behavioural switch.</b> Nothing in <c>src/</c>
/// branches on <see cref="DiscoveredFile.FileType"/> — it is assigned by discovery, carried
/// on the record, and read only by tests asserting what discovery labelled. Worth knowing
/// before treating a member here as load-bearing, and worth re-checking before making one so.
/// </para>
/// <para>
/// ⏳ <b>Product-specific members in a neutral enum is a known, stated deferral.</b> It is
/// the same debt as <c>ConfigFileDiscoverer</c>'s hardcoded Claude layouts: generalizing it
/// is a product-model change that still has no owning phase in the plan. The OpenCode members
/// were added when Phase 7 needed to construct a <see cref="DiscoveredFile"/> honestly rather
/// than mislabel one as Claude's. When the generalization does happen it should take the
/// whole enum, not carve out one product's members.
/// </para>
/// </remarks>
public enum ConfigFileType
{
    /// <summary>Claude Code settings.json — supports the full 4-scope hierarchy.</summary>
    ClaudeCodeSettings,

    /// <summary>Claude Code mcp.json — user-level MCP server overrides.</summary>
    McpJson,

    /// <summary>Claude Desktop claude_desktop_config.json — preferences + MCP servers.</summary>
    ClaudeDesktopConfig,

    /// <summary>A named profile's settings.json under ~/.claude/profiles/{name}/.</summary>
    ProfileSettings,

    /// <summary>A named profile's mcp.json under ~/.claude/profiles/{name}/.</summary>
    ProfileMcp,

    /// <summary>OpenCode's main config — opencode.json / opencode.jsonc.</summary>
    OpenCodeConfig,

    /// <summary>OpenCode's terminal-UI config — tui.json.</summary>
    OpenCodeTui,
}