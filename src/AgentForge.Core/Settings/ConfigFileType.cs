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
/// ⏳ <b>The remaining members are Claude's, and they leave with <c>ConfigFileDiscoverer</c></b>,
/// their only real user, in plans/00008 step 11. OpenCode's two members left in step 7
/// (decision 8); the parked OpenCodeForge branch keeps its own copy.
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
}