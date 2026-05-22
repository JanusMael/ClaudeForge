using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class EnabledPluginsEditorView : UserControl
{
    public EnabledPluginsEditorView()
    {
        InitializeComponent();
    }

    // Code-behind handler for the ItemsControl remove button.
    // {ReflectionBinding $parent[ItemsControl].DataContext.Command} cannot be compiled
    // because the parent DataContext type is not known to the AXAML compiler at the
    // DataTemplate level, so this event handler provides a trim-safe alternative.

    private void OnRemovePlugin(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ItemsControl>() is { DataContext: EnabledPluginsEditorViewModel vm })
        {
            vm.RemovePluginCommand.Execute(btn.DataContext);
        }
    }
}