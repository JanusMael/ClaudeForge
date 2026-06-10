namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Describes a single action Claude is about to take — the tool plus its typed
/// payload — for evaluation against permission rules by
/// <see cref="PermissionResolver"/>. This is the "what Claude wants to do" half
/// of a match; <see cref="ParsedPermissionRule"/> is the "what's allowed" half.
/// </summary>
/// <remarks>
/// Construct via the per-tool factories rather than the constructor so each call
/// site reads as the tool it models. The shape mirrors the tool families in the
/// Claude Code permissions spec
/// (<see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>).
/// </remarks>
public sealed record PermissionCandidate
{
    private PermissionCandidate(
        string toolName,
        string? commandText = null,
        string? path = null,
        string? url = null,
        bool isMcp = false,
        string? mcpServer = null,
        string? mcpTool = null,
        string? agentName = null)
    {
        ToolName = toolName;
        CommandText = commandText;
        Path = path;
        Url = url;
        IsMcp = isMcp;
        McpServer = mcpServer;
        McpTool = mcpTool;
        AgentName = agentName;
    }

    /// <summary>Tool name (<c>"Bash"</c>, <c>"Read"</c>, …); empty for MCP candidates.</summary>
    public string ToolName { get; }

    /// <summary>The shell command, for <c>Bash</c> / <c>PowerShell</c> candidates.</summary>
    public string? CommandText { get; }

    /// <summary>The file path, for <c>Read</c> / <c>Edit</c> / <c>Write</c> candidates.</summary>
    public string? Path { get; }

    /// <summary>The request URL, for <c>WebFetch</c> candidates.</summary>
    public string? Url { get; }

    /// <summary><see langword="true"/> for an MCP tool call.</summary>
    public bool IsMcp { get; }

    /// <summary>MCP server name, for MCP candidates.</summary>
    public string? McpServer { get; }

    /// <summary>MCP tool name, for MCP candidates.</summary>
    public string? McpTool { get; }

    /// <summary>Subagent name, for <c>Agent</c> candidates.</summary>
    public string? AgentName { get; }

    /// <summary>A Bash command Claude wants to run.</summary>
    public static PermissionCandidate Bash(string command) =>
        new("Bash", commandText: command);

    /// <summary>A PowerShell command Claude wants to run.</summary>
    public static PermissionCandidate PowerShell(string command) =>
        new("PowerShell", commandText: command);

    /// <summary>A file read.</summary>
    public static PermissionCandidate Read(string path) => new("Read", path: path);

    /// <summary>A file edit.</summary>
    public static PermissionCandidate Edit(string path) => new("Edit", path: path);

    /// <summary>A file write.</summary>
    public static PermissionCandidate Write(string path) => new("Write", path: path);

    /// <summary>A web fetch of <paramref name="url"/>.</summary>
    public static PermissionCandidate WebFetch(string url) => new("WebFetch", url: url);

    /// <summary>An MCP tool call. Pass <paramref name="tool"/> = null for a server-level probe.</summary>
    public static PermissionCandidate Mcp(string server, string? tool) =>
        new(string.Empty, isMcp: true, mcpServer: server, mcpTool: tool);

    /// <summary>A subagent invocation.</summary>
    public static PermissionCandidate Agent(string name) => new("Agent", agentName: name);

    /// <summary>
    /// A specifier-less tool use (Grep, Glob, WebSearch, TodoWrite, …) where the
    /// decision turns only on the tool name.
    /// </summary>
    public static PermissionCandidate Tool(string toolName) => new(toolName);
}
