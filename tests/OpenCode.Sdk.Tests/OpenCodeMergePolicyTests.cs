using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// One test per key in spike S1's measured table, as the plan asks. The two mistakes this
/// guards are both data-losing and both silent: unioning a replace-key resurrects a provider
/// the user disabled, and replacing a union-key drops the global <c>AGENTS.md</c> out of
/// <c>instructions</c>.
/// </summary>
[TestClass]
public class OpenCodeMergePolicyTests
{
    private static readonly IMergePolicy Policy = OpenCodeMergePolicy.Instance;

    /// <summary>
    /// The two union keys S1 measured. <c>everyValueIsArray: true</c> throughout because
    /// that is what these paths really hold — the point is that the answer does not come
    /// from that fact.
    /// </summary>
    [TestMethod]
    [DataRow("instructions")]
    [DataRow("plugin")]
    public void UnionKeys_Union(string path)
    {
        Assert.IsTrue(Policy.UnionsAt(path, everyValueIsArray: true),
            $"S1 measured '{path}' accumulating across layers. Replacing it silently drops "
            + "entries the user expects to still apply.");
    }

    /// <summary>
    /// The five replace keys S1 measured. Each is an array by shape, which is exactly why
    /// they are the dangerous ones: a policy that inferred union-ness from the value would
    /// get all five wrong and look reasonable doing it.
    /// </summary>
    [TestMethod]
    [DataRow("disabled_providers")]
    [DataRow("enabled_providers")]
    [DataRow("skills.paths")]
    [DataRow("skills.urls")]
    [DataRow("experimental.primary_tools")]
    public void ReplaceKeys_DoNotUnion_EvenThoughTheyAreArrays(string path)
    {
        Assert.IsFalse(Policy.UnionsAt(path, everyValueIsArray: true),
            $"S1 measured the highest-priority layer replacing '{path}' outright. Unioning "
            + "it brings back entries a higher layer deliberately removed — for "
            + "disabled_providers that means re-enabling a provider the user turned off.");
    }

    /// <summary>
    /// The inference trap, stated directly rather than only implied by the key tables. The
    /// interface passes <c>everyValueIsArray</c> because Claude Code uses it; OpenCode must
    /// not, and this pins that the parameter changes no answer.
    /// </summary>
    [TestMethod]
    [DataRow("instructions")]
    [DataRow("disabled_providers")]
    [DataRow("some.undeclared.path")]
    public void TheAnswerNeverDependsOnValueShape(string path)
    {
        Assert.AreEqual(
            Policy.UnionsAt(path, everyValueIsArray: true),
            Policy.UnionsAt(path, everyValueIsArray: false),
            $"'{path}' answered differently depending on the values it happened to hold. "
            + "Replacing is OpenCode's default for arrays it has not declared, so inferring "
            + "union-ness from shape is precisely the mistake this product must not make.");
    }

    /// <summary>
    /// An unknown path replaces. This is the failure mode chosen when the table falls behind
    /// upstream: a value the user set may be overridden by a higher layer, rather than a
    /// value the user removed coming back.
    /// </summary>
    [TestMethod]
    public void AnUnknownPath_Replaces()
    {
        Assert.IsFalse(Policy.UnionsAt("something.upstream.added.later", everyValueIsArray: true));
    }

    /// <summary>
    /// Not cosmetic. OpenCode evaluates the LAST matching permission rule, so a union built
    /// from the wrong end of the ladder inverts the user's intent with no file edited. This
    /// is also the one place OpenCode's order is contrasted with Claude's directly.
    /// </summary>
    [TestMethod]
    public void UnionOrder_IsLowestPriorityFirst_UnlikeClaude()
    {
        Assert.AreEqual(MergeUnionOrder.LowestPriorityFirst, Policy.UnionOrder,
            "S1 measured [\"global-x.md\", \"proj-a.md\", \"proj-b.md\", \"inline-z.md\"] — "
            + "lowest layer first. Reversed, a broad rule intended to sit first ends up last "
            + "and wins instead.");

        Assert.AreNotEqual(MergeUnionOrder.HighestPriorityFirst, Policy.UnionOrder);
    }

    /// <summary>Paths are matched exactly; a prefix is a different key, not the same one.</summary>
    [TestMethod]
    [DataRow("skills")]
    [DataRow("instruction")]
    [DataRow("instructions.extra")]
    [DataRow("Instructions")]
    public void MatchingIsExactAndCaseSensitive(string path)
    {
        Assert.IsFalse(Policy.UnionsAt(path, everyValueIsArray: true),
            $"'{path}' is not one of the two measured union paths and must not be treated "
            + "as one. JSON keys are case-sensitive.");
    }
}
