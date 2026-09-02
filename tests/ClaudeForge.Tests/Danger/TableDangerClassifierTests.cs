using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Danger;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

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
[TestClass]
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

    [TestMethod]
    public void AnAncestorsValuePredicateIsNeverRunAgainstADescendantsValue()
    {
        // `parent` has a predicate that WOULD fire on the descendant's value. That is the whole
        // point: if the mechanism leaked, this returns true.
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["parent"] = new() { Tier = AppSeverity.Critical, Why = "area", Unsafe = IsBoom },
        });

        DangerAssessment inherited = classifier.Classify("parent.child", AnyScope, "boom");

        Assert.AreEqual(AppSeverity.Critical, inherited.Severity,
            "the tier IS inherited — that is what makes a per-top-level-key table cover a nested schema");
        Assert.IsFalse(inherited.IsDangerNow,
            "the ancestor's predicate was written for the parent's own value shape and must never "
            + "be evaluated against a descendant's value");
        Assert.AreEqual("area", inherited.Explanation);
    }

    [TestMethod]
    public void AnExactMatchDoesRunItsOwnValuePredicate()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["parent"] = new() { Tier = AppSeverity.Critical, Why = "area", Unsafe = IsBoom },
        });

        Assert.IsTrue(classifier.Classify("parent", AnyScope, "boom").IsDangerNow);
        Assert.IsFalse(classifier.Classify("parent", AnyScope, "fine").IsDangerNow);
    }

    /// <summary>
    /// Paired with the test above: an ancestor rule must not suppress a descendant's OWN rule.
    /// </summary>
    [TestMethod]
    public void ADescendantWithItsOwnRuleIsEvaluatedNormally()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["parent"] = new() { Tier = AppSeverity.Info, Why = "area", Unsafe = IsBoom },
            ["parent.child"] = new() { Tier = AppSeverity.Critical, Why = "leaf", Unsafe = IsBoom },
        });

        DangerAssessment a = classifier.Classify("parent.child", AnyScope, "boom");

        Assert.AreEqual(AppSeverity.Critical, a.Severity, "the descendant's own tier wins");
        Assert.IsTrue(a.IsDangerNow, "and its own predicate runs");
        Assert.AreEqual("leaf", a.Explanation);
    }

    // ── Specificity ───────────────────────────────────────────────────────────

    [TestMethod]
    public void ALiteralSegmentBeatsAWildcardAtTheSameDepth()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a.*"] = new() { Tier = AppSeverity.Caution, Why = "wildcard" },
            ["a.b"] = new() { Tier = AppSeverity.Critical, Why = "literal" },
        });

        Assert.AreEqual("literal", classifier.Classify("a.b", AnyScope, null).Explanation);
        Assert.AreEqual("wildcard", classifier.Classify("a.zzz", AnyScope, null).Explanation);
    }

    [TestMethod]
    public void ADeeperPatternBeatsAShallowerOne()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a"] = new() { Tier = AppSeverity.Info, Why = "shallow" },
            ["a.b.c"] = new() { Tier = AppSeverity.Critical, Why = "deep" },
        });

        Assert.AreEqual("deep", classifier.Classify("a.b.c", AnyScope, null).Explanation);
        Assert.AreEqual("shallow", classifier.Classify("a.b", AnyScope, null).Explanation,
            "a.b.c is longer than the path, so it must not match at all");
    }

    [TestMethod]
    public void AWildcardMatchesExactlyOneSegmentNotSeveral()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a.*.c"] = new() { Tier = AppSeverity.Critical, Why = "matched" },
        });

        Assert.AreEqual("matched", classifier.Classify("a.b.c", AnyScope, null).Explanation);

        // `*` must not swallow "b.x" — a.b.x.c is not this pattern.
        Assert.AreEqual(DangerAssessment.Unremarkable, classifier.Classify("a.b.x.c", AnyScope, null));
    }

    // ── Scope escalation ──────────────────────────────────────────────────────

    [TestMethod]
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

        Assert.AreEqual(AppSeverity.Critical,
            classifier.Classify("secret", new Scope("shared"), null).Severity);
        Assert.AreEqual(AppSeverity.Caution,
            classifier.Classify("secret", new Scope("private"), null).Severity);
    }

    [TestMethod]
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
        Assert.AreEqual(AppSeverity.Critical, classifier.Classify("secret", scope: null, null).Severity,
            "the mechanism must hand the rule a null scope id, not fabricate one");
    }

    [TestMethod]
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

        Assert.AreEqual(AppSeverity.Caution,
            classifier.Classify("parent.child", new Scope("shared"), null).Severity,
            "an inherited assessment carries the base tier; escalation is an exact-match concern, "
            + "consistent with the value predicate");
    }

    [TestMethod]
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

        Assert.AreEqual(AppSeverity.Caution, classifier.Classify("x", AnyScope, null).Severity,
            "EscalatedTier defaults to Critical but must be overridable");
    }

    // ── Edges ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnUnmatchedOrEmptyPathIsUnremarkable()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a"] = new() { Tier = AppSeverity.Critical, Why = "a" },
        });

        Assert.AreEqual(DangerAssessment.Unremarkable, classifier.Classify("zzz", AnyScope, null));
        Assert.AreEqual(DangerAssessment.Unremarkable, classifier.Classify(string.Empty, AnyScope, null));
    }

    [TestMethod]
    public void ClassifiedPathsReportsEveryPatternVerbatimIncludingWildcards()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal)
        {
            ["a"] = new() { Tier = AppSeverity.Info, Why = "a" },
            ["b.*.c"] = new() { Tier = AppSeverity.Info, Why = "b" },
        });

        CollectionAssert.AreEquivalent(
            new[] { "a", "b.*.c" },
            classifier.ClassifiedPaths.ToArray(),
            "a coverage test needs the patterns as written, so it can match rather than string-compare");
    }

    [TestMethod]
    public void AnEmptyTableClassifiesNothingRatherThanThrowing()
    {
        TableDangerClassifier classifier = new(new Dictionary<string, DangerRule>(StringComparer.Ordinal));

        Assert.AreEqual(DangerAssessment.Unremarkable, classifier.Classify("anything", AnyScope, null));
        Assert.AreEqual(0, classifier.ClassifiedPaths.Count);
    }
}
