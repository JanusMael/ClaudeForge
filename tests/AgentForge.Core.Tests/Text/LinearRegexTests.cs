using System.Text.RegularExpressions;
using Bennewitz.Ninja.AgentForge.Core.Text;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Text;

/// <summary>
/// <see cref="LinearRegex"/>: every instance is linear-time with no match timeout, and
/// the cache shares instances and stays bounded.
/// </summary>
public sealed class LinearRegexTests
{
    [Fact]
    public void Get_IsNonBacktracking_WithNoTimeout()
    {
        Regex regex = LinearRegex.Get("^a.*b$", RegexOptions.IgnoreCase);

        Assert.True(regex.Options.HasFlag(RegexOptions.NonBacktracking), "Get must add NonBacktracking.");
        Assert.True(regex.Options.HasFlag(RegexOptions.IgnoreCase), "Get must keep the caller's options.");
        Assert.Equal(Regex.InfiniteMatchTimeout, regex.MatchTimeout);
    }

    [Fact]
    public void Create_IsNonBacktracking_WithNoTimeout()
    {
        Regex regex = LinearRegex.Create("^[a-z]+$", RegexOptions.CultureInvariant);

        Assert.True(regex.Options.HasFlag(RegexOptions.NonBacktracking), "Create must add NonBacktracking.");
        Assert.Equal(Regex.InfiniteMatchTimeout, regex.MatchTimeout);
    }

    [Fact]
    public void Get_SharesOneInstance_PerPatternAndOptions()
    {
        string pattern = "^shared-" + Guid.NewGuid().ToString("N") + "$";

        Regex first = LinearRegex.Get(pattern);
        Regex second = LinearRegex.Get(pattern);
        Regex otherOptions = LinearRegex.Get(pattern, RegexOptions.IgnoreCase);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.NotSame(first, otherOptions);
    }

    [Fact]
    public void Get_RejectsLookaround_AtConstruction()
    {
        // The contract a generator relies on: an unsupported construct fails loudly when
        // the regex is built, never by a wrong answer at match time.
        Assert.Throws<NotSupportedException>(() => LinearRegex.Get("^(?=a)a$"));
        Assert.Throws<NotSupportedException>(() => LinearRegex.Get(@"^(a)\1$"));
    }

    [Fact]
    public void Get_CacheStaysBounded()
    {
        string run = Guid.NewGuid().ToString("N");
        int peak = 0;
        for (int i = 0; i < LinearRegex.Capacity + 20; i++)
        {
            _ = LinearRegex.Get("^bounded-" + run + "-" + i + "$");
            peak = Math.Max(peak, LinearRegex.CachedCount);
        }

        // The premise: the cache reached capacity, so the bound was exercised.
        Assert.Equal(LinearRegex.Capacity, peak);
    }
}
