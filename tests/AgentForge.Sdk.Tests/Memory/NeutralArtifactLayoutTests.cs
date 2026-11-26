using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.AgentForge.Testing;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// The memory services read what an <see cref="IArtifactLayout"/> declares and nothing else, on the
/// neutral test product (<c>plans/00008</c> step 9, decision 6).
/// </summary>
/// <remarks>
/// ⭐ <b>Each test also plants Claude's file at Claude's location.</b> A service that still reached
/// for <c>~/.claude</c> instead of reading the layout would list it, so these fail on exactly the
/// regression the seam exists to prevent.
/// </remarks>
public sealed class NeutralArtifactLayoutTests : IDisposable
{
    private readonly string _profile;
    private readonly string _home;
    private readonly string _project;

    public NeutralArtifactLayoutTests()
    {
        _profile = Path.Combine(Path.GetTempPath(), "agentforge-layout-" + Path.GetRandomFileName());
        _home = Path.Combine(_profile, NeutralTestProduct.HomeDirectoryName);
        _project = Path.Combine(_profile, "work", "repo");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_project);
        PlatformPaths.TestUserProfileOverride = _profile;
    }

    public void Dispose()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            Directory.Delete(_profile, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp directory is left for the OS to clear.
        }
    }

    private static NeutralTestArtifactLayout Layout => NeutralTestArtifactLayout.UnderProfile();

    private static string Write(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private void PlantClaudeFiles()
    {
        Write(Path.Combine(_profile, ".claude", "CLAUDE.md"), "# claude");
        Write(Path.Combine(_profile, ".claude", "agents", "claude-agent.md"));
        Write(Path.Combine(_profile, ".claude", "settings.json"), "{}");
        Write(Path.Combine(_profile, ".claude", "projects", "claude-proj", "s.jsonl"), "{}");
    }

    // ── Inventory ────────────────────────────────────────────────────────────

    [Fact]
    public void Inventory_ListsTheLayoutsFiles_InTheirCategories_AndNoClaudeFile()
    {
        PlantClaudeFiles();
        string memory = Write(Path.Combine(_home, NeutralTestArtifactLayout.MemoryFileName), "# neutral");
        string agent = Write(Path.Combine(_home, NeutralTestArtifactLayout.AgentsDirectory, "reviewer.md"));
        string command = Write(Path.Combine(_home, NeutralTestArtifactLayout.CommandsDirectory, "ship.md"));
        string skill = Write(Path.Combine(
            _home, NeutralTestArtifactLayout.SkillsDirectory, "triage", NeutralTestArtifactLayout.SkillMarker));
        string config = Write(Path.Combine(_home, NeutralTestProduct.ConfigFileName), "{}");

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(Layout);

        MessageAssert.SameElements(
            new[]
            {
                (memory, UserMemoryCategory.PrimaryMemory),
                (agent, UserMemoryCategory.Subagent),
                (command, UserMemoryCategory.SlashCommand),
                (skill, UserMemoryCategory.Skill),
                (config, UserMemoryCategory.Configuration),
            },
            files.Select(f => (f.AbsolutePath, f.Category)));
    }

    [Fact]
    public void Inventory_AppliesTheSourcesSuffix_AndKeepsAConfigurationFilesExtension()
    {
        Write(Path.Combine(_home, NeutralTestProduct.ConfigFileName), "{}");
        Write(Path.Combine(_project, NeutralTestArtifactLayout.ProjectDirectory, NeutralTestProduct.ConfigFileName), "{}");

        string[] names = [.. UserMemoryService.SnapshotFiles(Layout, _project)
            .Where(f => f.Category == UserMemoryCategory.Configuration)
            .Select(f => f.DisplayName)];

        MessageAssert.SameElements(
            new[]
            {
                NeutralTestProduct.ConfigFileName,
                NeutralTestProduct.ConfigFileName + NeutralTestArtifactLayout.ProjectSuffix,
            },
            names);
    }

    [Fact]
    public void Inventory_ListsEveryDeclarationOfOneName_NotOnlyTheWinner()
    {
        string user = Write(Path.Combine(_home, NeutralTestArtifactLayout.MemoryFileName));
        string project = Write(Path.Combine(_project, NeutralTestArtifactLayout.MemoryFileName));

        IReadOnlyList<UserMemoryFile> files = UserMemoryService.SnapshotFiles(Layout, _project);

        Assert.Equal(user, files.Single(f => f.Category == UserMemoryCategory.PrimaryMemory).AbsolutePath);
        Assert.Equal(project, files.Single(f => f.Category == UserMemoryCategory.ProjectMemory).AbsolutePath);
    }

    [Fact]
    public void Inventory_WithoutAProject_HasNoProjectRows()
    {
        Write(Path.Combine(_project, NeutralTestArtifactLayout.MemoryFileName));
        Write(Path.Combine(_project, NeutralTestArtifactLayout.ProjectDirectory, NeutralTestProduct.ConfigFileName), "{}");

        Assert.Empty(UserMemoryService.SnapshotFiles(Layout, projectRoot: null));
    }

    // ── Editor ───────────────────────────────────────────────────────────────

    [Fact]
    public void Editor_ListsEachScope_AndOnlyTheVendorTreeIsReadOnly()
    {
        PlantClaudeFiles();
        string user = Write(Path.Combine(_home, NeutralTestArtifactLayout.AgentsDirectory, "reviewer.md"));
        string project = Write(Path.Combine(
            _project, NeutralTestArtifactLayout.ProjectDirectory, NeutralTestArtifactLayout.CommandsDirectory, "ship.md"));
        string vendor = Write(Path.Combine(
            _home, NeutralTestArtifactLayout.VendorDirectory, NeutralTestArtifactLayout.AgentsDirectory, "reviewer.md"));

        IReadOnlyList<EditableMemoryEntry> entries = EditableMemoryService.Snapshot(Layout, _project);

        MessageAssert.SameElements(
            new[]
            {
                (user, EditableMemoryScope.User, UserMemoryCategory.Subagent),
                (project, EditableMemoryScope.Project, UserMemoryCategory.SlashCommand),
                (vendor, EditableMemoryScope.Plugin, UserMemoryCategory.Subagent),
            },
            entries.Select(e => (e.AbsolutePath, e.Scope, e.Category)));
        Assert.False(entries.Single(e => e.Scope == EditableMemoryScope.Plugin).IsWritable);
        Assert.All(entries.Where(e => e.Scope != EditableMemoryScope.Plugin), e => Assert.True(e.IsWritable));
    }

    // ── Footprint ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Footprint_WalksTheLayoutsTranscriptDirectory_NotClaudes()
    {
        PlantClaudeFiles();
        Write(Path.Combine(_home, "projects", "repo-a", "session.jsonl"), "{}");

        IReadOnlyList<ProjectTranscriptStats> rows =
            await new FootprintService(Layout).GetProjectTranscriptStatsAsync(CancellationToken.None);

        Assert.Equal(Path.Combine(_home, "projects", "repo-a"), rows.Single().AbsolutePath);
    }

    [Fact]
    public async Task Footprint_WithNoTranscriptDirectory_ReportsNoRows_AndDeletesNothing()
    {
        string transcript = Write(Path.Combine(_home, "projects", "repo-a", "session.jsonl"), "{}");
        FootprintService service = new(new WithoutTranscripts(Layout));

        IReadOnlyList<ProjectTranscriptStats> rows =
            await service.GetProjectTranscriptStatsAsync(CancellationToken.None);
        await service.DeleteProjectTranscriptsAsync("repo-a", CancellationToken.None);

        Assert.Empty(rows);
        Assert.True(File.Exists(transcript), "A layout with no transcript directory must delete nothing.");
    }

    [Fact]
    public async Task Footprint_StatsCountTheLayoutsRoot_NotClaudes()
    {
        PlantClaudeFiles();
        Write(Path.Combine(_home, "projects", "repo-a", "session.jsonl"), "12345");

        IReadOnlyList<FootprintCategoryStats> rows =
            await new FootprintService(Layout).GetStatsAsync(CancellationToken.None);

        FootprintCategoryStats transcripts = rows.Single(r => r.Category == FootprintCategory.SessionTranscripts);
        Assert.Equal(1, transcripts.FileCount);
        Assert.Equal(5, transcripts.TotalBytes);
    }

    /// <summary>The neutral layout with its transcript directory taken away.</summary>
    private sealed class WithoutTranscripts(IArtifactLayout inner) : IArtifactLayout
    {
        public IReadOnlyList<CategorisedArtifactSource> InventorySources(string? projectRoot) =>
            inner.InventorySources(projectRoot);

        public IReadOnlyList<ScopedArtifactSource> EditorSources(string? projectRoot) =>
            inner.EditorSources(projectRoot);

        public FootprintCatalog FootprintCatalog => inner.FootprintCatalog;

        public FootprintRoots FootprintRoots => inner.FootprintRoots;

        public string? ProjectTranscriptsDirectory => null;
    }
}
