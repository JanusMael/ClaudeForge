using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// Claude's fixed locations, as an ordered artifact-source list.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>These cover what <c>UserMemoryServiceTests</c> cannot see.</b> That suite asserts the
/// flattened inventory, which is deliberately blind to grouping — every entry in a chain is listed,
/// so a chain that is wrong looks identical to one that is right. The claims about <i>identity</i>
/// (what shadows what) only become observable by resolving the sources directly, which is what
/// these tests do.
/// </para>
/// <para>
/// Two of them also pin behaviour the inventory has always had and never tested: <c>.bak</c>
/// sidecars staying out, and the sibling <c>.opencode</c> directory being walked.
/// </para>
/// </remarks>
[TestClass]
public sealed class ClaudeArtifactSourcesTests
{
    private string _fakeHome = null!;

    private string ClaudeHome => Path.Combine(_fakeHome, ".claude");

    /// <summary>The sandbox, addressed the way production addresses a real profile.</summary>
    private ClaudeArtifactPaths Paths => new(_fakeHome);

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "claudeforge-sources-" + Path.GetRandomFileName());
        Directory.CreateDirectory(ClaudeHome);
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (!Directory.Exists(_fakeHome))
        {
            return;
        }

        try
        {
            Directory.Delete(_fakeHome, recursive: true);
        }
        catch (IOException)
        {
            // Leave the temp directory if something still holds a handle.
        }
    }

    private void Write(string relativePath, string content = "x")
    {
        string full = Path.Combine(ClaudeHome, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private IReadOnlyList<ResolvedArtifact> Resolve(string? projectRoot = null)
    {
        return ArtifactResolver.Resolve(
            ClaudeArtifactSources.ForInventory(Paths, projectRoot).Select(s => s.Source));
    }

    // ── The source list itself ───────────────────────────────────────────────

    [TestMethod]
    public void EverySourceIdIsUnique()
    {
        // ⛔ Not cosmetic. The inventory maps SourceId back to a category through a dictionary, so
        // a duplicated id is not a mild inconsistency — it throws while building the map, i.e. the
        // Memory page fails to load at all.
        foreach (string? projectRoot in new[] { null, Path.Combine(_fakeHome, "proj") })
        {
            string[] ids = [.. ClaudeArtifactSources.ForInventory(Paths, projectRoot)
                                                    .Select(s => s.Source.Id)];

            CollectionAssert.AllItemsAreUnique(
                ids, $"Duplicate source id with projectRoot={projectRoot ?? "(none)"}.");
        }
    }

    [TestMethod]
    public void EveryCategoryHasAtLeastOneSource()
    {
        // UserMemoryCategory's own remarks say "adding a new category requires extending both the
        // enum AND the service's SnapshotFiles dispatch" — prose that nothing checked. A category
        // with no source is a silently empty group on the page, which reads as "you have none of
        // these" rather than "nobody looked".
        HashSet<UserMemoryCategory> covered =
        [
            .. ClaudeArtifactSources.ForInventory(Paths, Path.Combine(_fakeHome, "proj"))
                                    .Select(s => s.Category),
        ];

        UserMemoryCategory[] missing = [.. Enum.GetValues<UserMemoryCategory>().Except(covered)];

        Assert.AreEqual(
            0, missing.Length, $"No source supplies: {string.Join(", ", missing)}.");
    }

    [TestMethod]
    public void WithoutAProjectRoot_NothingResolvesAtProjectScope()
    {
        // Seeded on BOTH sides deliberately: the project file exists on disk and must stay
        // invisible, which is a stronger claim than "no project source was constructed".
        Write("CLAUDE.md");
        string projectRoot = Path.Combine(_fakeHome, "proj");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "# project");

        ArtifactRef[] entries = [.. Resolve(projectRoot: null).SelectMany(a => a.Entries)];

        Assert.IsTrue(entries.Any(e => e.Scope == ClaudeScopes.User));
        Assert.IsFalse(
            entries.Any(e => e.Scope == ClaudeScopes.Project),
            "A project file must stay invisible while no project is open.");
    }

    // ── Identity: what is, and is not, one artifact ──────────────────────────

    [TestMethod]
    public void ASiblingToolsAgentsFile_DoesNotShadowClaudesOwn()
    {
        // ⚠ Both are ArtifactKind.Memory and both files are literally named AGENTS.md. Taking the
        // file name as the identity would resolve them into one chain — i.e. state that Codex's
        // memory file shadows Claude's, which is not a thing that happens.
        Write("AGENTS.md");
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".codex"));
        File.WriteAllText(Path.Combine(_fakeHome, ".codex", "AGENTS.md"), "# codex");

        List<ResolvedArtifact> memory =
            [.. Resolve().Where(a => a.Kind == ArtifactKind.Memory)];

        Assert.AreEqual(2, memory.Count, "Two unrelated tools' files must not resolve as one artifact.");
        Assert.IsFalse(memory.Any(a => a.IsShadowed));
        CollectionAssert.AreEquivalent(
            new[] { "AGENTS", ".codex/AGENTS" }, memory.Select(a => a.Name).ToArray());
    }

    [TestMethod]
    public void RulesInDifferentSubdirectories_AreDistinctArtifacts()
    {
        // The rules walk is recursive, so a shared base name across subdirectories is ordinary —
        // and Claude reads both files. Neither shadows the other.
        Write(Path.Combine("rules", "common", "security.md"));
        Write(Path.Combine("rules", "csharp", "security.md"));

        List<ResolvedArtifact> rules = [.. Resolve().Where(a => a.Kind == ArtifactKind.Rule)];

        Assert.AreEqual(2, rules.Count);
        Assert.IsFalse(rules.Any(a => a.IsShadowed));
    }

    [TestMethod]
    public void ProjectMemoryAndUserMemory_ResolveAsOneChain_ProjectFirst()
    {
        // ⭐ The relationship the old walk could not express: these are two declarations of one
        // artifact, and the project's outranks the user's.
        Write("CLAUDE.md");
        string projectRoot = Path.Combine(_fakeHome, "proj");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "# project");

        ResolvedArtifact claudeMd = Resolve(projectRoot)
            .Single(a => a.Kind == ArtifactKind.Memory && a.Name == "CLAUDE");

        Assert.IsTrue(claudeMd.IsShadowed);
        Assert.AreEqual(2, claudeMd.Entries.Count);
        Assert.AreEqual(ClaudeScopes.Project, claudeMd.Effective.Scope);
        Assert.AreEqual(ClaudeScopes.User, claudeMd.Shadowed.Single().Scope);
    }

    [TestMethod]
    public void UserAndProjectSettings_ResolveAsOneChain_ProjectFirst()
    {
        Write("settings.json", "{}");
        string projectRoot = Path.Combine(_fakeHome, "proj");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".claude"));
        File.WriteAllText(Path.Combine(projectRoot, ".claude", "settings.json"), "{}");

        ResolvedArtifact settings = Resolve(projectRoot)
            .Single(a => a.Kind == ArtifactKind.Configuration && a.Name == "settings.json");

        Assert.AreEqual(2, settings.Entries.Count);
        Assert.AreEqual(ClaudeScopes.Project, settings.Effective.Scope);
    }

    [TestMethod]
    public void ManagedSettings_OutranksTheUsersOwn()
    {
        // Administrator policy is the highest layer Claude reads. The two files have different
        // names so they never actually collide — but the precedence is what a chain would be
        // ordered by the day anything does, and stating it wrongly would misreport whose value is
        // live.
        Assert.IsTrue(ClaudeScopes.Managed.Precedence > ClaudeScopes.Project.Precedence);
        Assert.IsTrue(ClaudeScopes.Project.Precedence > ClaudeScopes.User.Precedence);
        Assert.IsTrue(ClaudeScopes.User.Precedence > ClaudeScopes.CrossTool.Precedence);
    }

    // ── Grouping must not become filtering ───────────────────────────────────

    [TestMethod]
    public void BothCLAUDEmdFiles_AreListed_EvenThoughTheyResolveAsOneArtifact()
    {
        // ⛔ The failure mode this exists for: listing `Effective` instead of `Entries` would drop
        // the user's CLAUDE.md the moment a project also had one — silently, and only for users
        // who happen to have both. The inventory is a browsable list of files on disk, so every
        // entry in a chain is a row; deciding which one wins is a different question this page
        // does not ask.
        Write("CLAUDE.md", "# user");
        string projectRoot = Path.Combine(_fakeHome, "proj");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "# project");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(projectRoot);

        Assert.AreEqual(1, files.Count(f => f.Category == UserMemoryCategory.PrimaryMemory));
        Assert.AreEqual(1, files.Count(f => f.Category == UserMemoryCategory.ProjectMemory));
    }

    // ── Behaviour the inventory always had and never tested ──────────────────

    [TestMethod]
    public void BackupSidecars_AreNotArtifacts_InTheOneWalkWhereItMatters()
    {
        // *.bak files are left by RestoreEngine and by editors (vim, `sed -i.bak`). Surfacing them
        // would add one phantom row per past restore × memory file.
        //
        // ⭐ HOOKS, not agents — and that is the whole point of this test. Measured: .NET's
        // "*.md" does NOT match "reviewer.md.bak", so for agents / commands / plans / rules the
        // search pattern already excludes sidecars and the exclusion rule is decorative. The hooks
        // walk uses "*", because a hook may be a .sh, a .py, or extensionless — so it is the ONE
        // walk where the rule does any work. A version of this test written against agents/ passes
        // whether or not the exclusion exists, which a canary is what exposed.
        Write(Path.Combine("hooks", "precommit.sh"));
        Write(Path.Combine("hooks", "precommit.sh.bak"));

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles();

        Assert.AreEqual(1, files.Count(f => f.Category == UserMemoryCategory.Hook));
        Assert.IsFalse(files.Any(f => f.AbsolutePath.EndsWith(".bak", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SiblingOpenCodeDirectory_IsWalked_NotJustProbed()
    {
        // .opencode holds a directory of markdown rather than one known file, so it is the one
        // cross-tool source that is a walk. Nothing covered it before.
        string openCode = Path.Combine(_fakeHome, ".opencode");
        Directory.CreateDirectory(openCode);
        File.WriteAllText(Path.Combine(openCode, "rules.md"), "# rules");
        File.WriteAllText(Path.Combine(openCode, "notes.md"), "# notes");

        List<UserMemoryFile> cross =
        [
            .. UserMemoryService.SnapshotFiles()
                                .Where(f => f.Category == UserMemoryCategory.CrossToolMemory),
        ];

        Assert.AreEqual(2, cross.Count);
        CollectionAssert.AreEquivalent(new[] { "rules", "notes" }, cross.Select(f => f.DisplayName).ToArray());
    }
}
