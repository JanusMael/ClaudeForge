namespace Bennewitz.Ninja.ClaudeForge.Core.Settings;

/// <summary>
/// The type of configuration file being managed.
/// </summary>
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