using System;
using System.Collections.Generic;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Typed projection of a skill file's front-matter
/// (<c>~/.claude/skills/&lt;name&gt;/SKILL.md</c>).  Skills carry fewer
/// canonical keys than agents — just <c>name</c> and <c>description</c>
/// (the <c>description</c> is what Claude reads to decide when to trigger
/// the skill).  Any other keys round-trip verbatim through the underlying
/// <see cref="FrontMatter"/>.
/// </summary>
/// <param name="Name">The <c>name</c> scalar, or null if absent.</param>
/// <param name="Description">The <c>description</c> scalar, or null if absent.</param>
public sealed record SkillFrontMatter(
    string? Name,
    string? Description)
{
    /// <summary>The canonical keys this projection models; everything else is "extra".</summary>
    public static IReadOnlyList<string> KnownKeys { get; } = ["name", "description"];

    /// <summary>Project a parsed <see cref="FrontMatter"/> into the skill view.</summary>
    public static SkillFrontMatter From(FrontMatter fm)
    {
        ArgumentNullException.ThrowIfNull(fm);
        return new SkillFrontMatter(
            Name: fm.FindScalar("name"),
            Description: fm.FindScalar("description"));
    }
}
