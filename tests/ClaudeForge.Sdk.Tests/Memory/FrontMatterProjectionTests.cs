using System.Linq;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Memory;

/// <summary>
/// Locks the typed front-matter projections (<see cref="AgentFrontMatter"/>,
/// <see cref="SkillFrontMatter"/>, <see cref="SlashCommandFrontMatter"/>)
/// that the editor UI binds to.  The key behaviours: tolerant <c>tools</c>
/// parsing (comma-scalar vs YAML list), null-on-absent for optional keys,
/// and the guarantee that projecting NEVER loses un-modelled keys from the
/// underlying <see cref="FrontMatter"/>.
/// </summary>
[TestClass]
public sealed class FrontMatterProjectionTests
{
    // ── AgentFrontMatter ─────────────────────────────────────────────────

    [TestMethod]
    public void Agent_From_ReadsAllCanonicalScalars()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\n" +
            "name: code-reviewer\n" +
            "description: Expert reviewer\n" +
            "model: sonnet\n" +
            "tools: Read, Grep, Bash\n" +
            "---\n\nBody.\n");

        AgentFrontMatter agent = AgentFrontMatter.From(fm);

        Assert.AreEqual("code-reviewer", agent.Name);
        Assert.AreEqual("Expert reviewer", agent.Description);
        Assert.AreEqual("sonnet", agent.Model);
        CollectionAssert.AreEqual(new[] { "Read", "Grep", "Bash" }, agent.Tools.ToArray());
    }

    [TestMethod]
    public void Agent_From_CommaScalarTools_SplitToList()
    {
        // Claude Code's native form: tools is a comma-separated scalar.
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ntools: Read, Grep, Glob, Bash\n---\n\nBody.\n");

        AgentFrontMatter agent = AgentFrontMatter.From(fm);

        CollectionAssert.AreEqual(new[] { "Read", "Grep", "Glob", "Bash" }, agent.Tools.ToArray(),
            "A comma-separated scalar tools value must split into a trimmed list.");
    }

    [TestMethod]
    public void Agent_From_YamlListTools_ReadAsList()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ntools:\n  - Read\n  - Grep\n---\n\nBody.\n");

        AgentFrontMatter agent = AgentFrontMatter.From(fm);

        CollectionAssert.AreEqual(new[] { "Read", "Grep" }, agent.Tools.ToArray(),
            "A YAML block-list tools value must read as a list directly.");
    }

    [TestMethod]
    public void Agent_From_AbsentKeys_AreNullOrEmpty()
    {
        FrontMatter fm = YamlFrontMatter.Parse("---\nname: only-name\n---\n\nBody.\n");

        AgentFrontMatter agent = AgentFrontMatter.From(fm);

        Assert.AreEqual("only-name", agent.Name);
        Assert.IsNull(agent.Description);
        Assert.IsNull(agent.Model);
        Assert.AreEqual(0, agent.Tools.Count, "Absent tools key projects to an empty list, not null.");
    }

    [TestMethod]
    public void Agent_KnownKeys_AreNameDescriptionToolsModel()
    {
        CollectionAssert.AreEqual(
            new[] { "name", "description", "tools", "model" },
            AgentFrontMatter.KnownKeys.ToArray());
    }

    [TestMethod]
    public void Agent_Projection_DoesNotLoseExtraKeys()
    {
        // The projection is read-only over FrontMatter; an unknown key it
        // doesn't model must still survive in the source FrontMatter so a
        // later Compose preserves it.
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: foo\ncolor: blue\n---\n\nBody.\n");

        _ = AgentFrontMatter.From(fm);

        Assert.AreEqual("blue", fm.FindScalar("color"),
            "Projecting to AgentFrontMatter must not mutate or lose un-modelled keys.");
        StringAssert.Contains(YamlFrontMatter.Compose(fm), "color: blue");
    }

    // ── SkillFrontMatter ─────────────────────────────────────────────────

    [TestMethod]
    public void Skill_From_ReadsNameAndDescription()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\nname: pdf-tools\ndescription: Work with PDFs\n---\n\nBody.\n");

        SkillFrontMatter skill = SkillFrontMatter.From(fm);

        Assert.AreEqual("pdf-tools", skill.Name);
        Assert.AreEqual("Work with PDFs", skill.Description);
    }

    [TestMethod]
    public void Skill_KnownKeys_AreNameAndDescription()
    {
        CollectionAssert.AreEqual(new[] { "name", "description" }, SkillFrontMatter.KnownKeys.ToArray());
    }

    // ── SlashCommandFrontMatter ──────────────────────────────────────────

    [TestMethod]
    public void SlashCommand_From_ReadsDescription()
    {
        FrontMatter fm = YamlFrontMatter.Parse(
            "---\ndescription: Summarise the current PR\n---\n\nPrompt body.\n");

        SlashCommandFrontMatter cmd = SlashCommandFrontMatter.From(fm);

        Assert.AreEqual("Summarise the current PR", cmd.Description);
    }

    [TestMethod]
    public void SlashCommand_KnownKeys_AreDescriptionOnly()
    {
        CollectionAssert.AreEqual(new[] { "description" }, SlashCommandFrontMatter.KnownKeys.ToArray());
    }

    [TestMethod]
    public void SlashCommand_From_NoFrontMatter_DescriptionIsNull()
    {
        // A command file may be just a prompt body with no front-matter.
        FrontMatter fm = YamlFrontMatter.Parse("Just a prompt template, no front-matter.\n");

        SlashCommandFrontMatter cmd = SlashCommandFrontMatter.From(fm);

        Assert.IsNull(cmd.Description);
    }
}
