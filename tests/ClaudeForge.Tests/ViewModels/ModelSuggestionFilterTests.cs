using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Locks the model-picker fuzzy matcher: a short friendly fragment must surface every
/// shape of a model (alias, id, [1m] variant, brand label), tolerant of case and
/// separators, and additional tokens must narrow the result.
/// </summary>
[TestClass]
public sealed class ModelSuggestionFilterTests
{
    // A representative haystack: value + brand label + detail, as the control composes it.
    private const string OpusHaystack = "claude-opus-4-8 Opus 4.8 claude-opus-4-8";
    private const string Opus1mHaystack = "opus[1m] Opus 4.8 · 1M context opus[1m]";

    [DataTestMethod]
    [DataRow("Opus")]        // brand fragment, different case
    [DataRow("opus")]        // lower-case
    [DataRow("OPUS")]        // upper-case
    [DataRow("claude-opus")] // id fragment with separator
    [DataRow("opus 4")]      // two tokens, both present
    [DataRow("4.8")]         // label fragment with a dot
    public void Matches_FriendlyFragments_HitTheModel(string query)
    {
        Assert.IsTrue(ModelSuggestionFilter.Matches(query, OpusHaystack),
            $"'{query}' should fuzzy-match the Opus entry.");
    }

    [TestMethod]
    public void Matches_SeparatorInBracketVariant_IsIgnored()
    {
        // "[1m]" normalizes to a bare "1m" token; querying either form still matches.
        Assert.IsTrue(ModelSuggestionFilter.Matches("opus 1m", Opus1mHaystack));
        Assert.IsTrue(ModelSuggestionFilter.Matches("opus[1m]", Opus1mHaystack));
    }

    [TestMethod]
    public void Matches_EmptyOrWhitespaceQuery_MatchesEverything()
    {
        // Empty query → the picker shows the full list on focus.
        Assert.IsTrue(ModelSuggestionFilter.Matches(null, OpusHaystack));
        Assert.IsTrue(ModelSuggestionFilter.Matches("", OpusHaystack));
        Assert.IsTrue(ModelSuggestionFilter.Matches("   ", OpusHaystack));
    }

    [DataTestMethod]
    [DataRow("sonnet")]  // different model family
    [DataRow("opus 9")]  // right family, wrong version token
    [DataRow("haiku")]
    public void Matches_NonMatchingTokens_AreRejected(string query)
    {
        Assert.IsFalse(ModelSuggestionFilter.Matches(query, OpusHaystack),
            $"'{query}' should not match the Opus entry.");
    }
}
