using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

/// <summary>
/// The precedence layers OpenCode reads artifacts from — <b>and they are not one ladder.</b>
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>Agents and commands rank the global directory ABOVE the project; skills rank it below.</b>
/// Measured against v1.17.9, twice, with a deliberate name collision:
/// </para>
/// <list type="bullet">
/// <item>
/// An agent declared in the global config directory <i>and</i> in the project supplied the winning
/// <c>description</c> from the <b>global</b> file, while a field only the project file set survived
/// alongside it. Agent and command files <b>deep-merge per field</b>, loaded nearest-ancestor
/// outward and then global, and the <b>last writer wins</b> — so global ends up on top.
/// </item>
/// <item>
/// A skill of the same name in both places resolved to the <b>project</b> copy, as a single entry
/// with the global one not surfaced at all.
/// </item>
/// </list>
/// <para>
/// ⚠ <b>So a single shared precedence table would be wrong for one kind or the other</b>, and wrong
/// in the direction that matters: a page built on the intuitive "project beats global" would name
/// the wrong winning agent, confidently, on every machine with a global agent directory. The
/// numbers below therefore depend on the <see cref="ArtifactKind"/>, which is legal precisely
/// because <see cref="ArtifactScope"/> is a per-product record rather than a shared enum.
/// </para>
/// <para>
/// ⭐ <b>Within the project chain both kinds agree: the FARTHER ancestor wins.</b> Also measured —
/// a definition at the worktree root beat one in an intermediate directory nearer the working
/// directory, for agents and for skills alike. That is the opposite of the "most specific wins"
/// intuition, and it is not alphabetical either: the nearer path sorts later and still lost.
/// </para>
/// </remarks>
public static class OpenCodeArtifactScopes
{
    /// <summary>
    /// The bottom of the project band. Each ancestor sits at this plus its distance from the
    /// working directory, so the worktree root — the farthest — ends up highest.
    /// </summary>
    private const int ProjectBase = 1_000;

    /// <summary>
    /// Where the global directory sits for the kinds that deep-merge with global loaded last.
    /// Far above the project band so no realistic directory depth can reach it.
    /// </summary>
    private const int GlobalOverProject = 1_000_000;

    /// <summary>Where the global directory sits for the kinds the project outranks.</summary>
    private const int GlobalUnderProject = 500;

    /// <summary>Another tool's directory, read by arrangement rather than as OpenCode's own.</summary>
    private const int ExternalPrecedence = 100;

    /// <summary>Shipped inside the binary, and overridable by every other layer.</summary>
    private const int BuiltInPrecedence = 10;

    /// <summary>
    /// One ancestor directory of the working directory.
    /// </summary>
    /// <param name="directory">The ancestor itself — the parent of its <c>.opencode/</c>.</param>
    /// <param name="distance">
    /// How many levels above the working directory it sits; 0 is the working directory. Higher
    /// wins, per the measurement in this class's remarks.
    /// </param>
    /// <param name="isWorktreeRoot">
    /// Whether this ancestor is the git worktree root, which is worth saying in the UI: it is the
    /// boundary the walk stops at, and the layer that outranks every nearer one.
    /// </param>
    public static ArtifactScope Project(string directory, int distance, bool isWorktreeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(distance);

        string label = isWorktreeRoot ? "Project root" : "Project";
        return new ArtifactScope(
            $"project:{directory}",
            $"{label} — {Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}",
            ProjectBase + distance);
    }

    /// <summary>
    /// The global config directory, ranked for <paramref name="kind"/>.
    /// </summary>
    /// <param name="directory">
    /// The resolved global directory — <c>$OPENCODE_CONFIG_DIR</c> when set, otherwise
    /// <c>~/.config/opencode</c>.
    /// </param>
    /// <param name="kind">
    /// What is being ranked. <see cref="ArtifactKind.Agent"/> and
    /// <see cref="ArtifactKind.Command"/> put global on top; everything else puts it under the
    /// project. See this class's remarks for the measurements.
    /// </param>
    public static ArtifactScope Global(string directory, ArtifactKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        int precedence = MergesWithGlobalLast(kind) ? GlobalOverProject : GlobalUnderProject;
        return new ArtifactScope($"global:{directory}", "Global", precedence);
    }

    /// <summary>
    /// A root belonging to another tool that OpenCode reads anyway — <c>~/.claude/skills</c>,
    /// <c>~/.agents/skills</c>.
    /// </summary>
    /// <param name="id">A stable suffix identifying which one, e.g. <c>claude</c>.</param>
    /// <param name="displayName">What to show, e.g. <c>Claude Code</c>.</param>
    /// <remarks>
    /// ⭐ <b>Badge these rows.</b> Editing one of these files changes what a second tool does, and
    /// a user deleting a "duplicate" skill from what looks like OpenCode's own list would be
    /// editing Claude Code's installation. Three environment variables turn these scans off
    /// (<c>OPENCODE_DISABLE_EXTERNAL_SKILLS</c>, <c>OPENCODE_DISABLE_CLAUDE_CODE_SKILLS</c>,
    /// <c>OPENCODE_DISABLE_CLAUDE_CODE</c>), so their presence is not unconditional.
    /// </remarks>
    public static ArtifactScope External(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new ArtifactScope($"external:{id}", displayName, ExternalPrecedence);
    }

    /// <summary>Artifacts shipped inside the binary.</summary>
    /// <remarks>
    /// Every other layer overrides these by name, which is the documented way to retune a built-in
    /// agent. They are listed so the page can show that <c>build</c> exists before the user has
    /// declared anything at all.
    /// </remarks>
    public static ArtifactScope BuiltIn { get; } = new("built-in", "Built in", BuiltInPrecedence);

    /// <summary>
    /// Whether <paramref name="kind"/> is one of the two the global directory outranks the project
    /// for.
    /// </summary>
    /// <remarks>
    /// ⚠ Expressed as a predicate on the kind rather than a flag on the caller, so the two places
    /// that need the answer — scope construction and the chain the UI renders — cannot disagree
    /// about it. Only agents and commands were measured to merge this way; anything else is ranked
    /// the ordinary way rather than assumed into the same bucket.
    /// </remarks>
    public static bool MergesWithGlobalLast(ArtifactKind kind)
        => kind is ArtifactKind.Agent or ArtifactKind.Command;
}
