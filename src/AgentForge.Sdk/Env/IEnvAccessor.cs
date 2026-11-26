using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Sdk.Env;

/// <summary>
/// Accessor for the <c>env</c> map under settings.json: a generic dictionary
/// surface for arbitrary keys.
/// <para>
/// ⓘ <b>Product-neutral since plans/00008 step 10.</b> The typed convenience
/// properties for a product's well-known keys are that product's: Claude Code's
/// (<c>MaxThinkingTokens</c> and the rest) are extension properties in
/// <c>ClaudeForge.Sdk.Claude</c>, over this same surface.
/// </para>
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
/// <see cref="IAgentConfigClient.DefaultScope"/> unless overridden via
/// the generic <see cref="IAgentConfigClient.SetValue{T}(string, T, ConfigScope)"/>.
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
}