namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// every rule in
/// <see cref="PermissionsEditorViewModel.AllToolGroups"/> carries an explicit
/// <see cref="CommonActionKind"/>, the catch-all wildcard tier is the trailing
/// entry, and tools order their operation groups safe-first.  These assertions
/// guard against silent regressions when contributors add new rules.
/// </summary>
[TestClass]
public sealed class CommonActionsTaxonomyTests
{
    private static IReadOnlyList<ToolActionGroup> All => PermissionsEditorViewModel.AllToolGroups;

    // ── Structural shape ─────────────────────────────────────────────

    [TestMethod]
    public void AllToolGroups_HasAtLeastFourTools_PlusCatchAll()
    {
        // Concrete tools (File, Bash, PowerShell, Web) + 1 catch-all.
        List<ToolActionGroup> concrete = All.Where(t => !t.IsCatchAll).ToList();
        List<ToolActionGroup> catchAll = All.Where(t => t.IsCatchAll).ToList();

        Assert.IsTrue(concrete.Count >= 4,
            $"Expected at least 4 concrete tool groups; got {concrete.Count}.");
        Assert.AreEqual(1, catchAll.Count,
            "Exactly one ToolActionGroup must have IsCatchAll = true.");
    }

    [TestMethod]
    public void CatchAll_IsTrailingEntry()
    {
        Assert.IsTrue(All.Count > 0);
        Assert.IsTrue(All[^1].IsCatchAll,
            "The catch-all wildcard tier must be the last entry of AllToolGroups so the View "
            + "renders it pinned at the bottom outside the per-tool accordion stack.");
        for (int i = 0; i < All.Count - 1; i++)
        {
            Assert.IsFalse(All[i].IsCatchAll,
                $"Non-trailing entry {All[i].Tool} must not be marked IsCatchAll.");
        }
    }

    [TestMethod]
    public void EveryTool_HasAtLeastOneOperationGroup_AndAtLeastOneItem()
    {
        foreach (ToolActionGroup tool in All)
        {
            Assert.IsTrue(tool.OperationGroups.Count > 0,
                $"Tool '{tool.Tool}' has no operation groups; the AXAML would render an empty Expander.");
            foreach (CommonActionGroup group in tool.OperationGroups)
            {
                Assert.IsTrue(group.Items.Count > 0,
                    $"Tool '{tool.Tool}' / group '{group.Header}' has no items; an empty group "
                    + "produces an orphan section header in the View.");
            }
        }
    }

    // ── Kind classification ──────────────────────────────────────────

    [TestMethod]
    public void EveryRule_HasKindClassified()
    {
        // Defensive: this would fail at compile time today (CommonActionItem
        // requires Kind in its primary constructor) but the test guards
        // against a future change that introduces a default.
        List<CommonActionItem> allItems = All
                                          .SelectMany(t => t.OperationGroups)
                                          .SelectMany(g => g.Items)
                                          .ToList();
        Assert.IsTrue(allItems.Count > 0, "AllToolGroups must contain rules.");
        Assert.IsTrue(allItems.All(i => Enum.IsDefined(typeof(CommonActionKind), i.Kind)),
            "Every CommonActionItem.Kind must be a defined CommonActionKind value.");
    }

    [TestMethod]
    public void KnownReadEntries_AreClassifiedRead()
    {
        AssertKindForRules(CommonActionKind.Read,
            "Read", "Glob", "Grep",
            "Bash(cat *)", "Bash(ls *)", "Bash(git status)", "Bash(git log *)");
    }

    [TestMethod]
    public void KnownWriteEntries_AreClassifiedWrite()
    {
        AssertKindForRules(CommonActionKind.Write,
            "Edit", "Write",
            "Bash(git add *)", "Bash(git commit *)");
    }

    [TestMethod]
    public void KnownNetworkEntries_AreClassifiedNetwork()
    {
        AssertKindForRules(CommonActionKind.Network,
            "Bash(npm *)", "Bash(curl *)", "Bash(wget *)",
            "WebFetch", "WebSearch", "mcp__*");
    }

    [TestMethod]
    public void KnownDestructiveEntries_AreClassifiedDestructive()
    {
        // git push is irreversible (rewrites remote state visible to others).
        // Raw Bash / PowerShell wildcard rules grant unrestricted shell access.
        AssertKindForRules(CommonActionKind.Destructive,
            "Bash(git push *)", "PowerShell(git push *)",
            "Bash", "PowerShell", "Pwsh");
    }

    // ── Safe-first group ordering inside each tool ────────────────────

    [TestMethod]
    public void OperationGroups_OrderedSafeFirstWithinTool()
    {
        // The contract: the FIRST item's kind in each group is non-decreasing
        // by safety rank (Read < Write < Network < Destructive). Items within
        // a single group may mix kinds (e.g. Git (write) ends with the
        // Destructive `git push *`); the test only locks the inter-group
        // ordering, since that's what defines "safe operations before
        // dangerous operations" at the user-visible accordion level.
        foreach (ToolActionGroup tool in All.Where(t => !t.IsCatchAll))
        {
            List<int> ranks = tool.OperationGroups
                                  .Select(g => SafetyRank(g.Items[0].Kind))
                                  .ToList();
            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.IsTrue(ranks[i] >= ranks[i - 1],
                    $"Tool '{tool.Tool}': operation group #{i} ('{tool.OperationGroups[i].Header}') "
                    + $"has rank {ranks[i]} but follows '{tool.OperationGroups[i - 1].Header}' "
                    + $"with rank {ranks[i - 1]}. Operation groups must be safe-first within a tool.");
            }
        }
    }

    // ── Catch-all contents ───────────────────────────────────────────

    [TestMethod]
    public void CatchAll_ItemsOrderedSafeFirst()
    {
        // The catch-all "All Tools" tier is the most consequential surface
        // because each rule grants its kind across EVERY tool. Listing the
        // dangerous shell-access wildcards (Bash, PowerShell, Pwsh) first
        // would be poor pit-of-success design — the user's eye should land
        // on safe Read rules first and have to scroll past Write + Network
        // to reach the Destructive bare-tool wildcards. This test asserts
        // that within-group safety rank is non-decreasing top-to-bottom.
        ToolActionGroup catchAll = All.Single(t => t.IsCatchAll);
        foreach (CommonActionGroup group in catchAll.OperationGroups)
        {
            List<int> ranks = group.Items.Select(i => SafetyRank(i.Kind)).ToList();
            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.IsTrue(ranks[i] >= ranks[i - 1],
                    $"Catch-all group '{group.Header}': item #{i} ('{group.Items[i].Rule}', "
                    + $"{group.Items[i].Kind}) precedes item #{i - 1} ('{group.Items[i - 1].Rule}', "
                    + $"{group.Items[i - 1].Kind}) in safety rank. Catch-all items must be safe-first "
                    + "so dangerous wildcards are below safer affordances.");
            }
        }
    }

    [TestMethod]
    public void CatchAll_ContainsKnownWildcards()
    {
        ToolActionGroup catchAll = All.Single(t => t.IsCatchAll);
        List<string> rules = catchAll.OperationGroups
                                     .SelectMany(g => g.Items)
                                     .Select(i => i.Rule)
                                     .ToList();

        // Sanity: at minimum the bare-tool wildcards are present.
        CollectionAssert.Contains(rules, "Bash");
        CollectionAssert.Contains(rules, "PowerShell");
        CollectionAssert.Contains(rules, "WebFetch");
        CollectionAssert.Contains(rules, "mcp__*");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static void AssertKindForRules(CommonActionKind expected, params string[] rules)
    {
        List<CommonActionItem> allItems = All
                                          .SelectMany(t => t.OperationGroups)
                                          .SelectMany(g => g.Items)
                                          .ToList();
        foreach (string rule in rules)
        {
            List<CommonActionItem> found = allItems.Where(i => i.Rule == rule).ToList();
            Assert.IsTrue(found.Count > 0,
                $"Sanity: rule '{rule}' must exist somewhere in AllToolGroups.");
            Assert.IsTrue(found.All(i => i.Kind == expected),
                $"Rule '{rule}' must be classified as {expected}; saw "
                + $"{string.Join(", ", found.Select(i => i.Kind.ToString()))}.");
        }
    }

    /// <summary>
    /// Maps <see cref="CommonActionKind"/> to a numeric safety rank
    /// (Read = 0 → Destructive = 3) used by the safe-first ordering test.
    /// </summary>
    private static int SafetyRank(CommonActionKind kind)
    {
        return kind switch
        {
            CommonActionKind.Read => 0,
            CommonActionKind.Write => 1,
            CommonActionKind.Network => 2,
            CommonActionKind.Destructive => 3,
            var _ => int.MaxValue,
        };
    }
}