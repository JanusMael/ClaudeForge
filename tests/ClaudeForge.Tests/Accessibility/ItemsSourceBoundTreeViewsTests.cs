using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Every <c>TreeView</c> bound to <c>ItemsSource</c> must declare a <c>Style</c> that sets
/// <c>AutomationProperties.Name</c> on the generated <c>TreeViewItem</c>.
///
/// <para>
/// ⛔⛔ <b>Nothing inside the <c>ItemTemplate</c> can name the container — not even a lone bound
/// <c>TextBlock</c>.</b> Putting <c>AutomationProperties.Name</c> on a control inside the template
/// names that control and leaves the focusable <c>TreeViewItem</c> silent. Measured through UI
/// Automation on both running apps, and <b>both were broken</b>:
/// </para>
///
/// <list type="bullet">
/// <item><c>ClaudeForge/Views/MainWindow.axaml</c> — <b>26 rows, all announcing an empty name</b>,
/// while the <c>Tree</c>'s own name was present and the accessibility scan reported the file
/// clean.</item>
/// <item><c>OpenCodeForge/Views/MainWindow.axaml</c> — <b>3 rows, all empty</b>, despite its item
/// template being a single bound <c>TextBlock</c>.</item>
/// </list>
///
/// <para>
/// ⚠⚠ <b>That second measurement killed this guard's original escape hatch, and it is worth
/// keeping the story.</b> The first version of this test also passed a <c>TreeView</c> whose
/// template root was a lone text element, on the belief that a single text child supplies the
/// container's name. It does not. The belief came from an old UIA harness recipe that located each
/// page by finding the <c>Text</c> LEAF and walking UP to its <c>TreeItem</c> ancestor — so the
/// tree looked correctly labelled when only the <c>TextBlock</c> was. <b>The hatch would have
/// vouched for a tree that announced nothing.</b> Removed, and the second app fixed.
/// </para>
///
/// <para>
/// ⛔ <b>This is a DIFFERENT defect from <see cref="ItemsSourceBoundTabsTests"/>, and it has a
/// different fix — do not unify them.</b> A <c>TabItem</c> falls back to the item's
/// <c>ToString()</c>, so both of the repo's <c>ItemsSource</c>-bound TabControls announced their
/// view-model's type name and were fixed by overriding <c>ToString()</c>. A <c>TreeViewItem</c>
/// falls back to <b>nothing at all</b> and ignores <c>ToString()</c> entirely. Measured: a probe
/// override returning <c>"PROBE-" + Title</c> on <c>NavigationNodeViewModel</c> never reached UIA;
/// the 26 rows stayed empty. <b>A guard demanding <c>ToString()</c> on tree item types would have
/// sat green over a navigation tree that still announced nothing</b> — which is why the mechanism
/// was measured before this test was written.
/// </para>
///
/// <para>
/// ⚠ <b><see cref="AxamlAccessibilityCoverageTests"/> cannot see this class of defect and reported
/// the file as fully named</b>, for the same reason it could not see the TabControl version: it
/// asserts on <c>AutomationProperties.Name</c> attributes present in the markup, and the attribute
/// was present — on the <c>TextBlock</c>s inside the template. The name that reaches the user comes
/// from the generated container.
/// </para>
///
/// <para>
/// <b>Why a text scan.</b> Same reason as the TabControl guard: the item types live in assemblies
/// this test project does not all reference, so a reflection-based check would silently skip
/// whatever it could not load. It also lets the check see the <i>markup</i> decision — the
/// container style — which is where the fix actually lives.
/// </para>
///
/// <para>
/// ⓘ <b>Known limitation, stated rather than hidden:</b> the style must be declared in the same
/// file as the <c>TreeView</c>. A container theme supplied from <c>App.axaml</c> or a shared theme
/// would satisfy a screen reader but fail here. Both of the repo's trees declare in-file, and a
/// guard that chased styles across files would be far easier to fool into a false pass.
/// </para>
/// </summary>
[TestClass]
public sealed class ItemsSourceBoundTreeViewsTests
{
    [TestMethod]
    public void EveryItemsSourceBoundTreeViewNamesItsGeneratedContainers()
    {
        string repoRoot = FindRepoRoot();
        List<(string File, string Body)> trees = FindItemsSourceBoundTreeViews(repoRoot);

        // A scan that finds nothing proves nothing. Both apps' navigation trees must show up, so a
        // regex slip empties the list rather than silently passing.
        Assert.IsTrue(trees.Count >= 2,
            $"expected at least 2 ItemsSource-bound TreeViews (ClaudeForge's and OpenCodeForge's "
            + $"navigation), found {trees.Count}. The scan or its pattern is broken, not the repo.");

        List<string> failures = [];

        foreach ((string file, string body) in trees)
        {
            if (NamesContainersViaStyle(body))
            {
                continue;
            }

            failures.Add(
                $"  • {file} binds TreeView.ItemsSource but declares no Style setting "
                + "AutomationProperties.Name on TreeViewItem. Its rows will announce NOTHING to a "
                + "screen reader.");
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "ItemsSource-bound TreeView(s) whose rows announce nothing:\n"
                + string.Join('\n', failures)
                + "\n\nFix: add a style on the container, e.g.\n"
                + "    <Style Selector=\"TreeViewItem\" x:DataType=\"libvm:NavigationNodeViewModel\">\n"
                + "        <Setter Property=\"AutomationProperties.Name\" Value=\"{Binding Title}\" />\n"
                + "    </Style>\n"
                + "x:DataType keeps the binding compiled (a reflection binding is an IL2026 trim "
                + "error here). Setting AutomationProperties.Name inside the ItemTemplate does NOT "
                + "name the TreeViewItem, and neither does overriding ToString() on the item type "
                + "— unlike a TabItem, a TreeViewItem does not consult it.");
        }
    }

    /// <summary>
    /// A <c>Style</c> whose selector targets <c>TreeViewItem</c> and which sets
    /// <c>AutomationProperties.Name</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately does not require the selector to be exactly <c>"TreeViewItem"</c> — a scoped
    /// selector such as <c>"TreeView > TreeViewItem"</c> is legitimate. It does require the setter
    /// to sit inside that same <c>Style</c> element, so a name setter belonging to an unrelated
    /// style (the file has several targeting <c>/template/</c> parts) cannot vouch for it.
    /// </remarks>
    private static bool NamesContainersViaStyle(string body)
    {
        // ⚠ The attribute capture must SKIP QUOTED STRINGS rather than stop at the first '>'.
        // `Selector="TreeView > TreeViewItem"` contains a '>' inside its value, so a plain
        // `[^>]*` truncates the tag mid-attribute, the selector then has no closing quote, and a
        // perfectly good scoped name setter is reported as missing. Caught by canary D, which
        // asserts exactly that selector form is accepted.
        foreach (Match style in Regex.Matches(
                     body,
                     @"<Style\b(?<attrs>(?:[^>""]|""[^""]*"")*)>(?<inner>.*?)</Style>",
                     RegexOptions.Singleline))
        {
            string attrs = style.Groups["attrs"].Value;

            Match selector = Regex.Match(attrs, @"Selector\s*=\s*""(?<s>[^""]*)""");
            if (!selector.Success || !selector.Groups["s"].Value.Contains("TreeViewItem", StringComparison.Ordinal))
            {
                continue;
            }

            // A selector reaching into the control template (/template/ Border#PART_LayoutRoot) is
            // styling chrome, not the container — naming that would not reach the item.
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
    /// Every <c>TreeView</c> that binds <c>ItemsSource</c>, paired with its element body.
    /// </summary>
    private static List<(string File, string Body)> FindItemsSourceBoundTreeViews(string repoRoot)
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

            // ⚠ `[\s>]` after the element name is load-bearing: `<TreeView\b` also matches the
            // PROPERTY ELEMENTS `<TreeView.Styles>` and `<TreeView.ItemTemplate>`, which would make
            // the body boundary land inside the element and slice the styles out of the window.
            // The sibling TabControl guard paid for this exact mistake.
            foreach (Match tag in Regex.Matches(text, @"<TreeView[\s>][^>]*?>", RegexOptions.Singleline))
            {
                if (!tag.Value.Contains("ItemsSource", StringComparison.Ordinal))
                {
                    continue;
                }

                int from = tag.Index;
                int close = text.IndexOf("</TreeView>", from, StringComparison.Ordinal);
                int to = close >= 0 ? close : text.Length;

                found.Add((RepoRelative(repoRoot, path), text[from..to]));
            }
        }

        return found;
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
