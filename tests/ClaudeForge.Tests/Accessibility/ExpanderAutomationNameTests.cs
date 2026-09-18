using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Every <c>&lt;Expander&gt;</c> in the solution's markup must declare
/// <c>AutomationProperties.Name</c>.
/// <para>
/// ⭐ <b>This is the precondition of a style, not a preference.</b>
/// <c>Themes/AccessibilityNames.axaml</c> copies an Expander's name down to its
/// <c>ExpanderHeader</c> part, because the part is what takes focus and the theme gives it a
/// Grid as content — so without the copy a screen reader hears
/// <c>"Avalonia.Controls.Grid, button"</c> (finding <c>F10</c>: seven of them on the Environment
/// page, with nothing to tell <c>ANTHROPIC · 41</c> from <c>OTEL · 37</c>).
/// </para>
/// <para>
/// ⛔ <b>The style has nothing to copy when the host is unnamed</b>, and it fails silently in that
/// case — binding an unset attached property yields nothing, the setter never applies, and the
/// header goes back to announcing the layout type. Nothing about the build or the rendered UI
/// says so. This guard is what keeps the precondition true.
/// </para>
/// <para>
/// ⚠ <b>Why this is not already covered.</b> <see cref="AxamlAccessibilityCoverageTests"/> does
/// scan the whole repo and does list <c>Expander</c> as interactive — but it is a per-file
/// RATCHETING BASELINE. A file carrying a nonzero baseline can absorb a newly-unnamed Expander by
/// naming some other control, and stay green. This asks the exact question instead, with no slack:
/// zero unnamed Expanders, anywhere.
/// </para>
/// <para>
/// ⓘ A markup scan is the right tool here even though a markup scan could not have found
/// <c>F10</c> itself. The defect lived in a control template with no element to scan
/// (<c>TemplatePartAutomationNameTests</c> builds the real control and asks its peer); what a scan
/// CAN do is verify the input the template-level fix depends on.
/// </para>
/// </summary>
[TestClass]
public sealed class ExpanderAutomationNameTests
{
    private const string AutomationNameAttribute = "AutomationProperties.Name";

    [TestMethod]
    public void EveryExpanderInMarkup_DeclaresAnAutomationName()
    {
        string repoRoot = FindRepoRoot();
        string srcRoot = Path.Combine(repoRoot, "src");

        List<string> offenders = [];
        int expandersSeen = 0;

        foreach (string path in Directory.EnumerateFiles(srcRoot, "*.axaml", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Load(path, LoadOptions.SetLineInfo);
            }
            catch (System.Xml.XmlException)
            {
                // A file this scan cannot parse is the build's problem, not this guard's.
                continue;
            }

            foreach (XElement el in doc.Descendants())
            {
                if (!string.Equals(el.Name.LocalName, "Expander", StringComparison.Ordinal))
                {
                    continue;
                }

                expandersSeen++;
                if (el.Attributes().Any(a =>
                        string.Equals(a.Name.LocalName, AutomationNameAttribute, StringComparison.Ordinal)
                        || a.Name.LocalName.EndsWith(".Name", StringComparison.Ordinal)
                           && a.Name.LocalName.StartsWith("AutomationProperties", StringComparison.Ordinal)))
                {
                    continue;
                }

                int line = (el as System.Xml.IXmlLineInfo)?.LineNumber ?? 0;
                offenders.Add($"{Path.GetRelativePath(repoRoot, path)}:{line}");
            }
        }

        // ⛔ The premise, asserted rather than assumed. A scan that found no Expanders would
        // report success having measured nothing — the exact shape of a guard that passes
        // because its input vanished (a moved folder, a renamed element, a broken glob).
        Assert.IsTrue(expandersSeen > 0,
            "This scan found no <Expander> elements at all under src/. Either the markup moved or "
            + "the scan is looking in the wrong place; a green result here would mean nothing.");

        Assert.AreEqual(0, offenders.Count,
            $"{offenders.Count} of {expandersSeen} Expander(s) declare no {AutomationNameAttribute}. "
            + "Each one's header part will announce 'Avalonia.Controls.Grid' to a screen reader, "
            + "because the inherited-name style in Themes/AccessibilityNames.axaml has no name to "
            + $"copy and applies nothing. Offenders:{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c> and
    /// <see cref="AxamlAccessibilityCoverageTests"/>'s copy: the AXAML sources are not bundled
    /// into the test assembly, so they are reached by walking up to the repo root.
    /// </summary>
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
