using System.Globalization;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Memory;

/// <summary>
/// A fixture home that touches every Claude artifact and footprint location, and the one text form
/// both sides of <c>plans/00008</c> step 9 render it in.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The golden it is compared with was minted on <c>main</c> before the move</b>, through the
/// API <c>main</c> had then, by this same file with a different adapter. So the comparison says the
/// layout resolves what the hard-coded walk resolved, not merely what it resolves today.
/// </para>
/// <para>
/// ⚠ <b>Rows outside the fixture profile are dropped</b>: the managed-policy paths are system
/// directories, so what they list depends on the machine rather than on the layout.
/// </para>
/// </remarks>
internal static class ClaudeArtifactLayoutGolden
{
    /// <summary>The fixture's files, relative to the profile, and their contents.</summary>
    internal static readonly (string Path, string Content)[] Files =
    [
        (".claude/CLAUDE.md", "# Home memory\nsecond line\n"),
        (".claude/AGENTS.md", "agents memory\n"),
        (".claude/agents/reviewer.md", "---\nname: reviewer\ndescription: Reviews code\n---\nbody\n"),
        (".claude/agents/notes.txt", "not an agent\n"),
        (".claude/commands/ship.md", "---\ndescription: Ship it\n---\nsteps\n"),
        (".claude/hooks/pre.sh", "#!/bin/sh\necho hi\n"),
        (".claude/hooks/pre.sh.bak", "old\n"),
        (".claude/plans/plan-a.md", "# Plan A\n"),
        (".claude/rules/style.md", "# Style\n"),
        (".claude/rules/nested/deep.md", "# Deep\n"),
        (".claude/skills/triage/SKILL.md", "---\nname: triage\ndescription: Triage bugs\n---\n"),
        (".claude/settings.json", "{}\n"),
        (".claude/mcp.json", "{\"servers\":{}}\n"),
        (".claude/.credentials.json", "{\"token\":\"secret\"}\n"),
        (".claude.json", "{\"projects\":{}}\n"),
        (".codex/AGENTS.md", "codex\n"),
        (".gemini/GEMINI.md", "gemini\n"),
        (".opencode/notes.md", "opencode\n"),
        (".claude/plugins/marketplaces/mkt/plug/agents/helper.md", "---\ndescription: Helps\n---\n"),
        (".claude/plugins/marketplaces/mkt/plug/commands/do.md", "do\n"),
        (".claude/plugins/marketplaces/mkt/plug/skills/s1/SKILL.md", "---\nname: s1\n---\n"),
        (".claude/plugins/marketplaces/mkt/plug/node_modules/x/agents/hidden.md", "hidden\n"),
        ("work/repo/CLAUDE.md", "# Project memory\n"),
        ("work/repo/AGENTS.md", "project agents\n"),
        ("work/repo/.claude/settings.json", "{\"a\":1}\n"),
        ("work/repo/.claude/settings.local.json", "{\"b\":2}\n"),
        ("work/repo/.mcp.json", "{\"mcpServers\":{}}\n"),
        ("work/repo/.claude/agents/proj-agent.md", "---\ndescription: Project agent\n---\n"),
        ("work/repo/.claude/skills/ps/SKILL.md", "---\nname: ps\n---\n"),
        (".claude/projects/-work-repo/a.jsonl", "0123456789\n"),
        (".claude/projects/-work-repo/sub/b.jsonl", "01234\n"),
        (".claude/projects/-work-repo/notes.txt", "not a transcript\n"),
        (".claude/projects/-other/c.jsonl", "012\n"),
        (".claude/sessions/x.json", "{}\n"),
        (".claude/session-data/y.bin", "yy\n"),
        (".claude/session-env/z.env", "zzz\n"),
        (".claude/history.jsonl", "{\"p\":1}\n"),
        (".claude/bash-commands.log", "ls\n"),
        (".claude/cost-tracker.log", "1.00\n"),
        (".claude/todos/t.json", "[]\n"),
        (".claude/file-history/f1", "edit\n"),
    ];

    /// <summary>The fixture's project root, relative to the profile.</summary>
    internal const string ProjectRoot = "work/repo";

    /// <summary>Write the fixture under <paramref name="profile"/>.</summary>
    internal static void Plant(string profile)
    {
        foreach ((string path, string content) in Files)
        {
            string full = Path.Combine(profile, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
    }

    /// <summary>Render everything the four surfaces report for the fixture, sorted.</summary>
    internal static IReadOnlyList<string> Render(
        string profile,
        IReadOnlyList<UserMemoryFile> inventory,
        IReadOnlyList<EditableMemoryEntry> editor,
        IReadOnlyList<FootprintCategoryStats> footprint,
        IReadOnlyList<ProjectTranscriptStats> transcripts)
    {
        List<string> lines = [];
        foreach (UserMemoryFile f in inventory.Where(f => Inside(profile, f.AbsolutePath)))
        {
            lines.Add(Join("inventory", f.Category, f.DisplayName, Rel(profile, f.AbsolutePath), f.SizeBytes,
                f.Subtitle ?? "-"));
        }

        foreach (EditableMemoryEntry e in editor.Where(e => Inside(profile, e.AbsolutePath)))
        {
            lines.Add(Join("editor", e.Scope, e.Category, e.DisplayName, e.Source, e.IsWritable,
                Rel(profile, e.AbsolutePath), e.SizeBytes));
        }

        foreach (FootprintCategoryStats s in footprint)
        {
            lines.Add(Join("footprint", s.Category.Id, s.FileCount, s.TotalBytes, Rel(profile, s.AbsolutePath)));
        }

        foreach (ProjectTranscriptStats t in transcripts)
        {
            lines.Add(Join("transcripts", t.MangledName, t.DisplayName, t.FileCount, t.TotalBytes,
                Rel(profile, t.AbsolutePath)));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static bool Inside(string profile, string path) =>
        path.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string Rel(string profile, string path) =>
        Path.GetRelativePath(profile, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Join(params object[] parts) =>
        string.Join("|", parts.Select(p => Convert.ToString(p, CultureInfo.InvariantCulture)));
}
