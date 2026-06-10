using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Pre-processing for Bash/PowerShell command matching: splitting a compound
/// command into independently-evaluated subcommands and stripping recognized
/// process wrappers. Mirrors the behavior the Claude Code CLI applies before it
/// matches a command against Bash permission rules.
/// </summary>
/// <remarks>
/// <para>
/// <b>Spec.</b>
/// <see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"Compound commands" and §"Process wrappers". This is the reason the old
/// "a Bash allow rule is bypassable via <c>&amp;&amp;</c> chaining" warning is
/// <b>obsolete</b>: Claude Code is shell-operator aware and requires <i>every</i>
/// subcommand of a compound command to match a rule independently.
/// </para>
/// <para>
/// <b>Fidelity.</b> We replicate operator-splitting and the fixed wrapper list.
/// We do <b>not</b> replicate the full shell grammar (quoting, here-docs,
/// subshell nesting, variable expansion). This is faithful enough for the
/// dry-run tester's teaching purpose; it is not a security control (the real
/// control is Claude Code itself).
/// </para>
/// </remarks>
public static class BashCommandSplitter
{
    // Recognized command separators, longest-first so "||" / "|&" / "&&" are
    // matched before the single-character "|" / "&". Newlines are handled
    // separately as single characters.
    private static readonly string[] s_separators =
        ["&&", "||", "|&", ";", "|", "&"];

    // Process wrappers Claude Code strips before matching, so a rule for the
    // inner command also covers the wrapped form (e.g. "timeout 30 npm test").
    // Bare "xargs" (no flags) is handled specially in StripWrappers.
    private static readonly FrozenSet<string> s_wrappers =
        new[] { "timeout", "time", "nice", "nohup", "stdbuf" }
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Built-in read-only commands Claude Code runs without a permission prompt
    /// in every mode. Exposed for the dry-run tester to surface as an
    /// informational note (e.g. "Claude Code treats <c>ls</c> as read-only and
    /// won't prompt regardless of rules"). The matcher does NOT auto-allow these
    /// — replicating the full read-only semantics (including unquoted-glob
    /// nuances) is out of scope; see the plan's fidelity note.
    /// </summary>
    public static FrozenSet<string> ReadOnlyCommandNames { get; } =
        new[]
        {
            "ls", "cat", "echo", "pwd", "head", "tail", "grep", "find", "wc",
            "which", "diff", "stat", "du", "cd",
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Split <paramref name="command"/> into subcommands on the recognized
    /// separators (<c>&amp;&amp;</c>, <c>||</c>, <c>;</c>, <c>|</c>, <c>|&amp;</c>,
    /// <c>&amp;</c>, and newlines). Each returned entry is trimmed and non-empty.
    /// A simple command returns a single-element list.
    /// </summary>
    public static IReadOnlyList<string> SplitCompound(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return [];
        }

        List<string> parts = [];
        int start = 0;
        int i = 0;
        while (i < command.Length)
        {
            char c = command[i];
            if (c == '\n' || c == '\r')
            {
                AddPart(command, start, i, parts);
                i++;
                start = i;
                continue;
            }

            string? sep = MatchSeparatorAt(command, i);
            if (sep is not null)
            {
                AddPart(command, start, i, parts);
                i += sep.Length;
                start = i;
                continue;
            }

            i++;
        }

        AddPart(command, start, command.Length, parts);
        return parts;
    }

    // A wrapper's own argument: an option flag (-n, --foo) or a duration-like
    // token (30, 1.5, 30s, 5m) such as timeout's first argument. After a
    // recognized wrapper, these are consumed until the real inner command.
    private static readonly Regex s_wrapperArg =
        new(@"^(-.*|\d+(\.\d+)?[smhdSMHD]?)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Strip recognized leading process wrappers (and their option/duration
    /// arguments) from a single (already split) command. <c>timeout 30 npm
    /// test</c> → <c>npm test</c>; <c>nice -n 10 npm test</c> → <c>npm test</c>;
    /// <c>xargs grep foo</c> → <c>grep foo</c>. Bare <c>xargs</c> is stripped only
    /// when it has no flags (an <c>xargs -n1 …</c> invocation is left intact,
    /// matching Claude Code's behavior). Stripping repeats so stacked wrappers
    /// (<c>nice nohup cmd</c>) are all removed.
    /// </summary>
    public static string StripWrappers(string command)
    {
        string current = command.Trim();
        while (true)
        {
            int sp = current.IndexOf(' ');
            if (sp <= 0)
            {
                return current;
            }

            string head = current[..sp];
            string rest = current[(sp + 1)..].TrimStart();
            if (rest.Length == 0)
            {
                return current;
            }

            if (s_wrappers.Contains(head))
            {
                // Consume the wrapper's own flags/duration before the inner command.
                string inner = ConsumeWrapperArgs(rest);
                if (inner.Length == 0)
                {
                    return current;
                }

                current = inner;
                continue;
            }

            // Bare xargs (no flags): strip only when the next token isn't a flag.
            if (head == "xargs" && !rest.StartsWith('-'))
            {
                current = rest;
                continue;
            }

            return current;
        }
    }

    private static string ConsumeWrapperArgs(string rest)
    {
        string[] tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < tokens.Length && s_wrapperArg.IsMatch(tokens[i]))
        {
            i++;
        }

        return string.Join(' ', tokens[i..]);
    }

    private static void AddPart(string source, int start, int end, List<string> parts)
    {
        if (end <= start)
        {
            return;
        }

        string part = source[start..end].Trim();
        if (part.Length > 0)
        {
            parts.Add(part);
        }
    }

    private static string? MatchSeparatorAt(string s, int index)
    {
        foreach (string sep in s_separators)
        {
            if (index + sep.Length <= s.Length &&
                string.CompareOrdinal(s, index, sep, 0, sep.Length) == 0)
            {
                return sep;
            }
        }

        return null;
    }
}
