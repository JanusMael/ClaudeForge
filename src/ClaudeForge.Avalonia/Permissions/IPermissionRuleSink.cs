using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;

/// <summary>
/// Receives rules the guided builder produces. The host adapts this to its own
/// add path (in ClaudeForge, <c>PermissionsEditorViewModel.AddAllow/Deny/Ask</c>),
/// keeping the reusable builder unaware of how/where rules are persisted.
/// </summary>
/// <remarks>
/// Each add returns an optional <see cref="PermissionCollision"/> the host
/// detected while adding (a cross-bucket conflict or a same-bucket redundancy),
/// or <see langword="null"/> when the rule sits cleanly. The builder surfaces it
/// as a non-blocking note; the rule is added regardless.
/// </remarks>
public interface IPermissionRuleSink
{
    /// <summary>Append <paramref name="rule"/> to the Allow list at the editing scope.</summary>
    PermissionCollision? AddAllow(PermissionRule rule);

    /// <summary>Append <paramref name="rule"/> to the Deny list at the editing scope.</summary>
    PermissionCollision? AddDeny(PermissionRule rule);

    /// <summary>Append <paramref name="rule"/> to the Ask list at the editing scope.</summary>
    PermissionCollision? AddAsk(PermissionRule rule);
}
