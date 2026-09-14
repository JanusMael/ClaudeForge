using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// The solution's friend grants live in one shared, linked file — every project links it, and no
/// project declares a grant of its own.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The failure this closes is drift back, one project at a time.</b> Adding
/// <c>&lt;InternalsVisibleTo Include="X"/&gt;</c> to a single csproj works, builds clean, and
/// quietly re-creates the state the shared file replaced: a grant set that can only be known by
/// reading every project. Nothing else would notice.
/// </para>
/// <para>
/// ⚠ <b>Two spellings both count.</b> Before the consolidation this repo used the SDK item in
/// most projects and the raw <c>&lt;AssemblyAttribute Include="…InternalsVisibleTo"&gt;</c> in
/// three. A guard that knew only the first would have called those three clean.
/// </para>
/// <para>
/// ⓘ <b>A wrong link PATH cannot reach this guard</b>, and does not need to: a
/// <c>&lt;Compile&gt;</c> item naming a file that does not exist fails the build outright with
/// CS2001. What the build cannot catch is a project that links nothing at all, which is why the
/// first test below asserts presence rather than correctness.
/// </para>
/// </remarks>
[TestClass]
public sealed class SharedFriendGrantsTests
{
    private const string SharedFileName = "AssemblyInfo.InternalsVisibleTo.cs";

    /// <summary>
    /// Relative, forward-slashed, and identical in every project — the form that resolves the
    /// same way on Windows, Linux and macOS.
    /// </summary>
    private const string ExpectedInclude = "../../" + SharedFileName;

    [TestMethod]
    public void TheSharedGrantFile_SitsBesideTheSolution()
    {
        string repoRoot = FindRepoRoot();

        Assert.IsTrue(
            File.Exists(Path.Combine(repoRoot, SharedFileName)),
            $"Expected {SharedFileName} at the repo root, beside ClaudeForge.slnx. Every project "
            + "links it by a relative path from there; moving it means updating all of them.");

        // The premise. A file that exists but grants nothing would leave every test below
        // passing over a set of internals no longer visible to anyone.
        string text = File.ReadAllText(Path.Combine(repoRoot, SharedFileName));
        Assert.IsTrue(
            text.Contains("[assembly: InternalsVisibleTo(", StringComparison.Ordinal),
            $"{SharedFileName} declares no InternalsVisibleTo attributes at all.");
    }

    [TestMethod]
    public void EveryProject_LinksTheSharedGrantFile()
    {
        string repoRoot = FindRepoRoot();
        List<string> failures = [];

        foreach (string path in EnumerateProjects(repoRoot))
        {
            bool links = XDocument.Load(path)
                .Descendants()
                .Where(e => e.Name.LocalName == "Compile")
                .Select(e => e.Attribute("Include")?.Value?.Replace('\\', '/'))
                .Any(v => v is not null && v.EndsWith(SharedFileName, StringComparison.Ordinal));

            if (!links)
            {
                failures.Add($"  • {Rel(repoRoot, path)}");
            }
        }

        Assert.AreEqual(
            0,
            failures.Count,
            $"These projects do not link {SharedFileName}, so their internals are visible to "
            + "nobody and the shared list does not describe them:\n"
            + string.Join("\n", failures));
    }

    [TestMethod]
    public void EveryProject_LinksItByARelativeForwardSlashedPath()
    {
        string repoRoot = FindRepoRoot();
        List<string> failures = [];

        foreach (string path in EnumerateProjects(repoRoot))
        {
            foreach (XElement compile in XDocument.Load(path)
                         .Descendants()
                         .Where(e => e.Name.LocalName == "Compile"))
            {
                string? include = compile.Attribute("Include")?.Value;
                if (include is null || !include.Replace('\\', '/').EndsWith(SharedFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(include, ExpectedInclude, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"  • {Rel(repoRoot, path)} links it as '{include}'. Expected exactly "
                        + $"'{ExpectedInclude}': a backslash breaks the Linux and macOS builds "
                        + "this repo publishes for, and an absolute path breaks every clone but "
                        + "the one it was written on.");
                }
            }
        }

        Assert.AreEqual(0, failures.Count, "Link paths must be portable:\n" + string.Join("\n", failures));
    }

    [TestMethod]
    public void NoProject_DeclaresItsOwnFriendGrant()
    {
        string repoRoot = FindRepoRoot();
        List<string> failures = [];

        foreach (string path in EnumerateProjects(repoRoot))
        {
            XDocument doc = XDocument.Load(path);

            foreach (XElement el in doc.Descendants())
            {
                string tag = el.Name.LocalName;

                // Spelling 1: the SDK item.
                if (tag == "InternalsVisibleTo")
                {
                    failures.Add(
                        $"  • {Rel(repoRoot, path)} declares <InternalsVisibleTo "
                        + $"Include=\"{el.Attribute("Include")?.Value}\"/>.");
                }

                // Spelling 2: the raw assembly attribute. Three projects used this form, and a
                // guard that only knew spelling 1 would have reported them clean.
                if (tag == "AssemblyAttribute"
                    && (el.Attribute("Include")?.Value ?? string.Empty)
                        .EndsWith("InternalsVisibleTo", StringComparison.Ordinal))
                {
                    string target = el.Elements()
                        .FirstOrDefault(c => c.Name.LocalName == "_Parameter1")?.Value ?? "?";
                    failures.Add(
                        $"  • {Rel(repoRoot, path)} declares a raw InternalsVisibleTo "
                        + $"AssemblyAttribute for '{target}'.");
                }
            }
        }

        Assert.AreEqual(
            0,
            failures.Count,
            $"Friend grants belong in {SharedFileName}, not in individual projects — a per-project "
            + "grant re-creates the scattered set the shared file replaced, and builds clean while "
            + "doing it:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<string> EnumerateProjects(string repoRoot)
        => new[] { "src", "tests" }
            .SelectMany(d => Directory.EnumerateFiles(
                Path.Combine(repoRoot, d), "*.csproj", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    private static string Rel(string repoRoot, string path)
        => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');

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
