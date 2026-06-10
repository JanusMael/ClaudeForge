namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;

/// <summary>
/// The tool families the guided builder offers a tailored input affordance for.
/// Each maps to a Claude Code permission tool (or family) with its own specifier
/// grammar — see <see cref="GuidedRuleBuilderViewModel"/>.
/// </summary>
/// <remarks>
/// Declaration order is the dropdown order in both the builder and tester (both
/// expose <see cref="System.Enum.GetValues{TEnum}()"/>). Grouped to match the
/// Common-actions tab where the families overlap: file → shell → web → mcp → agent.
/// </remarks>
public enum PermissionBuilderTool
{
    /// <summary>File read (gitignore-style path).</summary>
    Read,

    /// <summary>File edit (gitignore-style path; also governs Write).</summary>
    Edit,

    /// <summary>File write (gitignore-style path).</summary>
    Write,

    /// <summary>Bash command (glob specifier).</summary>
    Bash,

    /// <summary>PowerShell command (glob specifier).</summary>
    PowerShell,

    /// <summary>Web fetch (domain specifier).</summary>
    WebFetch,

    /// <summary>MCP server / tool.</summary>
    Mcp,

    /// <summary>Subagent.</summary>
    Agent,
}
