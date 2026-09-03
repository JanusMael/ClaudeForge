using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Every <c>ListBox</c> bound to <c>ItemsSource</c> must name its generated
/// <c>ListBoxItem</c>s — by overriding <c>ToString()</c> on the item type, or by a container
/// style.
///
/// <para>
/// ⛔⛔ <b>Third container type, same defect, found the same way — by running the app.</b>
/// OpenCodeForge's search results announced
/// <c>Bennewitz.Ninja.AgentForge.Avalonia.Shell.Search.SearchResultViewModel</c> for every row,
/// measured via UI Automation. <c>SearchResultViewModel</c> had no <c>ToString()</c>, and a
/// <c>ListBoxItem</c> generated from <c>ItemsSource</c> takes its name from the ITEM.
/// </para>
///
/// <para>
/// ⭐ <b>The fallback differs per container and that is the whole trap.</b> Measured, three times
/// now: a <c>TabItem</c> falls back to the item's <c>ToString()</c>; a <c>TreeViewItem</c> falls
/// back to NOTHING and ignores <c>ToString()</c> entirely; a <c>ListBoxItem</c> falls back to
/// <c>ToString()</c>, like the TabItem. So the fix that works for one container is not
/// automatically the fix for another, and the sibling guards deliberately differ:
/// <see cref="ItemsSourceBoundTabsTests"/> requires <c>ToString()</c>,
/// <see cref="ItemsSourceBoundTreeViewsTests"/> requires a container style, and this one accepts
/// either because for a ListBox both genuinely work.
/// </para>
///
/// <para>
/// ⚠ <b>An <c>ItemsControl</c> is NOT in this class and is deliberately not scanned.</b> It
/// generates non-focusable <c>ContentPresenter</c>s, so the name a user hears comes from whatever
/// focusable control the template puts inside — ClaudeForge's search popup uses an
/// <c>ItemsControl</c> of <c>Button</c>s carrying an explicit name, which is why the same
/// view-model was broken in one app and correct in the other.
/// </para>
///
/// <para>
/// ⚠ <see cref="AxamlAccessibilityCoverageTests"/> cannot see this class of defect: the name
/// arrives from a bound view-model, and no scan of markup can evaluate that. It scored the file
/// clean.
/// </para>
///
/// <para>
/// <b>Why a text scan rather than reflection.</b> The item types live in assemblies this test
/// project does not all reference, and a reflection-based check would silently skip whatever it
/// could not load — the one failure mode a guard must not have.
/// </para>
/// </summary>
[TestClass]
public sealed class ItemsSourceBoundListBoxesTests
{
    [TestMethod]
    public void EveryItemsSourceBoundListBoxNamesItsGeneratedItems()
    {
        string repoRoot = FindRepoRoot();
        List<(string File, string Type, string Body)> bound = FindItemsSourceBoundListBoxes(repoRoot);

        // A scan that finds nothing proves nothing, and a regex slip empties the list silently
        // rather than throwing.
        Assert.IsTrue(bound.Count >= 1,
            "expected at least 1 ItemsSource-bound ListBox with an ItemTemplate x:DataType "
            + $"(OpenCodeForge's search results), found {bound.Count}. The scan is broken, not "
            + "the repo.");

        List<string> failures = [];

        foreach ((string file, string type, string body) in bound)
        {
            if (NamesContainersViaStyle(body))
            {
                continue;
            }

            string? declaring = FindDeclaringFile(repoRoot, type);
            if (declaring is null)
            {
                failures.Add(
                    $"  • {file}: could not find the file declaring '{type}'. Either the type was "
                    + "renamed or the x:DataType is wrong — both are worth failing on.");
                continue;
            }

            if (!Regex.IsMatch(File.ReadAllText(declaring), @"override\s+string\s+ToString\s*\("))
            {
                failures.Add(
                    $"  • {file} binds ItemsSource to '{type}', declared in "
                    + $"{RepoRelative(repoRoot, declaring)}, which does not override ToString() — "
                    + "and the ListBox sets no container name either. Every row will announce the "
                    + "type's full name.");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "ItemsSource-bound ListBox(es) whose rows announce a type name:\n"
                + string.Join('\n', failures)
                + "\n\nFix EITHER by overriding ToString() on the item type to return the row's "
                + "accessible name (the pattern used by GroupTab and "
                + "OpenCodeArtifactTabViewModel), OR with a container style:\n\n"
                + "    <Style Selector=\"ListBoxItem\" x:DataType=\"…:ItemType\">\n"
                + "        <Setter Property=\"AutomationProperties.Name\" Value=\"{Binding …}\" />\n"
                + "    </Style>\n\n"
                + "Setting AutomationProperties.Name inside the ItemTemplate does NOT name the "
                + "ListBoxItem — Avalonia takes a generated container's name from the item.");
        }
    }

    /// <summary>
    /// Whether the element body carries a <c>Style</c> targeting <c>ListBoxItem</c> that sets
    /// <c>AutomationProperties.Name</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>ItemsSourceBoundTreeViewsTests.NamesContainersViaStyle</c>, including the
    /// attribute pattern that SKIPS QUOTED STRINGS rather than stopping at the first <c>&gt;</c>:
    /// a scoped selector such as <c>"ListBox &gt; ListBoxItem"</c> holds a <c>&gt;</c> inside its
    /// own value, and a plain <c>[^&gt;]*</c> truncates the tag mid-attribute and reports a
    /// perfectly good setter as missing.
    /// </remarks>
    private static bool NamesContainersViaStyle(string body)
    {
        foreach (Match style in Regex.Matches(
                     body,
                     @"<Style\b(?<attrs>(?:[^>""]|""[^""]*"")*)>(?<inner>.*?)</Style>",
                     RegexOptions.Singleline))
        {
            Match selector = Regex.Match(style.Groups["attrs"].Value, @"Selector\s*=\s*""(?<s>[^""]*)""");
            if (!selector.Success ||
                !selector.Groups["s"].Value.Contains("ListBoxItem", StringComparison.Ordinal))
            {
                continue;
            }

            // A selector reaching into the control template is styling chrome, not the container.
            if (selector.Groups["s"].Value.Contains("/template/", StringComparison.Ordinal))
            {
                continue;
            }

            if (Regex.IsMatch(
                    style.Groups["inner"].Value,
                    @"<Setter\b[^>]*Property\s*=\s*""AutomationProperties\.Name"""))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every <c>ListBox</c> that binds <c>ItemsSource</c> and declares an item type, paired with
    /// its element body.
    /// </summary>
    private static List<(string File, string Type, string Body)> FindItemsSourceBoundListBoxes(
        string repoRoot)
    {
        List<(string, string, string)> found = [];

        foreach (string path in Directory.GetFiles(
                     Path.Combine(repoRoot, "src"), "*.axaml", SearchOption.AllDirectories))
        {
            if (IsUnderBuildOutput(repoRoot, path))
            {
                continue;
            }

            string text = File.ReadAllText(path);

            // ⚠ `[\s>]` after the element name is load-bearing: `<ListBox\b` also matches the
            // PROPERTY ELEMENTS `<ListBox.ItemTemplate>` and `<ListBox.Styles>`, which makes the
            // body boundary land inside the element and slices the template out of the window.
            // Both sibling guards paid for this exact mistake.
            foreach (Match tag in Regex.Matches(text, @"<ListBox[\s>][^>]*?>", RegexOptions.Singleline))
            {
                if (!tag.Value.Contains("ItemsSource", StringComparison.Ordinal))
                {
                    continue;
                }

                int from = tag.Index;
                int close = text.IndexOf("</ListBox>", from, StringComparison.Ordinal);
                int to = close >= 0 ? close : text.Length;
                string body = text[from..to];

                Match dt = Regex.Match(
                    body,
                    @"<ListBox\.ItemTemplate>.*?<DataTemplate[^>]*x:DataType\s*=\s*""(?:[\w]+:)?(?<t>[\w.]+)""",
                    RegexOptions.Singleline);

                if (dt.Success)
                {
                    found.Add((RepoRelative(repoRoot, path), dt.Groups["t"].Value, body));
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

        foreach (string dir in new[] { "src", "tests" })
        {
            string root = Path.Combine(repoRoot, dir);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsUnderBuildOutput(repoRoot, path))
                {
                    continue;
                }

                if (Regex.IsMatch(
                        File.ReadAllText(path),
                        $@"\b(?:class|record|struct)\s+{Regex.Escape(bare)}\b"))
                {
                    return path;
                }
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
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "could not locate the repository root (ClaudeForge.slnx)");
        return dir.FullName;
    }
}
