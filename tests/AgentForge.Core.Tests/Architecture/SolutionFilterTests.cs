using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Architecture;

/// <summary>
/// Asserts that the two scoped solution filters still describe the partition they claim to:
/// shared projects plus exactly one product's.
/// </summary>
/// <remarks>
/// <para>
/// <c>ClaudeForge.slnx</c> builds everything and is what CI builds. The filters beside it are
/// focused views — <c>ClaudeForge.Only.slnf</c> and <c>OpenCodeForge.Only.slnf</c> — so that
/// working on one product does not build and test the other.
/// </para>
/// <para>
/// MSBuild already enforces the direction that matters most: a filter naming a project absent
/// from the parent solution is a hard <c>MSB4025</c> error, so a project cannot hide in a
/// focused view while being missing from the solution CI builds. What MSBuild does <b>not</b>
/// notice is the opposite drift — a new project added to the parent and forgotten in the
/// filters, which then silently never builds in either focused view — or a product project
/// leaking into the wrong one, which quietly defeats the whole point of the split.
/// </para>
/// <para>
/// This lives in a <b>shared</b> test project, unlike the other architecture guards in
/// <c>ClaudeForge.Tests</c>, precisely so it runs inside <i>both</i> filters. A partition guard
/// that only ran in one half could not tell the other half it was broken.
/// </para>
/// </remarks>
[TestClass]
public class SolutionFilterTests
{
    private const string ParentSolution = "ClaudeForge.slnx";

    private static readonly Regex ProjectPathRegex = new(
        @"<Project\s+[\s\S]*?Path=""(?<path>[^""]+)""",
        RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, ParentSolution)))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not find {ParentSolution} by walking up from '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// Which product a project belongs to, by assembly-name prefix — the same convention
    /// <c>AssemblyLayeringTests</c> uses, so a new product assembly is classified the moment
    /// it is named rather than when someone remembers to register it.
    /// </summary>
    private static string ProductOf(string projectPath)
    {
        string name = Path.GetFileNameWithoutExtension(projectPath);
        if (name.StartsWith("ClaudeForge", StringComparison.Ordinal))
        {
            return "claude";
        }

        return name.StartsWith("OpenCode", StringComparison.Ordinal) ? "opencode" : "shared";
    }

    private static IReadOnlyList<string> ParentProjects(string repoRoot)
    {
        string xml = File.ReadAllText(Path.Combine(repoRoot, ParentSolution));
        return [.. ProjectPathRegex.Matches(xml).Select(m => m.Groups["path"].Value)];
    }

    private static IReadOnlyList<string> FilterProjects(string repoRoot, string filterFile)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(repoRoot, filterFile)))
            ?? throw new InvalidOperationException($"{filterFile} is empty.");

        Assert.AreEqual(
            ParentSolution,
            (string?)root["solution"]?["path"],
            $"{filterFile} must filter {ParentSolution}. Pointing it elsewhere silently "
            + "changes which solution CI's project set is validated against.");

        JsonArray projects = root["solution"]?["projects"]?.AsArray()
            ?? throw new InvalidOperationException($"{filterFile} declares no projects.");

        return [.. projects.Select(p => (string)p!)];
    }

    [TestMethod]
    public void BothFiltersExist_SoTheseTestsAreNotVacuous()
    {
        string repoRoot = FindRepoRoot();

        Assert.IsTrue(File.Exists(Path.Combine(repoRoot, "ClaudeForge.Only.slnf")));
        Assert.IsTrue(File.Exists(Path.Combine(repoRoot, "OpenCodeForge.Only.slnf")));
        Assert.IsTrue(ParentProjects(repoRoot).Count > 1,
            "The parent solution parsed as one project or none, so the regex no longer "
            + "matches how it is written and every assertion below is meaningless.");
    }

    /// <summary>
    /// Each filter is exactly the shared projects plus its own product's — no more, no less.
    /// </summary>
    /// <remarks>
    /// The failure this catches in practice: a project added to <see cref="ParentSolution"/>
    /// and not to the filters. It then builds in CI and never in either focused view, so a
    /// developer working in a filter gets a green run that proves less than they think.
    /// </remarks>
    [TestMethod]
    [DataRow("ClaudeForge.Only.slnf", "claude")]
    [DataRow("OpenCodeForge.Only.slnf", "opencode")]
    public void FilterIsSharedPlusExactlyOneProduct(string filterFile, string product)
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> parent = ParentProjects(repoRoot);

        string[] expected = [.. parent
            .Where(p => ProductOf(p) is "shared" || ProductOf(p) == product)
            .Order(StringComparer.Ordinal)];

        string[] actual = [.. FilterProjects(repoRoot, filterFile).Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(
            expected,
            actual,
            $"{filterFile} no longer matches 'shared + {product}'.\n"
            + $"  missing from the filter: {string.Join(", ", expected.Except(actual))}\n"
            + $"  unexpectedly present:    {string.Join(", ", actual.Except(expected))}");
    }

    /// <summary>
    /// Neither filter contains the other product. Stated separately from the equality check
    /// above because this is the specific mistake that makes the split pointless, and a
    /// message naming the leaked project is worth more than a set diff.
    /// </summary>
    [TestMethod]
    [DataRow("ClaudeForge.Only.slnf", "opencode")]
    [DataRow("OpenCodeForge.Only.slnf", "claude")]
    public void FilterExcludesTheOtherProduct(string filterFile, string forbidden)
    {
        string repoRoot = FindRepoRoot();

        string[] leaked = [.. FilterProjects(repoRoot, filterFile)
            .Where(p => ProductOf(p) == forbidden)];

        Assert.AreEqual(
            0,
            leaked.Length,
            $"{filterFile} pulls in {forbidden} project(s): {string.Join(", ", leaked)}. "
            + "The point of a focused view is not building the other product.");
    }

    /// <summary>
    /// Every product project appears in exactly one filter, so a new one cannot escape both
    /// and go unbuilt in every focused view.
    /// </summary>
    [TestMethod]
    public void EveryProductProjectIsInExactlyOneFilter()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> claude = FilterProjects(repoRoot, "ClaudeForge.Only.slnf");
        IReadOnlyList<string> openCode = FilterProjects(repoRoot, "OpenCodeForge.Only.slnf");

        foreach (string project in ParentProjects(repoRoot).Where(p => ProductOf(p) != "shared"))
        {
            int count = (claude.Contains(project) ? 1 : 0) + (openCode.Contains(project) ? 1 : 0);
            Assert.AreEqual(1, count,
                $"'{project}' appears in {count} filter(s); it must appear in exactly one.");
        }
    }
}
