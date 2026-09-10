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
}
