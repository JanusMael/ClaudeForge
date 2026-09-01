using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Every <c>TabControl</c> bound to <c>ItemsSource</c> must have an item type that overrides
/// <c>ToString()</c>.
///
/// <para>
/// ⛔⛔ <b>Because that is what names the tab, and nothing else does.</b> Avalonia names a generated
/// <c>TabItem</c> from the ITEM. The <c>ItemTemplate</c>'s contents are not the header's automation
/// name, so putting <c>AutomationProperties.Name</c> on a <c>TextBlock</c> inside the template
/// names the TextBlock and leaves the focusable <c>TabItem</c> announcing
/// <c>Namespace.Type.FullName</c>.
/// </para>
///
/// <para>
/// ⭐ <b>Both of the repo's <c>ItemsSource</c>-bound TabControls had this bug — a 100% hit rate on
/// the pattern.</b> Measured through UI Automation on the running apps, not inferred:
/// <c>OpenCodeArtifactsPageView</c>'s five tabs each announced
/// <c>…Artifacts.OpenCodeArtifactTabViewModel</c>, and <c>SettingsGroupEditorView</c>'s tabs each
/// announced <c>…Settings.GroupTab</c> — six of them on the Permissions group alone, in
/// ClaudeForge's primary surface. A pattern that fails every time it is used is worth a guard
/// rather than a fix.
/// </para>
///
/// <para>
/// ⚠ <b><c>AxamlAccessibilityCoverageTests</c> cannot see this class of defect and reported both
/// files as fully named.</b> It asserts on <c>AutomationProperties.Name</c> attributes present in
/// the AXAML, and in both cases the attribute WAS present — on the wrong element. The name that
/// reaches the user comes from a bound view-model, which no scan of the markup can evaluate.
/// </para>
///
/// <para>
/// <b>Why a text scan rather than reflection.</b> The item types live in different assemblies
/// (<c>AgentForge.Avalonia.Shell</c> and <c>OpenCode.Avalonia</c>), and this test project does not
/// reference all of them — a reflection-based check would silently skip whatever it could not
/// load, which is the failure mode a guard must not have. Reading the declaring source file works
/// uniformly and cannot skip.
/// </para>
/// </summary>
[TestClass]
public sealed class ItemsSourceBoundTabsTests
{
    [TestMethod]
    public void EveryItemsSourceBoundTabControlsItemTypeOverridesToString()
    {
        string repoRoot = FindRepoRoot();
        List<(string File, string Type)> bound = FindItemsSourceBoundTabControls(repoRoot);

        // A scan that finds nothing proves nothing — and this pattern is rare enough that a
        // regex slip would silently empty the list rather than throw.
        Assert.IsTrue(bound.Count >= 2,
            $"expected at least 2 ItemsSource-bound TabControls with an ItemTemplate x:DataType "
            + $"(OpenCodeArtifactsPageView and SettingsGroupEditorView), found {bound.Count}. "
            + "The scan or its pattern is broken, not the repo.");

        List<string> failures = [];

        foreach ((string file, string type) in bound)
        {
            string? declaring = FindDeclaringFile(repoRoot, type);
            if (declaring is null)
            {
                failures.Add(
                    $"  • {file}: could not find the file declaring '{type}'. Either the type was "
                    + "renamed or the x:DataType is wrong — both are worth failing on.");
                continue;
            }

            string source = File.ReadAllText(declaring);
            if (!Regex.IsMatch(source, @"override\s+string\s+ToString\s*\("))
            {
                failures.Add(
                    $"  • {file} binds ItemsSource to '{type}', declared in "
                    + $"{RepoRelative(repoRoot, declaring)}, which does not override ToString(). "
                    + $"Its tabs will announce the type's full name to a screen reader.");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "ItemsSource-bound TabControl(s) whose item type announces its own type name:\n"
                + string.Join('\n', failures)
                + "\n\nFix: override ToString() on the item type to return the tab's label "
                + "(prefer an explicit automation name, falling back to the visible header). "
                + "Setting AutomationProperties.Name inside the ItemTemplate does NOT name the "
                + "TabItem — Avalonia takes the generated container's name from the item.");
        }
    }

    /// <summary>
    /// Every <c>TabControl</c> with an <c>ItemsSource</c> paired with the <c>x:DataType</c> its
    /// <c>ItemTemplate</c> declares.
    /// </summary>
    /// <remarks>
    /// The <c>x:DataType</c> is how the item type is identified: these views use compiled
    /// bindings, so the template must declare it, and a <c>TabControl</c> that bound
    /// <c>ItemsSource</c> without one would already be an IL2026 trim error in this repo.
    /// </remarks>
    private static List<(string File, string Type)> FindItemsSourceBoundTabControls(string repoRoot)
    {
        List<(string, string)> found = [];

        foreach (string path in Directory.GetFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            if (IsUnderBuildOutput(repoRoot, path))
            {
                continue;
            }

            string text = File.ReadAllText(path);

            // Each <TabControl …> opening tag, then the ItemTemplate that follows it.
            //
            // ⚠ Requires whitespace or '>' after the element name. `<TabControl\b` also matches
            // the PROPERTY ELEMENT `<TabControl.ItemTemplate>`, which made the "next TabControl"
            // boundary below land BEFORE the template and slice it out of the search window — the
            // scan then found zero templates and the non-vacuity assertion above caught it.
            foreach (Match tag in Regex.Matches(text, @"<TabControl[\s>][^>]*?>", RegexOptions.Singleline))
            {
                if (!tag.Value.Contains("ItemsSource", StringComparison.Ordinal))
                {
                    continue;
                }

                // The ItemTemplate belongs to this TabControl if it appears before the next one.
                int from = tag.Index;
                Match next = Regex.Match(text[(from + tag.Length)..], @"<TabControl[\s>]");
                int to = next.Success ? from + tag.Length + next.Index : text.Length;

                Match dt = Regex.Match(
                    text[from..to],
                    @"<TabControl\.ItemTemplate>.*?<DataTemplate[^>]*x:DataType\s*=\s*""(?:[\w]+:)?(?<t>[\w.]+)""",
                    RegexOptions.Singleline);

                if (dt.Success)
                {
                    found.Add((RepoRelative(repoRoot, path), dt.Groups["t"].Value));
                }
            }
        }

        return found;
    }

    /// <summary>The <c>.cs</c> file declaring <paramref name="typeName"/>.</summary>
    private static string? FindDeclaringFile(string repoRoot, string typeName)
    {
        string bare = typeName.Contains('.', StringComparison.Ordinal)
            ? typeName[(typeName.LastIndexOf('.') + 1)..]
            : typeName;

        Regex declaration = new(
            $@"\b(?:class|record|struct)\s+{Regex.Escape(bare)}\b", RegexOptions.Compiled);

        foreach (string path in Directory.GetFiles(
                     Path.Combine(repoRoot, "src"), $"{bare}.cs", SearchOption.AllDirectories))
        {
            if (!IsUnderBuildOutput(repoRoot, path) && declaration.IsMatch(File.ReadAllText(path)))
            {
                return path;
            }
        }

        // The type need not live in a file of its own — EssentialsCardKind shares a file with
        // EssentialsCardViewModel, so fall back to a full sweep before giving up.
        foreach (string path in Directory.GetFiles(
                     Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (!IsUnderBuildOutput(repoRoot, path) && declaration.IsMatch(File.ReadAllText(path)))
            {
                return path;
            }
        }

        return null;
    }

    private static bool IsUnderBuildOutput(string repoRoot, string absolutePath)
    {
        string[] segments = Path.GetRelativePath(repoRoot, absolutePath)
                                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                              || s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRelative(string repoRoot, string absolutePath) =>
        Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');

    /// <summary>Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.</summary>
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null)
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
