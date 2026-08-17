using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Asserts that repo-relative <c>src/</c> and <c>tests/</c> paths hardcoded in CI workflows,
/// PowerShell scripts, and the solution file still point at something that exists.
/// </summary>
/// <remarks>
/// <para>
/// These paths are the blind spot in any project rename. The compiler never sees them and no
/// test exercised them, so they fail in the worst possible way — <b>silently, later, and
/// somewhere nobody is watching</b>:
/// </para>
/// <list type="bullet">
///   <item><description>
///   A workflow <c>paths:</c> trigger that no longer matches simply <b>stops firing</b>.
///   <c>schema-refresh.yml</c> and <c>model-catalog-refresh.yml</c> would just quietly never
///   run again — no error, no failed build, just a scheduled job that silently does nothing.
///   </description></item>
///   <item><description>
///   A stale path in <c>refresh-schema.ps1</c> or <c>validate-model-catalog.ps1</c> fails at
///   whatever hour the schedule fires, far from the change that caused it.
///   </description></item>
///   <item><description>
///   A project missing from <c>ClaudeForge.slnx</c> — which is hand-maintained — never builds
///   in CI at all.
///   </description></item>
/// </list>
/// <para>
/// Found during the Phase 1 <c>ClaudeForge.Core</c> → <c>AgentForge.Core</c> rename: four
/// files outside the compiler's view referenced <c>src/ClaudeForge.Core/Assets/…</c>. The
/// plan's Phase 1 checklist named the solution file but not the workflows or scripts.
/// </para>
/// </remarks>
[TestClass]
public sealed class BuildFilePathIntegrityTests
{
    /// <summary>
    /// Repo-relative paths under src/ or tests/. Deliberately narrow: it must match the
    /// literal forms these files actually use, and must not try to parse shell or YAML.
    /// Trailing glob segments (<c>/**</c>, <c>/*</c>) are trimmed before the check.
    /// </summary>
    /// <remarks>
    /// <c>*</c> must be allowed in the FIRST segment too, not only in later ones. Docs write
    /// <c>src/LayeredEditors.*</c> to mean a family of projects; without the wildcard there,
    /// the match truncates to <c>src/LayeredEditors.</c>, trailing punctuation is stripped,
    /// and the result is reported as a missing path that was never claimed to exist.
    /// </remarks>
    private static readonly Regex RepoPathRegex = new(
        @"(?<path>(?:src|tests)/[A-Za-z0-9._*-]+(?:/[A-Za-z0-9._*-]+)*)",
        RegexOptions.Compiled);

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

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        string workflows = Path.Combine(repoRoot, ".github", "workflows");
        if (Directory.Exists(workflows))
        {
            foreach (string f in Directory.GetFiles(workflows, "*.yml", SearchOption.AllDirectories))
            {
                yield return f;
            }
        }

        string scripts = Path.Combine(repoRoot, "scripts");
        if (Directory.Exists(scripts))
        {
            foreach (string f in Directory.GetFiles(scripts, "*.ps1", SearchOption.AllDirectories))
            {
                yield return f;
            }
        }

        foreach (string f in Directory.GetFiles(repoRoot, "*.slnx"))
        {
            yield return f;
        }

        // Root-level guidance docs and the per-area AGENTS.md sidecars. These are what a
        // fresh agent context reads first, so a stale path here does more damage than a
        // stale path in code: it sends the next reader to a directory that no longer exists
        // and quietly undermines trust in the rest of the document.
        //
        // docs/ is deliberately EXCLUDED — plan documents legitimately name files that do
        // not exist yet (future assemblies) and paths as they were before a rename. Asserting
        // against those would be wrong, not just noisy.
        foreach (string f in Directory.GetFiles(repoRoot, "*.md"))
        {
            // CHANGELOG is a historical record. Entries describe the tree as it was at that
            // release, so a path that has since moved is CORRECT there, not stale — the same
            // reason docs/ is excluded.
            if (Path.GetFileName(f).Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return f;
        }

        foreach (string area in new[] { "src", "tests" })
        {
            string areaDir = Path.Combine(repoRoot, area);
            if (!Directory.Exists(areaDir))
            {
                continue;
            }

            foreach (string f in Directory.GetFiles(areaDir, "AGENTS.md", SearchOption.AllDirectories))
            {
                if (!f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                {
                    yield return f;
                }
            }
        }
    }

    [TestMethod]
    public void ScanFindsFiles_SoThisTestIsNotVacuous()
    {
        string repoRoot = FindRepoRoot();
        Assert.IsTrue(
            ScannedFiles(repoRoot).Any(),
            $"No workflows, scripts, or .slnx found under '{repoRoot}'. This test would pass "
            + "without checking anything.");
    }

    [TestMethod]
    public void EveryHardcodedRepoPathInBuildFilesExists()
    {
        string repoRoot = FindRepoRoot();
        List<string> missing = [];
        int checkedCount = 0;

        foreach (string file in ScannedFiles(repoRoot))
        {
            string relativeFile = Path.GetRelativePath(repoRoot, file);

            foreach (string line in File.ReadAllLines(file))
            {
                foreach (Match match in RepoPathRegex.Matches(line))
                {
                    string candidate = match.Groups["path"].Value;

                    // Prose ends sentences with the path: "…lives in src/Foo." Strip trailing
                    // sentence punctuation before testing for existence.
                    candidate = candidate.TrimEnd('.', ',', ';', ':', ')');

                    // Trim trailing glob segments: 'src/X/Assets/**' -> 'src/X/Assets'.
                    while (candidate.EndsWith("/**", StringComparison.Ordinal)
                           || candidate.EndsWith("/*", StringComparison.Ordinal))
                    {
                        candidate = candidate[..candidate.LastIndexOf('/')];
                    }

                    // A '*' anywhere else is a pattern we cannot resolve to one path.
                    if (candidate.Contains('*', StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // An elision in prose ("src/Foo/...Bar.cs") is not a path claim.
                    if (candidate.Contains("...", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Build outputs only exist after a build/publish, so their absence says
                    // nothing about whether the documentation is correct.
                    if (candidate.Contains("/bin/", StringComparison.Ordinal)
                        || candidate.Contains("/obj/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    checkedCount++;
                    string absolute = Path.Combine(repoRoot, candidate.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(absolute) && !Directory.Exists(absolute))
                    {
                        missing.Add($"{relativeFile}: '{candidate}'");
                    }
                }
            }
        }

        Assert.IsTrue(
            checkedCount > 0,
            "No repo-relative src/ or tests/ paths were found in any build file. Either the "
            + "regex no longer matches how these files are written, or the paths moved out of "
            + "them — either way this test is no longer guarding anything.");

        Assert.IsTrue(
            missing.Count == 0,
            $"{missing.Count} hardcoded path(s) in build files point at something that no longer "
            + "exists. A stale path here fails silently — a workflow trigger simply stops firing, "
            + "or a scheduled script breaks at an hour nobody is watching:\n  "
            + string.Join("\n  ", missing.Distinct()));
    }
}
