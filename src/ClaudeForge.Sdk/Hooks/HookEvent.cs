using System.Text.Json.Nodes;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;

/// <summary>
/// A single hook registration: which event it fires on, which tool name it
/// matches, and what to run when it fires.
/// </summary>
/// <param name="EventName">
/// Hook lifecycle event — e.g. <c>"PreToolUse"</c>, <c>"PostToolUse"</c>,
/// <c>"Stop"</c>. Mirrors the keys accepted by the Claude Code CLI.
/// </param>
/// <param name="Matcher">
/// Tool name to match (e.g. <c>"Bash"</c>, <c>"Edit"</c>) or <c>"*"</c> to
/// match every tool.
/// </param>
/// <param name="CommandType">
/// Discriminator for how <see cref="CommandValue"/> is interpreted.
/// </param>
/// <param name="CommandValue">
/// The shell command, prompt text, or URL whose meaning is determined by
/// <see cref="CommandType"/>.
/// </param>
public sealed record HookEvent(
    string EventName,
    string Matcher,
    HookCommandType CommandType,
    string CommandValue)
{
    /// <summary>
    /// Optional per-hook timeout in seconds. Maps to the schema's
    /// <c>hookCommand.timeout</c> field. <see langword="null"/> when unset
    /// (CLI applies its own default).
    /// </summary>
    /// <remarks>
    /// promoted from
    /// <see cref="PreservedFields"/>).
    /// </remarks>
    public int? Timeout { get; init; }

    /// <summary>
    /// HTTP request headers for <see cref="HookCommandType.Url"/>-typed hooks.
    /// Maps to the schema's <c>hookCommand.headers</c> object on the
    /// http variant. <see langword="null"/> when unset; the SDK does not
    /// auto-default.
    /// </summary>
    /// <remarks>
    /// Required to make
    /// http-typed hooks useful (Authorization header, custom auth tokens).
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Environment variable names the hook is allowed to interpolate into
    /// its <see cref="Headers"/> values via <c>${VARNAME}</c> syntax. Maps
    /// to the schema's <c>hookCommand.allowedEnvVars</c> array on the
    /// http variant. <see langword="null"/> when unset.
    /// </summary>
    /// <remarks>
    /// Required for header
    /// interpolation against user-managed secrets.
    /// </remarks>
    public IReadOnlyList<string>? AllowedEnvVars { get; init; }

    /// <summary>
    /// Hook-entry sub-fields the SDK does not currently model
    /// (e.g. <c>async</c>, <c>statusMessage</c>, <c>model</c>, plus any
    /// future schema additions). The Claude Code schema's
    /// <c>$defs.hookCommand</c> defines several per-variant fields;
    /// preserved verbatim here so save round-trips don't silently drop
    /// them. Mirrors <c>McpServer.PreservedFields</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marked <c>internal</c> to keep <see cref="JsonObject"/> out of the
    /// public SDK surface. The GUI editor accesses this via
    /// <c>InternalsVisibleTo</c> when projecting an SDK record into its
    /// editor-side <c>HookEntry</c>.
    /// </para>
    /// <para>
    /// <c>timeout</c>, <c>headers</c>, and
    /// <c>allowedEnvVars</c> were promoted to typed properties (Stop B) and
    /// no longer appear in this bag. The bag now only carries fields the
    /// SDK genuinely doesn't model.
    /// </para>
    /// </remarks>
    internal JsonObject? PreservedFields { get; init; }

    /// <summary>
    /// Verbatim copy of the original inner JsonObject
    /// when the SDK does not recognize the hook's <c>type</c> discriminator
    /// (e.g. <c>"agent"</c>, <c>"http"</c>, future schema additions).
    /// When non-null, <see cref="HooksAccessor.Add"/> emits this object
    /// verbatim, bypassing typed serialisation — the typed fields
    /// (<see cref="Matcher"/>, <see cref="CommandType"/>,
    /// <see cref="CommandValue"/>, <see cref="Headers"/>,
    /// <see cref="AllowedEnvVars"/>, <see cref="Timeout"/>,
    /// <see cref="PreservedFields"/>) are populated with best-effort
    /// values but should NOT be relied upon for opaque hooks.
    /// </summary>
    /// <remarks>
    /// Mirrors the editor-side <c>HookEntry._opaqueJson</c> data-loss-
    /// prevention contract added 2026-04-29.  Prior to this field, the
    /// SDK-backed reload path (<see cref="HooksAccessor.MaterializeFrom"/>
    /// → <see cref="HooksAccessor.ParseCommandType"/> → fallback to
    /// <see cref="HookCommandType.Command"/>) silently downcasted unknown
    /// types, losing the type discriminator on save.
    /// PreservedFields partially mitigated by capturing unknown sub-keys,
    /// but the type itself was lost.  H-5b closed this by preserving the
    /// original inner JsonObject end-to-end.
    /// </remarks>
    internal JsonObject? OpaqueInnerJson { get; init; }
}