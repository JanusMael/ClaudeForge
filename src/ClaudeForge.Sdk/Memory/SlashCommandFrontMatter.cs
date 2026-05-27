namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Typed projection of a slash-command file's front-matter
/// (<c>~/.claude/commands/&lt;name&gt;.md</c>).  Slash commands carry just a
/// <c>description</c> (shown in the <c>/</c> command picker); the command's
/// name is the file name, and the body is the prompt template.  Any other
/// keys round-trip verbatim through the underlying <see cref="FrontMatter"/>.
/// </summary>
/// <param name="Description">The <c>description</c> scalar, or null if absent.</param>
public sealed record SlashCommandFrontMatter(
    string? Description)
{
    /// <summary>The canonical keys this projection models; everything else is "extra".</summary>
    public static IReadOnlyList<string> KnownKeys { get; } = ["description"];

    /// <summary>Project a parsed <see cref="FrontMatter"/> into the slash-command view.</summary>
    public static SlashCommandFrontMatter From(FrontMatter fm)
    {
        ArgumentNullException.ThrowIfNull(fm);
        return new SlashCommandFrontMatter(
            Description: fm.FindScalar("description"));
    }
}