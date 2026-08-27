using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Artifacts;

/// <summary>
/// Every place OpenCode reads artifacts from, as an ordered source list.
/// </summary>
/// <remarks>
/// <para>
/// ⭐⭐ <b>Each claim below was measured against opencode v1.17.9</b> — the version installed on the
/// machine this was written on, and the same version the project's earlier spikes probed — using
/// <c>opencode debug config</c> and <c>opencode debug skill</c> against a throwaway git worktree
/// and a sandbox <c>$OPENCODE_CONFIG_DIR</c>. Several of them contradict the plan, and two
/// contradict OpenCode's own bundled specification. Where a test says "measured", it means the
/// behaviour was observed in that harness, not read in a document.
/// </para>
/// <para>
/// ⚠ <b>The precedence tests are the ones that matter most.</b> Agents rank the global directory
/// above the project and skills rank it below — an inversion nobody would guess, and getting it
/// backwards makes the page name the wrong winner confidently rather than visibly failing.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeArtifactSourcesTests
{
    private string _profile = null!;
    private string _worktree = null!;
    private string _working = null!;

    /// <summary>The default global directory, as <c>$OPENCODE_CONFIG_DIR</c>-free installs use.</summary>
    private string GlobalDir => Path.Combine(_profile, ".config", "opencode");

    [TestInitialize]
    public void Setup()
    {
        _profile = Path.Combine(Path.GetTempPath(), "ocart-" + Path.GetRandomFileName());
        _worktree = Path.Combine(_profile, "repo");
        _working = Path.Combine(_worktree, "sub", "deeper");
        Directory.CreateDirectory(_working);

        // Bounds the ancestor walk. Without it the walk climbs out of the sandbox, which is
        // correct behaviour but makes every test read the real filesystem.
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

    private static string Skill(string? name, string description = "a skill")
    {
        string named = name is null ? string.Empty : $"name: {name}\n";
        return $"---\n{named}description: {description}\n---\n\nbody\n";
    }

    private static string Agent(string description = "an agent")
        => $"---\ndescription: {description}\nmode: subagent\n---\n\nbody\n";

    private IReadOnlyList<ResolvedArtifact> Resolve(OpenCodeEnvironment? env = null)
        => ArtifactResolver.Resolve(
            OpenCodeArtifactSources.ForPage(env ?? OpenCodeEnvironment.Empty, _working));

    private static ResolvedArtifact? Find(
        IReadOnlyList<ResolvedArtifact> all, ArtifactKind kind, string name)
        => all.FirstOrDefault(a => a.Kind == kind && a.Name == name);

    private static ResolvedArtifact Require(
        IReadOnlyList<ResolvedArtifact> all, ArtifactKind kind, string name)
    {
        ResolvedArtifact? found = Find(all, kind, name);
        Assert.IsNotNull(found, $"expected a {kind} named '{name}'. Present: " +
            string.Join(", ", all.Where(a => a.Kind == kind).Select(a => a.Name)));
        return found;
    }

    /// <summary>
    /// ⭐ Measured: <c>.opencode/agent/dup.md</c> and <c>.opencode/agents/dup2.md</c> both resolved.
    /// They are not alternatives — choosing one spelling finds nothing for half of all users.
    /// </summary>
    [TestMethod]
    public void BothSpellingsOfADirectoryResolveSimultaneously()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "singular.md"), Agent());
        Seed(Path.Combine(_worktree, ".opencode", "agents", "plural.md"), Agent());

        IReadOnlyList<ResolvedArtifact> all = Resolve();

        Require(all, ArtifactKind.Agent, "singular");
        Require(all, ArtifactKind.Agent, "plural");
    }

    /// <summary>
    /// ⛔ Measured, and absent from both the plan and OpenCode's bundled spec: agent discovery is
    /// recursive, and a nested file keeps the relative path <b>in its name</b>
    /// (<c>nested/nestedagent</c> appeared as an agent key).
    /// </summary>
    [TestMethod]
    public void ANestedAgentKeepsTheRelativePathInItsName()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "nested", "reviewer.md"), Agent());

        IReadOnlyList<ResolvedArtifact> all = Resolve();

        Require(all, ArtifactKind.Agent, "nested/reviewer");
        Assert.IsNull(Find(all, ArtifactKind.Agent, "reviewer"),
            "flattening it would collide with a top-level agent that is a different artifact");
    }

    /// <summary>
    /// ⛔⛔ The single most consequential measurement in this slice: a manifest in a folder called
    /// <c>dirname-x</c> declaring <c>name: frontmatter-y</c> registered as <c>frontmatter-y</c>.
    /// </summary>
    /// <remarks>
    /// The name is the identity resolution groups by, so taking it from the folder would invent
    /// shadowing between skills that merely share a folder name and miss it between two that
    /// genuinely collide.
    /// </remarks>
    [TestMethod]
    public void ASkillIsNamedByItsFrontMatter_NotItsFolder()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "dirname-x", "SKILL.md"),
            Skill("frontmatter-y"));

        IReadOnlyList<ResolvedArtifact> all = Resolve();

        Require(all, ArtifactKind.Skill, "frontmatter-y");
        Assert.IsNull(Find(all, ArtifactKind.Skill, "dirname-x"),
            "the folder name is not the identity when the manifest declares one");
    }

    /// <summary>
    /// ⚠ Measured: a manifest carrying a <c>description</c> but no <c>name</c> did not appear among
    /// the skills OpenCode resolved <b>at all</b>. It is listed here anyway, under its folder name.
    /// </summary>
    /// <remarks>
    /// Listing only what loads would hide the broken one at exactly the moment the user is looking
    /// for it. Diagnosing that it is inert is the consumer's job at render time.
    /// </remarks>
    [TestMethod]
    public void ASkillWithNoDeclaredNameIsStillListed_UnderItsFolderName()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "nameless", "SKILL.md"),
            Skill(name: null));

        Require(Resolve(), ArtifactKind.Skill, "nameless");
    }

    /// <summary>
    /// ⚠ Measured: <c>~/.agents/skills/microsoft-foundry/models/deploy-model/preset/SKILL.md</c>
    /// registers as the top-level skill <c>preset</c> — nested skills flatten onto their own folder.
    /// </summary>
    /// <remarks>
    /// Deliberately seeded without a declared name, so the flattening rule is what the assertion
    /// actually exercises rather than the front-matter rule tested above.
    /// </remarks>
    [TestMethod]
    public void ANestedSkillFlattensOntoItsOwnFolderName()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "group", "inner", "leaf", "SKILL.md"),
            Skill(name: null));

        IReadOnlyList<ResolvedArtifact> all = Resolve();

        Require(all, ArtifactKind.Skill, "leaf");
        Assert.IsNull(Find(all, ArtifactKind.Skill, "group/inner/leaf"),
            "skills flatten; only agents and commands keep the path");
    }

    /// <summary>
    /// ⛔⛔ Measured: with the same agent name in the project and in the global directory, the
    /// <b>global</b> file supplied the winning <c>description</c>, while a field only the project
    /// file set survived alongside it. Agent files deep-merge with global loaded last.
    /// </summary>
    [TestMethod]
    public void ForAgentsTheGlobalDirectoryOutranksTheProject()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "dup.md"), Agent("project"));
        Seed(Path.Combine(GlobalDir, "agent", "dup.md"), Agent("global"));

        ResolvedArtifact dup = Require(Resolve(), ArtifactKind.Agent, "dup");

        Assert.AreEqual(2, dup.Entries.Count, "both declarations belong in the chain");
        StringAssert.StartsWith(dup.Effective.Scope.Id, "global:",
            "the global directory wins for agents — measured, and the opposite of the intuition");
    }

    /// <summary>
    /// ⛔⛔ And the inversion: the same collision for a <b>skill</b> resolved to the project copy,
    /// as a single entry with the global one not surfaced by OpenCode at all.
    /// </summary>
    [TestMethod]
    public void ForSkillsTheProjectOutranksTheGlobalDirectory()
    {
        Seed(Path.Combine(_worktree, ".opencode", "skills", "dup", "SKILL.md"), Skill("dup"));
        Seed(Path.Combine(GlobalDir, "skills", "dup", "SKILL.md"), Skill("dup"));

        ResolvedArtifact dup = Require(Resolve(), ArtifactKind.Skill, "dup");

        Assert.AreEqual(2, dup.Entries.Count);
        StringAssert.StartsWith(dup.Effective.Scope.Id, "project:",
            "skills rank the project above global — the opposite way round from agents");
    }

    /// <summary>
    /// ⭐ Measured for agents and skills alike: a definition at the worktree root beat one in an
    /// intermediate directory nearer the working directory.
    /// </summary>
    /// <remarks>
    /// ⚠ Not alphabetical either — the nearer path sorts <i>after</i> the farther one and still
    /// lost, which rules out the obvious alternative explanation.
    /// </remarks>
    [TestMethod]
    public void TheFartherAncestorOutranksTheNearerOne()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "dup.md"), Agent("far"));
        Seed(Path.Combine(_worktree, "sub", ".opencode", "agent", "dup.md"), Agent("near"));

        ResolvedArtifact dup = Require(Resolve(), ArtifactKind.Agent, "dup");

        Assert.AreEqual(2, dup.Entries.Count);
        Assert.AreEqual(
            Path.Combine(_worktree, ".opencode", "agent", "dup.md"),
            dup.Effective.Location,
            "the worktree root outranks the directory nearer the user");
    }

    /// <summary>
    /// ⚠ Measured: a plugin at <c>.opencode/plugin/nested/thing.ts</c> was <b>not</b> loaded, while
    /// its siblings in the directory itself were. Plugins are the one kind here that is flat.
    /// </summary>
    [TestMethod]
    public void PluginsAreNotWalkedRecursively()
    {
        Seed(Path.Combine(_worktree, ".opencode", "plugin", "flat.ts"), "export default 1");
        Seed(Path.Combine(_worktree, ".opencode", "plugin", "nested", "deep.ts"), "export default 1");

        IReadOnlyList<ResolvedArtifact> all = Resolve();

        Require(all, ArtifactKind.Plugin, "flat.ts");
        Assert.IsNull(Find(all, ArtifactKind.Plugin, "nested/deep.ts"),
            "listing a file OpenCode ignores defeats the point of a tab showing what is live");
    }

    /// <summary>
    /// Both extensions load, so the extension is part of the identity: <c>a.ts</c> and <c>a.js</c>
    /// in one directory are two plugins, not one declared twice.
    /// </summary>
    [TestMethod]
    public void APluginKeepsItsExtension_BecauseBothExtensionsLoad()
    {
        Seed(Path.Combine(_worktree, ".opencode", "plugins", "a.ts"), "export default 1");
        Seed(Path.Combine(_worktree, ".opencode", "plugins", "a.js"), "export default 1");

        IReadOnlyList<ResolvedArtifact> all = Resolve();

        Assert.AreEqual(1, Require(all, ArtifactKind.Plugin, "a.ts").Entries.Count);
        Assert.AreEqual(1, Require(all, ArtifactKind.Plugin, "a.js").Entries.Count);
    }

    /// <summary>
    /// ⭐ Measured, and <b>missing from OpenCode's own bundled spec</b>, which lists only the
    /// user-level <c>~/.claude/skills</c>: a project's <c>.claude/skills/</c> is scanned too.
    /// </summary>
    [TestMethod]
    public void AProjectsDotClaudeSkillsDirectoryIsScanned()
    {
        Seed(Path.Combine(_worktree, ".claude", "skills", "shared", "SKILL.md"), Skill("shared"));

        Require(Resolve(), ArtifactKind.Skill, "shared");
    }

    /// <summary>
    /// ⚠⚠ Measured: with <c>$OPENCODE_CONFIG_DIR</c> pointed at a sandbox, a plugin in the sandbox
    /// <b>and</b> one in the real <c>~/.config/opencode/plugins/</c> both loaded, both reported by
    /// OpenCode as <c>scope: global</c>.
    /// </summary>
    /// <remarks>
    /// So the variable adds a root rather than relocating one, and honouring it alone would omit
    /// plugins that are genuinely running.
    /// </remarks>
    [TestMethod]
    public void TheDefaultPluginRootIsScannedEvenWhenConfigDirPointsElsewhere()
    {
        string custom = Path.Combine(_profile, "custom-config");
        Seed(Path.Combine(custom, "plugin", "sandboxed.ts"), "export default 1");
        Seed(Path.Combine(GlobalDir, "plugins", "real.js"), "export default 1");

        IReadOnlyList<ResolvedArtifact> all = Resolve(new OpenCodeEnvironment(ConfigDir: custom));

        Require(all, ArtifactKind.Plugin, "sandboxed.ts");
        Require(all, ArtifactKind.Plugin, "real.js");
    }

    /// <summary>
    /// The seven built-in agents are listed before the user has declared anything.
    /// </summary>
    [TestMethod]
    public void TheBuiltInAgentsAreListed_AndAreOverriddenByAFileOfTheSameName()
    {
        Seed(Path.Combine(_worktree, ".opencode", "agent", "build.md"), Agent("mine"));

        IReadOnlyList<ResolvedArtifact> all = Resolve();

        Assert.AreEqual(ArtifactForm.BuiltIn, Require(all, ArtifactKind.Agent, "plan").Effective.Form,
            "an undeclared built-in still has to be visible");

        ResolvedArtifact build = Require(all, ArtifactKind.Agent, "build");
        Assert.AreEqual(2, build.Entries.Count);
        Assert.AreEqual(ArtifactForm.File, build.Effective.Form,
            "every other layer overrides a built-in by name");
    }

    /// <summary>
    /// ⚠ A duplicate id is a crash, not an inconsistency: both consumers of this vocabulary map ids
    /// through a dictionary, so a repeat throws while the map is built and the page fails to load.
    /// </summary>
    /// <remarks>
    /// There is one source per ancestor per spelling per kind, so the count grows with the depth of
    /// the user's directory — which is exactly the shape that makes a hand-written id collide.
    /// </remarks>
    [TestMethod]
    public void EverySourceIdIsUnique()
    {
        IReadOnlyList<IArtifactSource> sources =
            OpenCodeArtifactSources.ForPage(OpenCodeEnvironment.Empty, _working);

        List<string> duplicates = [.. sources
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)];

        Assert.AreEqual(0, duplicates.Count,
            "duplicate source ids: " + string.Join(", ", duplicates));
        Assert.IsTrue(sources.Count > 20, "the sandbox has three ancestors, so this list is long");
    }

    /// <summary>
    /// With no working directory there are no project sources at all — and in particular no
    /// fallback to the process's own current directory, which means nothing to a GUI's user.
    /// </summary>
    [TestMethod]
    public void WithNoWorkingDirectoryOnlyTheNonProjectSourcesAreBuilt()
    {
        Seed(Path.Combine(GlobalDir, "agent", "global-only.md"), Agent());

        IReadOnlyList<ResolvedArtifact> all = ArtifactResolver.Resolve(
            OpenCodeArtifactSources.ForPage(OpenCodeEnvironment.Empty, null));

        Require(all, ArtifactKind.Agent, "global-only");
        Assert.IsFalse(
            all.SelectMany(a => a.Entries).Any(e => e.Scope.Id.StartsWith("project:", StringComparison.Ordinal)),
            "no project scope may appear when there is no project");
    }
}
