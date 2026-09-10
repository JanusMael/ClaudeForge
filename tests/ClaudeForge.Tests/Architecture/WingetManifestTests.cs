using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every app has a complete winget manifest set, and each set addresses that app's own
/// release assets.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>A wrong URL here is found by Microsoft's validation pipeline, not by anything in this
/// repository.</b> The installer manifest hardcodes the download URL of a release asset —
/// including the app's <b>tag prefix</b>, which differs per app because a monorepo tells its
/// apps' releases apart by prefix. Change a prefix in one place and the manifest keeps pointing
/// at a tag nothing publishes; the submission then fails somewhere between a fork, a PR and an
/// Azure DevOps validation run, hours after the release, with a 404 that reads as a missing
/// asset rather than a wrong tag.
/// </para>
/// <para>
/// ⚠ <b>And it is published permanently.</b> A winget manifest that reaches the catalog cannot
/// be retroactively repointed — which is also why this repository keeps a name that no longer
/// describes it. Getting this wrong is not a fix-forward situation.
/// </para>
/// <para>
/// ⓘ These are text assertions over the YAML rather than a parse: the repository has no YAML
/// dependency, and the fields that matter here are flat scalars whose exact spelling is the
/// thing being checked.
/// </para>
/// </remarks>
[TestClass]
public sealed class WingetManifestTests
{
    private const string ManifestDir = "packaging/winget";

    /// <summary>The version placeholder the submission scripts substitute.</summary>
    private const string VersionPlaceholder = "<PACKAGE_VERSION>";

    private static readonly Regex PackageIdentifierRegex = new(
        @"^PackageIdentifier:\s*(?<id>\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex InstallerUrlRegex = new(
        @"^\s*InstallerUrl:\s*(?<url>\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex RelativeFilePathRegex = new(
        @"^\s*-\s*RelativeFilePath:\s*(?<path>\S+)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static string ManifestPath(string repoRoot, string fileName) =>
        Path.Combine(repoRoot, ManifestDir.Replace('/', Path.DirectorySeparatorChar), fileName);

    [TestMethod]
    public void EveryAppHasACompleteManifestSet()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        List<PublishAppTable.Row> apps = PublishAppTable.Read(repoRoot);

        Assert.IsTrue(apps.Count > 0, "No apps parsed; this test would check nothing.");

        List<string> missing = [];
        foreach (PublishAppTable.Row app in apps)
        {
            string id = app.WingetPackageId;

            foreach (string required in new[] { $"{id}.yaml", $"{id}.installer.yaml" })
            {
                if (!File.Exists(ManifestPath(repoRoot, required)))
                {
                    missing.Add($"{app.Name}: {ManifestDir}/{required}");
                }
            }

            // At least one locale. winget requires the DefaultLocale manifest to exist; which
            // locales beyond it are shipped is a choice, so this asserts presence not identity.
            string dir = Path.Combine(repoRoot, ManifestDir.Replace('/', Path.DirectorySeparatorChar));
            bool anyLocale = Directory.Exists(dir)
                && Directory.EnumerateFiles(dir, $"{id}.locale.*.yaml").Any();
            if (!anyLocale)
            {
                missing.Add($"{app.Name}: {ManifestDir}/{id}.locale.<tag>.yaml (none found)");
            }
        }

        Assert.IsTrue(
            missing.Count == 0,
            $"{missing.Count} winget manifest file(s) are missing, so that app cannot be "
            + "submitted to the catalog:\n  " + string.Join("\n  ", missing));
    }

    [TestMethod]
    public void EveryManifestDeclaresThePackageIdItsFilenameClaims()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        string dir = Path.Combine(repoRoot, ManifestDir.Replace('/', Path.DirectorySeparatorChar));

        Assert.IsTrue(Directory.Exists(dir), $"'{ManifestDir}' not found.");

        List<string> problems = [];
        int checkedCount = 0;

        foreach (string path in Directory.GetFiles(dir, "*.yaml").Order(StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(path);
            Match declared = PackageIdentifierRegex.Match(File.ReadAllText(path));

            if (!declared.Success)
            {
                problems.Add($"{fileName}: no PackageIdentifier line.");
                continue;
            }

            checkedCount++;
            string id = declared.Groups["id"].Value;

            // The submission scripts select an app's manifests BY FILENAME STEM, so a file whose
            // declared identity disagrees with its name would be staged for one package and
            // published as another.
            if (!fileName.StartsWith(id + ".", StringComparison.Ordinal))
            {
                problems.Add(
                    $"{fileName}: declares PackageIdentifier '{id}', which is not its filename "
                    + "stem. The submission scripts pick an app's manifests by that stem.");
            }
        }

        Assert.IsTrue(checkedCount > 0, "No manifests were checked.");
        Assert.IsTrue(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Each installer manifest's URLs address that app's own tag shape and asset names.
    /// </summary>
    [TestMethod]
    public void InstallerUrlsUseTheAppsOwnTagPrefixAndAssetNames()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        List<PublishAppTable.Row> apps = PublishAppTable.Read(repoRoot);

        List<string> problems = [];
        int checkedCount = 0;

        foreach (PublishAppTable.Row app in apps)
        {
            string path = ManifestPath(repoRoot, $"{app.WingetPackageId}.installer.yaml");
            if (!File.Exists(path))
            {
                continue; // EveryAppHasACompleteManifestSet reports this.
            }

            string text = File.ReadAllText(path);
            List<string> urls = [.. InstallerUrlRegex
                .Matches(text)
                .Select(m => m.Groups["url"].Value)];

            Assert.IsTrue(
                urls.Count > 0,
                $"{app.WingetPackageId}.installer.yaml declares no InstallerUrl, so winget has "
                + "nothing to download.");

            // The tag segment a release of this app actually produces.
            //
            // ⚠ prefix + 'v' + version, not prefix + version. Both submission paths build
            // `$tag = "$TagPrefix" + "v$Version"`, so the conventional 'v' sits BETWEEN the
            // app prefix and the number: `v2026.3.810` and `opencodeforge-v2026.4.100`. An
            // earlier draft of this assertion dropped the 'v' and reddened against manifests
            // that were correct.
            string expectedTagSegment = $"/releases/download/{app.TagPrefix}v{VersionPlaceholder}/";

            foreach (string url in urls)
            {
                checkedCount++;

                if (!url.Contains(expectedTagSegment, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{app.Name}: InstallerUrl '{url}' does not contain "
                        + $"'{expectedTagSegment}'. This app publishes tags prefixed "
                        + $"'{app.TagPrefix}' (see its TagPrefix in the publish table and its "
                        + "ReleaseTagScheme in AgentForge.Core.Updates), so this URL points at a "
                        + "tag nothing creates and the submission 404s at validation.");
                }

                // …and at the archive the publish scripts actually write.
                if (!url.Contains($"/{app.AssemblyName}-win-", StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{app.Name}: InstallerUrl '{url}' does not name a "
                        + $"'{app.AssemblyName}-win-<arch>.zip' asset. Archive names come from "
                        + "the assembly name in the publish table.");
                }
            }

            // The nested portable file is the executable inside that zip.
            List<string> nested = [.. RelativeFilePathRegex
                .Matches(text)
                .Select(m => m.Groups["path"].Value)];

            foreach (string rel in nested)
            {
                if (!string.Equals(rel, $"{app.AssemblyName}.exe", StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{app.Name}: NestedInstallerFiles RelativeFilePath is '{rel}', expected "
                        + $"'{app.AssemblyName}.exe'. winget extracts that exact path from the "
                        + "zip; a wrong name installs a package whose command does not exist.");
                }
            }
        }

        Assert.IsTrue(
            checkedCount > 0,
            "No InstallerUrls were checked, so this test guarded nothing.");
        Assert.IsTrue(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// No app's manifest set overlaps another's by filename stem.
    /// </summary>
    /// <remarks>
    /// ⚠ The submission paths select an app's manifests by exact filename. They used to glob
    /// <c>*.yaml</c>, which was correct with one app and would have carried BOTH packages into a
    /// single winget-pkgs PR once there were two. This asserts the selection stays unambiguous:
    /// no package id may be a prefix of another, or "$id.*" filters would overlap.
    /// </remarks>
    [TestMethod]
    public void NoPackageIdIsAPrefixOfAnother()
    {
        string repoRoot = PublishAppTable.FindRepoRoot();
        List<PublishAppTable.Row> apps = PublishAppTable.Read(repoRoot);

        List<string> problems = [];
        foreach (PublishAppTable.Row a in apps)
        {
            foreach (PublishAppTable.Row b in apps)
            {
                if (ReferenceEquals(a, b))
                {
                    continue;
                }

                if (b.WingetPackageId.StartsWith(a.WingetPackageId, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"'{a.WingetPackageId}' ({a.Name}) is a prefix of "
                        + $"'{b.WingetPackageId}' ({b.Name}), so a filename filter for the first "
                        + "can absorb the second's manifests.");
                }
            }
        }

        Assert.IsTrue(problems.Count == 0, string.Join("\n", problems));
    }
}
