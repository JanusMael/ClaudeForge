namespace Bennewitz.Ninja.OpenCode.Sdk.Mcp;

/// <summary>
/// Which arm of the <c>mcp</c> union one server entry is.
/// </summary>
public enum OpenCodeMcpKind
{
    /// <summary>
    /// The entry could not be classified, and is held verbatim rather than interpreted.
    /// Deliberately the zero value: an unclassified entry is what an uninitialised field should
    /// mean, and it is the one state that never invents a shape for the user.
    /// </summary>
    Unrecognised,

    /// <summary><c>"type": "local"</c> — a process this machine runs.</summary>
    Local,

    /// <summary><c>"type": "remote"</c> — an HTTP endpoint.</summary>
    Remote,

    /// <summary>
    /// A bare <c>{ "enabled": bool }</c> with no <c>type</c>: toggles a server another scope
    /// declares, without restating it.
    /// </summary>
    EnabledOverride,
}

/// <summary>
/// OAuth settings for a remote server.
/// </summary>
/// <param name="ClientId">OAuth client id; absent means dynamic registration is attempted.</param>
/// <param name="ClientSecret">Client secret, when the authorization server requires one.</param>
/// <param name="Scope">Scopes to request.</param>
/// <param name="CallbackPort">Local callback port; shorthand for <paramref name="RedirectUri"/>.</param>
/// <param name="RedirectUri">Full redirect URI; takes precedence over the port.</param>
public sealed record OpenCodeMcpOAuth(
    string? ClientId = null,
    string? ClientSecret = null,
    string? Scope = null,
    long? CallbackPort = null,
    string? RedirectUri = null);

/// <summary>
/// One entry of the <c>mcp</c> map, as the file states it.
/// </summary>
/// <remarks>
/// <para>
/// One record covering every arm rather than a type per arm, because <see cref="Extras"/> and
/// <see cref="Raw"/> apply to all of them and the editor needs to hold two arms at once anyway —
/// flipping a server local↔remote must not destroy the fields the other arm did not use.
/// </para>
/// <para>
/// ⚠ <b>Every collection here preserves file order.</b> Neither <c>environment</c> nor
/// <c>headers</c> has order-dependent meaning the way a permission map does, so this is about
/// diffs rather than semantics: rewriting a user's config with their environment variables
/// reshuffled turns a one-line change into an unreviewable one.
/// </para>
/// </remarks>
public sealed record OpenCodeMcpServer
{
    /// <summary>Which arm of the union this is.</summary>
    public OpenCodeMcpKind Kind { get; init; } = OpenCodeMcpKind.Unrecognised;

    /// <summary>Command and arguments, for <see cref="OpenCodeMcpKind.Local"/>.</summary>
    public IReadOnlyList<string> Command { get; init; } = [];

    /// <summary>Working directory, for <see cref="OpenCodeMcpKind.Local"/>.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Environment variables, in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Environment { get; init; } = [];

    /// <summary>Endpoint, for <see cref="OpenCodeMcpKind.Remote"/>.</summary>
    public string? Url { get; init; }

    /// <summary>Request headers, in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>OAuth settings, when <c>oauth</c> was an object.</summary>
    public OpenCodeMcpOAuth? OAuth { get; init; }

    /// <summary>
    /// True when <c>oauth</c> was the literal <c>false</c> — auto-detection off.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="OAuth"/> being <see langword="null"/>, which means the key was
    /// absent and auto-detection applies. The schema permits only <c>false</c>, never
    /// <c>true</c>, so this cannot be folded into a nullable bool without inventing a third state.
    /// </remarks>
    public bool OAuthDisabled { get; init; }

    /// <summary>Whether the server starts, or <see langword="null"/> when unstated.</summary>
    public bool? Enabled { get; init; }

    /// <summary>Request timeout in milliseconds, or <see langword="null"/> when unstated.</summary>
    public long? TimeoutMs { get; init; }

    /// <summary>
    /// Fields this model does not surface, kept so a round trip does not delete them.
    /// </summary>
    /// <remarks>
    /// The schema says <c>additionalProperties: false</c>, so in theory this is always empty. In
    /// practice it is the difference between an editor that tolerates a config written by a newer
    /// OpenCode and one that quietly strips whatever it has not heard of.
    /// </remarks>
    public IReadOnlyList<KeyValuePair<string, object?>> Extras { get; init; } = [];

    /// <summary>
    /// The entry exactly as read, set only when <see cref="Kind"/> is
    /// <see cref="OpenCodeMcpKind.Unrecognised"/>.
    /// </summary>
    public object? Raw { get; init; }
}
