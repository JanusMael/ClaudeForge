using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Controls;

public partial class PropertyEditorWrapper : UserControl
{
    public PropertyEditorWrapper()
    {
        InitializeComponent();
    }

    // Code-behind handler for the string-array item remove button.
    // {Binding $parent[ItemsControl].DataContext.RemoveItemCommand} cannot be compiled
    // because the parent DataContext type is not known to the AXAML compiler inside an
    // x:DataType="x:String" DataTemplate, so this event handler provides a trim-safe alternative.
    private void OnRemoveItem(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ItemsControl>() is { DataContext: LibVm.StringArrayPropertyEditorViewModel vm })
        {
            vm.RemoveItemCommand.Execute(btn.DataContext as string);
        }
    }

    /// <summary>
    /// Handles the ▾ chevron next to a free-form enum AutoCompleteBox.
    /// Walks from the clicked Button up to its parent StackPanel then finds the
    /// sibling AutoCompleteBox named "FreeFormBox", focuses it, and opens the
    /// suggestion dropdown. <c>MinimumPrefixLength="0"</c> on the AutoCompleteBox
    /// ensures the full options list shows even when the field is empty.
    /// </summary>
    private void OnEnumDropdownToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        StackPanel? stack = button.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault();
        AutoCompleteBox? autoComplete = stack?
                                        .GetVisualDescendants()
                                        .OfType<AutoCompleteBox>()
                                        .FirstOrDefault(a => a.Name == "FreeFormBox");

        if (autoComplete is null)
        {
            return;
        }

        // Show the FULL list when the chevron is clicked rather
        // than the substring-filtered subset.  Pre-fix: after the user picked
        // "claude-3-5-sonnet" the chevron showed only entries containing that
        // string — usually just the one already selected.  Mirror of the
        // library-side fix in LayeredEditors.Avalonia/Controls/PropertyEditorWrapper.axaml.cs.
        AutoCompleteFilterMode originalMode = autoComplete.FilterMode;
        autoComplete.FilterMode = AutoCompleteFilterMode.None;

        EventHandler? restore = null;
        restore = (_, _) =>
        {
            autoComplete.FilterMode = originalMode;
            if (restore is not null)
            {
                autoComplete.DropDownClosed -= restore;
            }
        };
        autoComplete.DropDownClosed += restore;

        autoComplete.Focus();
        autoComplete.IsDropDownOpen = true;
    }
}