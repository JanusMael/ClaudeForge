using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class HooksEditorView : UserControl
{
    public HooksEditorView()
    {
        InitializeComponent();
    }

    // Code-behind handler for the DataGrid remove button.
    // {ReflectionBinding $parent[DataGrid].DataContext.Command} cannot be compiled
    // because the parent DataContext type is not known to the AXAML compiler at the
    // DataTemplate level, so this event handler provides a trim-safe alternative.

    private void OnRemoveHook(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<DataGrid>() is { DataContext: HookEventGroup group })
        {
            group.RemoveHookCommand.Execute(btn.DataContext as HookEntry);
        }
    }
}