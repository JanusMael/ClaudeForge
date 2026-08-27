using Bennewitz.Ninja.AgentForge.Artifacts;

namespace Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

/// <summary>
/// A skill root as OpenCode reads it: every <c>SKILL.md</c> at any depth, named by the
/// <c>name:</c> in its own front matter.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The name comes from the front matter, not the directory — and that is why this exists
/// instead of the shared <c>SkillDirectoryArtifactSource</c>.</b> Measured against v1.17.9: a
/// manifest in a folder called <c>dirname-x</c> declaring <c>name: frontmatter-y</c> registered as
/// <b><c>frontmatter-y</c></b>. Since the name is the identity resolution groups by, naming these
/// entries after their folder would <i>invent</i> shadowing between two skills that merely share a
/// folder name, and <i>miss</i> it between two that really do collide. Both errors end up stated
/// confidently in the UI, which is the failure this engine's naming rules exist to prevent.
/// </para>
/// <para>
/// ⚠ <b>A manifest with no <c>name:</c> is still listed here, and OpenCode will not load it.</b>
/// Measured: a folder whose manifest carried a <c>description</c> but no <c>name</c> did not appear
/// among the resolved skills at all. It is surfaced anyway, under its folder name, because it is a
/// file the user wrote and the page's job is to show that it exists <i>and</i> that it is inert —
/// listing only what loads would hide the broken one at exactly the moment the user is looking for
/// it. Diagnosing that is the consumer's work, at render time, where the front matter is read for
/// display anyway.
/// </para>
/// <para>
/// ⚠ <b>Discovery is recursive and nested skills flatten.</b>
/// <c>~/.agents/skills/microsoft-foundry/models/deploy-model/preset/SKILL.md</c> registers as the
/// top-level skill <c>preset</c> — so a "one folder per skill" walk finds a fraction of them. Note
/// this is the opposite of the agent and command rule, where a nested file keeps the relative path
/// in its name; the two are genuinely different and neither generalises to the other.
/// </para>
/// </remarks>
public sealed class OpenCodeSkillArtifactSource(
    ArtifactSourceIdentity identity,
    string root) : IArtifactSource
{
    /// <summary>The file that makes a directory a skill. Exact, and case-sensitive upstream.</summary>
    public const string ManifestFileName = "SKILL.md";

    /// <inheritdoc/>
    public string Id => identity.Id;

    /// <inheritdoc/>
    public IEnumerable<ArtifactRef> Enumerate()
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            // Materialised inside the try: EnumerateFiles walks as the caller iterates, so a try
            // around a bare return of the sequence catches nothing at all.
            List<ArtifactRef> found = [];
            IEnumerable<string> manifests =
                Directory.EnumerateFiles(root, ManifestFileName, SearchOption.AllDirectories);

            foreach (string manifest in manifests)
            {
                found.Add(new ArtifactRef(
                    NameOf(manifest), identity.Kind, identity.Scope,
                    ArtifactForm.File, manifest, identity.Id));
            }

            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// The declared name, or the containing folder's name when there is none to read.
    /// </summary>
    /// <remarks>
    /// ⭐ Reads through <see cref="OpenCodeSkillManifest"/> rather than parsing here, so the name
    /// this source GROUPS by and the name a consumer DISPLAYS come from one reader. Two readers
    /// would eventually disagree about a quoted or commented <c>name:</c>, and the symptom would be
    /// a row whose heading does not match the artifact it was grouped into.
    /// </remarks>
    private static string NameOf(string manifestPath)
    {
        string folder = Path.GetFileName(Path.GetDirectoryName(manifestPath) ?? string.Empty);
        string? declared = OpenCodeSkillManifest.Read(manifestPath).DeclaredName;
        return string.IsNullOrWhiteSpace(declared) ? folder : declared;
    }
}
