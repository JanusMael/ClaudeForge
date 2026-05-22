using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Controls;

/// <summary>
/// A chrome control that wraps any <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel"/>:
/// renders the property name, scope badge, override indicator, lock icon, reset button,
/// description, and delegates the actual input control to a DataTemplate.
/// Specialized editor types not listed in the built-in DataTemplates fall through to
/// <see cref="DataTemplates"/> so host applications can
/// register their own templates.
/// </summary>
public partial class PropertyEditorWrapper : UserControl
{
    public PropertyEditorWrapper()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles the ▾ chevron next to a free-form enum AutoCompleteBox.
    /// Walks from the Button to its sibling AutoCompleteBox (named "FreeFormBox")
    /// through the parent StackPanel, focuses it, and opens its dropdown. With
    /// <c>MinimumPrefixLength="0"</c> the full option list shows even when the
    /// text field is empty — giving the control a discoverable dropdown
    /// affordance without sacrificing free-form typing.
    /// </summary>
    // Code-behind handler for the string-array item remove button.
    // {Binding $parent[ItemsControl].DataContext.RemoveItemCommand} cannot be compiled
    // because the parent DataContext type is not known to the AXAML compiler inside an
    // x:DataType="x:String" DataTemplate, so this event handler provides a trim-safe alternative.
    private void OnRemoveItem(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn &&
            btn.FindAncestorOfType<ItemsControl>() is { DataContext: StringArrayPropertyEditorViewModel vm })
        {
            vm.RemoveItemCommand.Execute(btn.DataContext as string);
        }
    }

    private void OnEnumDropdownToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        // The StackPanel parent holds [AutoCompleteBox, Button] — find the sibling.
        StackPanel? stack = button.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault();
        AutoCompleteBox? autoComplete = stack?
                                        .GetVisualDescendants()
                                        .OfType<AutoCompleteBox>()
                                        .FirstOrDefault(a => a.Name == "FreeFormBox");

        if (autoComplete is null)
        {
            return;
        }

        // Show the FULL list when the chevron is clicked, not
        // the substring-filtered subset.  Pre-fix behaviour: AutoCompleteBox
        // uses its current Text as the filter, so after the user picked a
        // value (e.g. "claude-3-5-sonnet"), clicking the chevron showed only
        // entries containing that string — usually just the one already
        // selected.  Surprising to a user who isn't typing.
        //
        // Fix: temporarily swap FilterMode to None ("no filter, all items
        // returned" per Avalonia docs), open the dropdown, and restore the
        // original FilterMode when the dropdown closes.  Typing-to-filter
        // (the AutoCompleteBox's primary affordance) still works because the
        // original Contains mode is restored before the user's next keystroke.
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