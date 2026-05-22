using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;

/// <summary>
/// Strongly-typed accessor for the <c>mcpServers</c> block of Claude Code
/// settings. Servers are keyed by name within the workspace.
/// </summary>
public interface IMcpServersAccessor
{
    /// <summary>
    /// Snapshot of every configured MCP server, keyed by server name. The
    /// dictionary is materialized lazily (see SDK design doc §6.6) and
    /// frozen for the consumer's use.
    /// </summary>
    /// <remarks>
    /// Reads from the merged effective view across all loaded scopes. For the
    /// raw value at one specific scope (used by editor UIs that show "what's
    /// stored at THIS scope"), see <see cref="GetAt"/>.
    /// </remarks>
    IReadOnlyDictionary<string, McpServer> All { get; }

    /// <summary>
    /// Snapshot of MCP servers stored at the given <paramref name="scope"/>
    /// only (no effective merging). Used by GUI editors that bind to the
    /// per-scope view rather than the merged effective view.
    /// </summary>
    /// <remarks>Lazy materialization — same semantics as <see cref="All"/>.</remarks>
    IReadOnlyDictionary<string, McpServer> GetAt(ConfigScope scope);

    /// <summary>Returns the server registered as <paramref name="name"/>, or
    /// <see langword="null"/> when no entry exists at that key.</summary>
    McpServer? Get(string name);

    /// <summary>Insert or replace the server registered as <paramref name="name"/>.</summary>
    void Set(string name, McpServer server);

    /// <summary>Remove the server registered as <paramref name="name"/>. Returns
    /// <see langword="true"/> when an entry was removed.</summary>
    bool Remove(string name);

    /// <summary>Remove every MCP server at the default scope.</summary>
    void Clear();
}