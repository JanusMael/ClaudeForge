using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Permissions;

/// <summary>
/// One editable <c>pattern → action</c> rule inside a tool's rule list.
/// </summary>
/// <remarks>
/// A row knows its own position because position is meaning here: the last matching rule wins,
/// so "move down" strengthens a rule and "move up" weakens it. <see cref="IsShadowed"/> is set
/// by the owning editor after every change, because whether a rule can ever fire depends on the
/// rules below it rather than on anything the row itself holds.
/// </remarks>
public sealed partial class OpenCodePermissionRowViewModel : ObservableObject
{
    /// <summary>The actions a rule may take, for binding a selector.</summary>
    public static IReadOnlyList<PermissionOutcome> Actions { get; } =
        [PermissionOutcome.Allow, PermissionOutcome.Ask, PermissionOutcome.Deny];

    /// <summary>The glob, exactly as written in the file.</summary>
    [ObservableProperty] private string _pattern = string.Empty;

    /// <summary>Allow, ask or deny.</summary>
    [ObservableProperty] private PermissionOutcome _action = PermissionOutcome.Ask;

    /// <summary>
    /// True when a later rule matches everything this one does, so this rule can never fire.
    /// </summary>
    /// <remarks>
    /// Worth surfacing rather than silently tolerating: the common way to reach this state is
    /// not a typo but a merge. A narrow <c>deny</c> in a lower-priority file lands before a
    /// broad rule from a higher one, and the deny quietly stops applying with no edit to either
    /// file.
    /// </remarks>
    [ObservableProperty] private bool _isShadowed;

    /// <summary>Why this row is shadowed, for a tooltip. Empty when it is not.</summary>
    [ObservableProperty] private string _shadowReason = string.Empty;
}
