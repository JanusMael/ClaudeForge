using Bennewitz.Ninja.AgentForge.Artifacts;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Sdk.Memory;

/// <summary>
/// One artifact source plus the inventory category its entries belong to.
/// </summary>
/// <param name="Source">The source itself.</param>
/// <param name="Category">Which <see cref="UserMemoryCategory"/> the entries land in.</param>
/// <remarks>
/// ⚠ <b><see cref="UserMemoryCategory"/> conflates kind and scope, and
/// <see cref="ArtifactRef"/> deliberately does not.</b> Three categories —
/// <see cref="UserMemoryCategory.PrimaryMemory"/>, <see cref="UserMemoryCategory.ProjectMemory"/>
/// and <see cref="UserMemoryCategory.CrossToolMemory"/> — are all
/// <see cref="ArtifactKind.Memory"/>, separated only by where they came from. So the category
/// cannot be recovered from an entry alone; it is carried here, on the source that produces it,
/// which is the one place that knows both halves.
/// </remarks>
internal sealed record CategorisedArtifactSource(IArtifactSource Source, UserMemoryCategory Category);

/// <summary>
/// Claude Code's fixed artifact locations, expressed as an ordered source list.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Claude is the degenerate case, not a special case.</b> Its locations are a hardcoded set,
/// which is exactly why it is worth expressing through <see cref="IArtifactSource"/> first: if the
/// engine cannot reproduce a fixed walk exactly, it certainly cannot carry OpenCode's upward
/// traversal, three skill roots, and inline-JSON declarations. The existing inventory tests are the
/// proof, and they were not modified to accommodate this.
/// </para>
/// <para>
/// ⚠ <b>Every profile-rooted path arrives in <see cref="ClaudeArtifactPaths"/>; the three
/// project-scope ones are still read from <see cref="PlatformPaths"/>, and that is correct.</b>
/// <c>ProjectSettingsPath</c>, <c>LocalSettingsPath</c> and <c>ProjectMcpPath</c> are pure
/// functions of the project root this method already receives as an argument — there is no static
/// root in them to inject, and routing them through a profile-rooted provider would imply a
/// relationship that does not exist.
/// </para>
/// </remarks>
internal static class ClaudeArtifactSources
{
    // Editor and restore sidecars. Not authored artifacts: surfacing them would add one phantom
    // row per past restore × memory file, the same compounding noise the backup pipeline already
    // excludes (see ZipArchiveWriter.EnumerateRecursive and BackupEngine.ShouldSkipHomeFile).
    private static readonly string[] Sidecars = [".bak"];

    /// <summary>
    /// Build the source list backing the Tier 1 inventory, in walk order.
    /// </summary>
    /// <param name="paths">Where this profile's Claude files live.</param>
    /// <param name="projectRoot">
    /// The open project's root, or <see langword="null"/> when none is open. When absent the
    /// project sources are simply not created, so project categories contribute nothing.
    /// </param>
    internal static IReadOnlyList<CategorisedArtifactSource> ForInventory(
        ClaudeArtifactPaths paths, string? projectRoot)
    {
        List<CategorisedArtifactSource> sources = [];
        AddMemory(sources, paths.ClaudeHome, projectRoot);
        AddArtifactWalks(sources, paths.ClaudeHome);
        AddCrossTool(sources, paths.UserProfile);
        AddConfiguration(sources, paths, projectRoot);
        return sources;
    }

    /// <summary>
    /// The primary instruction files, at user scope and — when a project is open — project scope.
    /// </summary>
    private static void AddMemory(List<CategorisedArtifactSource> sources, string home, string? projectRoot)
    {
        sources.Add(new CategorisedArtifactSource(
            new FileProbeArtifactSource(
                new ArtifactSourceIdentity("claude-user-memory", ArtifactKind.Memory, ClaudeScopes.User),
                [
                    new ArtifactProbe("CLAUDE", Path.Combine(home, "CLAUDE.md")),
                    new ArtifactProbe("AGENTS", Path.Combine(home, "AGENTS.md")),
                ]),
            UserMemoryCategory.PrimaryMemory));

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        sources.Add(new CategorisedArtifactSource(
            new FileProbeArtifactSource(
                new ArtifactSourceIdentity("claude-project-memory", ArtifactKind.Memory, ClaudeScopes.Project),
                [
                    new ArtifactProbe("CLAUDE", Path.Combine(projectRoot, "CLAUDE.md")),
                    new ArtifactProbe("AGENTS", Path.Combine(projectRoot, "AGENTS.md")),
                ]),
            UserMemoryCategory.ProjectMemory));
    }

    /// <summary>
    /// The per-kind directory walks under <c>~/.claude/</c>.
    /// </summary>
    private static void AddArtifactWalks(List<CategorisedArtifactSource> sources, string home)
    {
        sources.Add(new CategorisedArtifactSource(
            Walk(new ArtifactSourceIdentity("claude-user-agents", ArtifactKind.Agent, ClaudeScopes.User),
                Path.Combine(home, "agents"), "*.md"),
            UserMemoryCategory.Subagent));

        sources.Add(new CategorisedArtifactSource(
            Walk(new ArtifactSourceIdentity("claude-user-commands", ArtifactKind.Command, ClaudeScopes.User),
                Path.Combine(home, "commands"), "*.md"),
            UserMemoryCategory.SlashCommand));

        // Hooks are scripts, so there is no agreed extension to filter on — "*" is the pattern
        // precisely because a hook may be a .sh, a .py, or extensionless.
        sources.Add(new CategorisedArtifactSource(
            Walk(new ArtifactSourceIdentity("claude-user-hooks", ArtifactKind.Hook, ClaudeScopes.User),
                Path.Combine(home, "hooks"), "*"),
            UserMemoryCategory.Hook));

        sources.Add(new CategorisedArtifactSource(
            Walk(new ArtifactSourceIdentity("claude-user-plans", ArtifactKind.Plan, ClaudeScopes.User),
                Path.Combine(home, "plans"), "*.md"),
            UserMemoryCategory.Plan));

        sources.Add(new CategorisedArtifactSource(
            Walk(new ArtifactSourceIdentity("claude-user-rules", ArtifactKind.Rule, ClaudeScopes.User),
                Path.Combine(home, "rules"), "*.md", recursive: true),
            UserMemoryCategory.Rule));

        sources.Add(new CategorisedArtifactSource(
            new SkillDirectoryArtifactSource(
                new ArtifactSourceIdentity("claude-user-skills", ArtifactKind.Skill, ClaudeScopes.User),
                Path.Combine(home, "skills"),
                "SKILL.md"),
            UserMemoryCategory.Skill));
    }

    /// <summary>
    /// Sibling agents' memory files, probed rather than discovered.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Names are tool-qualified.</b> <c>.codex/AGENTS.md</c> and <c>~/.claude/AGENTS.md</c>
    /// are both <see cref="ArtifactKind.Memory"/> named <c>AGENTS</c> if you take the file name at
    /// face value — and resolution would then report them as one artifact declared twice, i.e.
    /// claim that Codex's file shadows Claude's. They are different tools' files and neither
    /// shadows anything, so the owning directory is part of the identity.
    /// </remarks>
    private static void AddCrossTool(List<CategorisedArtifactSource> sources, string profile)
    {
        // ⭐ The profile arrives as a value now. It used to be recovered with
        // Directory.GetParent(home) plus a null branch that could never be taken — ClaudeHome is
        // always <profile>/.claude, so it always has a parent. Rooting the path provider at the
        // profile rather than at ClaudeHome deleted both.
        sources.Add(new CategorisedArtifactSource(
            new FileProbeArtifactSource(
                new ArtifactSourceIdentity("cross-tool-probes", ArtifactKind.Memory, ClaudeScopes.CrossTool),
                [
                    new ArtifactProbe(".codex/AGENTS", Path.Combine(profile, ".codex", "AGENTS.md")),
                    new ArtifactProbe(".gemini/GEMINI", Path.Combine(profile, ".gemini", "GEMINI.md")),
                ]),
            UserMemoryCategory.CrossToolMemory));

        // .opencode tends to be a directory of markdown files rather than one known file.
        sources.Add(new CategorisedArtifactSource(
            Walk(new ArtifactSourceIdentity("cross-tool-opencode", ArtifactKind.Memory, ClaudeScopes.CrossTool),
                Path.Combine(profile, ".opencode"), "*.md", namePrefix: ".opencode/"),
            UserMemoryCategory.CrossToolMemory));
    }

    /// <summary>
    /// The JSON configuration files themselves, so every file Claude reads is discoverable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>The user and project <c>settings.json</c> resolve as ONE artifact with a two-entry
    /// chain</b>, which is true rather than incidental: the project file really does override the
    /// user's. That relationship used to be invisible — the old walk produced two unrelated rows
    /// distinguished by a hand-written "(project)" suffix.
    /// </para>
    /// <para>
    /// ⚠ <see cref="PlatformPaths.CredentialsPath"/> is deliberately NOT a source. It holds live
    /// auth tokens, and a one-click "open" for it in a browsable list is a needless disclosure
    /// risk — the backup pipeline gates it behind an explicit opt-in for the same reason.
    /// </para>
    /// </remarks>
    private static void AddConfiguration(
        List<CategorisedArtifactSource> sources, ClaudeArtifactPaths paths, string? projectRoot)
    {
        sources.Add(new CategorisedArtifactSource(
            new FileProbeArtifactSource(
                new ArtifactSourceIdentity("claude-user-config", ArtifactKind.Configuration, ClaudeScopes.User),
                [
                    new ArtifactProbe("settings.json", paths.UserSettingsPath),
                    new ArtifactProbe("mcp.json", paths.UserMcpPath),
                    new ArtifactProbe(".claude.json", paths.ClaudeJsonPath),
                ]),
            UserMemoryCategory.Configuration));

        sources.Add(new CategorisedArtifactSource(
            new FileProbeArtifactSource(
                new ArtifactSourceIdentity("claude-managed-config", ArtifactKind.Configuration, ClaudeScopes.Managed),
                [new ArtifactProbe("managed-settings.json", paths.ManagedSettingsPath)]),
            UserMemoryCategory.Configuration));

        // Managed drop-ins: an admin-populated directory of *.json fragments. The name keeps its
        // directory prefix and its extension, because a fragment called 10-policy.json is
        // identified by where it sits as much as by what it is called.
        sources.Add(new CategorisedArtifactSource(
            Walk(new ArtifactSourceIdentity("claude-managed-dropins", ArtifactKind.Configuration, ClaudeScopes.Managed),
                paths.ManagedSettingsDropInDir, "*.json",
                stripExtension: false, namePrefix: "managed-settings.d/"),
            UserMemoryCategory.Configuration));

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return;
        }

        sources.Add(new CategorisedArtifactSource(
            new FileProbeArtifactSource(
                new ArtifactSourceIdentity("claude-project-config", ArtifactKind.Configuration, ClaudeScopes.Project),
                [
                    new ArtifactProbe("settings.json", PlatformPaths.ProjectSettingsPath(projectRoot)),
                    new ArtifactProbe("settings.local.json", PlatformPaths.LocalSettingsPath(projectRoot)),
                    new ArtifactProbe("mcp.json", PlatformPaths.ProjectMcpPath(projectRoot)),
                ]),
            UserMemoryCategory.Configuration));
    }

    /// <summary>
    /// A directory walk with the extension dropped unless the caller says the extension is part of
    /// the name.
    /// </summary>
    internal static DirectoryArtifactSource Walk(
        ArtifactSourceIdentity identity,
        string root,
        string searchPattern,
        bool recursive = false,
        bool stripExtension = true,
        string namePrefix = "")
    {
        return new DirectoryArtifactSource(identity, root, searchPattern, recursive, stripExtension)
        {
            // ⭐ Sidecar exclusion goes on the "*" walks ONLY, and that is a measurement rather
            // than an oversight: .NET's "*.md" does not match "reviewer.md.bak", so applying it to
            // a pattern-filtered walk is config that can never fire. Decorative config reads as
            // protection and provides none — the same failure family as a test that cannot fail.
            ExcludedSuffixes = searchPattern == "*" ? Sidecars : [],
            NamePrefix = namePrefix,
        };
    }
}
