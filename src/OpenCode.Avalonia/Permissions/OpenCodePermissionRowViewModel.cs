using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Permissions;

/// <summary>
/// One editable <c>pattern → action</c> rule inside a tool's rule list.
/// </summary>
/// <remarks>
/// <para>
/// A row knows its own position because position is meaning here: the last matching rule wins,
/// so "move down" strengthens a rule and "move up" weakens it. <see cref="IsShadowed"/> is set
/// by the owning editor after every change, because whether a rule can ever fire depends on the
/// rules below it rather than on anything the row itself holds.
/// </para>
/// <para>
/// ⚠ <b>The reorder and remove commands live here, on the row, deliberately.</b> The obvious
/// alternative — one set of commands on the list and per-row buttons reaching them with
/// <c>{Binding $parent[ItemsControl].DataContext.…}</c> — is an ancestor binding, which resolves
/// by reflection and trips <c>IL2026</c>. Under this repo's warnings-as-errors that is a build
/// failure, not a trim warning. Each command forwards to <see cref="Owner"/>, which is the only
/// object that can actually reorder the list.
/// </para>
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

    /// <summary>
    /// The rule list this row belongs to, or <see langword="null"/> for a detached row.
    /// </summary>
    /// <remarks>
    /// Optional so a row is still constructible on its own — the load path builds rows before the
    /// tool entry exists, and tests build them with no list at all. The commands are no-ops while
    /// it is null rather than throwing, because a detached row has nothing to reorder within.
    /// </remarks>
    internal OpenCodePermissionToolViewModel? Owner { get; set; }

    /// <summary>Remove this rule from its list.</summary>
    [RelayCommand]
    private void Remove() => Owner?.RemoveRule(this);

    /// <summary>Weaken this rule by moving it earlier — earlier rules lose to later ones.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Owner?.MoveRule(this, -1);

    /// <summary>Strengthen this rule by moving it later — the last match wins.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Owner?.MoveRule(this, +1);

    private bool CanMoveUp() => Owner is { } owner && owner.Rules.IndexOf(this) > 0;

    private bool CanMoveDown() =>
        Owner is { } owner
        && owner.Rules.IndexOf(this) is var i
        && i >= 0
        && i < owner.Rules.Count - 1;

    /// <summary>
    /// Re-evaluate the reorder commands after the list changed shape around this row.
    /// </summary>
    /// <remarks>
    /// Their <c>CanExecute</c> depends on this row's <i>index</i>, which nothing on this object
    /// notifies about — inserting a row above changes whether the row below may move up without
    /// touching either row's properties. The owner calls this on every row after any structural
    /// change; without it the first and last rows keep stale enabled/disabled buttons.
    /// </remarks>
    internal void RefreshMoveCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}
