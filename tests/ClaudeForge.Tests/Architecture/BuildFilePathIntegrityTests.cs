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

    /// <summary>
    /// An inline Markdown link: <c>[text](target)</c>, with an optional quoted title.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>[^)\s]+</c> for the target on purpose. A path containing a space would need angle
    /// brackets in Markdown anyway, and stopping at whitespace is what lets the optional
    /// <c>"title"</c> suffix be discarded instead of swallowed into the path.
    /// ⓘ Reference-style links (<c>[text][label]</c>) are not matched. Nothing here uses them;
    /// if that changes, this regex is where it would be noticed.
    /// </remarks>
    private static readonly Regex MarkdownLinkRegex = new(
        @"\[(?<text>[^\]]*)\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)",
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
        //
        // ⛔ THREE script roots, not one. `scripts/` was the only one scanned until Phase 15,
        // and the two that were missing are the ones that run at release time: `src/publish/`
        // (the publish orchestrator, its per-RID worker, the smoke gate, the closure analyzer)
        // and `packaging/` (the winget submission). Those hold the app-identity paths a rename
        // breaks, and nothing outside a release cut executes them — so a stale path there is
        // found by the person cutting the release, at the moment they can least afford it.
        foreach (string scriptRoot in new[] { "scripts", "src/publish", "packaging" })
        {
            string scripts = Path.Combine(
                repoRoot, scriptRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(scripts))
            {
                continue;
            }

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

    [Fact]
    public void ScanFindsFiles_SoThisTestIsNotVacuous()
    {
        string repoRoot = FindRepoRoot();
        Assert.True(
            ScannedFiles(repoRoot).Any(),
            $"No workflows, scripts, or .slnx found under '{repoRoot}'. This test would pass "
            + "without checking anything.");
    }

    /// <summary>
    /// Every repo-relative <c>src/</c> or <c>tests/</c> path hardcoded in a build file resolves
    /// to something a <b>fresh checkout</b> actually has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>"Exists" means <i>git tracks it</i>, not <i>it is on this disk</i>.</b> For three
    /// separate incidents this guard asked the developer's filesystem, which is the one machine
    /// where the answer is always yes. A path naming a directory git does not track passes here
    /// and fails on every CI runner, and the gap is invisible locally precisely because the
    /// local run is the thing that is wrong.
    /// </para>
    /// <para>
    /// The most recent, 2026-09-20: a comment in <c>release.yml</c> named <c>src/dist/logs/</c>,
    /// a gitignored publish output that is full of files on any machine that has published and
    /// absent everywhere else. <c>a6fd749</c> was green locally at 3,551 tests and reddened
    /// <b>all four</b> CI test jobs on this one assertion.
    /// </para>
    /// <para>
    /// ⚠ <b>Emptiness was the wrong diagnosis.</b> The mitigation this replaces was
    /// <c>find src tests -type d -empty</c>, and it cannot see this class of defect at all:
    /// <c>src/dist/logs/</c> is not empty locally, it is <i>untracked</i>. Emptiness is only one
    /// of the ways a directory fails to survive a clone, and it is not the one that keeps
    /// happening.
    /// </para>
    /// <para>
    /// The check is the <b>conjunction</b> — on disk <i>and</i> tracked — so it is strictly
    /// stronger than what it replaces. Dropping the filesystem half would weaken it: a path
    /// deleted locally but still in the index would start passing. Keeping both also closes a
    /// Windows-only hole for free, because <see cref="Directory.Exists"/> is case-insensitive
    /// there and git's index never is, so a path whose casing only works on Windows is now
    /// caught on Windows.
    /// </para>
    /// <para>
    /// <b>Three deliberate quirks decide what becomes a candidate</b>, and two of them read
    /// backwards:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>src/dist/*.zip</c> is <b>skipped entirely</b>. <c>*</c> is inside the regex
    ///   character class, so it is consumed as part of a segment, the candidate still contains
    ///   a star after glob trimming, and it is discarded as a pattern.
    ///   </description></item>
    ///   <item><description>
    ///   <c>src/dist/</c> is checked as bare <c>src/dist</c>. The trailing slash is never
    ///   matched in the first place, because the regex requires a segment after each separator
    ///   — so writing the slash does <b>not</b> mark the name as a directory and does
    ///   <b>not</b> exempt it.
    ///   </description></item>
    ///   <item><description>
    ///   <c>src/dist/*</c> is <b>trimmed back</b> to bare <c>src/dist</c> and then checked,
    ///   unlike the first case, which looks almost identical.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The rule that falls out: a glob is only invisible to this guard when something follows
    /// the star <i>inside the same segment</i>. <c>src/dist</c>, <c>src/dist/</c> and
    /// <c>src/dist/*</c> are one and the same check.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryHardcodedRepoPathInBuildFilesExists()
    {
        string repoRoot = FindRepoRoot();
        (HashSet<string> trackedFiles, HashSet<string> trackedDirectories) = GitTrackedPaths(repoRoot);

        List<string> broken = [];
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

                    // A TRAILING elision has already lost its dots to the punctuation trim
                    // above, so `src/...` arrives here as `src/`. Normalise the separator away:
                    // `Directory.Exists` cannot tell `src/` from `src`, but git's index can, and
                    // it lists no entry under that spelling. Without this the git half would
                    // redden five accurate prose references that name an area, not a path.
                    candidate = candidate.TrimEnd('/');
                    if (candidate.Length == 0)
                    {
                        continue;
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
                    bool onDisk = File.Exists(absolute) || Directory.Exists(absolute);

                    // Ordinal on purpose: git's index is case-sensitive on every platform, and
                    // matching it loosely here would reintroduce the Windows-only pass.
                    bool inGit = trackedFiles.Contains(candidate) || trackedDirectories.Contains(candidate);

                    if (!onDisk)
                    {
                        broken.Add($"{relativeFile}: '{candidate}' — nothing at that path");
                    }
                    else if (!inGit)
                    {
                        broken.Add(
                            $"{relativeFile}: '{candidate}' — present here, but git tracks nothing "
                            + "at or under it, so a fresh checkout does not have it");
                    }
                }
            }
        }

        Assert.True(
            checkedCount > 0,
            "No repo-relative src/ or tests/ paths were found in any build file. Either the "
            + "regex no longer matches how these files are written, or the paths moved out of "
            + "them — either way this test is no longer guarding anything.");

        // ONE list, not two assertions. Two would mask each other: the second reason could
        // never be reported while the first still had an entry.
        Assert.True(
            broken.Count == 0,
            $"{broken.Count} hardcoded path(s) in build files point at something a fresh checkout "
            + "does not have. A stale path here fails silently — a workflow trigger simply stops "
            + "firing, or a scheduled script breaks at an hour nobody is watching:\n  "
            + string.Join("\n  ", broken.Distinct())
            + "\n\n'present here, but git tracks nothing' means the path only survives on this "
            + "machine: either it is gitignored build output (fix the prose, not this guard), or "
            + "it is a new file nobody has `git add`ed yet. `git check-ignore -v <path>` says "
            + "which.");
    }

    /// <summary>
    /// The documents whose Markdown links must resolve: everything the sibling guard already
    /// reads, plus <c>docs/</c>, and never <c>plans/</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Separate from <see cref="ScannedFiles"/> on purpose.</b> Adding <c>docs/</c> there
    /// would widen the prose-path guard too, which must keep excluding it — a plan document names
    /// paths in prose that do not exist yet, and asserting against those would be wrong rather
    /// than merely noisy. Only the <i>link</i> rule extends.
    /// </remarks>
    private static IEnumerable<string> MarkdownFilesForLinkScan(string repoRoot)
    {
        foreach (string f in ScannedFiles(repoRoot)
                     .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
        {
            yield return f;
        }

        string docs = Path.Combine(repoRoot, "docs");
        if (!Directory.Exists(docs))
        {
            yield break;
        }

        foreach (string f in Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories))
        {
            yield return f;
        }
    }

    /// <summary>
    /// Every relative Markdown link in the guidance docs resolves to something git tracks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>This class's other guard cannot see a dead link, and four of them proved it.</b>
    /// <see cref="RepoPathRegex"/> only matches paths beginning <c>src/</c> or <c>tests/</c>, so
    /// when <c>plans/00003</c> step 0e deliberately deleted <c>OPENCODEFORGE-PLAN.md</c> from this
    /// branch, the documents still linking to it stayed green — <c>CLAUDE.md</c>'s where-to-look
    /// table, <c>PROGRESS.md</c>'s header, an <c>AGENTS.md</c> citation. They were found by
    /// auditing a branch for deletion safety, not by any test.
    /// </para>
    /// <para>
    /// ⭐ <b>A Markdown link is a different kind of claim from a path in prose</b>, which is why
    /// this is a separate assertion rather than a widened regex. Prose naming an area ("everything
    /// under <c>src/</c>") promises nothing about navigability; <c>[text](./path)</c> does.
    /// Widening the other regex to every path-shaped token would make it reject accurate prose,
    /// and this repo has already paid for a guard that accuses the innocent.
    /// </para>
    /// <para>
    /// ⚠ <b>The scan set is <see cref="ScannedFiles"/> filtered to <c>.md</c>, PLUS
    /// <c>docs/</c>.</b> The sibling guard excludes <c>docs/</c> because a document may
    /// legitimately name a path in <i>prose</i> that does not exist yet — a future assembly, or a
    /// path as it was before a rename. ⭐ <b>That reasoning does not transfer to a link.</b>
    /// <c>[text](./path)</c> is a promise of navigability whenever it is written, and one of the
    /// four dead references that prompted this guard lived in <c>docs/</c>. So the exclusion is
    /// deliberately NOT inherited here — and <c>ScannedFiles</c> is left alone, because widening
    /// it would change what the prose guard asserts.
    /// </para>
    /// <para>
    /// ⛔ <b><c>plans/</c> is never scanned, and that is a rule rather than an oversight.</b> An
    /// approved plan is frozen: its internal links are specification, not navigation, and are left
    /// alone when they stop resolving. A guard reddening on them would force the very edit the
    /// freeze forbids. ⓘ <c>CHANGELOG.md</c> stays out for the sibling's reason, which does
    /// transfer: an entry describes the tree as it was at that release.
    /// </para>
    /// <para>
    /// ⚠ <b>Tracked, not merely present</b> — the same conjunction the sibling guard uses, for the
    /// same reason: a link that resolves only on the author's machine is dead everywhere else.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryRelativeMarkdownLinkInGuidanceDocsResolves()
    {
        string repoRoot = FindRepoRoot();
        (HashSet<string> trackedFiles, HashSet<string> trackedDirectories) = GitTrackedPaths(repoRoot);

        List<string> broken = [];
        int checkedCount = 0;

        foreach (string file in MarkdownFilesForLinkScan(repoRoot))
        {
            string relativeFile = Path.GetRelativePath(repoRoot, file);
            string fileDirectory = Path.GetDirectoryName(file)!;

            foreach (Match match in MarkdownLinkRegex.Matches(File.ReadAllText(file)))
            {
                string original = match.Groups["target"].Value.Trim();
                string target = original;

                // An anchor into the same document, an external URL, or a mail link. None of
                // these is a claim about a file in this repository.
                if (target.Length == 0
                    || target.StartsWith('#')
                    || target.StartsWith("//", StringComparison.Ordinal)
                    || target.Contains("://", StringComparison.Ordinal)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Drop a fragment or query: `./AGENTS.md#section` names AGENTS.md.
                int cut = target.IndexOfAny(['#', '?']);
                if (cut >= 0)
                {
                    target = target[..cut];
                }

                // A percent-escape means this was written as a URL rather than a path, and
                // decoding it correctly is more machinery than the case is worth.
                if (target.Length == 0 || target.Contains('%', StringComparison.Ordinal))
                {
                    continue;
                }

                string absolute = Path.GetFullPath(
                    Path.Combine(fileDirectory, target.Replace('/', Path.DirectorySeparatorChar)));

                // A link climbing out of the repository is not this guard's business.
                string relative = Path.GetRelativePath(repoRoot, absolute);
                if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                {
                    continue;
                }

                checkedCount++;

                string gitSpelling = relative.Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/');
                bool onDisk = File.Exists(absolute) || Directory.Exists(absolute);
                bool inGit = trackedFiles.Contains(gitSpelling)
                    || trackedDirectories.Contains(gitSpelling);

                if (!onDisk)
                {
                    broken.Add($"{relativeFile}: ({original}) — nothing at that path");
                }
                else if (!inGit)
                {
                    broken.Add(
                        $"{relativeFile}: ({original}) — present here, but git tracks nothing at "
                        + "that path, so the link is dead in a fresh checkout");
                }
            }
        }

        // ⛔ Without this, deleting every guidance doc — or breaking the regex — reads as a pass.
        // The sibling guard carries the same assertion for the same reason.
        Assert.True(
            checkedCount > 0,
            "No relative Markdown links were found in any guidance document. Either the docs "
            + "stopped using Markdown links, or MarkdownLinkRegex no longer matches how they are "
            + "written — either way this guard is no longer looking at anything.");

        Assert.True(
            broken.Count == 0,
            $"{broken.Count} Markdown link(s) in the guidance docs point at nothing a fresh "
            + "checkout has. A reader following one gets an error, and every such link makes the "
            + "surrounding document a little less worth trusting:\n  "
            + string.Join("\n  ", broken.Distinct())
            + "\n\nIf the target was deleted on purpose — as plans/00003 step 0e deleted "
            + "OPENCODEFORGE-PLAN.md from this branch — say so in the text and name where it now "
            + "lives, rather than leaving a link that cannot resolve.");
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
    [Fact]
    public void EveryProjectOnDiskIsInTheSolution()
    {
        string repoRoot = FindRepoRoot();
        string solutionPath = Path.Combine(repoRoot, "ClaudeForge.slnx");
        Assert.True(
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

        Assert.True(
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

        Assert.True(
            onDisk > 0,
            $"Found no .csproj files under src/, tests/ or samples/ in '{repoRoot}'. This test "
            + "would pass without checking anything.");

        Assert.True(
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
    [Fact]
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

        Assert.True(
            sources.Count > 0,
            $"Found no .cs files under src/ or tests/ in '{repoRoot}'. This test would pass "
            + "without checking anything.");

        (int exitCode, string output, string error) = RunGit(repoRoot, "check-ignore --stdin -z", sources);

        // `git check-ignore --stdin` exits 0 when it matched something, 1 when it matched
        // nothing, and 128 on a real failure. Only 0 and 1 are answers.
        Assert.True(
            exitCode is 0 or 1,
            $"git check-ignore could not run (exit {exitCode}), so this guard checked nothing:\n"
            + output + error);

        List<string> ignored = [.. output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        Assert.True(
            ignored.Count == 0,
            $"{ignored.Count} compiled source file(s) are excluded by .gitignore, so they exist "
            + "only on this machine and CI will never see them:\n  "
            + string.Join("\n  ", ignored.Order(StringComparer.Ordinal))
            + "\n\nRun `git check-ignore -v <path>` to see which rule matches.");
    }

    /// <summary>
    /// Everything git has in its index for this worktree: the tracked files, and every directory
    /// prefix implied by them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>One invocation, matched in memory.</b> Asking git per candidate, with
    /// <c>ls-files --error-unmatch</c>, is several hundred process launches on Windows for a
    /// single test — which is how a guard ends up deleted or disabled for being slow.
    /// </para>
    /// <para>
    /// Git has no concept of an empty directory, so the directory set has to be <i>derived</i>
    /// from the file list: <c>src/dist</c> counts as present exactly when git tracks some file
    /// beneath it. That derivation is the whole point — it is what makes a gitignored build
    /// output stuffed with local files read as absent.
    /// </para>
    /// <para>
    /// <c>-z</c> keeps git from quoting names it considers unusual, so the paths come back
    /// byte-for-byte as they sit in the index. They always use forward slashes, on every
    /// platform, which is the same spelling the candidates are normalised to.
    /// </para>
    /// </remarks>
    private static (HashSet<string> Files, HashSet<string> Directories) GitTrackedPaths(string repoRoot)
    {
        (int exitCode, string output, string error) = RunGit(repoRoot, "ls-files -z", stdinPaths: null);

        // Loud, never silent: a guard that decides "no git, nothing to check" is the decorative
        // protection this whole file exists to avoid.
        Assert.True(
            exitCode == 0,
            $"`git ls-files` could not run (exit {exitCode}), so this guard checked nothing:\n"
            + output + error);

        HashSet<string> files = new(StringComparer.Ordinal);
        HashSet<string> directories = new(StringComparer.Ordinal);

        foreach (string path in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            files.Add(path);

            for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            {
                directories.Add(path[..slash]);
            }
        }

        Assert.True(
            files.Count > 0,
            $"`git ls-files` reported no tracked files in '{repoRoot}'. Every path would be "
            + "reported as missing, so this is a broken guard rather than a broken repository.");

        return (files, directories);
    }

    /// <summary>
    /// Run git in <paramref name="repoRoot"/>, optionally feeding <paramref name="stdinPaths"/> on
    /// stdin so the command-line length limit cannot truncate a long path list silently.
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
    private static (int ExitCode, string Output, string Error) RunGit(
        string repoRoot, string arguments, IReadOnlyList<string>? stdinPaths)
    {
        System.Diagnostics.ProcessStartInfo info = new("git", arguments)
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

            foreach (string path in stdinPaths ?? [])
            {
                // NOT WriteLine: see the -z note above.
                process.StandardInput.Write(path);
                process.StandardInput.Write('\0');
            }

            process.StandardInput.Close();

            // stdout is drained to completion first and deliberately: `ls-files` writes far more
            // than a pipe buffer holds, while stderr stays empty on every path that returns an
            // answer rather than an error.
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(milliseconds: 60_000);

            return (process.ExitCode, output, error);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (128, string.Empty, $"git could not be started: {ex.Message}");
        }
    }
}
