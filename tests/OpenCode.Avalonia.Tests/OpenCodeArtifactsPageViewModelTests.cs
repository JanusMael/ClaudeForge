using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Avalonia.Artifacts;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// The artifacts page: what it lists, what it says about a chain, and what the filter does.
/// </summary>
/// <remarks>
/// ⛔⛔ <b>The chain sentences are the assertions that matter.</b> "Merged from 2 declarations, the
/// lower ones are still live" and "highest of 2, the rest never load" are opposite claims about the
/// same count, and which is true depends on the KIND. Both render as a perfectly plausible row, so
/// nothing but a test distinguishes the page telling the truth from the page telling a user to
/// delete a file that is in force.
/// </remarks>
[TestClass]
public sealed class OpenCodeArtifactsPageViewModelTests
{
    private string _profile = null!;
    private string _worktree = null!;

    private string GlobalDir => Path.Combine(_profile, ".config", "opencode");

    [TestInitialize]
    public void Setup()
    {
        _profile = Path.Combine(Path.GetTempPath(), "ocpage-" + Path.GetRandomFileName());
        _worktree = Path.Combine(_profile, "repo");
        Directory.CreateDirectory(_worktree);
        Directory.CreateDirectory(Path.Combine(_worktree, ".git"));
        PlatformPaths.TestUserProfileOverride = _profile;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_profile))
            {
                Directory.Delete(_profile, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    private static void Seed(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Skill(string? name, string? description = "a skill")
    {
        string n = name is null ? string.Empty : $"name: {name}\n";
        string d = description is null ? string.Empty : $"description: {description}\n";
        return $"---\n{n}{d}---\n\nbody\n";
    }

    private static string Agent(string description = "an agent")
        => $"---\ndescription: {description}\nmode: subagent\n---\n\nbody\n";

    private OpenCodeArtifactsPageViewModel Page()
        => new(OpenCodeEnvironment.Empty, _worktree);

    private static OpenCodeArtifactTabViewModel Tab(
        OpenCodeArtifactsPageViewModel page, ArtifactKind kind)
        => page.Tabs.Single(t => t.Kind == kind);

    private static OpenCodeArtifactRowViewModel Row(
        OpenCodeArtifactsPageViewModel page, ArtifactKind kind, string name)
    {
        OpenCodeArtifactTabViewModel tab = Tab(page, kind);
        OpenCodeArtifactRowViewModel? row = tab.Rows.FirstOrDefault(r => r.Name == name);
        Assert.IsNotNull(row, $"no {kind} row named '{name}'. Present: "
            + string.Join(", ", tab.Rows.Select(r => r.Name)));
        return row;
    }

    [TestMethod]
    public void TheFiveTabsAppearInTheOrderThePlanSpecifies()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                ArtifactKind.Agent, ArtifactKind.Command, ArtifactKind.Skill,
                ArtifactKind.Memory, ArtifactKind.Plugin,
            },
            Page().Tabs.Select(t => t.Kind).ToArray());
    }

    /// <summary>
    /// ⛔⛔ An agent's lower declarations are LIVE. The sentence must not call them overridden.
    /// </summary>
    [TestMethod]
    public void AnAgentsChainSentenceSaysTheLowerDeclarationsAreStillLive()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "dup.md"), Agent("project"));
        Seed(Path.Combine(GlobalDir, "agent", "dup.md"), Agent("global"));

        OpenCodeArtifactRowViewModel row = Row(Page(), ArtifactKind.Agent, "dup");

        Assert.IsTrue(row.HasChain);
        StringAssert.Contains(row.ChainSummary, "Merged",
            "an agent's chain merges per field — saying anything else tells the user to delete "
            + "a file that is supplying settings");
        Assert.IsFalse(row.ChainSummary.Contains("never load", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⛔⛔ And the inversion: a skill's losing copy really is never loaded.
    /// </summary>
    [TestMethod]
    public void ASkillsChainSentenceSaysTheOthersNeverLoad()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "dup", "SKILL.md"), Skill("dup"));
        Seed(Path.Combine(GlobalDir, "skills", "dup", "SKILL.md"), Skill("dup"));

        OpenCodeArtifactRowViewModel row = Row(Page(), ArtifactKind.Skill, "dup");

        Assert.IsTrue(row.HasChain);
        StringAssert.Contains(row.ChainSummary, "never load");
        Assert.IsFalse(row.ChainSummary.Contains("Merged", StringComparison.Ordinal));
    }

    /// <summary>A single declaration is not a chain, so no chain sentence appears at all.</summary>
    [TestMethod]
    public void ASoleDeclarationShowsNoChainSentence()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "lonely.md"), Agent());

        Assert.IsFalse(Row(Page(), ArtifactKind.Agent, "lonely").HasChain);
    }

    /// <summary>
    /// ⚠ Filtering must reach every tab, or an unvisited tab's count contradicts the filter that
    /// is plainly active on screen.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>An earlier version of this test could not fail.</b> It seeded one agent and one skill,
    /// filtered for the skill, and checked the agent was gone and the skill remained — which passes
    /// whether or not the UNSELECTED tab was filtered, because the surviving row was the one being
    /// searched for. Caught by a canary that filtered only the selected tab and reddened nothing.
    /// The assertion that bites is on a row in an unselected tab that should have been REMOVED.
    /// </remarks>
    [TestMethod]
    public void FilteringAppliesToEveryTab_NotOnlyTheSelectedOne()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "beta", "SKILL.md"), Skill("beta"));
        Seed(Path.Combine(_worktree, ".opencode", "skills", "gamma", "SKILL.md"), Skill("gamma"));

        OpenCodeArtifactsPageViewModel page = Page();
        Assert.AreEqual(ArtifactKind.Agent, page.SelectedTab?.Kind,
            "this test needs Skills to be the UNSELECTED tab for the filter to prove anything");

        page.FilterText = "beta";

        OpenCodeArtifactTabViewModel skills = Tab(page, ArtifactKind.Skill);
        Assert.AreEqual(1, skills.Rows.Count(r => r.Name == "beta"));
        Assert.AreEqual(0, skills.Rows.Count(r => r.Name == "gamma"),
            "the unselected tab must be filtered too, or its count contradicts the active filter");
    }

    /// <summary>
    /// ⚠ "What is reading from that directory?" is a question users bring to this page, and a
    /// name-only filter answers it with an empty list.
    /// </summary>
    [TestMethod]
    public void TheFilterMatchesAPathAsWellAsAName()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "nested", "reviewer.md"), Agent());

        OpenCodeArtifactsPageViewModel page = Page();
        page.FilterText = ".opencode";

        Assert.IsTrue(Tab(page, ArtifactKind.Agent).Rows.Any(r => r.Name == "nested/reviewer"));
    }

    /// <summary>
    /// A count over a filtered list must say what is hidden, not imply it is everything.
    /// </summary>
    [TestMethod]
    public void TheCountSaysWhatIsHiddenWhileFilteringAndStaysQuietOtherwise()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "alpha.md"), Agent());
        Seed(Path.Combine(_worktree, ".opencode", "agent", "zulu.md"), Agent());

        OpenCodeArtifactsPageViewModel page = Page();
        string unfiltered = Tab(page, ArtifactKind.Agent).CountLabel;

        page.FilterText = "alpha";
        string filtered = Tab(page, ArtifactKind.Agent).CountLabel;

        Assert.AreNotEqual(unfiltered, filtered);
        StringAssert.Contains(filtered, "of",
            "a filtered count has to name the total it is a subset of");
    }

    /// <summary>
    /// ⭐ "You have none of these" and "your filter excluded them" are different facts.
    /// </summary>
    [TestMethod]
    public void AnEmptyTabDistinguishesHavingNoneFromFilteringThemAllOut()
    {
        Seed(Path.Combine(_worktree, ".opencode", "command", "deploy.md"),
            "---\ndescription: d\n---\n\nbody\n");

        OpenCodeArtifactsPageViewModel page = Page();
        string none = Tab(page, ArtifactKind.Plugin).EmptyMessage;

        page.FilterText = "nothing-matches-this";
        string filteredOut = Tab(page, ArtifactKind.Command).EmptyMessage;

        // ⚠⚠ Asserts WHICH sentence each is, not merely that they differ. An earlier version
        // compared them with AreNotEqual, and a canary that SWAPPED the two messages reddened
        // nothing — swapping keeps them different while making both of them wrong.
        Assert.AreEqual(Strings.ArtifactsEmptyNone, none,
            "a tab with nothing in it must say you have none of these");
        Assert.AreEqual(Strings.ArtifactsEmptyFiltered, filteredOut,
            "a tab emptied by the filter must say so, or it sends a user looking for a file "
            + "they do have");
    }

    /// <summary>
    /// Setting a project folder rebuilds the page against it — the whole reason the box exists.
    /// </summary>
    [TestMethod]
    public void SettingTheWorkingDirectoryReloadsAgainstThatProject()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "projectonly.md"), Agent());

        OpenCodeArtifactsPageViewModel page = new(OpenCodeEnvironment.Empty, null);
        Assert.IsTrue(page.HasNoProject);
        Assert.IsFalse(Tab(page, ArtifactKind.Agent).Rows.Any(r => r.Name == "projectonly"));

        page.WorkingDirectory = _worktree;

        Assert.IsFalse(page.HasNoProject);
        Assert.IsTrue(Tab(page, ArtifactKind.Agent).Rows.Any(r => r.Name == "projectonly"));
    }

    /// <summary>
    /// ⚠ A reload leaves the user where they were reading, rather than snapping back to Agents.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>An earlier version of this test could not fail either.</b> It built the page already
    /// pointed at the worktree and then assigned that same path again — and the generated setter
    /// no-ops on an equal value, so no reload ever ran and the selection trivially survived. Caught
    /// by a canary that snapped back to the first tab and reddened nothing. It now calls
    /// <see cref="OpenCodeArtifactsPageViewModel.Reload"/> directly, so the reload is not in doubt.
    /// </remarks>
    [TestMethod]
    public void ReloadingKeepsTheSelectedTab()
    {
        OpenCodeArtifactsPageViewModel page = Page();
        page.SelectedTab = Tab(page, ArtifactKind.Skill);

        page.Reload();

        Assert.AreEqual(ArtifactKind.Skill, page.SelectedTab?.Kind);
    }

    /// <summary>
    /// ⚠ A reload produces unfiltered tabs, so the filter has to be re-applied or the box sits
    /// populated beside a list plainly ignoring it.
    /// </summary>
    [TestMethod]
    public void ReloadingReappliesTheActiveFilter()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "alpha.md"), Agent());
        Seed(Path.Combine(_worktree, ".opencode", "agent", "zulu.md"), Agent());

        OpenCodeArtifactsPageViewModel page = new(OpenCodeEnvironment.Empty, null);
        page.FilterText = "zulu";
        page.WorkingDirectory = _worktree;

        IReadOnlyList<string> names = [.. Tab(page, ArtifactKind.Agent).Rows.Select(r => r.Name)];

        CollectionAssert.Contains(names.ToArray(), "zulu");
        CollectionAssert.DoesNotContain(names.ToArray(), "alpha");
    }

    /// <summary>
    /// A skill OpenCode will not load is badged and explained, and still listed.
    /// </summary>
    [TestMethod]
    public void AnInertSkillIsBadgedAndExplained()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "broken", "SKILL.md"),
            Skill(name: null));

        OpenCodeArtifactRowViewModel row = Row(Page(), ArtifactKind.Skill, "broken");

        Assert.IsTrue(row.IsInert);
        Assert.IsTrue(row.HasIssues);
        Assert.IsTrue(row.IssueMessages.Count > 0);
    }

    /// <summary>
    /// ⭐ Editing a cross-tool file changes another tool's installation, so the row says so.
    /// </summary>
    [TestMethod]
    public void ACrossToolRowIsBadgedAndTheTooltipExplainsWhyItMatters()
    {
        Seed(Path.Combine(_profile, ".claude", "skills", "shared", "SKILL.md"), Skill("shared"));

        OpenCodeArtifactRowViewModel row = Row(Page(), ArtifactKind.Skill, "shared");

        Assert.IsTrue(row.IsCrossTool);
        StringAssert.Contains(row.CrossToolTooltip, "another tool");
    }

    /// <summary>
    /// A built-in agent has no file, so nothing offers to open one for it.
    /// </summary>
    [TestMethod]
    public void ABuiltInAgentReportsNoFileToOpen()
    {
        OpenCodeArtifactRowViewModel row = Row(Page(), ArtifactKind.Agent, "plan");

        Assert.IsFalse(row.Declarations[0].HasFile);
        Assert.IsFalse(row.HasIssues);
    }
}
