using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// Pins OpenCode's scope ladder. Every assertion here exists because the corresponding
/// mistake is <b>silent</b>: a ladder listed in the wrong direction inverts precedence with
/// no symptom beyond the wrong file winning, and a rung whose read-only flag is wrong makes
/// policy-locked configuration editable.
/// </summary>
[TestClass]
public class OpenCodeScopesTests
{
    /// <summary>
    /// The order, spelled out. <see cref="ScopeLadder"/> takes rungs highest-priority first,
    /// while every prose description of OpenCode's layering runs the other way
    /// (<c>global → … → managed</c>). This test is the one place the two are reconciled, so
    /// it asserts the names in sequence rather than checking a property of the sequence.
    /// </summary>
    [TestMethod]
    public void Ladder_IsHighestPriorityFirst()
    {
        CollectionAssert.AreEqual(
            new[] { "Managed", "Inline", "Project", "Custom", "Global" },
            OpenCodeScopes.Ladder.All.Select(s => s.ToString()).ToArray(),
            "The ladder must read highest-priority first. Reversed, every layered value "
            + "resolves to the wrong scope and nothing else changes.");
    }

    /// <summary>
    /// The half spike S1 actually measured, asserted as relative precedence rather than as
    /// positions, so it keeps its meaning if an unmeasured rung is later added or removed
    /// above or below it.
    /// </summary>
    [TestMethod]
    public void MeasuredPrecedence_CustomBelowProjectBelowInline()
    {
        int inline = IndexOf(OpenCodeScopes.Inline);
        int project = IndexOf(OpenCodeScopes.Project);
        int custom = IndexOf(OpenCodeScopes.Custom);

        // Lower index == higher priority.
        Assert.IsTrue(inline < project,
            "S1 measured OPENCODE_CONFIG_CONTENT outranking the project config.");
        Assert.IsTrue(project < custom,
            "S1 measured the project config outranking OPENCODE_CONFIG.");
    }

    /// <summary>
    /// Exactly two rungs are read-only, and they are the two that have nowhere to write
    /// back to or must not be written: policy-deployed config, and a config handed over
    /// entirely through an environment variable.
    /// </summary>
    [TestMethod]
    public void ExactlyManagedAndInlineAreReadOnly()
    {
        string[] readOnly = OpenCodeScopes.Ladder.All
            .Where(s => s.IsReadOnly)
            .Select(s => s.ToString())
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "Managed", "Inline" },
            readOnly,
            "Inline is read-only because $OPENCODE_CONFIG_CONTENT has no file behind it — "
            + "an editable Inline scope would offer a save that cannot go anywhere.");
    }

    /// <summary>
    /// The trap 4f documented, from the other side. <see cref="ScopeLadder.Default"/> is
    /// Claude's ladder and is the value <c>ConfigScope</c> encodes as <see langword="null"/>,
    /// so a product that returned it — or any ladder equal to it — would silently adopt
    /// Claude's four rungs and Claude's single read-only rung.
    /// </summary>
    [TestMethod]
    public void Ladder_IsNotClaudesDefault()
    {
        Assert.AreNotSame(ScopeLadder.Default, OpenCodeScopes.Ladder);
        Assert.AreNotEqual(ScopeLadder.Default.Id, OpenCodeScopes.Ladder.Id);

        Assert.AreNotEqual(
            ScopeLadder.Default.All.Count,
            OpenCodeScopes.Ladder.All.Count,
            "Claude's ladder has four rungs and OpenCode's has five. If these ever match in "
            + "length, check that OpenCode did not quietly inherit the default ladder.");
    }

    /// <summary>
    /// A scope from OpenCode's ladder must never compare equal to a Claude scope, however
    /// similar the rung names look. Scope equality is ladder instance plus ordinal, and this
    /// is what stops <c>ConfigScope.Managed</c> — Claude's — from matching OpenCode's
    /// top rung at any of the sites that name the statics.
    /// </summary>
    [TestMethod]
    public void OpenCodesManaged_IsNotClaudesManaged()
    {
        ConfigScope openCodeManaged = OpenCodeScopes.Ladder.All[0];

        Assert.AreEqual("Managed", openCodeManaged.ToString(),
            "Precondition: both ladders really do have a rung spelled 'Managed'.");
        Assert.AreNotEqual(ConfigScope.Managed, openCodeManaged,
            "Two rungs sharing a name must still be different scopes — they belong to "
            + "different products and different files.");
    }

    private static int IndexOf(string rungName)
    {
        IReadOnlyList<ConfigScope> all = OpenCodeScopes.Ladder.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].ToString() == rungName)
            {
                return i;
            }
        }

        Assert.Fail($"No rung named '{rungName}' on OpenCode's ladder.");
        return -1;
    }
}
