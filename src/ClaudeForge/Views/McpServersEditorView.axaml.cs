using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class McpServersEditorView : UserControl
{
    public McpServersEditorView()
    {
        InitializeComponent();
    }

    // Code-behind handlers for remove buttons.
    // {Binding $parent[X].DataContext.Command} cannot be compiled because DataContext
    // is typed as object? at the AXAML compiler level, so these event handlers provide
    // a trim-safe alternative.

    private void OnRemoveServer(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ListBox>() is { DataContext: McpServersEditorViewModel vm })
        {
            vm.RemoveServerCommand.Execute(btn.DataContext as McpServerEntry);
        }
    }

    private void OnRemoveArg(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<DataGrid>() is { DataContext: McpServerEntry entry })
        {
            entry.RemoveArgCommand.Execute(btn.DataContext);
        }
    }

    private void OnRemoveEnv(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<DataGrid>() is { DataContext: McpServerEntry entry })
        {
            entry.RemoveEnvCommand.Execute(btn.DataContext);
        }
    }
}