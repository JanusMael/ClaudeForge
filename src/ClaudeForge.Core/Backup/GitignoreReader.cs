using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// A single parsed pattern from a <c>.gitignore</c> file.
/// </summary>
internal sealed class GitignorePattern
{
    public GitignorePattern(string rawPattern, bool negated, bool dirOnly, Regex regex)
    {
        RawPattern = rawPattern;
        Negated = negated;
        DirOnly = dirOnly;
        Regex = regex;
    }

    /// <summary>The raw pattern text (without leading <c>!</c> or trailing <c>/</c>).</summary>
    public string RawPattern { get; }

    /// <summary>True when the original line started with <c>!</c> — this pattern re-includes matched items.</summary>
    public bool Negated { get; }

    /// <summary>True when the original line ended with <c>/</c> — this pattern matches directories only.</summary>
    public bool DirOnly { get; }

    /// <summary>Pre-compiled regex for fast matching.</summary>
    public Regex Regex { get; }
}

/// <summary>
/// Minimal <c>.gitignore</c> parser and matcher sufficient for backing up project
/// directories.  Supports the patterns commonly found in real projects:
/// <list type="bullet">
/// <item><description><c>#</c> comments and blank lines are skipped.</description></item>
/// <item><description>Leading <c>!</c> — negation (re-includes a previously-ignored item).</description></item>
/// <item><description>Trailing <c>/</c> — directory-only pattern.</description></item>
/// <item><description><c>**</c> — matches any path depth (converted to <c>.*</c>).</description></item>
/// <item><description><c>*</c> — matches within one path segment (converted to <c>[^/]*</c>).</description></item>
/// <item><description><c>?</c> — matches a single non-<c>/</c> character.</description></item>
/// <item><description>All other characters are regex-escaped.</description></item>
/// </list>
/// Patterns are evaluated in declaration order; <b>the last matching pattern wins</b>
/// (consistent with git's own semantics).  A negated final match means the item is
/// <em>not</em> ignored.
/// </summary>
internal static class GitignoreReader
{
    // Pre-compiled empty list to avoid allocations when no .gitignore exists.
    private static readonly IReadOnlyList<GitignorePattern> _empty = Array.Empty<GitignorePattern>();

    /// <summary>
    /// Reads a <c>.gitignore</c> file at <paramref name="gitignorePath"/> and returns
    /// the parsed patterns.  Returns an empty list when the file does not exist.
    /// </summary>
    public static IReadOnlyList<GitignorePattern> Read(string gitignorePath)
    {
        if (!File.Exists(gitignorePath))
        {
            return _empty;
        }

        List<GitignorePattern> patterns = new();
        foreach (string rawLine in File.ReadAllLines(gitignorePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            bool negated = false;
            bool dirOnly = false;

            if (line[0] == '!')
            {
                negated = true;
                line = line[1..].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
            }

            if (line[^1] == '/')
            {
                dirOnly = true;
                line = line[..^1].TrimEnd('/');
                if (line.Length == 0)
                {
                    continue;
                }
            }

            Regex regex = PatternToRegex(line);
            patterns.Add(new GitignorePattern(line, negated, dirOnly, regex));
        }

        return patterns;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="name"/> (the bare file or directory name)
    /// or <paramref name="relativePathFromRoot"/> (path relative to the directory that
    /// contains the <c>.gitignore</c>, forward-slash separated) is excluded by
    /// <paramref name="patterns"/>.
    /// </summary>
    /// <param name="name">Bare name (e.g. <c>node_modules</c> or <c>foo.log</c>).</param>
    /// <param name="relativePathFromRoot">
    /// Path relative to the root directory that owns this pattern list, e.g.
    /// <c>subdir/node_modules</c>.  Forward slashes only.
    /// </param>
    /// <param name="isDirectory"><c>true</c> when the item is a directory.</param>
    /// <param name="patterns">Patterns returned by <see cref="Read"/>.</param>
    public static bool IsIgnored(
        string name,
        string relativePathFromRoot,
        bool isDirectory,
        IReadOnlyList<GitignorePattern> patterns)
    {
        if (patterns.Count == 0)
        {
            return false;
        }

        bool ignored = false;

        foreach (GitignorePattern p in patterns)
        {
            // Directory-only patterns do not match files.
            if (p.DirOnly && !isDirectory)
            {
                continue;
            }

            // Try matching against both the bare name and the relative path, so
            // a pattern like `*.log` matches `subdir/foo.log` and a pattern like
            // `dist/` matches the `dist` subdirectory at any depth.
            bool nameMatch, pathMatch;
            try
            {
                nameMatch = p.Regex.IsMatch(name);
                pathMatch = !nameMatch && p.Regex.IsMatch(relativePathFromRoot);
            }
            catch (RegexMatchTimeoutException)
            {
                Log.Warning(
                    "[GitignoreReader] Regex match timed out for pattern {Pattern} on input {Input} — treating as no-match",
                    p.RawPattern, name);
                continue;
            }

            if (nameMatch || pathMatch)
            {
                ignored = !p.Negated;
            }
        }

        return ignored;
    }

    // -----------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------

    private static Regex PatternToRegex(string pattern)
    {
        // Build a regex by iterating the pattern character-by-character.
        // `**` receives special treatment before we touch individual `*` globs.
        StringBuilder sb = new("^");
        int i = 0;

        while (i < pattern.Length)
        {
            if (i + 1 < pattern.Length && pattern[i] == '*' && pattern[i + 1] == '*')
            {
                // `**` — match any depth (including zero path separators)
                sb.Append(".*");
                i += 2;
                // Consume an optional surrounding `/` so `**/foo` and `foo/**` work.
                if (i < pattern.Length && pattern[i] == '/')
                {
                    i++;
                }
            }
            else if (pattern[i] == '*')
            {
                // Single `*` — match within one path segment only
                sb.Append("[^/]*");
                i++;
            }
            else if (pattern[i] == '?')
            {
                // `?` — one non-separator character
                sb.Append("[^/]");
                i++;
            }
            else
            {
                sb.Append(Regex.Escape(pattern[i].ToString()));
                i++;
            }
        }

        sb.Append('$');

        // Case sensitivity matches the OS default.  On Windows .gitignore is
        // effectively case-insensitive; on Unix it is case-sensitive.
        // Using IgnoreCase keeps things consistent and avoids false-negatives on
        // case-mismatched Windows repos.
        // matchTimeout: guard against pathological patterns (e.g. "a*a*a*a*") that
        // can cause catastrophic backtracking — IsIgnored catches the timeout and
        // treats the pattern as a non-match (safe default).
        return new Regex(sb.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
            matchTimeout: TimeSpan.FromMilliseconds(200));
    }

    /// <summary>
    /// Merges two pattern lists into a single list (inherited first,` then local).
    /// Returns a shared empty array if both are empty.
    /// </summary>
    public static IReadOnlyList<GitignorePattern> MergePatterns(
        IReadOnlyList<GitignorePattern>? inherited,
        IReadOnlyList<GitignorePattern> local)
    {
        bool hasInherited = inherited is { Count: > 0 };
        bool hasLocal = local.Count > 0;

        if (!hasInherited && !hasLocal)
        {
            return _empty;
        }

        if (!hasInherited)
        {
            return local;
        }

        if (!hasLocal)
        {
            return inherited!;
        }

        List<GitignorePattern> merged = new(inherited!.Count + local.Count);
        merged.AddRange(inherited);
        merged.AddRange(local);
        return merged;
    }
}