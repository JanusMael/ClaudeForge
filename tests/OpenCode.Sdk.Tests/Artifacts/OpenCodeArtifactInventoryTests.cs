using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Artifacts;

/// <summary>
/// The page's read model: what is listed, what the chain means, and what is wrong with it.
/// </summary>
/// <remarks>
/// ⛔⛔ <b>The claims here are the ones a user acts on.</b> "Two copies overridden" under a merging
/// agent tells them to delete a file that is contributing settings; "this skill is fine" over a
/// manifest with no <c>name:</c> leaves them debugging a skill that never loaded. Both would be
/// invisible without these tests, because both render as a perfectly plausible row.
/// </remarks>
[TestClass]
public sealed class OpenCodeArtifactInventoryTests
{
    private string _profile = null!;
    private string _worktree = null!;

    private string GlobalDir => Path.Combine(_profile, ".config", "opencode");

    [TestInitialize]
    public void Setup()
    {
        _profile = Path.Combine(Path.GetTempPath(), "ocinv-" + Path.GetRandomFileName());
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

    private static string Skill(string? name, string? description)
    {
        string n = name is null ? string.Empty : $"name: {name}\n";
        string d = description is null ? string.Empty : $"description: {description}\n";
        return $"---\n{n}{d}---\n\nbody\n";
    }

    private static string Agent(string description = "an agent")
        => $"---\ndescription: {description}\nmode: subagent\n---\n\nbody\n";

    private IReadOnlyList<OpenCodeArtifactGroup> Build()
        => OpenCodeArtifactInventory.Build(OpenCodeEnvironment.Empty, _worktree);

    private OpenCodeArtifactItem Require(ArtifactKind kind, string name)
    {
        OpenCodeArtifactGroup group = Build().Single(g => g.Kind == kind);
        OpenCodeArtifactItem? item = group.Items.FirstOrDefault(i => i.Name == name);
        Assert.IsNotNull(item, $"expected a {kind} named '{name}'. Present: "
            + string.Join(", ", group.Items.Select(i => i.Name)));
        return item;
    }

    /// <summary>
    /// The five tabs the plan specifies, in order, and only those.
    /// </summary>
    /// <remarks>
    /// ⚠ <see cref="ArtifactKind"/> is the shared vocabulary and includes kinds OpenCode has no
    /// analogue for. Rendering those as empty tabs would claim support that does not exist.
    /// </remarks>
    [TestMethod]
    public void TheGroupsAreTheFiveTabs_InOrder_AndNothingElse()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                ArtifactKind.Agent, ArtifactKind.Command, ArtifactKind.Skill,
                ArtifactKind.Memory, ArtifactKind.Plugin,
            },
            Build().Select(g => g.Kind).ToArray());

        CollectionAssert.DoesNotContain(Build().Select(g => g.Kind).ToArray(), ArtifactKind.Hook);
        CollectionAssert.DoesNotContain(Build().Select(g => g.Kind).ToArray(), ArtifactKind.Plan);
    }

    /// <summary>
    /// ⛔⛔ Measured: agent declarations deep-merge, so the lower ones are <b>live</b>. A row that
    /// calls them shadowed tells the user to delete a file that is still supplying fields.
    /// </summary>
    [TestMethod]
    public void AnAgentChainIsDeepMerge_SoTheLowerDeclarationsAreNotOverridden()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "dup.md"), Agent("project"));
        Seed(Path.Combine(GlobalDir, "agent", "dup.md"), Agent("global"));

        OpenCodeArtifactItem item = Require(ArtifactKind.Agent, "dup");

        Assert.AreEqual(OpenCodeChainSemantics.DeepMerge, item.Semantics);
        Assert.IsTrue(item.HasMultipleDeclarations);
        Assert.AreEqual(2, item.Declarations.Count);
    }

    /// <summary>
    /// ⛔⛔ And the inversion: a skill's losing copy really is never loaded, so "overridden" is the
    /// accurate word there and only there.
    /// </summary>
    [TestMethod]
    public void ASkillChainIsSingleWinner_SoTheLowerDeclarationsReallyAreOverridden()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "dup", "SKILL.md"), Skill("dup", "d"));
        Seed(Path.Combine(GlobalDir, "skills", "dup", "SKILL.md"), Skill("dup", "d"));

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "dup");

        Assert.AreEqual(OpenCodeChainSemantics.SingleWinner, item.Semantics);
        Assert.AreEqual(2, item.Declarations.Count);
        StringAssert.StartsWith(item.Effective.Scope.Id, "project:");
    }

    /// <summary>
    /// ✅ Measured: a manifest with no <c>name:</c> never appears among OpenCode's resolved skills.
    /// </summary>
    [TestMethod]
    public void ASkillWithNoDeclaredNameIsReportedInert()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "broken", "SKILL.md"),
            Skill(name: null, description: "has a description but no name"));

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "broken");

        CollectionAssert.Contains(item.Issues.ToArray(), OpenCodeArtifactIssue.SkillHasNoDeclaredName);
        Assert.IsTrue(item.IsInert, "this is a total failure, not a warning");
    }

    /// <summary>
    /// An inert skill is listed, never hidden — hiding it removes it from view at exactly the moment
    /// the user went looking for it.
    /// </summary>
    [TestMethod]
    public void AnInertSkillIsStillListed_NotFilteredOut()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "broken", "SKILL.md"),
            Skill(name: null, description: "d"));
        Seed(Path.Combine(_worktree, ".opencode", "skills", "fine", "SKILL.md"),
            Skill("fine", "d"));

        string[] names = [.. Build().Single(g => g.Kind == ArtifactKind.Skill).Items.Select(i => i.Name)];

        CollectionAssert.Contains(names, "broken");
        CollectionAssert.Contains(names, "fine");
    }

    /// <summary>
    /// ✅ Measured: the front matter wins, so the skill is invocable under a name nothing in the
    /// directory tree suggests. It loads, so this is a warning — not inert.
    /// </summary>
    [TestMethod]
    public void ASkillWhoseNameDiffersFromItsFolderIsWarned_ButNotInert()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "dirname-x", "SKILL.md"),
            Skill("frontmatter-y", "d"));

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "frontmatter-y");

        CollectionAssert.Contains(item.Issues.ToArray(), OpenCodeArtifactIssue.SkillNameDiffersFromFolder);
        Assert.IsFalse(item.IsInert, "it loads — it just loads under a surprising name");
    }

    /// <summary>
    /// ⚠ Spec-sourced, not measured: such a skill IS still discovered (measured), so it must not be
    /// reported as failing to load.
    /// </summary>
    [TestMethod]
    public void ASkillWithNoDescriptionIsWarned_ButNotReportedAsInert()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "quiet", "SKILL.md"),
            Skill("quiet", description: null));

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "quiet");

        CollectionAssert.Contains(item.Issues.ToArray(), OpenCodeArtifactIssue.SkillHasNoDescription);
        Assert.IsFalse(item.IsInert,
            "a missing description changes what the model sees, not whether the skill loads");
    }

    /// <summary>
    /// A correct skill carries no issues at all — otherwise the badge means nothing.
    /// </summary>
    [TestMethod]
    public void AWellFormedSkillHasNoIssues()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "good", "SKILL.md"),
            Skill("good", "does a thing, use when asked"));

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "good");

        Assert.AreEqual(0, item.Issues.Count, "issues: " + string.Join(", ", item.Issues));
        Assert.AreEqual("does a thing, use when asked", item.Description);
    }

    /// <summary>
    /// ⛔⛔ <b>The row that lied.</b>  A <c>description:</c> written as a folded block scalar —
    /// measured 2026-08-27 as the shape of 14 of the skills under <c>~/.claude/skills</c> — read
    /// as the literal marker <c>"&gt;-"</c>.  The page printed <c>&gt;-</c> where the description
    /// belonged, which is merely ugly; the damage was that <c>"&gt;-"</c> is non-empty, so
    /// <see cref="OpenCodeArtifactIssue.SkillHasNoDescription"/> never fired and the skill was
    /// reported as healthy.
    /// </summary>
    [TestMethod]
    public void ASkillWhoseDescriptionIsAFoldedBlockScalar_ShowsTheTextAndNoIssue()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "address-pr-feedback", "SKILL.md"),
            "---\n"
            + "name: address-pr-feedback\n"
            + "description: >-\n"
            + "  Fetch, vet, and act on review feedback left on a pull request\n"
            + "  - from AI reviewers or humans.\n"
            + "---\n\nbody\n");

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "address-pr-feedback");

        Assert.AreEqual(
            "Fetch, vet, and act on review feedback left on a pull request - from AI reviewers or humans.",
            item.Description,
            "The page must show the description, not the block-scalar marker.");
        Assert.AreEqual(0, item.Issues.Count, "issues: " + string.Join(", ", item.Issues));
    }

    /// <summary>
    /// ⛔⛔ The silent-wrong-answer itself: a block scalar with no body declares nothing, and the
    /// warning has to fire.  Before the fix the non-empty marker suppressed it.
    /// </summary>
    [TestMethod]
    public void ASkillWhoseBlockScalarDescriptionIsEmpty_IsStillWarned()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "hollow", "SKILL.md"),
            "---\nname: hollow\ndescription: >-\n---\n\nbody\n");

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "hollow");

        CollectionAssert.Contains(item.Issues.ToArray(), OpenCodeArtifactIssue.SkillHasNoDescription,
            "An unreadable description reported as healthy is worse than one reported as missing.");
        Assert.IsNull(item.Description);
    }

    /// <summary>
    /// ⛔⛔ <b>The phantom field, caught where it changes a different answer.</b>  A continuation
    /// line was split on its first colon into a top-level field, and the key was taken
    /// <i>trimmed</i> — so a description line reading <c>name: my-agent</c> produced a real
    /// <c>name</c> field.  It sat above the genuine one, and lookup takes the first match, so the
    /// page read the skill's name out of its own prose: it reported
    /// <see cref="OpenCodeArtifactIssue.SkillNameDiffersFromFolder"/> against a skill whose
    /// <c>name:</c> is perfectly correct, and named the wrong winner confidently.
    /// </summary>
    [TestMethod]
    public void ASkillWhoseDescriptionMentionsAKey_DoesNotHaveThatKeyReadOutOfItsProse()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "ordered", "SKILL.md"),
            "---\n"
            + "description: >-\n"
            + "  Sets the identity fields, for example\n"
            + "  name: my-agent\n"
            + "  and nothing else.\n"
            + "name: ordered\n"
            + "---\n\nbody\n");

        OpenCodeArtifactItem item = Require(ArtifactKind.Skill, "ordered");

        Assert.AreEqual(0, item.Issues.Count,
            "The declared name is 'ordered', which matches the folder. issues: "
            + string.Join(", ", item.Issues));
        Assert.AreEqual("Sets the identity fields, for example name: my-agent and nothing else.",
            item.Description,
            "And the sentence stays in the description it was written in.");
    }

    /// <summary>
    /// ⭐ Editing one of these edits Claude Code's installation, so the row has to say so.
    /// </summary>
    [TestMethod]
    public void AUserLevelClaudeSkillIsBadgedCrossTool()
    {
        Seed(Path.Combine(_profile, ".claude", "skills", "shared", "SKILL.md"), Skill("shared", "d"));

        Assert.IsTrue(Require(ArtifactKind.Skill, "shared").IsCrossTool);
    }

    /// <summary>
    /// ⚠⚠ The case the scope alone cannot answer: a project's own <c>.claude/skills/</c> is in the
    /// <b>project</b> scope — the same scope as <c>.opencode/skills/</c> beside it.
    /// </summary>
    /// <remarks>
    /// Badging off <c>scope.Id</c> would catch the two user-level roots and silently miss this one,
    /// which is the copy sitting inside the user's own repository and the likeliest to be edited.
    /// </remarks>
    [TestMethod]
    public void AProjectsOwnDotClaudeSkillIsAlsoBadgedCrossTool()
    {
        Seed(Path.Combine(_worktree, ".claude", "skills", "inrepo", "SKILL.md"), Skill("inrepo", "d"));

        Assert.IsTrue(Require(ArtifactKind.Skill, "inrepo").IsCrossTool);
    }

    /// <summary>
    /// And OpenCode's own directories are not badged, or the badge is decoration.
    /// </summary>
    [TestMethod]
    public void ASkillInOpenCodesOwnDirectoryIsNotBadgedCrossTool()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "mine", "SKILL.md"), Skill("mine", "d"));

        Assert.IsFalse(Require(ArtifactKind.Skill, "mine").IsCrossTool);
    }

    /// <summary>
    /// Rows are ordered by name so the list is scannable and stable between loads.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>The names are deliberately split ACROSS SOURCES, and an earlier version of this test
    /// was vacuous for want of that.</b> Seeding all three into one directory proved nothing:
    /// the resolver preserves source order, and a single directory walk already hands names back
    /// alphabetically, so removing the sort entirely reddened <b>nothing</b>. Putting <c>zebra</c>
    /// in the project and <c>alpha</c>/<c>Mango</c> in the global directory makes the natural order
    /// <c>zebra, alpha, Mango</c> — which only the sort can fix. <c>Mango</c> also pins that the
    /// comparison is case-insensitive; an ordinal sort would put it before <c>alpha</c>.
    /// </remarks>
    [TestMethod]
    public void ItemsAreOrderedByName()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "zebra.md"), Agent());
        Seed(Path.Combine(GlobalDir, "agent", "alpha.md"), Agent());
        Seed(Path.Combine(GlobalDir, "agent", "Mango.md"), Agent());

        string[] agents = [.. Build().Single(g => g.Kind == ArtifactKind.Agent).Items
            .Select(i => i.Name)
            .Where(n => n is "alpha" or "Mango" or "zebra")];

        CollectionAssert.AreEqual(new[] { "alpha", "Mango", "zebra" }, agents);
    }

    /// <summary>
    /// Non-skill kinds are not diagnosed, and must not inherit a skill's issue vocabulary.
    /// </summary>
    [TestMethod]
    public void AnAgentIsNotDiagnosedAgainstSkillRules()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "plain.md"), Agent());

        OpenCodeArtifactItem item = Require(ArtifactKind.Agent, "plain");

        Assert.AreEqual(0, item.Issues.Count,
            "an agent has no SKILL.md, so skill diagnoses cannot apply to it");
        Assert.IsFalse(item.IsInert);
    }

    /// <summary>
    /// A built-in agent has no file, so nothing may try to read one.
    /// </summary>
    [TestMethod]
    public void ABuiltInAgentIsListedWithNoIssuesAndNoDescription()
    {
        OpenCodeArtifactItem item = Require(ArtifactKind.Agent, "plan");

        Assert.AreEqual(ArtifactForm.BuiltIn, item.Effective.Form);
        Assert.AreEqual(0, item.Issues.Count);
        Assert.IsNull(item.Description);
    }
}
