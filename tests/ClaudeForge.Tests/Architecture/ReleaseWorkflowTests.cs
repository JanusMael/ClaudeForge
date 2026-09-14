using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Each app's release workflow publishes that app, under that app's tag shape, and no tag can
/// reach the wrong one.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The failure this exists for is silent in both directions.</b> A repository hosting two
/// apps tells their releases apart by tag prefix — see
/// <c>AgentForge.Core.Updates.ReleaseTagScheme</c>. If a workflow's trigger and its app's prefix
/// drift apart, either the app publishes under a tag its own installed copies do not recognise
/// (so the update check finds nothing, forever, with no error anywhere), or a tag reaches the
/// wrong workflow and attaches one app's binaries to the other app's release. Nothing fails at
/// build time in either case, and nobody finds out until a release is cut.
/// </para>
/// <para>
/// ⚠ <b>A shared workflow with an app matrix cannot work here, and this test is where that is
/// enforced.</b> A workflow is selected by its tag trigger, and the two tag shapes are disjoint
/// by construction — so one workflow catching both would have to widen its pattern until it
/// caught the sibling's tags too. Both release workflows say this in their own headers; this is
/// the executable form.
/// </para>
/// </remarks>
[TestClass]
public sealed class ReleaseWorkflowTests
{
    /// <summary>A quoted entry under a <c>tags:</c> list, e.g. <c>- 'opencodeforge-v*.*.*'</c>.</summary>
    private static readonly Regex TagEntryRegex = new(
        @"^\s*-\s*'(?<tag>[^']+)'\s*$",
        RegexOptions.Compiled);

    /// <summary><c>APP_NAME: Something</c> in the workflow's env block.</summary>
    private static readonly Regex AppNameRegex = new(
        @"^\s*APP_NAME:\s*(?<name>\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Every <c>-App &lt;Name&gt;</c> passed to a publish script.</summary>
    private static readonly Regex AppArgRegex = new(
        @"publish\.ps1\s+-App\s+(?<name>[A-Za-z0-9._-]+)",
        RegexOptions.Compiled);

    private sealed record ReleaseWorkflow(string FileName, string AppName, List<string> Tags, List<string> AppArgs);

    /// <summary>
    /// Workflows that publish a release: they trigger on tags AND drive a publish script.
    /// </summary>
    /// <remarks>
    /// Discovered rather than listed. A third app's workflow is then covered the day it lands,
    /// which is the point at which a hand-maintained list would have been the thing that was
    /// forgotten.
    /// </remarks>
    private static List<ReleaseWorkflow> Discover(string repoRoot)
    {
        List<ReleaseWorkflow> found = [];
        string dir = Path.Combine(repoRoot, ".github", "workflows");
        if (!Directory.Exists(dir))
        {
            return found;
        }

        foreach (string path in Directory.GetFiles(dir, "*.yml").Order(StringComparer.Ordinal))
        {
            string[] lines = File.ReadAllLines(path);
            string text = string.Join('\n', lines);

            List<string> appArgs = [.. AppArgRegex
                .Matches(text)
                .Select(m => m.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)];

            if (appArgs.Count == 0)
            {
                continue;
            }

            // Collect the entries under the `tags:` key. Indentation-scoped rather than
            // YAML-parsed: the list ends at the first line that is not a list entry.
            List<string> tags = [];
            bool inTags = false;
            foreach (string line in lines)
            {
                if (Regex.IsMatch(line, @"^\s*tags:\s*$"))
                {
                    inTags = true;
                    continue;
                }

                if (!inTags)
                {
                    continue;
                }

                Match entry = TagEntryRegex.Match(line);
                if (entry.Success)
                {
                    tags.Add(entry.Groups["tag"].Value);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    inTags = false;
                }
            }

            if (tags.Count == 0)
            {
                continue;
            }

            Match appName = AppNameRegex.Match(text);
            found.Add(new ReleaseWorkflow(
                Path.GetFileName(path),
                appName.Success ? appName.Groups["name"].Value : string.Empty,
                tags,
                appArgs));
        }

        return found;
    }

    /// <summary>
    /// Turn a GitHub tag filter into a regex. <c>*</c> matches any run of characters except a
    /// path separator; everything else is literal.
    /// </summary>
    private static Regex TagFilterToRegex(string pattern)
    {
        string escaped = Regex.Escape(pattern).Replace(@"\*", "[^/]*", StringComparison.Ordinal);
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase);
    }

    [TestMethod]
    public void EveryPublishableAppHasItsOwnReleaseWorkflow()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        List<PublishAppTable.Row> apps = PublishAppTable.Read(repoRoot);
        List<ReleaseWorkflow> workflows = Discover(repoRoot);

        Assert.IsTrue(
            apps.Count > 0,
            $"{PublishAppTable.RelativePath} parsed to no rows; this test would check nothing.");
        Assert.IsTrue(
            workflows.Count > 0,
            "Found no release workflows (a tag trigger plus a publish.ps1 -App invocation). "
            + "Either the workflows changed shape and this scan no longer recognises them, or "
            + "there are none — the first case makes every assertion below vacuous.");

        List<string> unreleasable = [.. apps
            .Select(a => a.Name)
            .Where(name => !workflows.Any(w =>
                w.AppArgs.Contains(name, StringComparer.Ordinal)))];

        Assert.IsTrue(
            unreleasable.Count == 0,
            $"{unreleasable.Count} app(s) can be published locally but have no release workflow, "
            + "so they can never actually ship:\n  " + string.Join("\n  ", unreleasable));
    }

    [TestMethod]
    public void EachReleaseWorkflowPublishesExactlyOneAppAndSaysWhichInAppName()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        List<ReleaseWorkflow> workflows = Discover(repoRoot);
        Assert.IsTrue(workflows.Count > 0, "No release workflows discovered.");

        List<string> problems = [];
        foreach (ReleaseWorkflow w in workflows)
        {
            if (w.AppArgs.Count != 1)
            {
                problems.Add(
                    $"{w.FileName}: publishes {w.AppArgs.Count} different apps "
                    + $"({string.Join(", ", w.AppArgs)}). A release attaches one app's binaries "
                    + "to one tag, so a workflow building two produces a release that is wrong "
                    + "for both.");
                continue;
            }

            if (!string.Equals(w.AppName, w.AppArgs[0], StringComparison.Ordinal))
            {
                problems.Add(
                    $"{w.FileName}: APP_NAME is '{w.AppName}' but it publishes '{w.AppArgs[0]}'. "
                    + "APP_NAME titles the release and names every file in its download table, "
                    + "so a mismatch ships one app under the other's name.");
            }
        }

        Assert.IsTrue(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Each workflow's tag filters match its own app's <c>TagPrefix</c> and nothing else's.
    /// </summary>
    [TestMethod]
    public void EachWorkflowsTagFilterMatchesItsAppsTagPrefix()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        List<PublishAppTable.Row> apps = PublishAppTable.Read(repoRoot);
        List<ReleaseWorkflow> workflows = Discover(repoRoot);

        List<string> problems = [];
        int checkedCount = 0;

        foreach (ReleaseWorkflow w in workflows)
        {
            PublishAppTable.Row? app = apps.FirstOrDefault(
                a => w.AppArgs.Contains(a.Name, StringComparer.Ordinal));
            if (app is null)
            {
                problems.Add(
                    $"{w.FileName}: publishes '{string.Join(", ", w.AppArgs)}', which is not a row "
                    + $"in {PublishAppTable.RelativePath}.");
                continue;
            }

            foreach (string tag in w.Tags)
            {
                checkedCount++;

                if (app.TagPrefix.Length > 0)
                {
                    if (!tag.StartsWith(app.TagPrefix, StringComparison.Ordinal))
                    {
                        problems.Add(
                            $"{w.FileName}: tag filter '{tag}' does not start with "
                            + $"'{app.TagPrefix}', the prefix {app.Name}'s update check looks "
                            + "for. Releases published by this workflow would be invisible to "
                            + "every installed copy of the app.");
                    }
                }
                else
                {
                    // The unprefixed app must not claim a prefixed sibling's space.
                    string? stolen = apps
                        .Where(a => a.TagPrefix.Length > 0)
                        .Select(a => a.TagPrefix)
                        .FirstOrDefault(p => tag.StartsWith(p, StringComparison.Ordinal));

                    if (stolen is not null)
                    {
                        problems.Add(
                            $"{w.FileName}: tag filter '{tag}' begins with '{stolen}', which "
                            + "belongs to another app.");
                    }
                }
            }
        }

        Assert.IsTrue(
            checkedCount > 0,
            "No tag filters were checked, so this test guarded nothing.");
        Assert.IsTrue(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// No tag can fire two release workflows.
    /// </summary>
    /// <remarks>
    /// Checked by construction rather than by inspection: for every app, build the tag that app
    /// would actually publish and confirm exactly one workflow's filters accept it. A pattern
    /// widened by one character — <c>v*</c> for <c>v*.*.*</c> — would start swallowing the
    /// sibling's tags, and reading the two files side by side is exactly how that gets missed.
    /// </remarks>
    [TestMethod]
    public void NoTagMatchesMoreThanOneReleaseWorkflow()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        List<PublishAppTable.Row> apps = PublishAppTable.Read(repoRoot);
        List<ReleaseWorkflow> workflows = Discover(repoRoot);

        List<string> problems = [];
        int checkedCount = 0;

        foreach (PublishAppTable.Row app in apps)
        {
            // A representative tag in this app's own shape: prefix + v + three numbers.
            foreach (string sample in new[]
                     {
                         app.TagPrefix + "v2026.4.100",
                         app.TagPrefix + "v2026.4.100-rc.1",
                     })
            {
                checkedCount++;

                List<string> matched = [.. workflows
                    .Where(w => w.Tags.Any(t => TagFilterToRegex(t).IsMatch(sample)))
                    .Select(w => w.FileName)];

                if (matched.Count == 0)
                {
                    problems.Add(
                        $"'{sample}' ({app.Name}) matches NO release workflow, so pushing that "
                        + "tag publishes nothing and reports no error.");
                }
                else if (matched.Count > 1)
                {
                    problems.Add(
                        $"'{sample}' ({app.Name}) matches {matched.Count} release workflows "
                        + $"({string.Join(", ", matched)}). Both would run and both would attach "
                        + "their own app's binaries to the same release.");
                }
            }
        }

        Assert.IsTrue(checkedCount > 0, "No sample tags were checked.");
        Assert.IsTrue(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Every job that publishes resolves its version from the tag first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>A release that does not pin the version stamps the day CI RAN, and looks correct
    /// while doing it.</b> The version is a CalVer stamp computed from a captured instant, so a
    /// tag cut minutes before its build agrees with it by coincidence — and diverges on a re-run
    /// days later, or a tag cut near midnight. Nothing reports the difference; the binaries
    /// simply carry a version that is not in any tag.
    /// </para>
    /// <para>
    /// ⚠ <b>It has to be per JOB, which is why this counts rather than merely finds one.</b>
    /// <c>$GITHUB_ENV</c> does not cross a job boundary, so a workflow that resolved the version
    /// once and published from three jobs would pin one of them. Counting the resolve steps
    /// against the publish steps is what notices a job added later without one.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryPublishingJobResolvesItsVersionFromTheTag()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();

        // ⚠ NOT Discover(): that finds workflows driving `publish.ps1 -App`, which is the two
        // APP releases. The package release publishes eleven libraries and touches no app, so it
        // would have sat outside this guard entirely — the one workflow whose output is
        // permanently immutable.
        List<string> problems = [];
        int covered = 0;

        foreach (string path in TagTriggeredWorkflows(repoRoot))
        {
            // ⚠ Comment lines are dropped BEFORE counting, and that is not tidiness. Every one
            // of these workflows explains this mechanism in its header, naming the resolver
            // script — so counting raw occurrences let a header comment stand in for a missing
            // step. Caught by canarying this test rather than by reading it.
            string text = string.Join('\n', File.ReadAllLines(path)
                .Where(l => !l.TrimStart().StartsWith('#')));

            int publishes = Regex.Matches(text, @"publish\.ps1\s+-App\s").Count
                + Regex.Matches(text, @"Publish-Packages\.ps1").Count;

            if (publishes == 0)
            {
                continue;
            }

            covered++;
            int resolves = Regex.Matches(text, @"Resolve-ReleaseVersion\.ps1").Count;

            if (resolves < publishes)
            {
                problems.Add(
                    $"{Path.GetFileName(path)} publishes {publishes} time(s) but resolves the "
                    + $"version from the tag only {resolves} time(s). $GITHUB_ENV does not cross "
                    + "a job boundary, so the unresolved job stamps the CI run's calendar date "
                    + "instead of the tag's. Add a 'Resolve version from tag' step to it.");
            }
        }

        Assert.AreNotEqual(0, covered,
            "No tag-triggered workflow publishes anything, so this guard is measuring nothing.");

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>
    /// A workflow's <c>TAG_PREFIX</c> is the prefix its own trigger accepts.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>The two are used at opposite ends and nothing connects them.</b> The trigger decides
    /// which tags reach the workflow; <c>TAG_PREFIX</c> is what
    /// <c>Resolve-ReleaseVersion.ps1</c> strips off the front of one. Drift between them does not
    /// produce a wrong version — the resolver refuses a tag that does not start with the prefix
    /// it was given — but it turns every release of that app into a failed run, discovered at
    /// release time. The agreement is cheap to assert and impossible to see by reading two ends
    /// of one file.
    /// </remarks>
    [TestMethod]
    public void EveryTagPrefixMatchesItsOwnWorkflowsTrigger()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();

        List<string> problems = [];
        int checkedCount = 0;

        foreach (string path in TagTriggeredWorkflows(repoRoot))
        {
            string[] lines = File.ReadAllLines(path);
            string text = string.Join('\n', lines.Where(l => !l.TrimStart().StartsWith('#')));

            Match prefix = Regex.Match(text, @"^\s*TAG_PREFIX:\s*(?<value>\S+)\s*$",
                RegexOptions.Multiline);

            if (!prefix.Success)
            {
                continue;
            }

            checkedCount++;
            string declared = prefix.Groups["value"].Value;

            // Every tag this workflow accepts must begin with the prefix it strips.
            List<string> tags = TagsOf(lines);
            foreach (string tag in tags)
            {
                if (!tag.StartsWith(declared, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{Path.GetFileName(path)} declares TAG_PREFIX '{declared}' but triggers "
                        + $"on '{tag}', which does not start with it. Resolve-ReleaseVersion.ps1 "
                        + "refuses a tag that does not carry the prefix it was handed, so every "
                        + "release through this workflow would fail — at release time.");
                }
            }
        }

        Assert.AreNotEqual(0, checkedCount,
            "No workflow declares TAG_PREFIX, so this guard is measuring nothing.");

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>Every workflow file that triggers on tags.</summary>
    private static IEnumerable<string> TagTriggeredWorkflows(string repoRoot)
    {
        string dir = Path.Combine(repoRoot, ".github", "workflows");
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (string path in Directory.GetFiles(dir, "*.yml").Order(StringComparer.Ordinal))
        {
            if (TagsOf(File.ReadAllLines(path)).Count > 0)
            {
                yield return path;
            }
        }
    }

    /// <summary>The entries under a workflow's <c>tags:</c> key.</summary>
    /// <remarks>
    /// Indentation-scoped rather than YAML-parsed, matching <see cref="Discover"/>: the list ends
    /// at the first line that is not a list entry.
    /// </remarks>
    private static List<string> TagsOf(string[] lines)
    {
        List<string> tags = [];
        bool inTags = false;

        foreach (string line in lines)
        {
            if (Regex.IsMatch(line, @"^\s*tags:\s*$"))
            {
                inTags = true;
                continue;
            }

            if (!inTags)
            {
                continue;
            }

            Match entry = TagEntryRegex.Match(line);
            if (entry.Success)
            {
                tags.Add(entry.Groups["tag"].Value);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                inTags = false;
            }
        }

        return tags;
    }

    /// <summary>
    /// No workflow sets <c>PublicVersion</c>, which looks like the version input and is not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>The property is not forbidden — a SECOND PLACE SETTING IT is.</b>
    /// <c>scripts/Resolve-ReleaseVersion.ps1</c> emits it beside <c>BuildTimestamp</c>, both from
    /// the same tag, so they cannot disagree about which release they describe. A workflow that
    /// also sets it is how they start to.
    /// </para>
    /// <para>
    /// ⛔ <b>What made this dangerous is that the name promises the version and does not deliver
    /// it.</b> It is AutoVersioning's own documented CI-version property, but the generator writes
    /// it only as <c>[AssemblyMetadata("PublicVersion", …)]</c> — measured: a build with
    /// <c>-p:PublicVersion=2026.3.901</c> carries that attribute and stamps
    /// <c>2026.3.914.1346</c>. Both release workflows set it directly for a phase, and their
    /// comments described a version flow that was not happening.
    /// </para>
    /// <para>
    /// ⓘ Comment lines are skipped on purpose: the workflows explain this in prose, and that
    /// prose is the reason it will not be reintroduced.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void NoWorkflowSetsPublicVersion()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        string dir = Path.Combine(repoRoot, ".github", "workflows");

        Assert.IsTrue(Directory.Exists(dir), $"No workflows directory at '{dir}'.");

        List<string> offenders = [];
        int scanned = 0;

        foreach (string path in Directory.GetFiles(dir, "*.yml").Order(StringComparer.Ordinal))
        {
            scanned++;
            string[] lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith('#'))
                {
                    continue;
                }

                if (lines[i].Contains("PublicVersion", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(path)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.AreNotEqual(0, scanned, "Scanned no workflow files, so this guard proves nothing.");

        Assert.AreEqual(0, offenders.Count,
            "These workflow lines set PublicVersion themselves. It is not the version input — the "
            + "generator writes it only as assembly METADATA, and the numbers come from "
            + "BuildTimestamp — so a workflow setting it independently produces a binary whose "
            + "recorded version and stamped version can name different tags. "
            + "scripts/Resolve-ReleaseVersion.ps1 emits both from one tag; call that instead. "
            + "Offenders: " + string.Join("; ", offenders));
    }
}
