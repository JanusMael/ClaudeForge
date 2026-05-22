using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Bennewitz.Ninja.ClaudeForge.Controls;

/// <summary>
/// A stretch-filling, transparent ContentControl for DataGrid cell templates.
/// Setting <see cref="Tip"/> exposes a tooltip over the entire cell background, not
/// just the text-glyph hit region, matching the pattern used in EnvironmentEditorView.
/// </summary>
public sealed class TipCell : ContentControl
{
    public static readonly StyledProperty<object?> TipProperty =
        AvaloniaProperty.Register<TipCell, object?>(nameof(Tip));

    public object? Tip
    {
        get => GetValue(TipProperty);
        set => SetValue(TipProperty, value);
    }

    static TipCell()
    {
        BackgroundProperty.OverrideDefaultValue<TipCell>(Brushes.Transparent);
        HorizontalAlignmentProperty.OverrideDefaultValue<TipCell>(HorizontalAlignment.Stretch);
        VerticalAlignmentProperty.OverrideDefaultValue<TipCell>(VerticalAlignment.Stretch);
        HorizontalContentAlignmentProperty.OverrideDefaultValue<TipCell>(HorizontalAlignment.Stretch);
        VerticalContentAlignmentProperty.OverrideDefaultValue<TipCell>(VerticalAlignment.Stretch);
        PaddingProperty.OverrideDefaultValue<TipCell>(new Thickness(0));
        TipProperty.Changed.AddClassHandler<TipCell>((cell, e) =>
            ToolTip.SetTip(cell, e.NewValue));
    }
}