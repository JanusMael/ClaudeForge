using System.Globalization;
using Bennewitz.Ninja.ClaudeForge.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Converters;

/// <summary>
/// Pins the contract that the Effective Settings scope pill label relies on:
/// every <see cref="ConfigScope"/> value converts to a non-empty string, and
/// a null input yields a null output (so the pill's IsVisible binding hides
/// the Border entirely rather than rendering an empty colored box).
/// </summary>
public sealed class ScopeToDisplayNameConverterTests
{
    private static object? Convert(object? value)
    {
        return new ScopeToDisplayNameConverter().Convert(value, typeof(string), null, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Converts_EachConfigScope_ToNonEmptyDisplayLabel()
    {
        foreach (ConfigScope scope in ConfigScope.All)
        {
            string? label = Convert(scope) as string;
            Assert.False(string.IsNullOrWhiteSpace(label),
                $"ConfigScope.{scope} must produce a non-empty display label.");
        }
    }

    [Fact]
    public void ReturnsNull_ForNullInput()
    {
        Assert.Null(Convert(null));
    }

    [Fact]
    public void ConvertsKnownScopes_ToExpectedLabels()
    {
        Assert.Equal("Managed", Convert(ConfigScope.Managed));
        Assert.Equal("User", Convert(ConfigScope.User));
        Assert.Equal("Project", Convert(ConfigScope.Project));
        Assert.Equal("Local", Convert(ConfigScope.Local));
    }
}