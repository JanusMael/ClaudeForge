using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Helpers;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Maps an <see cref="AppSeverity"/> to the brush the host application declares for it.
/// </summary>
/// <remarks>
/// <para>
/// Resolution happens at <see cref="Convert"/>-time rather than in a static field, for the same
/// reason <see cref="BoolToStatusBrushConverter"/> does it: the host's resource dictionary is not
/// loaded when the converter is constructed, so a field would capture the fallback forever.
/// </para>
/// <para>
/// ⚠ <b>Themed lookup, not the flat one.</b> The <c>AppSeverity*Brush</c> keys are declared per
/// variant under <c>ResourceDictionary.ThemeDictionaries</c> in each app's <c>App.axaml</c>, so
/// this uses <see cref="BrushHelper.ResolveThemed"/>. Calling the flat
/// <see cref="BrushHelper.Resolve"/> would find nothing and quietly return the fallback hex —
/// re-introducing the single hardcoded colour that this whole type exists to delete.
/// </para>
/// <para>
/// ⚠ <b>The fallback hexes below are a backstop, not a palette.</b> They are the light-variant
/// values, and they are only reached when a host has failed to declare a key.
/// <c>AppSeverityTokenCoverageTests</c> asserts every member has both variants declared in both
/// apps, so reaching one of these is a test failure elsewhere rather than a colour decision here.
/// </para>
/// <para>
/// An unrecognised value maps to <see cref="AppSeverity.Neutral"/> rather than throwing: a
/// converter that throws inside a template takes the whole page down, and "no emphasis" is the
/// safe reading of "I do not know how severe this is".
/// </para>
/// </remarks>
public sealed class AppSeverityToBrushConverter : IValueConverter
{
    /// <summary>Resource key for <paramref name="severity"/>.</summary>
    /// <remarks>
    /// Public and static so the coverage test asserts against the same string the converter
    /// actually asks for, rather than against its own copy of the naming convention.
    /// </remarks>
    public static string KeyFor(AppSeverity severity) => $"AppSeverity{severity}Brush";

    private static string FallbackFor(AppSeverity severity) => severity switch
    {
        AppSeverity.Critical => "#A8071A",
        AppSeverity.Caution => "#874400",
        AppSeverity.Info => "#0050B3",
        var _ => "#666666",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        AppSeverity severity = value is AppSeverity s ? s : AppSeverity.Neutral;
        return BrushHelper.ResolveThemed(KeyFor(severity), FallbackFor(severity));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
