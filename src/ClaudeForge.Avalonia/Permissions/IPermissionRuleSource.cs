using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;

/// <summary>
/// Supplies the permission rules the dry-run tester evaluates. The host adapts
/// its own data source (in ClaudeForge, the SDK <c>IPermissionsAccessor</c> plus
/// the editor's in-memory unsaved edits) to this small contract so the reusable
/// tester stays decoupled from any one config client.
/// </summary>
public interface IPermissionRuleSource
{
    /// <summary>
    /// The scope the user is currently editing. Used as the single-scope view's
    /// target and to overlay unsaved edits onto the merged view.
    /// </summary>
    ConfigScope EditingScope { get; }

    /// <summary>
    /// The effective <c>defaultMode</c> that applies when no rule matches.
    /// </summary>
    PermissionDefaultMode? DefaultMode { get; }

    /// <summary>
    /// The allow/deny/ask rules for the editing scope, reflecting any unsaved
    /// in-memory edits so the tester mirrors what the user is currently building.
    /// </summary>
    ScopedPermissionRules GetEditingScopeRules();

    /// <summary>
    /// All scopes' rule lists for the merged view, ordered by precedence or not
    /// (the resolver re-orders). The editing scope's entry should reflect unsaved
    /// edits; other scopes reflect persisted state.
    /// </summary>
    IReadOnlyList<ScopedPermissionRules> GetAllScopeRules();
}
