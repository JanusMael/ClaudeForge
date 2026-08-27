using System.Globalization;
using Avalonia.Media;
using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;
using Bennewitz.Ninja.Layer.Avalonia.Services.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Converters;

/// <summary>
/// The shared <see cref="PermissionOutcome"/> brush mapping.
/// </summary>
/// <remarks>
/// <para>
/// It had no tests while it lived in one product and served one view. It has two consumers now —
/// Claude's rule tester and OpenCode's permission grid — so a silent change to the mapping would
/// alter what two different products tell users about the same three-word vocabulary.
/// </para>
/// <para>
/// ⚠ The colours are asserted as <b>values</b>, not by comparing against the converter's own
/// fields. Comparing a converter to itself passes no matter what the colours become; the point of
/// pinning them is that green-means-allow is a claim about what users see, and it survives a
/// refactor only if something states it independently.
/// </para>
/// </remarks>
[TestClass]
public sealed class PermissionOutcomeToBrushConverterTests
{
    private static Color ColorFor(object? value)
    {
        object? result = PermissionOutcomeToBrushConverter.Instance.Convert(
            value,
            typeof(IBrush),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.IsInstanceOfType<ISolidColorBrush>(result, $"Expected a solid brush for '{value}'.");
        return ((ISolidColorBrush)result!).Color;
    }

    [TestMethod]
    public void AllowIsGreen_AskIsAmber_DenyIsRed()
    {
        Assert.AreEqual(Color.FromRgb(0x2E, 0x7D, 0x32), ColorFor(PermissionOutcome.Allow));
        Assert.AreEqual(Color.FromRgb(0xF4, 0xB4, 0x00), ColorFor(PermissionOutcome.Ask));
        Assert.AreEqual(Color.FromRgb(0xD3, 0x2F, 0x2F), ColorFor(PermissionOutcome.Deny));
    }

    /// <remarks>
    /// <see cref="PermissionOutcome.Default"/> means no rule matched, which is not an affirmative
    /// grant — so it must not read as the green one. It is also the enum's zero value, the one that
    /// arrives by accident.
    /// </remarks>
    [TestMethod]
    public void DefaultIsNeutral_NotAllow()
    {
        Color neutral = ColorFor(PermissionOutcome.Default);

        Assert.AreEqual(Color.FromRgb(0x9E, 0x9E, 0x9E), neutral);
        Assert.AreNotEqual(
            ColorFor(PermissionOutcome.Allow),
            neutral,
            "'No rule matched' must never be shown in the colour that means 'permitted'.");
    }

    [TestMethod]
    public void AnythingThatIsNotAnOutcome_IsNeutralRatherThanThrowing()
    {
        // Bindings hand converters nulls and unset values during load and teardown. Throwing here
        // surfaces as a binding error and an uncoloured control, not as a useful failure.
        Assert.AreEqual(Color.FromRgb(0x9E, 0x9E, 0x9E), ColorFor(null));
        Assert.AreEqual(Color.FromRgb(0x9E, 0x9E, 0x9E), ColorFor("Allow"));
        Assert.AreEqual(Color.FromRgb(0x9E, 0x9E, 0x9E), ColorFor(42));
    }

    [TestMethod]
    public void ConvertBack_IsNotSupported()
    {
        // A colour does not identify an outcome, and a two-way binding onto a status brush would
        // be a bug rather than a feature.
        Assert.ThrowsExactly<NotSupportedException>(() =>
            PermissionOutcomeToBrushConverter.Instance.ConvertBack(
                Brushes.Red,
                typeof(PermissionOutcome),
                parameter: null,
                CultureInfo.InvariantCulture));
    }
}
