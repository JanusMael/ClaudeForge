using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// "Lists" permissions tab body — Allow / Deny / Ask rule lists + manual-add
/// inputs. Extracted from PermissionsEditorView. DataContext is the
/// <see cref="PermissionsEditorViewModel"/>, set by <c>GroupTabBodyTemplate</c>.
/// </summary>
public partial class PermissionsListsView : UserControl
{
    public PermissionsListsView()
    {
        InitializeComponent();
    }

    // Rule remove buttons. Code-behind (not a binding) because compiled bindings
    // cannot resolve {Binding $parent[ItemsControl].DataContext.RemoveXxxCommand}
    // without an IL2026 reflection-binding fallback. The button's ancestor
    // ItemsControl carries the PermissionsEditorViewModel as DataContext.
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
}
