using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Env;

/// <summary>
/// Strongly-typed accessor for the <c>env</c> map under Claude Code
/// settings.json.  Mirrors the
/// <see cref="Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.IPermissionsAccessor"/> shape:
/// generic dictionary surface for arbitrary keys plus typed convenience
/// properties for the well-known high-importance ones declared in
/// <see cref="EnvVarKey"/>.
/// <para>
/// <strong>Scope of this accessor:</strong> the persisted-config slice
/// only — the <c>settings.json</c> <c>env</c> object.  The OS-level
/// environment-variable surface (Windows registry HKCU/HKLM, the
/// user's shell profile on Linux/macOS) is owned by the existing
/// <c>EnvironmentEditorViewModel</c> + <c>IEnvironmentProvider</c> in
/// the GUI layer.  Keeping the SDK accessor focused on
/// <c>settings.json</c> means non-GUI consumers (CLI tools, future MCP
/// integration) can use it without dragging in Avalonia or the Windows
/// registry abstraction.  The Essentials page's env-var cards combine
/// both surfaces into one card UI; the SDK does not.
/// </para>
/// </summary>
/// <remarks>
/// Mutations write to the client's
/// <see cref="IClaudeConfigClient.DefaultScope"/> unless overridden via
/// the generic <see cref="IClaudeConfigClient.SetValue{T}(string, T, ConfigScope)"/>.
/// Setters that accept <see langword="null"/> remove the key entirely
/// (mirrors the <c>RemoveValue</c> semantics of typed properties on
/// <c>IPermissionsAccessor</c>).
/// </remarks>
public interface IEnvAccessor
{
    // ── Generic dictionary surface ────────────────────────────────────

    /// <summary>
    /// The string value stored at <c>env.<paramref name="varName"/></c>
    /// from the effective merged view across all loaded scopes, or
    /// <see langword="null"/> when unset.
    /// </summary>
    string? Get(string varName);

    /// <summary>
    /// The string value stored at <c>env.<paramref name="varName"/></c>
    /// at <paramref name="scope"/> only (no effective merging), or
    /// <see langword="null"/> when the scope has no explicit value.
    /// </summary>
    string? GetAt(string varName, ConfigScope scope);

    /// <summary>
    /// Set <c>env.<paramref name="varName"/></c> at the default scope.
    /// Pass <see langword="null"/> or empty string to remove the key.
    /// </summary>
    void Set(string varName, string? value);

    /// <summary>
    /// Set <c>env.<paramref name="varName"/></c> at <paramref name="scope"/>.
    /// Pass <see langword="null"/> or empty string to remove the key.
    /// </summary>
    void SetAt(string varName, string? value, ConfigScope scope);

    /// <summary>
    /// Snapshot of every <c>env.*</c> key from the effective merged
    /// view.  Returns an empty dictionary when the <c>env</c> object is
    /// absent.  Lazy materialization — same semantics as the
    /// list-returning accessors on <c>IPermissionsAccessor</c>.
    /// </summary>
    IReadOnlyDictionary<string, string> All { get; }

    /// <summary>
    /// Snapshot of every <c>env.*</c> key stored at <paramref name="scope"/>
    /// only (no effective merging).
    /// </summary>
    IReadOnlyDictionary<string, string> AllAt(ConfigScope scope);

    // ── Typed convenience properties for well-known keys ──────────────
    //
    // Each pair (Get / Set) maps to a constant in EnvVarKey.  Setters
    // accept null to mean "remove the key from the env map".  Getters
    // parse leniently — invalid stored values (e.g. the user typed
    // "abc" into MAX_THINKING_TOKENS) return null rather than throwing,
    // matching the rest of the SDK's "best-effort read, strict write"
    // posture.

    /// <summary>
    /// <see cref="EnvVarKey.MaxThinkingTokens"/> as a parsed
    /// <see cref="int"/>.  <see langword="null"/> when unset OR when
    /// the stored string is not a valid base-10 integer.
    /// </summary>
    int? MaxThinkingTokens { get; set; }

    /// <summary>
    /// <see cref="EnvVarKey.MaxOutputTokens"/> as a parsed
    /// <see cref="int"/>.  <see langword="null"/> when unset OR when
    /// the stored string is not a valid base-10 integer.
    /// </summary>
    int? MaxOutputTokens { get; set; }

    /// <summary>
    /// <see cref="EnvVarKey.DisableAutoMemory"/> parsed as a tri-state
    /// boolean.  Claude Code's convention: stored as <c>"1"</c> (true)
    /// or <c>"0"</c> (false), absent for "not set / inherit".  Other
    /// stored values yield <see langword="null"/>.
    /// </summary>
    bool? DisableAutoMemory { get; set; }

    /// <summary>
    /// <see cref="EnvVarKey.DisableAutoUpdater"/> parsed as tri-state
    /// boolean.  Same convention as <see cref="DisableAutoMemory"/>.
    /// </summary>
    bool? DisableAutoUpdater { get; set; }

    /// <summary>
    /// <see cref="EnvVarKey.AnthropicModel"/> as a free-form string.
    /// </summary>
    string? AnthropicModel { get; set; }
}