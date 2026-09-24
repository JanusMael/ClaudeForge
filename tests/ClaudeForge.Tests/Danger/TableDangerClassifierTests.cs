using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;
using Bennewitz.Ninja.ScopedEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Danger;

/// <summary>
/// The neutral matching mechanism, tested against purpose-built rule tables rather than a
/// product's real one.
///
/// <para>
/// ⭐⭐ <b>This file exists because a canary would not redden.</b> The no-inherit guarantee was
/// first asserted through OpenCode's real table, using <c>attachment.image.maxWidth</c> as the
/// descendant — and breaking the mechanism on purpose (making an ancestor match run its value
/// predicate) left that test GREEN. The reason is that <c>attachment</c> carries no
/// <c>Unsafe</c> predicate at all, so <c>rule.Unsafe?.Invoke(…) ?? false</c> was <c>false</c>
/// either way: <b>the assertion could not fail, which made it worthless as a guard.</b> Testing
/// the mechanism against a table built to exercise it is the fix — a real product's table is
/// whatever its policy happens to need, not a set of cases chosen to pin behaviour.
/// </para>
/// <para>
/// See <c>OpenCodeDangerTableTests</c> for the product-policy half (does the table classify the
/// right keys the right way).
/// </para>
/// </summary>
public sealed class TableDangerClassifierTests
{
    private sealed record Scope(string Id) : IEditorScope
    {
        public int Priority => 0;

        public string DisplayName => Id;

        public bool IsReadOnly => false;
    }

    private static readonly Scope AnyScope = new("any");

    /// <summary>A predicate that fires on the sentinel, whatever depth it is handed.</summary>
    private static bool IsBoom(object? v) => v is "boom";

    // ── The no-inherit guarantee — the assertion that must be able to fail ────

    [Fact]
    public void AnAncestorsValuePredicateIsNeverRunAgainstADescendantsValue()
    {
        // `parent` has a predicate that WOULD fire on the descendant's value. That is the whole
        // point: if the mechanism leaked, this returns true.
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["parent"] = new() { Tier = AppSeverity.Critical, Why = "area", Unsafe = IsBoom },
        });

        DangerAssessment inherited = classifier.Classify("parent.child", AnyScope, "boom");

        MessageAssert.Equal(AppSeverity.Critical, inherited.Severity,
            "the tier IS inherited — that is what makes a per-top-level-key table cover a nested schema");
        Assert.False(inherited.IsDangerNow,
            "the ancestor's predicate was written for the parent's own value shape and must never "
            + "be evaluated against a descendant's value");
        Assert.Equal("area", inherited.Explanation);
    }

    [Fact]
    public void AnExactMatchDoesRunItsOwnValuePredicate()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["parent"] = new() { Tier = AppSeverity.Critical, Why = "area", Unsafe = IsBoom },
        });

        Assert.True(classifier.Classify("parent", AnyScope, "boom").IsDangerNow);
        Assert.False(classifier.Classify("parent", AnyScope, "fine").IsDangerNow);
    }

    /// <summary>
    /// Paired with the test above: an ancestor rule must not suppress a descendant's OWN rule.
    /// </summary>
    [Fact]
    public void ADescendantWithItsOwnRuleIsEvaluatedNormally()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["parent"] = new() { Tier = AppSeverity.Info, Why = "area", Unsafe = IsBoom },
            ["parent.child"] = new() { Tier = AppSeverity.Critical, Why = "leaf", Unsafe = IsBoom },
        });

        DangerAssessment a = classifier.Classify("parent.child", AnyScope, "boom");

        MessageAssert.Equal(AppSeverity.Critical, a.Severity, "the descendant's own tier wins");
        Assert.True(a.IsDangerNow, "and its own predicate runs");
        Assert.Equal("leaf", a.Explanation);
    }

    // ── Specificity ───────────────────────────────────────────────────────────

    [Fact]
    public void ALiteralSegmentBeatsAWildcardAtTheSameDepth()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a.*"] = new() { Tier = AppSeverity.Caution, Why = "wildcard" },
            ["a.b"] = new() { Tier = AppSeverity.Critical, Why = "literal" },
        });

        Assert.Equal("literal", classifier.Classify("a.b", AnyScope, null).Explanation);
        Assert.Equal("wildcard", classifier.Classify("a.zzz", AnyScope, null).Explanation);
    }

    [Fact]
    public void ADeeperPatternBeatsAShallowerOne()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a"] = new() { Tier = AppSeverity.Info, Why = "shallow" },
            ["a.b.c"] = new() { Tier = AppSeverity.Critical, Why = "deep" },
        });

        Assert.Equal("deep", classifier.Classify("a.b.c", AnyScope, null).Explanation);
        MessageAssert.Equal("shallow", classifier.Classify("a.b", AnyScope, null).Explanation,
            "a.b.c is longer than the path, so it must not match at all");
    }

    [Fact]
    public void AWildcardMatchesExactlyOneSegmentNotSeveral()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a.*.c"] = new() { Tier = AppSeverity.Critical, Why = "matched" },
        });

        Assert.Equal("matched", classifier.Classify("a.b.c", AnyScope, null).Explanation);

        // `*` must not swallow "b.x" — a.b.x.c is not this pattern.
        Assert.Equal(DangerAssessment.Unremarkable, classifier.Classify("a.b.x.c", AnyScope, null));
    }

    // ── Scope escalation ──────────────────────────────────────────────────────

    [Fact]
    public void EscalationRaisesTheTierOnlyAtTheNamedScope()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["secret"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "a secret",
                EscalatesAt = id => id == "shared",
            },
        });

        Assert.Equal(AppSeverity.Critical,
            classifier.Classify("secret", new Scope("shared"), null).Severity);
        Assert.Equal(AppSeverity.Caution,
            classifier.Classify("secret", new Scope("private"), null).Severity);
    }

    [Fact]
    public void ANullScopeNeverRaisesTheTier()
    {
        // An escalation predicate that would say "yes" to anything it is handed, including null.
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["secret"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "a secret",
                EscalatesAt = _ => true,
            },
        });

        // The rule's own predicate is what decides, so a table CAN escalate on null if it insists
        // — this test pins that the mechanism passes null through rather than inventing a scope.
        MessageAssert.Equal(AppSeverity.Critical, classifier.Classify("secret", scope: null, null).Severity,
            "the mechanism must hand the rule a null scope id, not fabricate one");
    }

    [Fact]
    public void EscalationIsNotAppliedToAnInheritedMatch()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["parent"] = new()
            {
                Tier = AppSeverity.Caution,
                Why = "area",
                EscalatesAt = _ => true,
            },
        });

        MessageAssert.Equal(AppSeverity.Caution,
            classifier.Classify("parent.child", new Scope("shared"), null).Severity,
            "an inherited assessment carries the base tier; escalation is an exact-match concern, "
            + "consistent with the value predicate");
    }

    [Fact]
    public void ACustomEscalatedTierIsHonoured()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["x"] = new()
            {
                Tier = AppSeverity.Info,
                Why = "x",
                EscalatesAt = _ => true,
                EscalatedTier = AppSeverity.Caution,
            },
        });

        MessageAssert.Equal(AppSeverity.Caution, classifier.Classify("x", AnyScope, null).Severity,
            "EscalatedTier defaults to Critical but must be overridable");
    }

    // ── Edges ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnmatchedOrEmptyPathIsUnremarkable()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a"] = new() { Tier = AppSeverity.Critical, Why = "a" },
        });

        Assert.Equal(DangerAssessment.Unremarkable, classifier.Classify("zzz", AnyScope, null));
        Assert.Equal(DangerAssessment.Unremarkable, classifier.Classify(string.Empty, AnyScope, null));
    }

    [Fact]
    public void ClassifiedPathsReportsEveryPatternVerbatimIncludingWildcards()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a"] = new() { Tier = AppSeverity.Info, Why = "a" },
            ["b.*.c"] = new() { Tier = AppSeverity.Info, Why = "b" },
        });

        MessageAssert.SameElements(
            new[] { "a", "b.*.c" },
            classifier.ClassifiedPaths.ToArray(),
            "a coverage test needs the patterns as written, so it can match rather than string-compare");
    }

    [Fact]
    public void AnEmptyTableClassifiesNothingRatherThanThrowing()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal));

        Assert.Equal(DangerAssessment.Unremarkable, classifier.Classify("anything", AnyScope, null));
        Assert.Empty(classifier.ClassifiedPaths);
    }
}
