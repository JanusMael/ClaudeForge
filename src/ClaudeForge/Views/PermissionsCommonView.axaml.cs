using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// "Common" permissions tab body — one-click taxonomy presets. Extracted from
/// PermissionsEditorView so it can be a top-level group tab (see
/// <see cref="ViewModels.ClaudeGroupTabCustomizer"/>). DataContext is the
/// <see cref="PermissionsEditorViewModel"/>, set by <c>GroupTabBodyTemplate</c>.
/// </summary>
public partial class PermissionsCommonView : UserControl
{
    public PermissionsCommonView()
    {
        InitializeComponent();
    }

    // Common Actions — Allow / Deny / Ask. Each button's DataContext is the
    // CommonActionItem for its row; we walk up to this UserControl to reach the
    // PermissionsEditorViewModel, then execute the matching command. Code-behind
    // (not a binding) because compiled bindings can't resolve the ancestor VM's
    // command without an IL2026 reflection-binding fallback.
    private void OnCommonAddAllow(object? sender, RoutedEventArgs e) =>
        HandleCommonAdd(sender, static vm => vm.AddToAllowCommand);

    private void OnCommonAddDeny(object? sender, RoutedEventArgs e) =>
        HandleCommonAdd(sender, static vm => vm.AddToDenyCommand);

    private void OnCommonAddAsk(object? sender, RoutedEventArgs e) =>
        HandleCommonAdd(sender, static vm => vm.AddToAskCommand);

    private static void HandleCommonAdd(
        object? sender,
        Func<PermissionsEditorViewModel, IRelayCommand<string>> getCmd)
    {
        if (sender is not Button { DataContext: CommonActionItem { Rule: var rule } } btn)
        {
            return;
        }

        PermissionsEditorViewModel? vm =
            btn.FindAncestorOfType<UserControl>()?.DataContext as PermissionsEditorViewModel;
        if (vm != null)
        {
            getCmd(vm).Execute(rule);
        }
    }
}
