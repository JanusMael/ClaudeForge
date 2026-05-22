using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.McpServers;

/// <summary>
/// One MCP server entry. The shape varies by transport: <c>Stdio</c> uses
/// <see cref="Command"/> / <see cref="Args"/> / <see cref="Env"/>;
/// <c>Sse</c> and <c>StreamableHttp</c> use <see cref="Url"/> /
/// <see cref="Headers"/>.
/// </summary>
/// <param name="Name">Unique server name within the workspace.</param>
/// <param name="Transport">Transport discriminator.</param>
/// <param name="Command">Executable path (Stdio transport only).</param>
/// <param name="Args">Command-line arguments (Stdio transport only).</param>
/// <param name="Env">Environment variables (Stdio transport only).</param>
/// <param name="Url">Server URL (Sse / StreamableHttp transports only).</param>
/// <param name="Headers">HTTP headers (Sse / StreamableHttp transports only).</param>
/// <param name="Description">
/// Free-form description shown alongside the server in tooling. Common in
/// plugin-managed marketplaces (e.g. <c>everything-claude-code</c> populates
/// this on every server it ships). 2026-05-01 promoted from
/// <c>PreservedFields</c> to a typed property — read/write programmatically
/// via this property; the typed value takes precedence over any colliding
/// <c>"description"</c> key in <see cref="PreservedFields"/>.
/// </param>
public sealed record McpServer(
    string Name,
    McpTransport Transport,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string>? Env = null,
    string? Url = null,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? Description = null)
{
    /// <summary>
    /// JSON fields the SDK does not currently model (e.g. <c>description</c>).
    /// Captured verbatim from the on-disk JsonObject during read and emitted
    /// back unchanged during write so round-trips do not drop user data.
    /// see <c>McpServersAccessor</c>.
    /// </summary>
    /// <remarks>
    /// Marked <c>internal</c> to keep <see cref="JsonObject"/> out of the
    /// public SDK surface (locked by <c>NoPublicApi_LeaksSystemTextJsonNodes</c>).
    /// The GUI editor assembly accesses this via <c>InternalsVisibleTo</c>
    /// when constructing an updated server in response to user edits, so the
    /// preserved fields survive an edit-then-Set cycle.
    /// </remarks>
    internal JsonObject? PreservedFields { get; init; }
}