using System.IO;
using System.Linq;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Memory;

/// <summary>
/// Locks the scope-aware discovery contract for the editor pages
/// (see <c>docs/SKILLS-AGENTS-COMMANDS-PLAN.md</c>, group #2): User +
/// Project scopes are writable, Plugin scope is read-only, skills are
/// keyed by their parent directory name, and the front-matter
/// <c>description</c> surfaces as the list subtitle.
/// </summary>
[TestClass]
public sealed class EditableMemoryServiceTests
{
    private string _sandbox = string.Empty;
    private string _project = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_editmem_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _project = Path.Combine(Path.GetTempPath(), "claudetest_editmem_proj_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_project);
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        foreach (string dir in new[] { _sandbox, _project })
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Home => Path.Combine(_sandbox, ".claude");

    [TestMethod]
    public void Snapshot_UserScopeAgent_IsDiscoveredWritable()
    {
        WriteFile(Path.Combine(Home, "agents", "reviewer.md"),
            "---\nname: reviewer\ndescription: Reviews code\n---\n\nBody.\n");

        var entries = EditableMemoryService.Snapshot();

        EditableMemoryEntry agent = entries.Single(e => e.Category == UserMemoryCategory.Subagent);
        Assert.AreEqual("reviewer", agent.DisplayName);
        Assert.AreEqual(EditableMemoryScope.User, agent.Scope);
        Assert.IsTrue(agent.IsWritable, "User-scope agents must be writable.");
        Assert.AreEqual("User", agent.Source, "User-scope source label is 'User'.");
        // Description is loaded lazily now (stat-only snapshot) — see LoadDescription tests.
    }

    [TestMethod]
    public void Snapshot_UserScopeSkill_DisplayNameIsParentDirectory()
    {
        WriteFile(Path.Combine(Home, "skills", "pdf-tools", "SKILL.md"),
            "---\nname: pdf-tools\ndescription: Work with PDFs\n---\n\nBody.\n");

        var entries = EditableMemoryService.Snapshot();

        EditableMemoryEntry skill = entries.Single(e => e.Category == UserMemoryCategory.Skill);
        Assert.AreEqual("pdf-tools", skill.DisplayName,
            "A skill's display name is its parent directory, not the literal 'SKILL'.");
        Assert.AreEqual(EditableMemoryScope.User, skill.Scope);
        Assert.IsTrue(skill.IsWritable);
    }

    [TestMethod]
    public void Snapshot_UserScopeSlashCommand_IsDiscovered()
    {
        WriteFile(Path.Combine(Home, "commands", "summarise.md"),
            "---\ndescription: Summarise the PR\n---\n\nPrompt.\n");

        var entries = EditableMemoryService.Snapshot();

        EditableMemoryEntry cmd = entries.Single(e => e.Category == UserMemoryCategory.SlashCommand);
        Assert.AreEqual("summarise", cmd.DisplayName);
        Assert.AreEqual("User", cmd.Source);
    }

    [TestMethod]
    public void Snapshot_ProjectScope_DiscoveredWhenProjectRootGiven()
    {
        WriteFile(Path.Combine(_project, ".claude", "agents", "proj-agent.md"),
            "---\nname: proj-agent\n---\n\nBody.\n");

        // Without projectRoot → not found.
        Assert.IsFalse(EditableMemoryService.Snapshot().Any(e => e.DisplayName == "proj-agent"),
            "Project-scope artifacts must NOT appear when no projectRoot is passed.");

        // With projectRoot → found, Project scope, writable.
        EditableMemoryEntry entry = EditableMemoryService.Snapshot(_project)
            .Single(e => e.DisplayName == "proj-agent");
        Assert.AreEqual(EditableMemoryScope.Project, entry.Scope);
        Assert.IsTrue(entry.IsWritable, "Project-scope artifacts are writable.");
    }

    [TestMethod]
    public void Snapshot_PluginSkill_IsReadOnly()
    {
        // Plugin layout nests SKILL.md at varying depth; the walk is recursive.
        WriteFile(Path.Combine(Home, "plugins", "some-marketplace", "cool-plugin", "skills", "widget", "SKILL.md"),
            "---\nname: widget\ndescription: A plugin skill\n---\n\nBody.\n");

        EditableMemoryEntry skill = EditableMemoryService.Snapshot()
            .Single(e => e.Category == UserMemoryCategory.Skill && e.Scope == EditableMemoryScope.Plugin);

        Assert.AreEqual("widget", skill.DisplayName);
        Assert.IsFalse(skill.IsWritable, "Plugin-provided skills must be read-only.");
        Assert.AreEqual("some-marketplace/cool-plugin", skill.Source,
            "Plugin source is the path segments under plugins/ before the skills/ dir.");
    }

    [TestMethod]
    public void Snapshot_PluginAgentsAndCommands_AreReadOnly()
    {
        WriteFile(Path.Combine(Home, "plugins", "p", "agents", "pa.md"), "---\nname: pa\n---\n\nB.\n");
        WriteFile(Path.Combine(Home, "plugins", "p", "commands", "pc.md"), "---\ndescription: d\n---\n\nB.\n");

        var plugin = EditableMemoryService.Snapshot()
            .Where(e => e.Scope == EditableMemoryScope.Plugin)
            .ToList();

        Assert.IsTrue(plugin.Any(e => e is { Category: UserMemoryCategory.Subagent, DisplayName: "pa", IsWritable: false }));
        Assert.IsTrue(plugin.Any(e => e is { Category: UserMemoryCategory.SlashCommand, DisplayName: "pc", IsWritable: false }));
    }

    [TestMethod]
    public void Snapshot_AllThreeScopes_CoexistInOneSnapshot()
    {
        WriteFile(Path.Combine(Home, "agents", "user-a.md"), "---\nname: user-a\n---\n\nB.\n");
        WriteFile(Path.Combine(_project, ".claude", "agents", "proj-a.md"), "---\nname: proj-a\n---\n\nB.\n");
        WriteFile(Path.Combine(Home, "plugins", "x", "skills", "plug-s", "SKILL.md"), "---\nname: plug-s\n---\n\nB.\n");

        var entries = EditableMemoryService.Snapshot(_project);

        Assert.IsTrue(entries.Any(e => e is { DisplayName: "user-a", Scope: EditableMemoryScope.User }));
        Assert.IsTrue(entries.Any(e => e is { DisplayName: "proj-a", Scope: EditableMemoryScope.Project }));
        Assert.IsTrue(entries.Any(e => e is { DisplayName: "plug-s", Scope: EditableMemoryScope.Plugin }));
    }

    [TestMethod]
    public void Snapshot_NoClaudeDir_ReturnsEmpty_NeverThrows()
    {
        // Fresh sandbox with no ~/.claude content at all.
        var entries = EditableMemoryService.Snapshot();
        Assert.AreEqual(0, entries.Count);
    }

    [TestMethod]
    public void LoadDescription_ReturnsFrontMatterDescription()
    {
        string path = Path.Combine(Home, "agents", "d.md");
        WriteFile(path, "---\nname: d\ndescription: The description\n---\n\nBody.\n");

        Assert.AreEqual("The description", EditableMemoryService.LoadDescription(path));
    }

    [TestMethod]
    public void LoadDescription_AbsentKey_ReturnsNull()
    {
        string path = Path.Combine(Home, "agents", "nd.md");
        WriteFile(path, "---\nname: nd\n---\n\nBody.\n");

        Assert.IsNull(EditableMemoryService.LoadDescription(path),
            "Absent description front-matter key → null (UI shows '(no description)').");
    }

    [TestMethod]
    public void LoadDescription_MissingFile_ReturnsNull()
    {
        Assert.IsNull(EditableMemoryService.LoadDescription(Path.Combine(Home, "agents", "ghost.md")));
    }

    [TestMethod]
    public void Snapshot_IsStatOnly_DoesNotPopulateDescriptionEagerly()
    {
        // The entry record no longer carries a description — discovery is
        // stat-only.  This test documents that contract: callers must use
        // LoadDescription for the subtitle.  (Compile-time guarantee: the
        // record has no Description member; the assertion below pins Source.)
        WriteFile(Path.Combine(Home, "agents", "x.md"), "---\nname: x\ndescription: y\n---\n\nB.\n");
        EditableMemoryEntry e = EditableMemoryService.Snapshot().Single(x => x.DisplayName == "x");
        Assert.AreEqual("User", e.Source);
    }

    [TestMethod]
    public void Snapshot_PluginWalk_SkipsNodeModules()
    {
        // A SKILL.md buried in node_modules must NOT be picked up — the walk
        // skips dependency dirs so it doesn't crawl a plugin's deps.
        WriteFile(Path.Combine(Home, "plugins", "p", "node_modules", "dep", "skills", "x", "SKILL.md"),
            "---\nname: x\n---\n\nB.\n");
        // A legit one at a normal depth IS found.
        WriteFile(Path.Combine(Home, "plugins", "p", "skills", "real", "SKILL.md"),
            "---\nname: real\n---\n\nB.\n");

        var skills = EditableMemoryService.Snapshot()
            .Where(e => e.Category == UserMemoryCategory.Skill)
            .Select(e => e.DisplayName)
            .ToList();

        CollectionAssert.Contains(skills, "real");
        CollectionAssert.DoesNotContain(skills, "x", "node_modules must be skipped during the plugin walk.");
    }

    [TestMethod]
    public async Task ReadAsync_ReturnsContent_ThenNullForMissing()
    {
        string path = Path.Combine(Home, "agents", "r.md");
        WriteFile(path, "---\nname: r\n---\n\nHello.\n");

        string? content = await EditableMemoryService.ReadAsync(path, CancellationToken.None);
        Assert.IsNotNull(content);
        StringAssert.Contains(content!, "Hello.");

        string? missing = await EditableMemoryService.ReadAsync(
            Path.Combine(Home, "agents", "does-not-exist.md"), CancellationToken.None);
        Assert.IsNull(missing, "ReadAsync returns null for a missing file rather than throwing.");
    }
}
