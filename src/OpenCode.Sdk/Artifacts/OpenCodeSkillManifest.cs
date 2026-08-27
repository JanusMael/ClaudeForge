using System.Text;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

/// <summary>
/// What a <c>SKILL.md</c>'s front matter declares about itself.
/// </summary>
/// <param name="FrontMatterPresent">
/// Whether a front-matter block was found at all. Distinguished from an empty one because a file
/// with no delimiters is a different mistake from a file whose block is missing a key.
/// </param>
/// <param name="DeclaredName">
/// The <c>name:</c> value, or <see langword="null"/>. ⚠ <b>OpenCode does not load a skill without
/// one</b> — measured.
/// </param>
/// <param name="Description">
/// The <c>description:</c> value, or <see langword="null"/>. Effectively required upstream: skills
/// without one are filtered out before the model ever sees them.
/// </param>
public sealed record OpenCodeSkillManifest(
    bool FrontMatterPresent,
    string? DeclaredName,
    string? Description)
{
    /// <summary>What an unreadable or absent manifest looks like.</summary>
    public static OpenCodeSkillManifest None { get; } = new(false, null, null);

    /// <summary>
    /// How much of a manifest is read.
    /// </summary>
    /// <remarks>
    /// Front matter sits at the top, so a bounded read keeps a root of several hundred skills off
    /// the whole-file path. A block that does not close within this budget parses as absent, which
    /// degrades to the folder name rather than to a partial one.
    /// </remarks>
    private const int HeadBytes = 16 * 1024;

    /// <summary>
    /// Read the front matter at <paramref name="manifestPath"/>. Never throws.
    /// </summary>
    /// <remarks>
    /// ⭐ <b>The repository's one front-matter parser, deliberately.</b> A second scanner looking
    /// for <c>name:</c> directly would be a second set of answers about quoting, comments, BOMs and
    /// delimiters, and the two would drift — the editor that writes these files uses this one.
    /// </remarks>
    public static OpenCodeSkillManifest Read(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return None;
        }

        try
        {
            using FileStream stream = new(
                manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            byte[] buffer = new byte[HeadBytes];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            string head = Encoding.UTF8.GetString(buffer, 0, read);

            FrontMatter frontMatter = YamlFrontMatter.Parse(head);
            if (!frontMatter.Present)
            {
                return None;
            }

            return new OpenCodeSkillManifest(
                FrontMatterPresent: true,
                DeclaredName: Clean(frontMatter.FindScalar("name")),
                Description: Clean(frontMatter.FindScalar("description")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            // One unreadable manifest costs its own row's detail, not the whole page.
            return None;
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
