namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

/// <summary>
/// Fallback behaviour for tool calls that do not match any explicit
/// Allow / Deny / Ask rule. Mirrors the values accepted by the Claude Code
/// CLI's <c>permissions.defaultMode</c> field.
/// </summary>
public enum PermissionDefaultMode
{
    /// <summary>Prompts on first use of each tool (standard behaviour).</summary>
    Default,

    /// <summary>Auto-accepts file edits; other tools still prompt.</summary>
    AcceptEdits,

    /// <summary>Read-only — no modifications or side effects.</summary>
    Plan,

    /// <summary>Auto-approves tool calls, with background safety checks.</summary>
    Auto,

    /// <summary>Auto-denies any tool not explicitly pre-approved in Allow.</summary>
    DontAsk,

    /// <summary>Skips all prompts. Use only in isolated / trusted environments.</summary>
    BypassPermissions,

    /// <summary>Coordination-only mode for agent team leads. Experimental.</summary>
    Delegate,
}