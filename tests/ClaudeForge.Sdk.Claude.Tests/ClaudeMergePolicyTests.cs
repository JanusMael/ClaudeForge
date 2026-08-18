using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests;

/// <summary>
/// Pins <see cref="ClaudeMergePolicy"/> — Claude's documented merge rules, which until Phase
/// 4c were a private static list inside the product-neutral <c>SettingsWorkspace</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Written because nothing asserted them.</b> The list moved without a single test
/// failing, which is the same hole Phase 4a found by transposing the two products: almost
/// nothing in the suite distinguishes one product's rules from another's, so "green after
/// the refactor" says very little. Emptying the list entirely also kept the suite green,
/// because nearly every workspace built in tests holds ONE document, and a single scope has
/// nothing to union with.
/// </para>
/// <para>
/// The whole list is enumerated deliberately. A test that spot-checked two paths would let
/// the other nine be dropped silently, and each one is a documented Claude behaviour: losing
/// <c>permissions.deny</c> from it means a project-level deny stops being added to the
/// user's and starts replacing it.
/// </para>
/// </remarks>
[TestClass]
public class ClaudeMergePolicyTests
{
    /// <summary>
    /// Every path Claude's documentation says merges across scopes rather than overriding.
    /// </summary>
    private static readonly string[] DeclaredUnionPaths =
    [
        "claudeMdExcludes",
        "availableModels",
        "httpHookAllowedEnvVars",
        "allowedHttpHookUrls",
        "permissions.allow",
        "permissions.deny",
        "permissions.ask",
        "permissions.additionalDirectories",
        "enabledMcpjsonServers",
        "disabledMcpjsonServers",
        "companyAnnouncements",
    ];

    [TestMethod]
    public void EveryDocumentedPath_Unions_EvenWhenTheValuesAreNotArrays()
    {
        // everyValueIsArray: false isolates the DECLARATION from the inference — a pass here
        // can only come from the path being in the list. With `true` this test would pass
        // even against an empty list, which is exactly how the list moved unnoticed.
        foreach (string path in DeclaredUnionPaths)
        {
            Assert.IsTrue(
                ClaudeMergePolicy.Instance.UnionsAt(path, everyValueIsArray: false),
                $"'{path}' is documented as merging across scopes. If it stops unioning, a "
                + "lower scope's entries are silently dropped instead of contributed.");
        }
    }

    [TestMethod]
    public void UndeclaredScalarPath_DoesNotUnion()
    {
        // The counter-direction: without this, a policy that returned true unconditionally
        // would satisfy the test above and quietly union every scalar in the file.
        Assert.IsFalse(
            ClaudeMergePolicy.Instance.UnionsAt("model", everyValueIsArray: false),
            "A scalar setting must be won outright by the highest-priority scope.");
        Assert.IsFalse(
            ClaudeMergePolicy.Instance.UnionsAt("permissions.defaultMode", everyValueIsArray: false),
            "A nested scalar is no different — the dotted path is not itself a union hint.");
    }

    [TestMethod]
    public void UndeclaredPath_Unions_WhenEveryScopeHoldsAnArray()
    {
        // Claude infers union-ness for array paths its list does not name; the list records
        // what the docs state, not an exhaustive schema walk. Pinned because it is precisely
        // what OpenCode must NOT do — replacing is its default for arrays it has not listed
        // (Spike S1) — so the two products differ here and the difference is easy to lose.
        Assert.IsTrue(
            ClaudeMergePolicy.Instance.UnionsAt("someFutureArraySetting", everyValueIsArray: true),
            "An all-array path unions even when undeclared.");
    }

    [TestMethod]
    public void UnionOrder_IsHighestPriorityFirst()
    {
        // Claude presents the winning scope's entries first. OpenCode was measured doing the
        // opposite (Spike S1), so this is a real product difference rather than a default.
        Assert.AreEqual(
            MergeUnionOrder.HighestPriorityFirst,
            ClaudeMergePolicy.Instance.UnionOrder);
    }
}
