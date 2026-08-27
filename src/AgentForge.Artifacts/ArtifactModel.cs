namespace Bennewitz.Ninja.AgentForge.Artifacts;

/// <summary>
/// What kind of thing an artifact is.
/// </summary>
/// <remarks>
/// ⚠ <b>Deliberately NOT one enum per product.</b> These names are the shared vocabulary — both
/// products have agents, commands, skills and rules, and the differences are in <i>where</i> they
/// come from, which is what <see cref="IArtifactSource"/> expresses. A product that has no analogue
/// for a kind simply contributes no source for it.
/// </remarks>
public enum ArtifactKind
{
    /// <summary>A primary instruction file — <c>CLAUDE.md</c>, <c>AGENTS.md</c>.</summary>
    Memory,

    /// <summary>An agent or subagent definition.</summary>
    Agent,

    /// <summary>A slash command.</summary>
    Command,

    /// <summary>A skill — a directory with a manifest.</summary>
    Skill,

    /// <summary>An additional rules file.</summary>
    Rule,

    /// <summary>A hook script.</summary>
    Hook,

    /// <summary>A plugin.</summary>
    Plugin,

    /// <summary>A saved plan.</summary>
    Plan,

    /// <summary>A configuration file, surfaced so every file the tool reads is discoverable.</summary>
    Configuration,
}

/// <summary>
/// How an artifact reached the tool: the form it is declared in.
/// </summary>
/// <remarks>
/// ⭐ <b>Separate from precedence on purpose.</b> Spike S7 measured that OpenCode does not let a
/// markdown file <i>shadow</i> an inline-JSON agent of the same name — the two <b>deep-merge, with
/// the file winning per field</b>, so an inline-only <c>temperature</c> stays live. A consumer can
/// only implement that if it can still see which form each entry came from after resolution, which
/// is why the form travels on the entry rather than being collapsed into an ordering.
/// </remarks>
public enum ArtifactForm
{
    /// <summary>A file found by walking a conventional directory.</summary>
    File,

    /// <summary>A map declared inline in a configuration file.</summary>
    Inline,

    /// <summary>Built into the tool and overridable by name.</summary>
    BuiltIn,

    /// <summary>
    /// Declared as a remote URL. <b>Listed, never fetched</b> — see the v1 limit in the plan.
    /// </summary>
    Remote,
}

/// <summary>
/// Where an artifact came from, as an ordered precedence layer.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>global</c> or <c>project</c>.</param>
/// <param name="DisplayName">What the UI shows.</param>
/// <param name="Precedence">
/// Higher wins. Compared only within one resolution, so the numbers are relative and a product may
/// choose any scale.
/// </param>
/// <remarks>
/// ⚠ <b>A scope is supplied by the product, not enumerated here.</b> Claude has user and project;
/// OpenCode has a global root, three separate skill roots, and every ancestor directory up to the
/// git worktree root — a closed enum would have to be widened for each, and widening a shared enum
/// per product is the mistake <c>EditableMemoryScope</c> already demonstrates.
/// </remarks>
public sealed record ArtifactScope(string Id, string DisplayName, int Precedence);

/// <summary>
/// One declaration of one artifact, by one source.
/// </summary>
/// <param name="Name">
/// The identity resolution groups by. Two entries with the same name and kind are the same logical
/// artifact declared twice.
/// </param>
/// <param name="Kind">What kind of artifact this is.</param>
/// <param name="Scope">The precedence layer this declaration sits in.</param>
/// <param name="Form">How it was declared.</param>
/// <param name="Location">
/// Where it is, for display and for opening: an absolute path for a file, a JSON path for an inline
/// declaration, a URL for a remote one.
/// </param>
/// <param name="SourceId">
/// Which <see cref="IArtifactSource"/> produced this, so a chain can say not just "global" but
/// "the global skills root" versus "the Claude skills root this product also reads".
/// </param>
/// <remarks>
/// ⚠ <b>No content and no parsed front matter.</b> Enumeration must stay cheap enough to populate a
/// page with hundreds of skill files, so a source stats rather than reads — the existing
/// <c>UserMemoryService</c> already draws that line and this preserves it. Reading is a separate,
/// lazy step keyed on <see cref="Location"/>.
/// </remarks>
public sealed record ArtifactRef(
    string Name,
    ArtifactKind Kind,
    ArtifactScope Scope,
    ArtifactForm Form,
    string Location,
    string SourceId);

/// <summary>
/// Every declaration of one artifact name, ordered by precedence.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Mirrors <c>LayeredValue</c> deliberately</b> — <see cref="Entries"/> for the chain,
/// <see cref="Effective"/> for the winner, <see cref="IsShadowed"/> for "more than one contributed".
/// That is the vocabulary the settings UI's scope badges already bind to, so showing an artifact's
/// provenance needs no new controls.
/// </para>
/// <para>
/// ⚠⚠ <b><see cref="Effective"/> is the highest-precedence DECLARATION, which is not always the
/// effective ARTIFACT.</b> For a file-only kind the two coincide. For OpenCode's agents they do not:
/// S7 measured a per-field merge, so the effective agent is assembled from the whole chain. That
/// fold is per-kind policy and lives in the consumer; this type's contract is only that the chain is
/// complete and correctly ordered.
/// </para>
/// </remarks>
public sealed record ResolvedArtifact
{
    /// <summary>The artifact's name.</summary>
    public required string Name { get; init; }

    /// <summary>What kind of artifact this is.</summary>
    public required ArtifactKind Kind { get; init; }

    /// <summary>
    /// Every declaration, highest precedence first. Never empty.
    /// </summary>
    /// <remarks>
    /// Ties — two declarations at the same precedence — keep the order their sources were listed
    /// in, because that is the only ordering the resolver can honestly claim. A product that needs a
    /// tie broken should give the two sources different precedences rather than rely on this.
    /// </remarks>
    public required IReadOnlyList<ArtifactRef> Entries { get; init; }

    /// <summary>The highest-precedence declaration.</summary>
    public ArtifactRef Effective => Entries[0];

    /// <summary>The declarations the winner takes precedence over, in order.</summary>
    public IEnumerable<ArtifactRef> Shadowed => Entries.Skip(1);

    /// <summary>True when more than one source declared this name.</summary>
    public bool IsShadowed => Entries.Count > 1;
}

/// <summary>
/// One place artifacts come from.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This interface is the phase's whole point.</b> "Walk these directories" cannot express
/// OpenCode's upward traversal, its three skill roots, or an artifact declared as both a file and
/// inline JSON. "Resolve from an ordered list of sources" can, and Claude's fixed locations become
/// one such list — the degenerate case rather than a special case.
/// </para>
/// <para>
/// ⚠ <b><see cref="Enumerate"/> must not throw.</b> A source whose directory is missing, unreadable
/// or full of broken symlinks contributes nothing and lets its neighbours contribute normally. The
/// existing memory inventory already promises this ("never throws on enumeration") and losing it
/// would turn one bad ACL into an empty page.
/// </para>
/// <para>
/// ⛔ <b>Deliberately NOT <c>Kind</c> and <c>Scope</c> properties.</b> An earlier draft had them,
/// and the second consumer proved they cannot be answered: one depth-bounded walk of the installed
/// plugin tree yields agents, commands AND skills, from a different plugin — a different
/// <see cref="ArtifactScope"/> — at every level, and splitting it into one source per kind would
/// walk that tree three times to satisfy properties nothing reads. Kind and scope are per-ENTRY
/// facts; a source that happens to have exactly one of each says so through
/// <c>ArtifactSourceIdentity</c>, which stamps them onto the entries it produces.
/// </para>
/// </remarks>
public interface IArtifactSource
{
    /// <summary>
    /// Stable identifier, recorded on every entry this source produces.
    /// </summary>
    /// <remarks>
    /// This is the one thing a source can always answer about itself, and consumers rely on it:
    /// the memory inventory maps it back to a category that <see cref="ArtifactRef"/> deliberately
    /// does not carry.
    /// </remarks>
    string Id { get; }

    /// <summary>The declarations this source currently offers. Never throws.</summary>
    IEnumerable<ArtifactRef> Enumerate();
}
