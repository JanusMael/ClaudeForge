using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

namespace Bennewitz.Ninja.ClaudeForge.Views;

public partial class MarketplacesEditorView : UserControl
{
    public MarketplacesEditorView()
    {
        InitializeComponent();
    }

    // Code-behind handler for the marketplace remove button.
    // {Binding $parent[ItemsControl].DataContext.RemoveMarketplaceCommand} cannot be
    // compiled because DataContext is typed as object? at the AXAML compiler level,
    // so this event handler provides a trim-safe alternative.
    private void OnRemoveMarketplace(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ItemsControl>() is { DataContext: MarketplacesEditorViewModel vm })
        {
            vm.RemoveMarketplaceCommand.Execute(btn.DataContext as MarketplaceEntry);
        }
    }
}