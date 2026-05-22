using System.Globalization;
using Avalonia.Data.Converters;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// One-way value converters that map an <see cref="EssentialsCardKind"/>
/// onto a <see cref="bool"/> indicating whether the card is of that kind.
/// Used by the per-Kind <c>IsVisible</c> bindings on
/// <c>EssentialsView.axaml</c> to switch between editor surfaces
/// (CheckBox / NumericUpDown / ComboBox / StringList Add-row).
/// </summary>
/// <remarks>
/// A separate converter per kind avoids stringly-typed
/// <c>ConverterParameter="Bool"</c> hops that would otherwise hide the
/// match in AXAML.  Each converter is exposed as a single static
/// instance via <c>{x:Static}</c>.
/// </remarks>
public sealed class EssentialsCardKindConverter : IValueConverter
{
    private readonly EssentialsCardKind _expected;

    public EssentialsCardKindConverter(EssentialsCardKind expected)
    {
        _expected = expected;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is EssentialsCardKind kind && kind == _expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Singleton converters for each <see cref="EssentialsCardKind"/> variant,
/// referenced from AXAML via <c>{x:Static vm:EssentialsCardKindConverters.IsBool}</c>.
/// </summary>
public static class EssentialsCardKindConverters
{
    public static readonly EssentialsCardKindConverter IsBool = new(EssentialsCardKind.Bool);
    public static readonly EssentialsCardKindConverter IsInt = new(EssentialsCardKind.Int);
    public static readonly EssentialsCardKindConverter IsEnumString = new(EssentialsCardKind.EnumString);
    public static readonly EssentialsCardKindConverter IsStringList = new(EssentialsCardKind.StringList);
}