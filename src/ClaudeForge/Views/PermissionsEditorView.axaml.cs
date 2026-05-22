using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class PermissionsEditorView : UserControl
{
    public PermissionsEditorView()
    {
        InitializeComponent();
    }

    // -----------------------------------------------------------------------
    // Rule remove buttons
    // Code-behind handlers are required because compiled bindings cannot resolve
    // {Binding $parent[ItemsControl].DataContext.RemoveXxxCommand} — DataContext
    // is typed as object? at the AXAML compiler level, producing a fallback
    // reflection binding with IL2026 trim warnings.
    // -----------------------------------------------------------------------

    private void OnRemoveAllow(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ItemsControl>() is { DataContext: PermissionsEditorViewModel vm })
        {
            vm.RemoveAllowCommand.Execute(btn.DataContext as PermissionRuleViewModel);
        }
    }

    private void OnRemoveDeny(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ItemsControl>() is { DataContext: PermissionsEditorViewModel vm })
        {
            vm.RemoveDenyCommand.Execute(btn.DataContext as PermissionRuleViewModel);
        }
    }

    private void OnRemoveAsk(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ItemsControl>() is { DataContext: PermissionsEditorViewModel vm })
        {
            vm.RemoveAskCommand.Execute(btn.DataContext as PermissionRuleViewModel);
        }
    }

    // -----------------------------------------------------------------------
    // Common Actions — Allow / Deny / Ask buttons
    // Each button's DataContext is the CommonActionItem for its row (set by the
    // inner DataTemplate).  We walk up the visual tree to the UserControl to
    // reach PermissionsEditorViewModel, then execute the appropriate command.
    // The same visual-tree approach is used for the Remove buttons above to keep
    // the code-behind consistent and trim-safe.
    // -----------------------------------------------------------------------

    private void OnCommonAddAllow(object? sender, RoutedEventArgs e)
    {
        HandleCommonAdd(sender, static vm => vm.AddToAllowCommand);
    }

    private void OnCommonAddDeny(object? sender, RoutedEventArgs e)
    {
        HandleCommonAdd(sender, static vm => vm.AddToDenyCommand);
    }

    private void OnCommonAddAsk(object? sender, RoutedEventArgs e)
    {
        HandleCommonAdd(sender, static vm => vm.AddToAskCommand);
    }

    private static void HandleCommonAdd(
        object? sender,
        Func<PermissionsEditorViewModel, IRelayCommand<string>> getCmd)
    {
        if (sender is not Button { DataContext: CommonActionItem { Rule: var rule } } btn)
        {
            return;
        }

        PermissionsEditorViewModel? vm = btn.FindAncestorOfType<UserControl>()?.DataContext as PermissionsEditorViewModel;
        if (vm != null)
        {
            getCmd(vm).Execute(rule);
        }
    }
}