namespace Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;

/// <summary>
/// Transport discriminator for an MCP server registration.
/// </summary>
public enum McpTransport
{
    /// <summary>Local stdio transport — <see cref="McpServer.Command"/> spawns a child process.</summary>
    Stdio,

    /// <summary>Server-sent events transport over <see cref="McpServer.Url"/>.</summary>
    Sse,

    /// <summary>Streamable HTTP transport over <see cref="McpServer.Url"/>.</summary>
    StreamableHttp,
}