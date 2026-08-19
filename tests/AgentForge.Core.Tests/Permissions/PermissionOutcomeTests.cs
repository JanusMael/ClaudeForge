using Bennewitz.Ninja.AgentForge.Abstractions.Permissions;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Permissions;

/// <summary>
/// Pins the two properties <see cref="PermissionOutcome"/> must keep now that it is shared
/// vocabulary on <c>AgentForge.Abstractions</c> rather than a Claude-only enum: which value
/// an uninitialised field carries, and that the four names exist to be switched on.
/// <para>
/// Both failures are silent. Nothing in the product reads an unresolved outcome today —
/// the tester's field is gated behind <c>HasResult</c> — so a zero value meaning "allowed"
/// produces no failing test and no visible defect until a second product holds one without
/// the same gate. That is exactly the shape of bug this repo has been bitten by before, and
/// it is cheap to pin.
/// </para>
/// </summary>
[TestClass]
public class PermissionOutcomeTests
{
    /// <summary>
    /// The safety property. A permission verdict that arrives by accident — a
    /// zero-initialised field, a struct default, a deserialised object with the property
    /// absent — must not read as an affirmative grant.
    /// </summary>
    [TestMethod]
    public void Default_IsDefault_NotAllow_SoAnUnsetOutcomeNeverReadsAsPermission()
    {
        // Read through an uninitialised field rather than `default(PermissionOutcome)`.
        // Two compile-time constants fold to a tautology and MSTEST0032 fails the build —
        // and a field is the real shape of the hazard anyway: the tester holds exactly
        // this, as `[ObservableProperty] private PermissionOutcome _outcome;`.
        UnresolvedVerdict verdict = new();

        Assert.AreEqual(
            PermissionOutcome.Default,
            verdict.Outcome,
            "An uninitialised PermissionOutcome must mean 'nothing decided this', not "
            + "'allowed'. The members were ordered Allow-first when this type was "
            + "Claude-only, which made the zero value an affirmative grant.");

        Assert.AreNotEqual(PermissionOutcome.Allow, verdict.Outcome);
    }

    /// <summary>
    /// Stands in for any view-model or record holding an outcome before one is resolved.
    /// An auto-property rather than a bare field only because warnings are errors here and
    /// a never-assigned field is CS0649 — the compiler objecting to the very thing under
    /// test. The property is never set, which is the point.
    /// </summary>
    private sealed class UnresolvedVerdict
    {
        public PermissionOutcome Outcome { get; set; }
    }

    /// <summary>
    /// The vocabulary itself: exactly the three answers both products spell the same way,
    /// plus the fall-through. A product adding a fifth outcome is a real decision that
    /// should break this test and be made deliberately, not arrive as a merge artefact.
    /// </summary>
    [TestMethod]
    public void TheVocabularyIsTheThreeSharedAnswersPlusFallThrough()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                PermissionOutcome.Default,
                PermissionOutcome.Allow,
                PermissionOutcome.Ask,
                PermissionOutcome.Deny,
            },
            Enum.GetValues<PermissionOutcome>());
    }
}
