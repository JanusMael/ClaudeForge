using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// The eleven packages and the assemblies inside them carry one version, from one source.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>What this guards is a SILENT wrong answer, not a failure.</b> <c>PackageVersion</c> has an
/// SDK default, so a repo that computes a version and never assigns it does not fail to pack — it
/// packs eleven packages at <c>1.0.0</c>, which is what this repo did until plan 00001 work item 3.
/// GitHub Packages refuses to re-push a version, so that mistake is immutable the moment a release
/// runs.
/// </para>
/// <para>
/// ⭐ <b>The stamp is read out of built DLLs, never out of the property that produced it.</b>
/// Asserting <c>$(PackageVersion) == $(AutoPackageVersion)</c> would be the property agreeing with
/// itself: it holds just as well when the generator writes something else into the assemblies, and
/// that disagreement — a package claiming a version the DLL inside it does not carry — is the whole
/// thing worth catching.
/// </para>
/// <para>
/// See <c>plans/00001-shared-libraries-as-private-nuget-packages.md</c>, work item 3.
/// </para>
/// </remarks>
[TestClass]
public sealed class PackageVersionLockstepTests
{
    /// <summary>The prefix every package published from this repo carries.</summary>
    private const string PackagePrefix = "Bennewitz.Ninja.";

    /// <summary>
    /// The premise: the root targets file assigns <c>PackageVersion</c> from the AutoVersioning
    /// property, and does it in the one file that can.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Moving this assignment into <c>src/Directory.Build.props</c> is the tempting mistake,
    /// and it fails silently.</b> That file is imported before the NuGet-generated props that
    /// define <c>AutoPackageVersion</c>, so the assignment evaluates to the empty string, the SDK's
    /// own default has already run, and every package packs at <c>1.0.0</c> again with nothing
    /// reporting anything. The rest of the package identity lives in that file, which is exactly
    /// why someone will try.
    /// </remarks>
    [TestMethod]
    public void PackageVersionIsAssignedFromTheAutoVersioningProperty()
    {
        string repoRoot = FindRepoRoot();
        string targetsPath = Path.Combine(repoRoot, "Directory.Build.targets");

        Assert.IsTrue(File.Exists(targetsPath),
            $"The root Directory.Build.targets is missing ('{targetsPath}'). It carries the package "
            + "version assignment, the shared-library reference switch and the publish strip; this "
            + "guard's subject is gone, not merely renamed.");

        string[] assignments = XDocument.Load(targetsPath).Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => e.Value.Trim())
            .ToArray();

        Assert.AreNotEqual(0, assignments.Length,
            "The root Directory.Build.targets no longer assigns <PackageVersion>. Without it the "
            + "SDK's default applies and all eleven packages pack at 1.0.0 — pack still succeeds, "
            + "and a release would publish that version immutably. See plans/00001 work item 3.");

        Assert.IsTrue(
            assignments.Any(v => v.Contains("$(AutoPackageVersion)", StringComparison.Ordinal)),
            "The root Directory.Build.targets assigns <PackageVersion> from something other than "
            + "$(AutoPackageVersion) (it reads: " + string.Join(" / ", assignments) + "). That "
            + "property is the version AutoVersioning stamps into the assemblies; assigning "
            + "anything else reintroduces two sources of truth, and the packages and the DLLs "
            + "inside them can then disagree.");

        // The same mistake, stated where it would be made. A value here evaluates empty.
        XDocument srcProps = XDocument.Load(Path.Combine(repoRoot, "src", "Directory.Build.props"));

        Assert.IsFalse(
            srcProps.Descendants().Any(e => e.Name.LocalName == "PackageVersion"),
            "src/Directory.Build.props assigns <PackageVersion>. That file is imported BEFORE the "
            + "NuGet-generated props that define $(AutoPackageVersion), so the assignment "
            + "evaluates to the empty string and the packages fall back to 1.0.0 — silently. The "
            + "root Directory.Build.targets is the file that can read it.");
    }

    /// <summary>
    /// Every packable assembly carries the same three-part stamp, read from the DLLs themselves —
    /// in its <b>identity</b> as much as in its file version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>AssemblyVersion is asserted alongside FileVersion because it is the one a CONSUMER of
    /// these packages resolves against.</b> A library whose identity stays <c>1.0.0.0</c> while its
    /// package version moves is indistinguishable from every earlier build at load time, and a
    /// diagnostic that reports "which version is running" reports the wrong thing. Both come from
    /// AutoVersioning today; nothing but this says they have to.
    /// </para>
    /// <para>
    /// ⚠ <b>The identity assertion could NOT be canaried red on this repo, and that is worth
    /// knowing rather than glossing.</b> Two measured attempts: building a packable project with
    /// <c>-p:AssemblyVersion=3.3.3.0</c> produces the AutoVersioning stamp anyway — the generator
    /// writes the attribute and a hand-set property is ignored outright — and switching
    /// <c>GenerateAutoVersionedAssemblyInfo</c> off fails the build with
    /// <c>BAUTOVERSIONING00</c> rather than falling back to the SDK's own assembly info. So the
    /// skew is currently unreachable from MSBuild, and this guard's subject is a future change to
    /// that package, or its replacement, rather than a mistake someone can make today. What WAS
    /// canaried is the comparison itself: run against the third-party assemblies sitting in the
    /// same output directory it separates them correctly — <c>HarfBuzzSharp</c> alone carries
    /// identity <c>1.0.0</c> against file version <c>8.3.1</c>.
    /// </para>
    /// <para>
    /// ⓘ The fourth part is minute-resolution and deliberately not compared: two assemblies built
    /// by separate invocations either side of a minute differ there, which is a property of the
    /// stamp rather than a defect. The first three parts are what becomes the package version.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryPackableAssemblyCarriesTheSameThreePartStamp()
    {
        string repoRoot = FindRepoRoot();
        string[] packable = PackableProjectNames(repoRoot);

        Assert.AreNotEqual(0, packable.Length,
            "Found no packable project under src/, so this guard is measuring nothing. "
            + "PackageMetadataTests explains what <IsPackable> is doing here.");

        Dictionary<string, string> stamps = [];
        List<string> identityMismatches = [];
        List<string> missing = [];

        foreach (string name in packable)
        {
            string dll = Path.Combine(AppContext.BaseDirectory, name + ".dll");

            if (!File.Exists(dll))
            {
                missing.Add(name);
                continue;
            }

            string fileStamp = ThreePartStamp(FileVersionInfo.GetVersionInfo(dll).FileVersion);
            string identityStamp = ThreePartStamp(
                System.Reflection.AssemblyName.GetAssemblyName(dll).Version?.ToString());

            stamps[name] = fileStamp;

            if (!string.Equals(fileStamp, identityStamp, StringComparison.Ordinal))
            {
                identityMismatches.Add($"{name}: AssemblyVersion {identityStamp}, FileVersion {fileStamp}");
            }
        }

        Assert.AreEqual(0, missing.Count,
            "These packable assemblies are not in this test's output directory, so their stamp was "
            + "never read and this guard covers less than it claims. They arrive transitively "
            + "through the app reference; if one has been dropped from the graph, say so here "
            + "rather than letting the scan quietly shrink. Missing: " + string.Join(", ", missing));

        Assert.AreEqual(0, identityMismatches.Count,
            "These packable assemblies' IDENTITY does not match their file version, so the package "
            + "version would name something a consumer never binds to: the assembly loads under a "
            + "different version than the package it came from, and every build looks like the "
            + "same assembly. Both are AutoVersioning's to write — check that "
            + "GenerateAutoVersionedAssemblyInfo is still on and that nothing is setting "
            + "<AssemblyVersion> by hand. Offenders: " + string.Join("; ", identityMismatches));

        string[] distinct = stamps.Values.Distinct(StringComparer.Ordinal).ToArray();

        Assert.AreEqual(1, distinct.Length,
            "The packable assemblies do not agree on one version stamp, so the eleven packages "
            + "built from them would claim a version at least one of them does not carry: "
            + string.Join(", ", stamps.Select(kv => kv.Key + " = " + kv.Value)));

        Assert.AreNotEqual("1.0.0", distinct[0],
            "Every packable assembly is stamped 1.0.0, which is the SDK's default rather than a "
            + "computed version. Either AutoVersioning is no longer running "
            + "(GenerateAutoVersionedAssemblyInfo, root Directory.Build.props) or its stamp is "
            + "being overridden. A release at 1.0.0 cannot be un-published.");
    }

    /// <summary>
    /// Inside a packed feed: one version across all eleven, and every inter-package dependency
    /// naming it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>A mixed-version feed restores without complaint and is the failure worth catching.</b>
    /// If one package is packed at a stale version, a consumer resolving the set gets two copies of
    /// the shared libraries at different versions and the newer loses to the older in some graphs.
    /// Nothing fails; the app is simply built from code nobody chose.
    /// </para>
    /// <para>
    /// ⓘ <b>This is inconclusive rather than passing when there is no feed</b>, because there is no
    /// packed output in an ordinary test run and a green tick would claim a measurement that was
    /// never taken. <c>scripts/package-canary.ps1</c> produces one.
    /// </para>
    /// <para>
    /// ⛔ <b>A sibling is a package IN THIS FEED, not anything named <c>Bennewitz.Ninja.*</c>.</b>
    /// Since plans/00005 the packed AgentForge packages depend on
    /// <c>Bennewitz.Ninja.ScopedEditors.*</c> and <c>Bennewitz.Ninja.AppServices*</c>: the same prefix,
    /// other repositories, their own versions. Matching on the prefix reported those six
    /// dependencies as version drift and failed the package canary on the first CI run that could
    /// restore. The rule is unchanged for what this repository packs.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryPackageInTheLocalFeedNamesOneVersion()
    {
        string feed = Path.Combine(FindRepoRoot(), "artifacts", "localfeed");

        string[] packages = Directory.Exists(feed)
            ? Directory.EnumerateFiles(feed, PackagePrefix + "*.nupkg", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : [];

        if (packages.Length == 0)
        {
            Assert.Inconclusive(
                $"No packages under '{feed}', so the version agreement between them was not "
                + "measured. Produce them with: "
                + "pwsh -NoProfile -File scripts/package-canary.ps1 -PackOnly");
        }

        Dictionary<string, string> versions = [];
        List<string> mismatchedDependencies = [];
        var nuspecs = packages.Select(ReadNuspec).ToList();
        HashSet<string> siblings = new(nuspecs.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        int siblingDependencies = 0;

        foreach ((string id, string version, List<(string Id, string Version)> dependencies) in nuspecs)
        {
            versions[id] = version;

            foreach ((string depId, string depVersion) in dependencies)
            {
                if (!siblings.Contains(depId))
                {
                    continue;
                }

                siblingDependencies++;

                // NuGet writes a bare version as the lower bound of an inclusive-minimum range,
                // so a dependency pinned to 2026.3.914 reads back as exactly that string.
                if (!string.Equals(depVersion, version, StringComparison.OrdinalIgnoreCase))
                {
                    mismatchedDependencies.Add($"{id} {version} -> {depId} {depVersion}");
                }
            }
        }

        string[] distinct = versions.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.AreEqual(1, distinct.Length,
            "The local feed holds more than one version of the shared packages, so a consumer can "
            + "resolve a mixed set: "
            + string.Join(", ", versions.Select(kv => kv.Key + " = " + kv.Value))
            + ". The feed is packed in one shot at one version; delete it and re-pack.");

        // Premise: the packed set does depend on itself (AgentForge.Sdk on AgentForge.Core, and so
        // on). Zero would mean the sibling set or the nuspec reader broke, and the check below
        // would pass having compared nothing.
        Assert.IsTrue(siblingDependencies > 0,
            $"No package in '{feed}' depends on another package in it, so no version agreement "
            + "was checked. The nuspec reader or the sibling set is broken.");

        Assert.AreEqual(0, mismatchedDependencies.Count,
            "These packages depend on a sibling at a version other than their own, which is what "
            + "lets two copies of a shared library into one graph: "
            + string.Join("; ", mismatchedDependencies));
    }

    /// <summary>The id, version and dependencies of the nuspec inside a <c>.nupkg</c>.</summary>
    private static (string Id, string Version, List<(string Id, string Version)> Dependencies)
        ReadNuspec(string nupkgPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(nupkgPath);

        ZipArchiveEntry entry = archive.Entries
            .FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"'{Path.GetFileName(nupkgPath)}' contains no .nuspec, so it is not a package "
                + "this repo produced.");

        using Stream stream = entry.Open();
        XDocument doc = XDocument.Load(stream);

        XElement metadata = doc.Descendants().First(e => e.Name.LocalName == "metadata");

        string Value(string name) => metadata.Elements()
            .FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

        List<(string, string)> dependencies = doc.Descendants()
            .Where(e => e.Name.LocalName == "dependency")
            .Select(e => (
                e.Attribute("id")?.Value ?? string.Empty,
                e.Attribute("version")?.Value ?? string.Empty))
            .ToList();

        return (Value("id"), Value("version"), dependencies);
    }

    /// <summary>The first three parts of a four-part file version.</summary>
    private static string ThreePartStamp(string? fileVersion)
    {
        string[] parts = (fileVersion ?? string.Empty).Split('.');

        return parts.Length >= 3
            ? string.Join('.', parts[0], parts[1], parts[2])
            : fileVersion ?? "(none)";
    }

    /// <summary>
    /// The names of the packable projects, read off disk rather than listed here — the same rule
    /// <c>PackageMetadataTests</c> uses, and for the same reason.
    /// </summary>
    private static string[] PackableProjectNames(string repoRoot)
        => Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => XDocument.Load(f).Descendants()
                .Where(e => e.Name.LocalName == "IsPackable")
                .Select(e => e.Value.Trim())
                .LastOrDefault() == "true")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Matches <c>PackageMetadataTests.FindRepoRoot()</c>.</summary>
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
