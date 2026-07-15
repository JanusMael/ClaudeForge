using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Controls;

/// <summary>
/// The single model picker shared by the Essentials card and the Model &amp; Effort property
/// editor: a fuzzy <see cref="FuzzyModelAutoCompleteBox"/> over friendly
/// <see cref="ModelSuggestionItem"/>s (brand label + dim raw id) plus a chevron that drops
/// the FULL list, so it behaves like a combo box while still accepting a typed custom id.
/// </summary>
public partial class ModelPicker : UserControl
{
    /// <summary>The suggestion list (see <see cref="ModelSuggestionCatalog"/>).</summary>
    public static readonly StyledProperty<IReadOnlyList<ModelSuggestionItem>?> SuggestionsProperty =
        AvaloniaProperty.Register<ModelPicker, IReadOnlyList<ModelSuggestionItem>?>(nameof(Suggestions));

    public IReadOnlyList<ModelSuggestionItem>? Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>
    /// The committed model id (an id / alias / <c>[1m]</c> form). Two-way by default so the
    /// host VM's property is written when the user picks or types.
    /// </summary>
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<ModelPicker, string?>(
            nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Placeholder text shown while the box is empty.</summary>
    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<ModelPicker, string?>(nameof(Watermark));

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>Accessible name for the input (the host supplies the property's title).</summary>
    public static readonly StyledProperty<string?> AutomationLabelProperty =
        AvaloniaProperty.Register<ModelPicker, string?>(nameof(AutomationLabel));

    public string? AutomationLabel
    {
        get => GetValue(AutomationLabelProperty);
        set => SetValue(AutomationLabelProperty, value);
    }

    public ModelPicker()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Chevron → drop the FULL list, not the fuzzy-filtered subset (otherwise, once a model
    /// is selected, the list narrows to just that entry). The enum editor does this by
    /// blanking <c>FilterMode</c>, but an <see cref="AutoCompleteBox.ItemFilter"/> delegate
    /// SUPERSEDES FilterMode — so here we swap the delegate for a match-everything one and
    /// restore the fuzzy filter when the drop-down closes.
    /// </summary>
    private void OnDropdownClick(object? sender, RoutedEventArgs e)
    {
        AutoCompleteBox? box = this.GetVisualDescendants()
                                   .OfType<AutoCompleteBox>()
                                   .FirstOrDefault();
        if (box is null)
        {
            return;
        }

        AutoCompleteFilterPredicate<object?>? original = box.ItemFilter;
        box.ItemFilter = (_, _) => true;

        EventHandler? restore = null;
        restore = (_, _) =>
        {
            box.ItemFilter = original;
            if (restore is not null)
            {
                box.DropDownClosed -= restore;
            }
        };
        box.DropDownClosed += restore;

        box.Focus();
        box.IsDropDownOpen = true;
    }
}
