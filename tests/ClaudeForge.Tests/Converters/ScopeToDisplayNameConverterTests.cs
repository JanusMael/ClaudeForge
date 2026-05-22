using System.Globalization;
using Bennewitz.Ninja.ClaudeForge.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Converters;

/// <summary>
/// Pins the contract that the Effective Settings scope pill label relies on:
/// every <see cref="ConfigScope"/> value converts to a non-empty string, and
/// a null input yields a null output (so the pill's IsVisible binding hides
/// the Border entirely rather than rendering an empty colored box).
/// </summary>
[TestClass]
public sealed class ScopeToDisplayNameConverterTests
{
    private static object? Convert(object? value)
    {
        return new ScopeToDisplayNameConverter().Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public void Converts_EachConfigScope_ToNonEmptyDisplayLabel()
    {
        foreach (ConfigScope scope in Enum.GetValues<ConfigScope>())
        {
            string? label = Convert(scope) as string;
            Assert.IsFalse(string.IsNullOrWhiteSpace(label),
                $"ConfigScope.{scope} must produce a non-empty display label.");
        }
    }

    [TestMethod]
    public void ReturnsNull_ForNullInput()
    {
        Assert.IsNull(Convert(null));
    }

    [TestMethod]
    public void ConvertsKnownScopes_ToExpectedLabels()
    {
        Assert.AreEqual("Managed", Convert(ConfigScope.Managed));
        Assert.AreEqual("User", Convert(ConfigScope.User));
        Assert.AreEqual("Project", Convert(ConfigScope.Project));
        Assert.AreEqual("Local", Convert(ConfigScope.Local));
    }
}