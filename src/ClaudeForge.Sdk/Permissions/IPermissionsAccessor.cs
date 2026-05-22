using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;

/// <summary>
/// Strongly-typed accessor for the <c>permissions</c> block of Claude Code
/// settings. Mutations write to the client's
/// <see cref="IClaudeConfigClient.DefaultScope"/> unless overridden via the
/// generic <see cref="IClaudeConfigClient.SetValue{T}(string, T, ConfigScope)"/>
/// escape hatch.
/// </summary>
/// <remarks>
/// All <see cref="IReadOnlyList{T}"/> returns are lazy snapshots — see the
/// SDK design doc §6.6 for the materialization semantics.
/// </remarks>
public interface IPermissionsAccessor
{
    /// <summary>
    /// The <c>defaultMode</c> setting from the effective merged view, or
    /// <see langword="null"/> when unset across every loaded scope. Setter
    /// targets <see cref="IClaudeConfigClient.DefaultScope"/>.
    /// </summary>
    /// <remarks>For the value at one specific scope, see <see cref="GetDefaultModeAt"/>.</remarks>
    PermissionDefaultMode? DefaultMode { get; set; }

    /// <summary>
    /// Returns the <c>defaultMode</c> value stored at <paramref name="scope"/>
    /// only (no effective merging), or <see langword="null"/> when the scope
    /// has no explicit value.
    /// </summary>
    PermissionDefaultMode? GetDefaultModeAt(ConfigScope scope);

    /// <summary>Allowed permission rules from the effective merged view.</summary>
    /// <remarks>For the per-scope view, see <see cref="AllowAt"/>.</remarks>
    IReadOnlyList<PermissionRule> Allow { get; }

    /// <summary>Denied permission rules from the effective merged view.</summary>
    /// <remarks>For the per-scope view, see <see cref="DenyAt"/>.</remarks>
    IReadOnlyList<PermissionRule> Deny { get; }

    /// <summary>Permission rules that prompt the user, from the effective view.</summary>
    /// <remarks>For the per-scope view, see <see cref="AskAt"/>.</remarks>
    IReadOnlyList<PermissionRule> Ask { get; }

    /// <summary>
    /// Allowed permission rules stored at <paramref name="scope"/> only
    /// (no effective merging). Used by GUI editors that bind to the per-scope
    /// view rather than the merged effective view. Lazy materialization.
    /// </summary>
    IReadOnlyList<PermissionRule> AllowAt(ConfigScope scope);

    /// <summary>Denied permission rules at the given scope (no merging).</summary>
    /// <remarks>Lazy materialization — same semantics as <see cref="AllowAt"/>.</remarks>
    IReadOnlyList<PermissionRule> DenyAt(ConfigScope scope);

    /// <summary>Ask permission rules at the given scope (no merging).</summary>
    /// <remarks>Lazy materialization — same semantics as <see cref="AllowAt"/>.</remarks>
    IReadOnlyList<PermissionRule> AskAt(ConfigScope scope);

    /// <summary>Append <paramref name="rule"/> to the Allow list.
    /// No-op when the rule is already present.</summary>
    void AddAllow(PermissionRule rule);

    /// <summary>Append <paramref name="rule"/> to the Deny list.
    /// No-op when the rule is already present.</summary>
    void AddDeny(PermissionRule rule);

    /// <summary>Append <paramref name="rule"/> to the Ask list.
    /// No-op when the rule is already present.</summary>
    void AddAsk(PermissionRule rule);

    /// <summary>
    /// Remove <paramref name="rule"/> from the Allow list. Returns
    /// <see langword="true"/> when an entry was removed.
    /// </summary>
    bool RemoveAllow(PermissionRule rule);

    /// <inheritdoc cref="RemoveAllow"/>
    bool RemoveDeny(PermissionRule rule);

    /// <inheritdoc cref="RemoveAllow"/>
    bool RemoveAsk(PermissionRule rule);

    /// <summary>Remove all permissions at the default scope.</summary>
    void Clear();

    /// <summary>
    /// Additional directories the permission scope extends into, from the
    /// effective merged view. Maps to the schema's
    /// <c>permissions.additionalDirectories</c> array — non-empty path
    /// strings the user wants Claude to consider in scope. Returns an empty
    /// list when unset.
    /// </summary>
    /// <remarks>
    /// promoted from opaque preservation (the editor's
    /// <c>_preservedFields</c> stash) to a typed property.
    /// For the per-scope view see <see cref="AdditionalDirectoriesAt"/>.
    /// </remarks>
    IReadOnlyList<string> AdditionalDirectories { get; }

    /// <summary>
    /// Additional directories stored at <paramref name="scope"/> only
    /// (no effective merging).
    /// </summary>
    IReadOnlyList<string> AdditionalDirectoriesAt(ConfigScope scope);

    /// <summary>
    /// Append <paramref name="path"/> to the additional-directories list at
    /// the default scope. No-op when the path is already present.
    /// </summary>
    void AddAdditionalDirectory(string path);

    /// <summary>
    /// Remove <paramref name="path"/> from the additional-directories list at
    /// the default scope. Returns <see langword="true"/> when an entry was
    /// removed.
    /// </summary>
    bool RemoveAdditionalDirectory(string path);

    /// <summary>
    /// Maps to the schema's <c>permissions.disableBypassPermissionsMode</c>
    /// boolean. <see langword="true"/> when the org has disabled the
    /// <c>bypassPermissions</c> default-mode option for this user / scope.
    /// <see langword="null"/> when unset across every loaded scope.
    /// Setter targets <see cref="IClaudeConfigClient.DefaultScope"/>; pass
    /// <see langword="null"/> to remove the override.
    /// </summary>
    /// <remarks>
    /// Most commonly set in
    /// Managed scope by org policy (the matching <c>defaultMode = bypassPermissions</c>
    /// is then prevented from taking effect). For per-scope inspection see
    /// <see cref="GetDisableBypassPermissionsModeAt"/>.
    /// </remarks>
    bool? DisableBypassPermissionsMode { get; set; }

    /// <summary>
    /// Returns the <c>disableBypassPermissionsMode</c> value stored at
    /// <paramref name="scope"/> only (no effective merging), or
    /// <see langword="null"/> when the scope has no explicit value.
    /// </summary>
    bool? GetDisableBypassPermissionsModeAt(ConfigScope scope);
}