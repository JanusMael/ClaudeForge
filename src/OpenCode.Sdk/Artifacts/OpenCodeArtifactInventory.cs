using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

/// <summary>
/// Something wrong with an artifact that the file system alone does not reveal.
/// </summary>
/// <remarks>
/// ⚠ <b>Each member records its evidence level, and they are not the same.</b> Two were measured
/// against v1.17.9; one is taken from OpenCode's own bundled specification because the probes
/// available here cannot see it. Presenting an asserted claim in the same voice as a measured one
/// is how a plan turns into folklore — see this repository's plan for how often that has cost time.
/// </remarks>
public enum OpenCodeArtifactIssue
{
    /// <summary>
    /// A <c>SKILL.md</c> with no <c>name:</c> in its front matter. <b>It never loads.</b>
    /// </summary>
    /// <remarks>
    /// ✅ <b>Measured.</b> A manifest carrying a <c>description</c> but no <c>name</c> did not appear
    /// in <c>opencode debug skill</c> at all. This is the one that most deserves surfacing: the
    /// folder looks complete and the skill is simply inert.
    /// </remarks>
    SkillHasNoDeclaredName,

    /// <summary>
    /// The declared <c>name:</c> does not match the folder the manifest sits in.
    /// </summary>
    /// <remarks>
    /// ✅ <b>Measured.</b> Folder <c>dirname-x</c> declaring <c>name: frontmatter-y</c> registered as
    /// <c>frontmatter-y</c> — the front matter wins. It loads, so this is a warning rather than a
    /// failure, but the skill is invocable under a name nothing in the directory tree suggests.
    /// </remarks>
    SkillNameDiffersFromFolder,

    /// <summary>
    /// A <c>SKILL.md</c> with no <c>description:</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Sourced from OpenCode's bundled spec, NOT measured here</b> — it says skills without a
    /// description "are filtered out and never surfaced to the model". The probes available cannot
    /// confirm it: such a skill <i>is</i> still listed by <c>opencode debug skill</c> (measured), so
    /// discovery and exposure to the model are different questions and only the first is observable.
    /// Report it as a warning about model visibility, never as "this does not load".
    /// </remarks>
    SkillHasNoDescription,
}

/// <summary>
/// One declaration of an artifact, with the facts a row needs about it.
/// </summary>
/// <param name="Entry">The declaration itself.</param>
/// <param name="IsCrossTool">
/// Whether it lives in another tool's directory, so editing it changes that tool too.
/// </param>
public sealed record OpenCodeArtifactDeclaration(ArtifactRef Entry, bool IsCrossTool);

/// <summary>
/// One artifact, its whole declaration chain, and what that chain means.
/// </summary>
public sealed record OpenCodeArtifactItem
{
    /// <summary>The name OpenCode knows it by.</summary>
    public required string Name { get; init; }

    /// <summary>What kind of artifact it is.</summary>
    public required ArtifactKind Kind { get; init; }

    /// <summary>Every declaration, highest precedence first. Never empty.</summary>
    public required IReadOnlyList<OpenCodeArtifactDeclaration> Declarations { get; init; }

    /// <summary>
    /// What it means that there is more than one declaration.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>A row must read this before it says a word about the losers.</b> Under
    /// <see cref="OpenCodeChainSemantics.DeepMerge"/> they are not losers at all — they are
    /// contributing fields — and calling them overridden tells the user to delete a file that is
    /// still in force.
    /// </remarks>
    public required OpenCodeChainSemantics Semantics { get; init; }

    /// <summary>Anything wrong, in the order found.</summary>
    public required IReadOnlyList<OpenCodeArtifactIssue> Issues { get; init; }

    /// <summary>The declared description, when the kind has one and it could be read.</summary>
    public string? Description { get; init; }

    /// <summary>The highest-precedence declaration.</summary>
    public ArtifactRef Effective => Declarations[0].Entry;

    /// <summary>True when more than one source declared this name.</summary>
    public bool HasMultipleDeclarations => Declarations.Count > 1;

    /// <summary>True when any declaration lives in another tool's directory.</summary>
    public bool IsCrossTool => Declarations.Any(d => d.IsCrossTool);

    /// <summary>
    /// True when OpenCode will not load this at all, as opposed to loading it imperfectly.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: only the measured, total failure counts. A warning that shares a
    /// treatment with a hard failure trains users to ignore both.
    /// </remarks>
    public bool IsInert => Issues.Contains(OpenCodeArtifactIssue.SkillHasNoDeclaredName);
}

/// <summary>
/// One tab's worth of artifacts.
/// </summary>
/// <param name="Kind">The kind this group holds.</param>
/// <param name="Items">Its artifacts, ordered by name.</param>
public sealed record OpenCodeArtifactGroup(ArtifactKind Kind, IReadOnlyList<OpenCodeArtifactItem> Items);

/// <summary>
/// Everything OpenCode would read for a given working directory, grouped, ordered and diagnosed.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The read model the page binds to, and the layer where every truth-claim lives.</b>
/// Resolution order comes from <see cref="OpenCodeArtifactSources"/>; what the resulting chain
/// <i>means</i> comes from <see cref="OpenCodeArtifactSemantics"/>; and the per-artifact problems
/// come from reading manifests. Keeping all three here means the view can be a dumb projection and
/// every claim it renders is testable without a UI.
/// </para>
/// <para>
/// ⚠ <b>Nothing is filtered out.</b> An inert skill, a manifest with no front matter and a file in
/// another tool's directory are all listed — flagged, never hidden. Hiding a broken artifact removes
/// it from view at exactly the moment the user has gone looking for it, and it happens silently and
/// only to the users who have a problem.
/// </para>
/// <para>
/// ⚠ <b>Known cost, stated rather than discovered later: each skill's manifest is read twice.</b>
/// Once by <see cref="OpenCodeSkillArtifactSource"/> to learn the name it groups by, once here to
/// diagnose it — both bounded to the front-matter head. On the machine this was measured against
/// that is 248 short reads for 124 skills, which is well inside a page load, and the alternative is
/// a cache whose invalidation is a worse problem than the reads. Revisit only with a measurement.
/// </para>
/// </remarks>
public static class OpenCodeArtifactInventory
{
    /// <summary>
    /// The order the tabs appear in, and the only kinds this page shows.
    /// </summary>
    /// <remarks>
    /// <see cref="ArtifactKind"/> is the shared vocabulary and carries kinds OpenCode has no
    /// analogue for (<c>Hook</c>, <c>Plan</c>). Listing them as empty tabs would claim the tool
    /// supports something it does not.
    /// </remarks>
    public static IReadOnlyList<ArtifactKind> TabOrder { get; } =
    [
        ArtifactKind.Agent,
        ArtifactKind.Command,
        ArtifactKind.Skill,
        ArtifactKind.Memory,
        ArtifactKind.Plugin,
    ];

    /// <summary>
    /// Build the whole page's read model.
    /// </summary>
    /// <param name="env">The environment, which decides the global directory.</param>
    /// <param name="workingDirectory">
    /// Where the user is working, or <see langword="null"/> for no project.
    /// </param>
    public static IReadOnlyList<OpenCodeArtifactGroup> Build(
        OpenCodeEnvironment env, string? workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(env);

        IReadOnlyList<ResolvedArtifact> resolved =
            ArtifactResolver.Resolve(OpenCodeArtifactSources.ForPage(env, workingDirectory));

        Dictionary<ArtifactKind, List<OpenCodeArtifactItem>> byKind = [];
        foreach (ArtifactKind kind in TabOrder)
        {
            byKind[kind] = [];
        }

        foreach (ResolvedArtifact artifact in resolved)
        {
            if (!byKind.TryGetValue(artifact.Kind, out List<OpenCodeArtifactItem>? items))
            {
                continue;
            }

            items.Add(Describe(artifact));
        }

        List<OpenCodeArtifactGroup> groups = new(TabOrder.Count);
        foreach (ArtifactKind kind in TabOrder)
        {
            groups.Add(new OpenCodeArtifactGroup(
                kind,
                [.. byKind[kind].OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)]));
        }

        return groups;
    }

    /// <summary>
    /// Turn one resolved chain into a row.
    /// </summary>
    private static OpenCodeArtifactItem Describe(ResolvedArtifact artifact)
    {
        List<OpenCodeArtifactDeclaration> declarations = new(artifact.Entries.Count);
        foreach (ArtifactRef entry in artifact.Entries)
        {
            declarations.Add(new OpenCodeArtifactDeclaration(
                entry, OpenCodeArtifactSemantics.IsCrossTool(entry)));
        }

        (IReadOnlyList<OpenCodeArtifactIssue> issues, string? description) =
            Diagnose(artifact);

        return new OpenCodeArtifactItem
        {
            Name = artifact.Name,
            Kind = artifact.Kind,
            Declarations = declarations,
            Semantics = OpenCodeArtifactSemantics.For(artifact.Kind),
            Issues = issues,
            Description = description,
        };
    }

    /// <summary>
    /// Read what the winning declaration says about itself.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Only the effective declaration is read, not the whole chain.</b> Reading every copy
    /// would multiply the file reads by the chain length to answer a question about the artifact
    /// that is actually in force. A lower copy's own problems become visible when it wins — which is
    /// exactly when they start to matter.
    /// </remarks>
    private static (IReadOnlyList<OpenCodeArtifactIssue> Issues, string? Description) Diagnose(
        ResolvedArtifact artifact)
    {
        if (artifact.Kind != ArtifactKind.Skill || artifact.Effective.Form != ArtifactForm.File)
        {
            return ([], null);
        }

        string manifestPath = artifact.Effective.Location;
        OpenCodeSkillManifest manifest = OpenCodeSkillManifest.Read(manifestPath);

        List<OpenCodeArtifactIssue> issues = [];
        if (string.IsNullOrWhiteSpace(manifest.DeclaredName))
        {
            issues.Add(OpenCodeArtifactIssue.SkillHasNoDeclaredName);
        }
        else
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(manifestPath) ?? string.Empty);
            if (!string.Equals(manifest.DeclaredName, folder, StringComparison.Ordinal))
            {
                issues.Add(OpenCodeArtifactIssue.SkillNameDiffersFromFolder);
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.Description))
        {
            issues.Add(OpenCodeArtifactIssue.SkillHasNoDescription);
        }

        return (issues, manifest.Description);
    }
}
