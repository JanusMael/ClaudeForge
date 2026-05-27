namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Typed projection of a sub-agent file's front-matter
/// (<c>~/.claude/agents/&lt;name&gt;.md</c>).  Surfaces the canonical keys
/// Claude Code recognises — <c>name</c>, <c>description</c>, <c>tools</c>,
/// <c>model</c> — as strongly-typed members for the editor UI.  Any other
/// keys stay in the underlying <see cref="FrontMatter"/> and round-trip
/// verbatim; this projection never sees or loses them.
/// </summary>
/// <param name="Name">The <c>name</c> scalar, or null if absent.</param>
/// <param name="Description">The <c>description</c> scalar, or null if absent.</param>
/// <param name="Tools">
/// The <c>tools</c> allow-list.  Claude Code writes this as a comma-separated
/// scalar (<c>tools: Read, Grep, Bash</c>) but a YAML inline/block list is
/// also accepted; <see cref="From"/> normalises both shapes to a list.  Empty
/// when the key is absent.
/// </param>
/// <param name="Model">The <c>model</c> scalar (e.g. <c>sonnet</c>), or null if absent.</param>
public sealed record AgentFrontMatter(
    string? Name,
    string? Description,
    IReadOnlyList<string> Tools,
    string? Model)
{
    /// <summary>The canonical keys this projection models; everything else is "extra".</summary>
    public static IReadOnlyList<string> KnownKeys { get; } = ["name", "description", "tools", "model"];

    /// <summary>Project a parsed <see cref="FrontMatter"/> into the agent view.</summary>
    public static AgentFrontMatter From(FrontMatter fm)
    {
        ArgumentNullException.ThrowIfNull(fm);
        return new AgentFrontMatter(
            Name: fm.FindScalar("name"),
            Description: fm.FindScalar("description"),
            Tools: ReadToolsList(fm),
            Model: fm.FindScalar("model"));
    }

    /// <summary>
    /// Read the <c>tools</c> key tolerantly: a YAML list is returned as-is; a
    /// comma-separated scalar (Claude Code's native form) is split on commas
    /// and trimmed.  Empty / absent → empty list.
    /// </summary>
    private static IReadOnlyList<string> ReadToolsList(FrontMatter fm)
    {
        IReadOnlyList<string>? list = fm.FindList("tools");
        if (list is not null)
        {
            return list;
        }

        string? scalar = fm.FindScalar("tools");
        if (string.IsNullOrWhiteSpace(scalar))
        {
            return [];
        }

        return scalar
               .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .ToList();
    }
}