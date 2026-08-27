using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk.Agents;

namespace Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

/// <summary>
/// The built-in agents, as artifacts, so the page can show that <c>build</c> exists before the
/// user has declared anything.
/// </summary>
/// <remarks>
/// Reads <see cref="OpenCodeBuiltInAgents.Names"/> rather than restating the seven names, so the
/// schema-drift guard that already watches that set covers this list too.
/// </remarks>
internal sealed class OpenCodeBuiltInAgentSource(string id) : IArtifactSource
{
    /// <inheritdoc/>
    public string Id => id;

    /// <inheritdoc/>
    public IEnumerable<ArtifactRef> Enumerate()
    {
        List<ArtifactRef> found = [];
        foreach (string name in OpenCodeBuiltInAgents.Names)
        {
            // Location is the name itself: there is no file, and inventing a plausible path
            // would send "open this" somewhere that does not exist.
            found.Add(new ArtifactRef(
                name, ArtifactKind.Agent, OpenCodeArtifactScopes.BuiltIn,
                ArtifactForm.BuiltIn, name, id));
        }

        return found;
    }
}

/// <summary>
/// Every place OpenCode reads agents, commands, skills, plugins and rules from, as one ordered
/// source list.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This is the case the artifact engine was built for.</b> Claude's locations are a fixed
/// set; OpenCode's are computed — one <c>.opencode/</c> per ancestor directory up to the git
/// worktree root, two spellings of every directory name, three skill roots belonging to two other
/// tools, and a global directory whose precedence depends on which kind is being resolved.
/// </para>
/// <para>
/// ⚠ <b>Both spellings of every directory are scanned, and they are not alternatives.</b>
/// <c>agent/</c> and <c>agents/</c> resolve <b>simultaneously</b> — measured — as do
/// <c>command(s)/</c>, <c>skill(s)/</c> and <c>plugin(s)/</c>. Picking one would silently find
/// nothing for half of all users, and picking "whichever exists" would find nothing for anyone
/// using both.
/// </para>
/// <para>
/// ⚠ <b>Every source id embeds its directory.</b> There are as many project sources as the user has
/// ancestor directories, so a fixed id per kind would collide the moment a project has two levels —
/// and both consumers of this vocabulary map ids through a dictionary, where a duplicate throws
/// while the map is built and the page fails to load entirely.
/// </para>
/// </remarks>
public static class OpenCodeArtifactSources
{
    /// <summary>
    /// Prefix on the scope id of a root belonging to another tool.
    /// </summary>
    /// <remarks>
    /// Public so the cross-tool badge is derived from the same string that creates these scopes.
    /// A consumer restating <c>"external:"</c> would keep working right up until this prefix
    /// changed, and then badge nothing with no test noticing.
    /// </remarks>
    public const string ExternalScopePrefix = "external:";

    /// <summary>
    /// Suffix on the source id of a project's own <c>.claude/skills/</c> walk.
    /// </summary>
    /// <remarks>
    /// ⚠ That directory is cross-tool but sits in the <b>project</b> scope, so it is identifiable
    /// only by its source. See <c>OpenCodeArtifactSemantics.IsCrossTool</c>.
    /// </remarks>
    public const string ClaudeSkillsSourceSuffix = ":claude-skills";

    /// <summary>The two spellings OpenCode accepts for each artifact directory.</summary>
    private static readonly (string Singular, string Plural) AgentDirs = ("agent", "agents");
    private static readonly (string Singular, string Plural) CommandDirs = ("command", "commands");
    private static readonly (string Singular, string Plural) SkillDirs = ("skill", "skills");
    private static readonly (string Singular, string Plural) PluginDirs = ("plugin", "plugins");

    /// <summary>Plugin files are loaded by extension, and both extensions load.</summary>
    private static readonly string[] PluginPatterns = ["*.ts", "*.js"];

    /// <summary>
    /// Build the full source list for a working directory, in walk order.
    /// </summary>
    /// <param name="env">
    /// The environment, which decides the global directory. ⚠ Note that
    /// <c>$OPENCODE_CONFIG_DIR</c> does <b>not</b> relocate plugin discovery — see
    /// <see cref="AddGlobal"/>.
    /// </param>
    /// <param name="workingDirectory">
    /// Where the user is working. <see langword="null"/> or blank contributes no project sources at
    /// all, rather than silently falling back to the process's own directory — this library is
    /// hosted by a GUI whose current directory means nothing to the user.
    /// </param>
    public static IReadOnlyList<IArtifactSource> ForPage(
        OpenCodeEnvironment env, string? workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(env);

        List<IArtifactSource> sources = [];
        AddProject(sources, workingDirectory);
        AddGlobal(sources, env);
        AddExternalSkillRoots(sources);
        sources.Add(new OpenCodeBuiltInAgentSource("oc-built-in-agents"));
        return sources;
    }

    /// <summary>
    /// One set of sources per ancestor directory, nearest first.
    /// </summary>
    private static void AddProject(List<IArtifactSource> sources, string? workingDirectory)
    {
        IReadOnlyList<string> ancestors = OpenCodeProjectWalk.Ancestors(workingDirectory);

        for (int distance = 0; distance < ancestors.Count; distance++)
        {
            string directory = ancestors[distance];

            // The walk stops AT the worktree root, so only the last entry can be it — and only
            // when the walk ended by finding one rather than by running out of parents.
            bool isRoot = distance == ancestors.Count - 1
                          && (Directory.Exists(Path.Combine(directory, ".git"))
                              || File.Exists(Path.Combine(directory, ".git")));

            ArtifactScope scope = OpenCodeArtifactScopes.Project(directory, distance, isRoot);
            string openCode = Path.Combine(directory, ".opencode");
            string tag = $"project:{directory}";

            AddDirectoryPair(sources, scope, ArtifactKind.Agent, openCode, AgentDirs, $"{tag}:agent");
            AddDirectoryPair(sources, scope, ArtifactKind.Command, openCode, CommandDirs, $"{tag}:command");
            AddSkillPair(sources, scope, openCode, SkillDirs, $"{tag}:skill");
            AddPluginPair(sources, scope, openCode, $"{tag}:plugin");

            // ⭐ Measured, and absent from OpenCode's own bundled spec: a project's
            // .claude/skills/ is scanned too, not only the user-level ~/.claude/skills.
            sources.Add(new OpenCodeSkillArtifactSource(
                new ArtifactSourceIdentity(
                    tag + ClaudeSkillsSourceSuffix, ArtifactKind.Skill, scope),
                Path.Combine(directory, ".claude", "skills")));

            AddRuleProbes(sources, scope, directory, tag);
        }
    }

    /// <summary>
    /// The global config directory's own artifact roots.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b><c>$OPENCODE_CONFIG_DIR</c> does not relocate plugin discovery — it adds to it.</b>
    /// Measured: with the variable pointed at a sandbox, a plugin in the sandbox <i>and</i> a plugin
    /// in the real <c>~/.config/opencode/plugins/</c> both loaded, both reported by OpenCode as
    /// <c>scope: global</c>. So the default directory is scanned for plugins even when the user has
    /// redirected the config elsewhere, and a page that honoured the variable alone would omit
    /// plugins that are genuinely running. Same family as the documented gotcha that a global
    /// <c>AGENTS.md</c> under <c>$OPENCODE_CONFIG_DIR</c> is silently ignored: the variable is
    /// honoured for some surfaces and not others, so each surface is treated on its own evidence.
    /// </remarks>
    private static void AddGlobal(List<IArtifactSource> sources, OpenCodeEnvironment env)
    {
        string directory = OpenCodePaths.GlobalDirectory(env);
        string tag = $"global:{directory}";

        AddDirectoryPair(
            sources, OpenCodeArtifactScopes.Global(directory, ArtifactKind.Agent),
            ArtifactKind.Agent, directory, AgentDirs, $"{tag}:agent");
        AddDirectoryPair(
            sources, OpenCodeArtifactScopes.Global(directory, ArtifactKind.Command),
            ArtifactKind.Command, directory, CommandDirs, $"{tag}:command");
        AddSkillPair(
            sources, OpenCodeArtifactScopes.Global(directory, ArtifactKind.Skill),
            directory, SkillDirs, $"{tag}:skill");
        AddPluginPair(
            sources, OpenCodeArtifactScopes.Global(directory, ArtifactKind.Plugin),
            directory, $"{tag}:plugin");

        sources.Add(new FileProbeArtifactSource(
            new ArtifactSourceIdentity(
                $"{tag}:rules", ArtifactKind.Memory,
                OpenCodeArtifactScopes.Global(directory, ArtifactKind.Memory)),
            [new ArtifactProbe("AGENTS", Path.Combine(directory, "AGENTS.md"))]));

        // The default directory's plugins load regardless of the variable — see the remarks.
        string fallback = Path.Combine(PlatformPaths.UserProfile, ".config", "opencode");
        if (!string.Equals(Path.TrimEndingDirectorySeparator(fallback),
                           Path.TrimEndingDirectorySeparator(directory),
                           StringComparison.OrdinalIgnoreCase))
        {
            AddPluginPair(
                sources, OpenCodeArtifactScopes.Global(fallback, ArtifactKind.Plugin),
                fallback, $"global:{fallback}:plugin");
        }
    }

    /// <summary>
    /// The two user-level skill roots belonging to other tools.
    /// </summary>
    /// <remarks>
    /// ⭐ These are real and populated: on the machine this was measured on, 118 of the 124 skills
    /// OpenCode resolved came from <c>~/.claude/skills</c> and 6 more from <c>~/.agents/skills</c>.
    /// Cross-tool overlap is the normal case here, not an edge one.
    /// </remarks>
    private static void AddExternalSkillRoots(List<IArtifactSource> sources)
    {
        string profile = PlatformPaths.UserProfile;

        sources.Add(new OpenCodeSkillArtifactSource(
            new ArtifactSourceIdentity(
                "oc-external-claude-skills", ArtifactKind.Skill,
                OpenCodeArtifactScopes.External("claude", "Claude Code")),
            Path.Combine(profile, ".claude", "skills")));

        sources.Add(new OpenCodeSkillArtifactSource(
            new ArtifactSourceIdentity(
                "oc-external-agents-skills", ArtifactKind.Skill,
                OpenCodeArtifactScopes.External("agents", "Shared agents")),
            Path.Combine(profile, ".agents", "skills")));
    }

    /// <summary>
    /// The rule files at one directory, in the order OpenCode consults them.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><c>CLAUDE.md</c> is consulted at project level too</b>, not only as the
    /// <c>~/.claude/CLAUDE.md</c> fallback the plan describes: the documented local rule is
    /// "traverse up for <c>AGENTS.md</c>, then <c>CLAUDE.md</c>". They are named apart so a project
    /// holding both does not resolve as one file declared twice.
    /// </remarks>
    private static void AddRuleProbes(
        List<IArtifactSource> sources, ArtifactScope scope, string directory, string tag)
    {
        sources.Add(new FileProbeArtifactSource(
            new ArtifactSourceIdentity($"{tag}:rules", ArtifactKind.Memory, scope),
            [
                new ArtifactProbe("AGENTS", Path.Combine(directory, "AGENTS.md")),
                new ArtifactProbe("CLAUDE", Path.Combine(directory, "CLAUDE.md")),
            ]));
    }

    /// <summary>
    /// Both spellings of one artifact directory, walked recursively.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Recursive, and the name keeps the relative path.</b> Measured: an agent at
    /// <c>.opencode/agent/nested/reviewer.md</c> resolves under the name
    /// <c>nested/reviewer</c> — path included — which is exactly what
    /// <c>DirectoryArtifactSource</c> already produces. Commands behave identically. Skills do not,
    /// which is why they have their own source.
    /// </remarks>
    private static void AddDirectoryPair(
        List<IArtifactSource> sources, ArtifactScope scope, ArtifactKind kind,
        string parent, (string Singular, string Plural) names, string tag)
    {
        foreach (string name in new[] { names.Singular, names.Plural })
        {
            sources.Add(new DirectoryArtifactSource(
                new ArtifactSourceIdentity($"{tag}:{name}", kind, scope),
                Path.Combine(parent, name),
                "*.md",
                recursive: true,
                stripExtension: true));
        }
    }

    /// <summary>Both spellings of a skill root.</summary>
    private static void AddSkillPair(
        List<IArtifactSource> sources, ArtifactScope scope,
        string parent, (string Singular, string Plural) names, string tag)
    {
        foreach (string name in new[] { names.Singular, names.Plural })
        {
            sources.Add(new OpenCodeSkillArtifactSource(
                new ArtifactSourceIdentity($"{tag}:{name}", ArtifactKind.Skill, scope),
                Path.Combine(parent, name)));
        }
    }

    /// <summary>
    /// Both spellings of a plugin directory, both extensions, <b>not</b> recursive.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Flat, unlike every other kind here.</b> Measured: a plugin at
    /// <c>.opencode/plugin/nested/thing.ts</c> was <b>not</b> loaded, while its siblings in the
    /// directory itself were. Walking it recursively would list files OpenCode ignores, in a tab
    /// whose whole purpose is showing which plugins are live.
    /// </remarks>
    private static void AddPluginPair(
        List<IArtifactSource> sources, ArtifactScope scope, string parent, string tag)
    {
        foreach (string name in new[] { PluginDirs.Singular, PluginDirs.Plural })
        {
            foreach (string pattern in PluginPatterns)
            {
                sources.Add(new DirectoryArtifactSource(
                    new ArtifactSourceIdentity($"{tag}:{name}{pattern}", ArtifactKind.Plugin, scope),
                    Path.Combine(parent, name),
                    pattern,
                    recursive: false,

                    // The extension is part of what the file is: a.ts and a.js in one directory
                    // are two plugins and OpenCode loads both.
                    stripExtension: false));
            }
        }
    }
}
