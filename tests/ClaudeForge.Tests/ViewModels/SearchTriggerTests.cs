namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// The neutral half of the synthetic-search seam: when a product's declared
/// phrases match a typed query.
///
/// <para>
/// Deliberately names no product. Phase 5 slice 3 moved this matcher out of the
/// Claude search view-model precisely so a second product inherits the rules
/// rather than re-deriving them, and the three positive rule kinds differ in ways
/// that are easy to get wrong by hand — <see cref="SearchTrigger.Phrases"/> is
/// bidirectional, <see cref="SearchTrigger.Mentions"/> is not, and
/// <see cref="SearchTrigger.PrefixOf"/> is narrower than both.
/// </para>
/// </summary>
public sealed class SearchTriggerTests
{
    [Fact]
    public void Phrases_MatchInBothDirections()
    {
        SearchTrigger trigger = new() { Phrases = ["sandbox"] };

        Assert.True(trigger.Matches("sandbox"), "Exact phrase must match.");
        Assert.True(trigger.Matches("bash sandbox settings"),
            "Query containing the phrase must match.");
        Assert.True(trigger.Matches("san"),
            "Phrase containing the query must match — this is what makes partial typing land early.");
        Assert.False(trigger.Matches("network"), "Unrelated query must not match.");
    }

    [Fact]
    public void PrefixOf_MatchesOnlyFromTheFront()
    {
        SearchTrigger trigger = new() { PrefixOf = ["dangerouslyskippermissions"], MinQueryLength = 3 };

        Assert.True(trigger.Matches("danger"), "A prefix of the term must match.");
        Assert.True(trigger.Matches("dangerouslyskippermissions"), "The whole term must match.");
        Assert.False(trigger.Matches("skip"),
            "An interior fragment must NOT match — that is the whole reason this rule is not Phrases.");
        Assert.False(trigger.Matches("permissions"),
            "A trailing fragment must not match either.");
    }

    /// <summary>
    /// The distinction that keeps a narrow entry narrow. Were the enable-bypass
    /// row declared with <see cref="SearchTrigger.Phrases"/> instead of
    /// <see cref="SearchTrigger.Mentions"/>, a query of "pass" would pin it.
    /// </summary>
    [Fact]
    public void Mentions_IsOneDirectional_UnlikePhrases()
    {
        SearchTrigger mentions = new() { Mentions = ["bypass"], MinQueryLength = 3 };
        SearchTrigger phrases = new() { Phrases = ["bypass"], MinQueryLength = 3 };

        Assert.True(mentions.Matches("bypass permissions"), "Query containing the term matches.");
        Assert.False(mentions.Matches("pass"),
            "A query the term merely contains must NOT match a Mentions rule.");
        Assert.True(phrases.Matches("pass"),
            "…whereas it does match a Phrases rule. Confirms the two kinds are genuinely different.");
    }

    [Fact]
    public void Excluding_VetoesAMatchAPositiveRuleWouldAllow()
    {
        SearchTrigger trigger = new()
        {
            Mentions = ["bypass"],
            Excluding = ["disable"],
            MinQueryLength = 3,
        };

        Assert.True(trigger.Matches("bypass"), "Without the veto word, the row matches.");
        Assert.False(trigger.Matches("disable bypass"),
            "The veto must beat the positive rule — the opposite intent gets its own row.");
    }

    [Fact]
    public void MinQueryLength_GatesShortQueries()
    {
        SearchTrigger trigger = new() { Phrases = ["model"], MinQueryLength = 2 };

        Assert.False(trigger.Matches("m"), "One character is below the gate.");
        Assert.True(trigger.Matches("mo"), "Two characters clear it.");

        SearchTrigger stricter = trigger with { MinQueryLength = 3 };
        Assert.False(stricter.Matches("mo"), "A higher gate rejects the same query.");
    }

    /// <summary>
    /// An entry nobody can reach is a visible bug; an entry everybody reaches
    /// would pin a row to the top of every single search. The empty trigger
    /// therefore fails closed.
    /// </summary>
    [Fact]
    public void NoRules_MatchesNothing()
    {
        SearchTrigger trigger = new();

        Assert.False(trigger.Matches("anything"));
        Assert.False(trigger.Matches(string.Empty));
    }

    [Fact]
    public void Matching_IsOrdinal_AgainstAnAlreadyNormalisedQuery()
    {
        // The caller lower-cases and trims; the rules then compare ordinally, so a
        // rule written in mixed case simply never fires. Pinning this stops someone
        // "fixing" a non-matching rule by making the comparison case-insensitive
        // here, which would double-normalise and hide the real bug in the table.
        SearchTrigger trigger = new() { Phrases = ["Sandbox"] };

        Assert.False(trigger.Matches("sandbox"),
            "An upper-case rule does not match a normalised query — declare rules lower-case.");
    }
}
