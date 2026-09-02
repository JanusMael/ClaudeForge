using System.Globalization;
using Avalonia.Automation;
using Avalonia.Data.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Converts a bool "this row is purely decorative" flag to an
/// <see cref="AccessibilityView"/>: <c>true</c> → <see cref="AccessibilityView.Raw"/> (drop the
/// element out of the UIA control view entirely), <c>false</c> → <see cref="AccessibilityView.Content"/>.
/// </summary>
/// <remarks>
/// <para>
/// Exists for the navigation tree's divider nodes. A divider is a real
/// <c>NavigationNodeViewModel</c> in the same <c>ItemsSource</c> collection as the selectable
/// pages — that is how one <c>TreeDataTemplate</c> can render both a nav row and a section rule —
/// so it also becomes a real <c>TreeViewItem</c> that a screen reader would stop on and announce.
/// </para>
/// <para>
/// ⚠ <b>Naming a divider is worse than leaving it unnamed.</b> Its <c>Title</c> is the vestigial
/// string <c>"─────────────"</c> (thirteen box-drawing characters) left over from a text-based
/// separator; the template has rendered a 1px <c>Border</c> for a long time and never displays
/// that text. Binding the container's automation name to <c>Title</c> — which is what names the
/// other 24 rows — therefore made the two dividers announce thirteen glyphs each. Measured on the
/// running app, not inferred.
/// </para>
/// <para>
/// ⭐ <b><see cref="AccessibilityView.Raw"/> removes the element from the control view, which is
/// the honest answer for a decoration.</b> Also measured: applying <c>Raw</c> to every
/// <c>TreeViewItem</c> took a UIA <c>ControlViewWalker</c> census of the navigation tree from 26
/// items to 0.
/// </para>
/// </remarks>
public sealed class BoolToAccessibilityViewConverter : IValueConverter
{
    public static readonly BoolToAccessibilityViewConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? AccessibilityView.Raw : AccessibilityView.Content;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
