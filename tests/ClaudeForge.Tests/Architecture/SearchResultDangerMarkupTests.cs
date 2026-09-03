using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every surface that renders search hits must render their severity dot.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The view-model half and the markup half never reference each other.</b>
/// <c>SearchResultViewModel.Danger</c> can be perfectly wired — and asserted green by
/// <c>SearchResultDangerTests</c> and <c>SearchDangerAgreementTests</c> — while a template simply
/// does not bind it. The result is a Critical knob rendering as an ordinary row, and nothing goes
/// red. This is the same two-independent-halves shape as a factory arm without a matching
/// <c>App.axaml</c> DataTemplate, and it gets the same treatment: read the markup.
/// </para>
/// <para>
/// ⭐ <b>The template files are DISCOVERED, not listed.</b> A hardcoded pair would keep passing
/// after a third surface (or a second window) started rendering hits — and it is the newest
/// surface, written by whoever had not read this file, that is most likely to omit the dot. The
/// discovery key is the <c>x:DataType</c> every such template must declare.
/// </para>
/// <para>
/// Reads source TEXT rather than loading AXAML, matching <c>ItemsSourceBoundTabsTests</c>: the
/// markup lives in two different app assemblies and this test project references only one of
/// them, so a reflection- or loader-based scan would silently skip what it could not load.
/// </para>
/// </remarks>
[TestClass]
public sealed class SearchResultDangerMarkupTests
{
    /// <summary>
    /// What a template must bind. Both, not either: the glyph without the accessible text is a
    /// sighted-only signal, and the accessible text without the glyph is invisible.
    /// </summary>
    private static readonly string[] RequiredBindings = ["HasDangerSeverity", "DangerAccessibleText"];

    [TestMethod]
    public void EverySurfaceThatRendersSearchHitsRendersTheirSeverity()
    {
        string repoRoot = FindRepoRoot();
        string src = Path.Combine(repoRoot, "src");

        List<(string File, string Missing)> gaps = [];
        List<string> scanned = [];

        foreach (string path in Directory.EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories))
        {
            // Build output can contain copies of the markup; scanning those would double-count and
            // could vouch for a stale copy after the real file regressed.
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(path);

            // The DataTemplate's declared item type is the only reliable marker: a file that binds
            // SearchResultViewModel members without declaring the type would not compile under
            // compiled bindings.
            if (!Regex.IsMatch(text, @"x:DataType\s*=\s*""[^""]*:SearchResultViewModel"""))
            {
                continue;
            }

            string relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            scanned.Add(relative);

            string[] missing = RequiredBindings
                .Where(binding => !text.Contains(binding, StringComparison.Ordinal))
                .ToArray();

            if (missing.Length > 0)
            {
                gaps.Add((relative, string.Join(", ", missing)));
            }
        }

        // A discovery scan that finds nothing passes vacuously, which is worse than no test: the
        // zero gets quoted as evidence the surfaces are fine.
        Assert.IsTrue(scanned.Count >= 2,
            $"only {scanned.Count} search-result template(s) found ({string.Join(", ", scanned)}). "
            + "Both apps render search hits, so the discovery regex has stopped matching and this "
            + "test is no longer checking anything.");

        Assert.IsTrue(gaps.Count == 0,
            $"{gaps.Count} search-result template(s) render hits without their severity:\n  "
            + string.Join("\n  ", gaps.Select(g => $"{g.File} — missing {g.Missing}"))
            + "\n\nA hit with no dot tells the user the knob is unremarkable. Bind "
            + "HasDangerSeverity (visibility) and DangerAccessibleText (HelpText + tooltip), as "
            + "PropertyEditorWrapper.axaml does for the settings row.");
    }

    /// <summary>
    /// ⛔ <c>AutomationProperties.Name</c> is IGNORED on a <c>TextBlock</c> — the <c>Text</c>
    /// always wins — so a dot annotated that way announces the glyph character and nothing else.
    /// Measured via UIA; see <c>docs/AVALONIA-GOTCHAS.md</c>.
    /// </summary>
    [TestMethod]
    public void TheSeverityGlyphIsAnnotatedWithHelpTextNotName()
    {
        string repoRoot = FindRepoRoot();
        List<string> offenders = [];
        int checkedFiles = 0;

        foreach (string path in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(path);
            if (!text.Contains("DangerAccessibleText", StringComparison.Ordinal))
            {
                continue;
            }

            checkedFiles++;
            if (Regex.IsMatch(text, @"AutomationProperties\.Name\s*=\s*""\{Binding DangerAccessibleText\}"""))
            {
                offenders.Add(Path.GetRelativePath(repoRoot, path).Replace('\\', '/'));
            }
        }

        Assert.IsTrue(checkedFiles >= 2,
            $"only {checkedFiles} file(s) mention DangerAccessibleText; the scan has lost its "
            + "subjects and would pass without checking anything.");

        Assert.IsTrue(offenders.Count == 0,
            "AutomationProperties.Name is ignored on a TextBlock (its Text wins), so these "
            + $"annotations announce nothing:\n  {string.Join("\n  ", offenders)}\n\n"
            + "Use AutomationProperties.HelpText. Do not wrap the glyph to work around it — a "
            + "Border and a ContentControl both get no automation peer at all.");
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "could not locate the repository root (ClaudeForge.slnx)");
        return dir.FullName;
    }
}
