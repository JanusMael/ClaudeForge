using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every surface that renders a danger-annotated view-model must render its severity.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The view-model half and the markup half never reference each other.</b> The classifier
/// can be perfectly wired — and asserted green by the table, wiring and search tests — while a
/// template simply does not bind it. The result is a Critical setting rendering as an ordinary
/// row, and nothing goes red. This is the same two-independent-halves shape as a factory arm
/// without a matching <c>App.axaml</c> DataTemplate, and it gets the same treatment: read the
/// markup.
/// </para>
/// <para>
/// ⚠ <b>There are FOUR such files, not two, because both apps keep their own copy of the property
/// wrapper.</b> OpenCodeForge renders the shared
/// <c>LayeredEditors.Avalonia/Controls/PropertyEditorWrapper.axaml</c>; ClaudeForge has its own
/// under <c>src/ClaudeForge/Controls/</c>. A "shared library" fix does not reach both apps, and
/// that asymmetry has already produced one live defect in this area — which is why the surfaces
/// here are DISCOVERED by their <c>x:DataType</c> rather than listed.
/// </para>
/// <para>
/// Reads source TEXT rather than loading AXAML, matching <c>ItemsSourceBoundTabsTests</c>: the
/// markup lives in several app assemblies and this test project references only some of them, so
/// a reflection- or loader-based scan would silently skip what it could not load.
/// </para>
/// </remarks>
[TestClass]
public sealed class DangerSurfaceMarkupTests
{
    /// <summary>
    /// A kind of danger surface: how to recognise its templates, and how many must exist.
    /// </summary>
    /// <param name="DataType">
    /// The <c>x:DataType</c> suffix every such template declares. This is the discovery key
    /// because compiled bindings require it — a file binding these members without declaring the
    /// type would not compile.
    /// </param>
    /// <param name="Minimum">
    /// How many files must be found. A discovery scan that matches nothing passes vacuously,
    /// which is worse than no test: the zero gets quoted as evidence the surfaces are fine.
    /// </param>
    /// <param name="DelegatesTo">
    /// A control that already renders this surface's severity. A template whose job is to HOST
    /// that control satisfies the requirement by composition rather than by binding the members
    /// itself — <c>SettingsPageHost</c> and <c>GroupPropertiesView</c> declare the item type and
    /// then hand the row to <c>PropertyEditorWrapper</c>.
    /// <para>
    /// ⚠ <b>This is an escape hatch, and it is sound only because the delegate target is itself
    /// under guard here</b> (the minimum count below, plus
    /// <see cref="BothPropertyWrappersRenderTheIsDangerNowBanner"/>). A previous guard in this
    /// repo shipped a hatch resting on an unverified belief and would have vouched for a
    /// navigation tree that announced nothing — a hatch needs the thing it defers to to be
    /// checked, not assumed.
    /// </para>
    /// </param>
    private sealed record Surface(string DataType, int Minimum, string Description, string? DelegatesTo = null);

    private static readonly Surface[] Surfaces =
    [
        new("PropertyEditorViewModel", 2,
            "the settings row (both apps keep their own PropertyEditorWrapper)",
            DelegatesTo: "PropertyEditorWrapper"),
        new("SearchResultViewModel", 2, "a search hit (both apps render results)"),
        // ⚠ ClaudeForge renders effective values TWICE — the group editor's Effective tab and the
        // standalone Effective Settings page — from the same row type through two unrelated
        // producers. OpenCodeForge draws no tab strip and hosts no such page, so both files are
        // ClaudeForge's; the minimum is 2 because a fix applied to one of them is the exact
        // failure this discovery-driven scan exists to catch.
        new("EffectivePropertyRow", 2,
            "an effective-value row (the group's Effective tab and the standalone page)"),
    ];

    /// <summary>
    /// What a template must bind. Both, not either: the glyph without the accessible text is a
    /// sighted-only signal, and the accessible text without the glyph is invisible.
    /// </summary>
    private static readonly string[] RequiredBindings = ["HasDangerSeverity", "DangerAccessibleText"];

    [TestMethod]
    public void EveryDangerSurfaceRendersItsSeverity()
    {
        string repoRoot = FindRepoRoot();
        List<string> problems = [];

        foreach (Surface surface in Surfaces)
        {
            List<string> found = [];

            foreach ((string relative, string text) in AxamlFiles(repoRoot))
            {
                if (!Regex.IsMatch(text, $@"x:DataType\s*=\s*""[^""]*:{Regex.Escape(surface.DataType)}"""))
                {
                    continue;
                }

                found.Add(relative);

                // Hosting the control that draws the severity is a valid way to render it.
                //
                // ⚠ The element carries an xmlns PREFIX that differs per file — `controls:` in
                // ClaudeForge, `lecontrols:` in OpenCodeForge — so a plain "<Name" test matches
                // none of them and the hatch never opens.
                //
                // ⛔⛔ A FILE CANNOT DELEGATE TO ITSELF, and this exclusion is load-bearing.
                // Both wrappers render nested object children by RECURSING into
                // `<ctrl:PropertyEditorWrapper />`, so without this the hatch opens for the very
                // two files it exists to check — a guard that vouches for its own subject.
                // Caught by canary C21, which removed the dot from ClaudeForge's wrapper and the
                // guard stayed green. This repo has shipped that shape of hole before.
                if (surface.DelegatesTo is { } control
                    && !Regex.IsMatch(text, $@"x:Class\s*=\s*""[^""]*\.{Regex.Escape(control)}""")
                    && Regex.IsMatch(text, $@"<\w+:{Regex.Escape(control)}\b"))
                {
                    continue;
                }

                string[] missing = RequiredBindings
                    .Where(binding => !text.Contains(binding, StringComparison.Ordinal))
                    .ToArray();

                if (missing.Length > 0)
                {
                    problems.Add(
                        $"{relative} renders {surface.Description} without its severity — "
                        + $"missing {string.Join(", ", missing)}");
                }
            }

            Assert.IsTrue(found.Count >= surface.Minimum,
                $"expected at least {surface.Minimum} template(s) with "
                + $"x:DataType=\"…:{surface.DataType}\", found {found.Count} "
                + $"({string.Join(", ", found)}). The discovery pattern has stopped matching and "
                + "this test is no longer checking anything.");
        }

        Assert.IsTrue(problems.Count == 0,
            $"{problems.Count} danger surface(s) render without severity:\n  "
            + string.Join("\n  ", problems)
            + "\n\nA row with no dot tells the user the setting is unremarkable. Bind "
            + "HasDangerSeverity (visibility) and DangerAccessibleText (HelpText + tooltip).");
    }

    /// <summary>
    /// ⛔ <c>AutomationProperties.Name</c> is IGNORED on a <c>TextBlock</c> — the <c>Text</c>
    /// always wins — so a severity glyph annotated that way announces the glyph character and
    /// nothing else. Measured via UIA; see <c>docs/AVALONIA-GOTCHAS.md</c>.
    /// </summary>
    [TestMethod]
    public void TheSeverityGlyphIsAnnotatedWithHelpTextNotName()
    {
        string repoRoot = FindRepoRoot();
        List<string> offenders = [];
        int checkedFiles = 0;

        foreach ((string relative, string text) in AxamlFiles(repoRoot))
        {
            if (!text.Contains("DangerAccessibleText", StringComparison.Ordinal))
            {
                continue;
            }

            checkedFiles++;
            if (Regex.IsMatch(text, @"AutomationProperties\.Name\s*=\s*""\{Binding DangerAccessibleText\}"""))
            {
                offenders.Add(relative);
            }
        }

        Assert.IsTrue(checkedFiles >= 6,
            $"only {checkedFiles} file(s) mention DangerAccessibleText; two wrappers, two search "
            + "templates and two effective-value grids carry it, so the scan has lost its "
            + "subjects and would pass without checking anything.");

        Assert.IsTrue(offenders.Count == 0,
            "AutomationProperties.Name is ignored on a TextBlock (its Text wins), so these "
            + $"annotations announce nothing:\n  {string.Join("\n  ", offenders)}\n\n"
            + "Use AutomationProperties.HelpText. Do not wrap the glyph to work around it — a "
            + "Border and a ContentControl both get no automation peer at all.");
    }

    /// <summary>
    /// Both wrappers must agree on the banner, not just the dot.
    /// </summary>
    /// <remarks>
    /// ⚠ The dot and the banner answer different questions — the tier versus "the value held
    /// right now is the unsafe one" — so a wrapper carrying only the dot silently drops the
    /// warning that matters most, on the rows where something is actually wrong.
    /// </remarks>
    [TestMethod]
    public void BothPropertyWrappersRenderTheIsDangerNowBanner()
    {
        string repoRoot = FindRepoRoot();

        List<string> wrappers = [.. AxamlFiles(repoRoot)
            .Where(f => Path.GetFileName(f.Relative).Equals("PropertyEditorWrapper.axaml", StringComparison.Ordinal))
            .Select(f => f.Relative)];

        Assert.AreEqual(2, wrappers.Count,
            $"expected exactly 2 PropertyEditorWrapper.axaml files (the shared one OpenCodeForge "
            + $"renders and ClaudeForge's own copy), found {wrappers.Count}: "
            + string.Join(", ", wrappers));

        List<string> missing = [.. AxamlFiles(repoRoot)
            .Where(f => wrappers.Contains(f.Relative))
            .Where(f => !f.Text.Contains("IsDangerNow", StringComparison.Ordinal))
            .Select(f => f.Relative)];

        Assert.IsTrue(missing.Count == 0,
            $"wrapper(s) with no IsDangerNow banner: {string.Join(", ", missing)}. The dot alone "
            + "says the setting matters; only the banner says the current value is wrong.");
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
