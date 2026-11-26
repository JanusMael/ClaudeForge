using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Testing;

/// <summary>
/// The neutral test product's artifact layout, for testing the memory and footprint services in
/// <c>AgentForge.Sdk</c> without Claude's (<c>plans/00008</c> decision 6, step 9).
/// </summary>
/// <param name="home">
/// The product home, resolved on every read as <see cref="IArtifactLayout"/> requires.
/// </param>
/// <remarks>
/// <para>
/// ⭐ <b>Every location here differs from Claude's on purpose.</b> The memory file, the artifact
/// directories, the configuration file and the plugin tree all have names of their own, so a test
/// built on this layout fails if a service still reaches for a Claude location instead of reading
/// the layout.
/// </para>
/// <para>
/// ⚠ <b>Its footprint catalog is <see cref="FootprintCatalog.Default"/> until step 13</b>, which
/// moves Claude's catalog out of the SDK; its transcript directory follows that catalog's
/// session-transcripts category, so the two agree. The home they resolve against is still its
/// own.
/// </para>
/// <para>
/// ⛔ Linked into each test project that needs it, never copied, so there is one definition.
/// </para>
/// </remarks>
internal sealed class NeutralTestArtifactLayout(Func<string> home) : IArtifactLayout
{
    /// <summary>The primary memory file, at the home and at a project root.</summary>
    public const string MemoryFileName = "NEUTRAL.md";

    /// <summary>The directory of agents.</summary>
    public const string AgentsDirectory = "personas";

    /// <summary>The directory of commands.</summary>
    public const string CommandsDirectory = "macros";

    /// <summary>The directory of skill folders.</summary>
    public const string SkillsDirectory = "abilities";

    /// <summary>The file that marks a skill folder.</summary>
    public const string SkillMarker = "ABILITY.md";

    /// <summary>The read-only vendor tree, whose agents land in the plugin scope.</summary>
    public const string VendorDirectory = "vendor";

    /// <summary>The project directory holding the project's own artifacts.</summary>
    public const string ProjectDirectory = ".neutral";

    /// <summary>The suffix that tells a project configuration row from the user's.</summary>
    public const string ProjectSuffix = " [project]";

    /// <summary>The user scope.</summary>
    public static readonly ArtifactScope User = new("neutral-user", "Neutral user", 10);

    /// <summary>The project scope.</summary>
    public static readonly ArtifactScope Project = new("neutral-project", "Neutral project", 20);

    /// <summary>The vendor scope.</summary>
    public static readonly ArtifactScope Vendor = new("neutral-vendor", "Neutral vendor", 0);

    private readonly Func<string> _home = home ?? throw new ArgumentNullException(nameof(home));

    /// <summary>
    /// The layout under the current process profile, re-reading
    /// <see cref="PlatformPaths.UserProfile"/> on every use.
    /// </summary>
    public static NeutralTestArtifactLayout UnderProfile() =>
        new(() => Path.Combine(PlatformPaths.UserProfile, NeutralTestProduct.HomeDirectoryName));

    /// <summary>The home this read resolves to.</summary>
    public string Home => _home();

    /// <inheritdoc/>
    public IReadOnlyList<CategorisedArtifactSource> InventorySources(string? projectRoot)
    {
        string home = Home;
        List<CategorisedArtifactSource> sources =
        [
            new(Probe("neutral-user-memory", ArtifactKind.Memory, User, "NEUTRAL", Path.Combine(home, MemoryFileName)),
                UserMemoryCategory.PrimaryMemory),
            new(Walk("neutral-user-agents", ArtifactKind.Agent, User, Path.Combine(home, AgentsDirectory)),
                UserMemoryCategory.Subagent),
            new(Walk("neutral-user-commands", ArtifactKind.Command, User, Path.Combine(home, CommandsDirectory)),
                UserMemoryCategory.SlashCommand),
            new(Skills("neutral-user-skills", User, Path.Combine(home, SkillsDirectory)),
                UserMemoryCategory.Skill),
            new(Probe("neutral-user-config", ArtifactKind.Configuration, User,
                    NeutralTestProduct.ConfigFileName, Path.Combine(home, NeutralTestProduct.ConfigFileName)),
                UserMemoryCategory.Configuration),
        ];

        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            sources.Add(new(
                Probe("neutral-project-memory", ArtifactKind.Memory, Project, "NEUTRAL",
                    Path.Combine(projectRoot, MemoryFileName)),
                UserMemoryCategory.ProjectMemory));
            sources.Add(new(
                Probe("neutral-project-config", ArtifactKind.Configuration, Project,
                    NeutralTestProduct.ConfigFileName,
                    Path.Combine(projectRoot, ProjectDirectory, NeutralTestProduct.ConfigFileName)),
                UserMemoryCategory.Configuration,
                ProjectSuffix));
        }

        return sources;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ScopedArtifactSource> EditorSources(string? projectRoot)
    {
        string home = Home;
        List<ScopedArtifactSource> sources = [];
        AddEditable(sources, home, "neutral-user", User, EditableMemoryScope.User);

        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            AddEditable(sources, Path.Combine(projectRoot, ProjectDirectory), "neutral-project", Project,
                EditableMemoryScope.Project);
        }

        sources.Add(new(
            Walk("neutral-vendor-agents", ArtifactKind.Agent, Vendor,
                Path.Combine(home, VendorDirectory, AgentsDirectory)),
            EditableMemoryScope.Plugin));

        return sources;
    }

    /// <inheritdoc/>
    public FootprintCatalog FootprintCatalog => FootprintCatalog.Default;

    /// <inheritdoc/>
    public FootprintRoots FootprintRoots => FootprintRoots.Single(FootprintRoots.Home, Home);

    /// <inheritdoc/>
    public string? ProjectTranscriptsDirectory => Path.Combine(Home, "projects");

    private static void AddEditable(
        List<ScopedArtifactSource> sources, string directory, string prefix, ArtifactScope scope,
        EditableMemoryScope editable)
    {
        sources.Add(new(Walk($"{prefix}-agents", ArtifactKind.Agent, scope, Path.Combine(directory, AgentsDirectory)),
            editable));
        sources.Add(new(Walk($"{prefix}-commands", ArtifactKind.Command, scope, Path.Combine(directory, CommandsDirectory)),
            editable));
        sources.Add(new(Skills($"{prefix}-skills", scope, Path.Combine(directory, SkillsDirectory)), editable));
    }

    private static FileProbeArtifactSource Probe(
        string id, ArtifactKind kind, ArtifactScope scope, string name, string path) =>
        new(new ArtifactSourceIdentity(id, kind, scope), [new ArtifactProbe(name, path)]);

    private static DirectoryArtifactSource Walk(string id, ArtifactKind kind, ArtifactScope scope, string root) =>
        new(new ArtifactSourceIdentity(id, kind, scope), root, "*.md", recursive: false, stripExtension: true);

    private static SkillDirectoryArtifactSource Skills(string id, ArtifactScope scope, string root) =>
        new(new ArtifactSourceIdentity(id, ArtifactKind.Skill, scope), root, SkillMarker);
}
