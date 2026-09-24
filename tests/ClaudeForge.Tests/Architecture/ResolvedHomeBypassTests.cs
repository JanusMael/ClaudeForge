using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Production code resolves the Claude home through the sanctioned accessors, never by asking the
/// OS for a profile or composing <c>".claude"</c> onto one itself.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This is plans/00002 step 5 — "a guard against the next bypass".</b> The refactor that made
/// <c>ClaudeEnvironment</c> a required parameter found ~104 stale sites because omitting it became
/// a compile error. Nothing stops a NEW site composing the path by hand, and that site would be
/// invisible: it compiles, every test passes, and it silently reads <c>~/.claude</c> for a user who
/// has relocated their home. Exactly the shape that shipped twice already — the managed-settings
/// directory, and <c>CLAUDE_CONFIG_DIR</c> being documented to the user and then ignored.
/// </para>
/// <para>
/// ⚠ <b>A source scan, not reflection.</b> Reflection cannot see which accessor a call site chose,
/// and this repository has shipped that exact blind spot once — see
/// <c>ProductionSchemaRegistryTests</c>, which exists for the same reason.
/// </para>
/// <para>
/// ⛔⛔ <b>Comments are stripped first, and that is load-bearing rather than tidiness.</b> Measured
/// while this guard was being written: of 17 raw matches for <c>ClaudeEnvironment.Empty</c> across
/// production sources, <b>nine were doc comments explaining the rule</b>. A scan that did not strip
/// them would be reporting its own documentation — and the better the comment above an accessor,
/// the more reliably such a guard lies.
/// </para>
/// <para>
/// ⭐ <b>The <c>".claude"</c> rule discriminates PROFILE composition from PROJECT composition, and
/// that distinction is the whole reason it is usable.</b> <c>CLAUDE_CONFIG_DIR</c> relocates the
/// USER home; a project's own <c>&lt;repo&gt;/.claude/</c> is not moved by it and never should be.
/// Measured before this guard was written: the literal appears at 8 production sites, and
/// <b>6 of them are project-scope</b> — a guard matching the bare literal would arrive with a
/// six-entry allow-list of correct code, which is how a guard becomes noise and then becomes
/// suppressed.
/// </para>
/// </remarks>
public sealed class ResolvedHomeBypassTests
{
    /// <summary>
    /// The sanctioned owners, by file name, each with the rule ids it may satisfy.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>This list SHRINKS. An addition is a decision and needs its reason written beside it.</b>
    /// </remarks>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        // The one accessor permitted to ask the OS for a profile, to compose the home onto it, and
        // to name the per-OS system policy directory. Everything else derives from it.
        ["PlatformPaths.cs"] = ["profile-read", "home-compose", "managed-compose"],

        // The second path implementation, deliberately duplicating the literals so that a
        // different root is a constructor argument rather than a process-global mutation.
        // ClaudeArtifactPathsTests pins the two equal, including for a relocated home.
        ["ClaudeArtifactPaths.cs"] = ["home-compose"],

        // Reads CLAUDE_CONFIG_DIR exactly once, which is the point of the type.
        ["ClaudeEnvironment.cs"] = ["config-dir-read"],

        // Composes the managed paths from a policyRoot PARAMETER rather than ambient state --
        // that parameter is the sandbox seam the managed-settings tests use.
        ["ConfigFileDiscoverer.cs"] = ["managed-compose"],
    };

    private static readonly Regex ProfileRead =
        new(@"SpecialFolder\.UserProfile", RegexOptions.Compiled);

    private static readonly Regex ConfigDirRead =
        new(@"GetEnvironmentVariable\(\s*""CLAUDE_CONFIG_DIR""", RegexOptions.Compiled);

    /// <summary>A profile-rooted token on the same line as the home literal.</summary>
    private static readonly Regex ProfileRootToken =
        new(@"\b(UserProfile|sandbox|GetFolderPath|HomeDirectory)\b", RegexOptions.Compiled);

    private static readonly Regex HomeLiteral =
        new(@"""\.claude""", RegexOptions.Compiled);

    private static readonly Regex ManagedLiteral =
        new(@"""managed-(settings|mcp)", RegexOptions.Compiled);

    [Fact]
    public void NoProductionCodeResolvesTheClaudeHomeOutsideTheSanctionedAccessors()
    {
        string repoRoot = FindRepoRoot();
        string srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"Expected production sources at '{srcDir}'.");

        List<string> violations = [];
        Dictionary<string, int> allowedSeen = new(StringComparer.Ordinal);
        int filesScanned = 0;

        foreach (string file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            filesScanned++;
            string name = Path.GetFileName(file);
            string[] allowed = Allowed.TryGetValue(name, out string[]? a) ? a : [];

            int lineNumber = 0;
            foreach (string raw in File.ReadAllLines(file))
            {
                lineNumber++;
                string code = StripComment(raw);
                if (code.Length == 0)
                {
                    continue;
                }

                Record("profile-read", ProfileRead.IsMatch(code));
                Record("config-dir-read", ConfigDirRead.IsMatch(code));
                // ⭐ PROFILE-rooted composition only. A project's own .claude/ is not relocated by
                // CLAUDE_CONFIG_DIR, so `Path.Combine(projectRoot, ".claude")` is correct code.
                Record("home-compose", HomeLiteral.IsMatch(code) && ProfileRootToken.IsMatch(code));
                Record("managed-compose", ManagedLiteral.IsMatch(code) && code.Contains("Path.Combine", StringComparison.Ordinal));

                void Record(string rule, bool hit)
                {
                    if (!hit)
                    {
                        return;
                    }

                    if (allowed.Contains(rule, StringComparer.Ordinal))
                    {
                        allowedSeen[rule] = allowedSeen.GetValueOrDefault(rule) + 1;
                        return;
                    }

                    violations.Add($"{name}:{lineNumber} [{rule}]  {code.Trim()}");
                }
            }
        }

        Assert.True(filesScanned > 100, $"Only {filesScanned} production files scanned — src/ moved.");

        // ⛔ The other half of the canary, and the one that is easy to omit: a scan that matches
        // NOTHING passes. If the sanctioned sites stopped being seen, the regexes have drifted and
        // this guard is green while guarding nothing.
        foreach (string rule in new[] { "profile-read", "config-dir-read", "home-compose", "managed-compose" })
        {
            Assert.True(
                allowedSeen.GetValueOrDefault(rule) > 0,
                $"Rule '{rule}' matched no sanctioned site at all. Either the accessor it guards was "
                + "removed (drop the rule and its allow-list entry) or the pattern no longer matches "
                + "the code — in which case this rule is guarding nothing and passing.");
        }

        MessageAssert.Equal(
            0, violations.Count,
            $"{violations.Count} production site(s) resolve the Claude home outside the sanctioned "
            + "accessors. Each compiles and passes every test while silently reading ~/.claude for a "
            + "user who has relocated it — take the path from PlatformPaths or ClaudeArtifactPaths, "
            + "both of which require a ClaudeEnvironment:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Strip a line comment, leaving code. A <c>//</c> inside a string literal is respected so a
    /// path or a URL is not mistaken for a comment.
    /// </summary>
    private static string StripComment(string raw)
    {
        string trimmed = raw.TrimStart();
        if (trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        bool inString = false;
        for (int i = 0; i < raw.Length - 1; i++)
        {
            if (raw[i] == '"' && (i == 0 || raw[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && raw[i] == '/' && raw[i + 1] == '/')
            {
                return raw[..i];
            }
        }

        return raw;
    }

    /// <summary>Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.</summary>
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }
}
