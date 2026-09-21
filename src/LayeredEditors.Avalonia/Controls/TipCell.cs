using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Controls;

/// <summary>
/// A stretch-filling, transparent ContentControl for DataGrid cell templates.
/// Setting <see cref="Tip"/> exposes a tooltip over the entire cell background, not
/// just the text-glyph hit region, matching the pattern used in EnvironmentEditorView.
/// </summary>
/// <remarks>
/// ⚠ <b>Lives here, not in an app, because both apps' Backup pages need it.</b> It was
/// <c>ClaudeForge.Controls.TipCell</c> until OpenCodeForge grew a Backup / Restore grid of its
/// own; the two products cannot reference each other, so the alternatives were a second copy or
/// this move. Nothing about it is product-shaped — it is a <see cref="ContentControl"/> that
/// forwards one property to <see cref="ToolTip"/>.
/// </remarks>
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