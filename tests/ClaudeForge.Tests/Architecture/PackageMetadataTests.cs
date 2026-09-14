using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every project under <c>src/</c> says for itself whether it is packaged, and the shared
/// metadata that names those packages is present and derived rather than listed.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This deliberately does NOT assert "the packable set is exactly these eleven".</b> Such a
/// test needs a list of the eleven, which is the copy-of-the-truth this repo's guards exist to
/// avoid: the list and the csproj files drift, and the test then defends the copy. Asserting that
/// nothing <i>defaults</i> is the non-vacuous form — a new project cannot be published, or
/// skipped, by being forgotten, because it cannot be added at all without answering the question.
/// </para>
/// <para>
/// ⚠ <b>Silence is the failure mode being closed.</b> <c>IsPackable</c> defaults to
/// <c>true</c> for a library, so a product-specific half added under <c>src/</c> would be packed
/// and pushed to the feed by a release that was never told about it — and GitHub Packages refuses
/// to re-push a version, so the mistake is immutable. Nothing would have failed on the way there.
/// </para>
/// <para>
/// See <c>plans/00001-shared-libraries-as-private-nuget-packages.md</c>, work item 2.
/// </para>
/// </remarks>
[TestClass]
public sealed class PackageMetadataTests
{
    [TestMethod]
    public void EverySrcProjectStatesIsPackableExplicitly()
    {
        string repoRoot = FindRepoRoot();
        XDocument srcProps = LoadSrcProps(repoRoot);

        // The premise. IsPackable=true is only meaningful while the shared block supplies the
        // identity those packages are published under; without it eleven packages would pack
        // under their bare assembly names with no repository, licence or readme.
        Assert.IsTrue(
            Declares(srcProps, "PackageId"),
            "src/Directory.Build.props no longer declares <PackageId>. That block is what gives "
            + "every packable project its identity, licence, repository and readme, so this "
            + "guard's premise is gone: re-read it before deleting it.");

        string readme = Path.Combine(repoRoot, "src", "PACKAGE-README.md");
        Assert.IsTrue(File.Exists(readme),
            "src/PACKAGE-README.md is missing. src/Directory.Build.props sets "
            + "<PackageReadmeFile>README.md</PackageReadmeFile> and packs this file for every "
            + "project that has no README.md of its own, so pack would fail with NU5039 for "
            + "ten of the eleven packages.");

        List<string> offenders = [];
        int scanned = 0;

        foreach (string csproj in EnumerateSrcProjects(repoRoot))
        {
            scanned++;

            string? value = XDocument.Load(csproj).Descendants()
                .Where(e => e.Name.LocalName == "IsPackable")
                .Select(e => e.Value.Trim())
                .LastOrDefault();

            if (value is not "true" and not "false")
            {
                offenders.Add($"{Path.GetRelativePath(repoRoot, csproj)} ({value ?? "absent"})");
            }
        }

        Assert.IsTrue(scanned > 0,
            "Scanned no project files under src/; the scan has been narrowed to nothing.");

        Assert.AreEqual(0, offenders.Count,
            "These projects under src/ do not state <IsPackable>true</IsPackable> or "
            + "<IsPackable>false</IsPackable>. A library that says nothing DEFAULTS to packable, "
            + "so it would be pushed to the private feed by the next release and could never be "
            + "un-pushed. Say which it is, in the csproj itself, next to <OutputType>. "
            + "Offenders: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The package id is built from the csproj file name, so a project whose
    /// <c>AssemblyName</c> diverges from it would ship under the wrong id.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>src/Directory.Build.props</c> cannot read <c>$(AssemblyName)</c> — it is imported at
    /// the top of every csproj, before that property is assigned, so it would evaluate empty and
    /// all eleven packages would collide on the id <c>Bennewitz.Ninja.</c>. The file explains why
    /// a <c>src/Directory.Build.targets</c>, which could read it, is the worse option. This test
    /// is what makes <c>$(MSBuildProjectName)</c> safe to use in its place.
    /// </remarks>
    [TestMethod]
    public void EverySrcProjectsAssemblyNameMatchesItsFileName()
    {
        string repoRoot = FindRepoRoot();

        // The premise, stated the same way round as the test above: if the id stops being
        // derived from the file name, this test is measuring nothing and should say so.
        string packageId = LoadSrcProps(repoRoot).Descendants()
            .Where(e => e.Name.LocalName == "PackageId")
            .Select(e => e.Value.Trim())
            .LastOrDefault() ?? string.Empty;

        Assert.IsTrue(
            packageId.Contains("$(MSBuildProjectName)", StringComparison.Ordinal),
            "src/Directory.Build.props no longer derives <PackageId> from $(MSBuildProjectName) "
            + $"(it reads '{packageId}'). The file-name/assembly-name agreement below only "
            + "matters because of that derivation: re-read both before deleting this test.");

        List<string> offenders = [];

        foreach (string csproj in EnumerateSrcProjects(repoRoot))
        {
            string? assemblyName = XDocument.Load(csproj).Descendants()
                .Where(e => e.Name.LocalName == "AssemblyName")
                .Select(e => e.Value.Trim())
                .LastOrDefault();

            // Not declaring one is fine — the SDK defaults it to the file name, which is
            // exactly what the package id uses.
            if (string.IsNullOrEmpty(assemblyName))
            {
                continue;
            }

            string fileName = Path.GetFileNameWithoutExtension(csproj);
            if (!string.Equals(assemblyName, fileName, StringComparison.Ordinal))
            {
                offenders.Add($"{Path.GetRelativePath(repoRoot, csproj)} → '{assemblyName}'");
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "These projects' <AssemblyName> differs from their csproj file name, so the package "
            + "id derived from the file name would not name the assembly inside it. Rename the "
            + "file to match, or give that project an explicit <PackageId>. Offenders: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Every project that packs says what it is, in its own words.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>The SDK supplies a default, and the default is the string "Package Description".</b>
    /// So a package with nothing to say does not fail to pack — it publishes that sentence onto
    /// its feed page, immutably, which is what <c>AgentForge.Core</c> and <c>AgentForge.Sdk</c>
    /// were about to do. Silence looks like an answer here, exactly as it does for
    /// <c>IsPackable</c> above.
    /// </remarks>
    [TestMethod]
    public void EveryPackableProjectDescribesItself()
    {
        string repoRoot = FindRepoRoot();
        List<string> offenders = [];
        int packable = 0;

        foreach (string csproj in EnumerateSrcProjects(repoRoot))
        {
            XDocument doc = XDocument.Load(csproj);

            bool isPackable = doc.Descendants()
                .Where(e => e.Name.LocalName == "IsPackable")
                .Select(e => e.Value.Trim())
                .LastOrDefault() == "true";

            if (!isPackable)
            {
                continue;
            }

            packable++;

            string description = doc.Descendants()
                .Where(e => e.Name.LocalName == "Description")
                .Select(e => e.Value.Trim())
                .LastOrDefault() ?? string.Empty;

            if (description.Length == 0
                || description.Equals("Package Description", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(Path.GetRelativePath(repoRoot, csproj));
            }
        }

        Assert.IsTrue(packable > 0,
            "Found no packable project under src/, so this guard is measuring nothing.");

        Assert.AreEqual(0, offenders.Count,
            "These packable projects have no <Description>, so the SDK supplies its placeholder "
            + "and the package ships with the literal text \"Package Description\" on its feed "
            + "page — a published version cannot be replaced. Offenders: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The name prefix the package-mode reference switch matches on selects exactly the set of
    /// projects that are actually packaged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>This is the premise of the whole switch.</b> The root <c>Directory.Build.targets</c>
    /// rewrites a <c>ProjectReference</c> into a <c>PackageReference</c> when the referenced
    /// project's file name begins <c>AgentForge.</c> or <c>LayeredEditors.</c>. It matches on a
    /// name because MSBuild cannot read the referenced project's <c>IsPackable</c> from there —
    /// so the name and the packability have to agree, and this is what makes them.
    /// </para>
    /// <para>
    /// ⛔ <b>Both directions fail silently, which is why both are asserted.</b> A packable project
    /// named outside the prefixes keeps its <c>ProjectReference</c> in package mode: the canary
    /// then builds the app against project output while claiming to validate packages. A
    /// prefix-named project that is <i>not</i> packable is rewritten to a
    /// <c>PackageReference</c> for a package nobody publishes, which at least fails loudly at
    /// restore — but only once someone runs the canary.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheSwitchesNamePrefixSelectsExactlyThePackableProjects()
    {
        string repoRoot = FindRepoRoot();

        // The same selectors the switch in Directory.Build.targets uses. Kept in sync by this
        // test failing, which is the point: there is no third place that lists the eleven.
        //
        // ⚠ JsonC is named outright rather than matched by a family prefix, because it IS a
        // family of one — a general-purpose JSONC reader, renamed out of AgentForge before
        // first publish so the id would not claim agent knowledge it does not have. An
        // exception in a rule-based selector is a smell; it earns its place only because the
        // assertion below is two-directional, so the exception cannot rot unnoticed.
        string[] prefixes = ["AgentForge.", "LayeredEditors."];
        string[] exactNames = ["JsonC"];

        List<string> byName = [];
        List<string> byPackability = [];

        foreach (string csproj in EnumerateSrcProjects(repoRoot))
        {
            string name = Path.GetFileNameWithoutExtension(csproj);

            if (prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))
                || exactNames.Contains(name, StringComparer.Ordinal))
            {
                byName.Add(name);
            }

            bool isPackable = XDocument.Load(csproj).Descendants()
                .Where(e => e.Name.LocalName == "IsPackable")
                .Select(e => e.Value.Trim())
                .LastOrDefault() == "true";

            if (isPackable)
            {
                byPackability.Add(name);
            }
        }

        byName.Sort(StringComparer.Ordinal);
        byPackability.Sort(StringComparer.Ordinal);

        Assert.IsTrue(byPackability.Count > 0,
            "No packable project under src/, so this guard is measuring nothing.");

        string[] packableButNotPrefixed = byPackability.Except(byName, StringComparer.Ordinal).ToArray();
        string[] prefixedButNotPackable = byName.Except(byPackability, StringComparer.Ordinal).ToArray();

        Assert.AreEqual(0, packableButNotPrefixed.Length,
            "These projects are packaged but the reference switch's selector does not match them "
            + "(prefixes: " + string.Join(", ", prefixes) + "; exact: " + string.Join(", ", exactNames)
            + "), so it will NOT rewrite references to them. In package mode they stay "
            + "ProjectReferences and the canary silently validates project output instead of the "
            + "package. Rename the project, or teach the switch another selector — and this test "
            + "with it. Offenders: "
            + string.Join(", ", packableButNotPrefixed));

        Assert.AreEqual(0, prefixedButNotPackable.Length,
            "These projects carry a shared-library name prefix but are not packaged, so in "
            + "package mode the switch rewrites references to them into PackageReferences for "
            + "packages that are never published, and restore fails. Either mark them packable "
            + "or move them out of the AgentForge.*/LayeredEditors.* namespace. Offenders: "
            + string.Join(", ", prefixedButNotPackable));
    }

    private static XDocument LoadSrcProps(string repoRoot)
        => XDocument.Load(Path.Combine(repoRoot, "src", "Directory.Build.props"));

    private static bool Declares(XDocument doc, string property)
        => doc.Descendants().Any(e => e.Name.LocalName == property && !string.IsNullOrWhiteSpace(e.Value));

    /// <summary>Every <c>.csproj</c> under <c>src/</c>, excluding build output.</summary>
    private static IEnumerable<string> EnumerateSrcProjects(string repoRoot)
        => Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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
