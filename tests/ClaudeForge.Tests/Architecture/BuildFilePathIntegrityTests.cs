using System.Text.RegularExpressions;
using System.Xml.Linq;

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

        // ⛔ BOTH extensions. Scanning *.ps1 alone is how `refresh-schema.sh` kept pointing at
        // `src/ClaudeForge.Core/` for the entire life of this guard — a path the very rename
        // that PROMPTED this test deleted. Its PowerShell twin was fixed at the time; the shell
        // copy was not, because nothing looked at it. Add an extension here whenever a new kind
        // of build script appears, or it inherits exactly that blind spot.
        string scripts = Path.Combine(repoRoot, "scripts");
        if (Directory.Exists(scripts))
        {
            foreach (string pattern in new[] { "*.ps1", "*.sh" })
            {
                foreach (string f in Directory.GetFiles(scripts, pattern, SearchOption.AllDirectories))
                {
                    yield return f;
                }
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

    /// <summary>
    /// Every project on disk is listed in <c>ClaudeForge.slnx</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>This is the OTHER direction, and for eight commits nothing checked it.</b>
    /// <see cref="EveryHardcodedRepoPathInBuildFilesExists"/> asserts that every path a build
    /// file names still exists. The third bullet in this class's own summary describes the
    /// reverse — "a project missing from <c>ClaudeForge.slnx</c> … never builds in CI at all" —
    /// and that was documented, not guarded. <c>src/OpenCode.Avalonia</c> then sat in no
    /// solution, referenced by nothing, for its whole first phase: its green local build proved
    /// nothing, because nothing was building it.
    /// </para>
    /// <para>
    /// The solution file is the CI surface. <c>ci.yml</c> runs <c>dotnet restore</c>,
    /// <c>build</c> and <c>test</c> against <c>ClaudeForge.slnx</c> and nothing else, so a
    /// project outside it is invisible to every OS in the matrix — and a <b>local</b> build
    /// cannot detect that, because the developer's own <c>dotnet build</c> reads the same
    /// incomplete file and reports success.
    /// </para>
    /// <para>
    /// The failure is quiet in the worst way: the missing project's warnings-as-errors,
    /// analyzer rules, and layering guards all go unrun, and its unversioned
    /// <c>PackageReference</c>s never get a chance to fail restore. There is no opt-out
    /// mechanism on purpose — a project this repo deliberately excluded from the build would be
    /// a project to delete.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryProjectOnDiskIsInTheSolution()
    {
        string repoRoot = FindRepoRoot();
        string solutionPath = Path.Combine(repoRoot, "ClaudeForge.slnx");
        Assert.IsTrue(
            File.Exists(solutionPath),
            $"'{solutionPath}' not found. This test cannot check solution membership without it.");

        // Attribute values, never file text: a path inside an XML comment describing what the
        // solution used to contain would otherwise register as membership.
        HashSet<string> listed = XDocument
            .Load(solutionPath)
            .Descendants("Project")
            .Select(p => (string?)p.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.IsTrue(
            listed.Count > 0,
            $"Parsed no <Project Path=…> entries out of '{solutionPath}'. Either the solution "
            + "format changed or this test is no longer reading it — either way it is guarding "
            + "nothing.");

        List<string> unlisted = [];
        int onDisk = 0;

        foreach (string area in new[] { "src", "tests", "samples" })
        {
            string areaDir = Path.Combine(repoRoot, area);
            if (!Directory.Exists(areaDir))
            {
                continue;
            }

            foreach (string projectFile in Directory.GetFiles(areaDir, "*.csproj", SearchOption.AllDirectories))
            {
                string relative = Path
                    .GetRelativePath(repoRoot, projectFile)
                    .Replace('\\', '/');

                // Build outputs can contain generated project files; they are not source.
                if (relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                onDisk++;
                if (!listed.Contains(relative))
                {
                    unlisted.Add(relative);
                }
            }
        }

        Assert.IsTrue(
            onDisk > 0,
            $"Found no .csproj files under src/, tests/ or samples/ in '{repoRoot}'. This test "
            + "would pass without checking anything.");

        Assert.IsTrue(
            unlisted.Count == 0,
            $"{unlisted.Count} project(s) exist on disk but are absent from ClaudeForge.slnx, so "
            + "CI never builds or tests them and a local build cannot tell you that:\n  "
            + string.Join("\n  ", unlisted.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// No compiled source file may be hidden from git by an ignore rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>This is the worst failure mode in this file, and it is the one nothing checked.</b>
    /// A source file that <c>.gitignore</c> swallows builds, tests and publishes perfectly on the
    /// machine that wrote it, and then <b>does not exist</b> for anybody else — CI included. There is
    /// no error to read, because from CI's point of view the file was never written.
    /// </para>
    /// <para>
    /// Found the hard way in Phase 11a: <c>.gitignore</c> carried an unanchored <c>artifacts/</c>
    /// (meaning the .NET SDK's root output folder), <c>core.ignorecase=true</c> on Windows made it
    /// match the capital spelling, and it silently swallowed <b>six</b> new source files under
    /// <c>src/OpenCode.Sdk/Artifacts/</c> and <c>tests/OpenCode.Sdk.Tests/Artifacts/</c> — a whole
    /// slice that was locally green and would have reached CI as a compile error in files that
    /// were not there. The rule is now anchored; this test is what stops the next one.
    /// </para>
    /// <para>
    /// ⚠ <b>It fails rather than skips when git is unavailable.</b> A guard that quietly opts out
    /// on the machines where it cannot run is the same decorative-protection problem it exists to
    /// catch — and every machine that can clone this repository has git.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void NoCompiledSourceFileIsHiddenFromGitByAnIgnoreRule()
    {
        string repoRoot = FindRepoRoot();

        List<string> sources = [];
        foreach (string area in new[] { "src", "tests" })
        {
            string root = Path.Combine(repoRoot, area);
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

                // Build output is ignored on purpose and is the overwhelming majority of matches.
                if (relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                sources.Add(relative);
            }
        }

        Assert.IsTrue(
            sources.Count > 0,
            $"Found no .cs files under src/ or tests/ in '{repoRoot}'. This test would pass "
            + "without checking anything.");

        (int exitCode, string output) = RunGitCheckIgnore(repoRoot, sources);

        // `git check-ignore --stdin` exits 0 when it matched something, 1 when it matched
        // nothing, and 128 on a real failure. Only 0 and 1 are answers.
        Assert.IsTrue(
            exitCode is 0 or 1,
            $"git check-ignore could not run (exit {exitCode}), so this guard checked nothing:\n{output}");

        List<string> ignored = [.. output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        Assert.IsTrue(
            ignored.Count == 0,
            $"{ignored.Count} compiled source file(s) are excluded by .gitignore, so they exist "
            + "only on this machine and CI will never see them:\n  "
            + string.Join("\n  ", ignored.Order(StringComparer.Ordinal))
            + "\n\nRun `git check-ignore -v <path>` to see which rule matches.");
    }

    /// <summary>
    /// Ask git which of <paramref name="relativePaths"/> are ignored, feeding them on stdin so the
    /// command-line length limit cannot truncate the list silently.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><c>-z</c> is load-bearing on Windows, not tidiness.</b> Without it the paths are
    /// newline-separated, <c>StreamWriter.WriteLine</c> emits <c>\r\n</c>, and git splits on
    /// <c>\n</c> alone — so every path it tests carries a trailing <c>\r</c> and is not the path on
    /// disk. It happened to still match a directory-prefix rule while this was being written, which
    /// is precisely the danger: a rule matching an exact file name would have been <b>missed
    /// silently</b> and the guard would have reported all-clear. NUL separation removes the
    /// question, and also stops git quoting names it thinks are unusual.
    /// </remarks>
    private static (int ExitCode, string Output) RunGitCheckIgnore(
        string repoRoot, IReadOnlyList<string> relativePaths)
    {
        System.Diagnostics.ProcessStartInfo info = new("git", "check-ignore --stdin -z")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)
                ?? throw new InvalidOperationException("git did not start.");

            foreach (string path in relativePaths)
            {
                // NOT WriteLine: see the -z note above.
                process.StandardInput.Write(path);
                process.StandardInput.Write('\0');
            }

            process.StandardInput.Close();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(milliseconds: 60_000);

            return (process.ExitCode, process.ExitCode is 0 or 1 ? output : output + error);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (128, $"git could not be started: {ex.Message}");
        }
    }
}
