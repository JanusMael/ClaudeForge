using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every severity glyph must take its size from the severity, not from a literal.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The size was hardcoded at NINE sites in seven files across two apps</b>, which is why
/// Critical and Caution were necessarily drawn at the same point size and the louder tier came
/// out smaller. Deleting the literals once fixes nothing on its own: the next surface to render a
/// severity will reach for <c>FontSize="11"</c> because that is what the surrounding markup does.
/// This test is what makes the new site do otherwise.
/// </para>
/// <para>
/// ⚠ <b>A malformed <c>ConverterParameter</c> is invisible at runtime by design.</b>
/// <c>AppSeverityToFontSizeConverter</c> falls back rather than throwing, because a converter that
/// throws inside a template takes the whole page down. The cost of that choice is that a typo
/// renders a plausible wrong size in silence, so the typo has to be caught HERE or not at all —
/// which is why the parameter is checked for being a number and not merely for being present.
/// </para>
/// <para>
/// Reads source TEXT rather than loading AXAML, for the same reason
/// <see cref="DangerSurfaceMarkupTests"/> does: the markup lives in several app assemblies and
/// this test project references only some of them, so a loader-based scan would silently skip
/// what it could not load.
/// </para>
/// </remarks>
[TestClass]
public sealed class SeverityGlyphFontSizeMarkupTests
{
    /// <summary>
    /// How many glyph-rendering elements must be found. A discovery scan that matches nothing
    /// passes vacuously, and the zero then gets quoted as evidence the surfaces are fine.
    /// </summary>
    /// <remarks>
    /// Nine when first written: two in each app's <c>PropertyEditorWrapper</c> (the row dot and
    /// the <c>IsDangerNow</c> banner), plus the nav badge, search hit, both effective-value grids,
    /// the save dialog and OpenCodeForge's nav badge. The shared wrapper's two now ship in the
    /// <c>Bennewitz.Ninja.ScopedEditors.AvaloniaUI</c> package (plans/00005).
    /// </remarks>
    // TWO-APP GUARD NARROWED — plans/00003 Phase 0. Was 9; one site was OpenCodeForge's.
    // Restore it when OpenCodeForge rejoins.
    // LIBRARY GUARD NARROWED — plans/00005. Was 8; the shared PropertyEditorWrapper's two sites
    // (the row dot and the IsDangerNow banner) moved to the Bennewitz.Ninja.ScopedEditors package,
    // where no markup scan checks them yet. Restore that coverage IN THAT REPOSITORY. See PROGRESS.md.
    private const int ExpectedGlyphSites = 6;

    /// <summary>
    /// A <c>TextBlock</c> whose <c>Text</c> comes from the severity glyph converter. Attributes
    /// never contain a bare <c>&gt;</c> — markup extensions and bindings do not produce one — so
    /// stopping at the first is safe, and <c>[^&gt;]</c> already spans the newlines these
    /// multi-line elements are written across.
    /// </summary>
    private static readonly Regex GlyphElement = new(
        @"<TextBlock\b[^>]*?SeverityToGlyph[^>]*?/>",
        RegexOptions.Compiled);

    private static readonly Regex SizeBinding = new(
        @"Converter=\{StaticResource\s+SeverityToFontSize\}\s*,\s*ConverterParameter\s*=\s*""?(?<base>[0-9]+(?:\.[0-9]+)?)""?",
        RegexOptions.Compiled);

    private static readonly Regex HardcodedSize = new(
        @"FontSize\s*=\s*""[0-9]", RegexOptions.Compiled);

    /// <summary>
    /// ⚠ The xmlns PREFIX differs per file — <c>libconv:</c> in ClaudeForge, <c>conv:</c> in the
    /// shared library and OpenCodeForge — so a literal prefix would match none of them and the
    /// check would pass over every file.
    /// </summary>
    private static readonly Regex Declaration = new(
        @"<\w+:AppSeverityToFontSizeConverter\s+x:Key\s*=\s*""SeverityToFontSize""",
        RegexOptions.Compiled);

    [TestMethod]
    public void EverySeverityGlyphSizesItselfFromTheSeverity()
    {
        string repoRoot = FindRepoRoot();
        List<string> problems = [];
        int sites = 0;

        foreach ((string relative, string text) in AxamlFiles(repoRoot))
        {
            foreach (Match element in GlyphElement.Matches(text))
            {
                sites++;

                if (HardcodedSize.IsMatch(element.Value))
                {
                    problems.Add(
                        $"{relative} renders a severity glyph at a literal FontSize. Critical and "
                        + "Caution then draw at the same point size, and ⊗ is 20% shorter than ⚠ "
                        + "at equal points, so the loudest tier comes out the smallest.");
                    continue;
                }

                Match size = SizeBinding.Match(element.Value);
                if (!size.Success)
                {
                    problems.Add(
                        $"{relative} renders a severity glyph without binding SeverityToFontSize "
                        + "with a numeric ConverterParameter. The converter falls back silently "
                        + "on a bad parameter, so nothing else will tell you.");
                }
            }
        }

        Assert.AreEqual(ExpectedGlyphSites, sites,
            $"found {sites} severity-glyph element(s), expected {ExpectedGlyphSites}. Either the "
            + "discovery pattern has stopped matching — in which case this test is no longer "
            + "checking anything — or a surface was added or removed and the count needs a "
            + "deliberate update.");

        Assert.IsTrue(problems.Count == 0,
            $"{problems.Count} severity glyph(s) do not size themselves:\n  "
            + string.Join("\n  ", problems));
    }

    /// <summary>
    /// A binding to a resource key the file never declares compiles and then resolves to nothing
    /// at runtime, leaving the glyph at its inherited size — the defect back, with the markup
    /// looking fixed.
    /// </summary>
    [TestMethod]
    public void EveryFileUsingTheSizeConverterAlsoDeclaresIt()
    {
        string repoRoot = FindRepoRoot();
        List<string> missing = [];
        int users = 0;

        foreach ((string relative, string text) in AxamlFiles(repoRoot))
        {
            if (!text.Contains("StaticResource SeverityToFontSize", StringComparison.Ordinal))
            {
                continue;
            }

            users++;
            if (!Declaration.IsMatch(text))
            {
                missing.Add(relative);
            }
        }

        // TWO-APP GUARD NARROWED — plans/00003 Phase 0. Was 7; one of the seven was
        // OpenCodeForge's. Restore it when OpenCodeForge rejoins.
        // LIBRARY GUARD NARROWED — plans/00005. Was 6; the shared PropertyEditorWrapper moved to the
        // Bennewitz.Ninja.ScopedEditors package. Restore that coverage IN THAT REPOSITORY.
        Assert.IsTrue(users >= 5,
            $"only {users} file(s) use SeverityToFontSize; five render a severity glyph, so the "
            + "scan has lost its subjects and would pass without checking anything");

        Assert.IsTrue(missing.Count == 0,
            "these files bind SeverityToFontSize without declaring it, so the binding resolves to "
            + $"nothing and the glyph keeps its inherited size:\n  {string.Join("\n  ", missing)}");
    }

    private static IEnumerable<(string Relative, string Text)> AxamlFiles(string repoRoot)
    {
        foreach (string path in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            // Build output can hold copies of the markup; scanning those would double-count and
            // could vouch for a stale copy after the real file regressed.
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (
                Path.GetRelativePath(repoRoot, path).Replace('\\', '/'),
                File.ReadAllText(path));
        }
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
