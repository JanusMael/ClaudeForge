using Bennewitz.Ninja.XamlQuality;
using Bennewitz.Ninja.XamlQuality.Rules;

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
/// <remarks>
/// <para>
/// ⭐⭐ <b>The scan itself now comes from <c>Bennewitz.Ninja.XamlQuality</c> (<c>XQ1001</c>); this
/// class supplies the repository's scope and the assertion.</b> The 126 lines it replaced walked
/// <c>src/</c>, parsed each <c>.axaml</c> and checked attributes by hand. Keeping that copy meant
/// maintaining a second implementation of a rule another repository already had to fix.
/// </para>
/// <para>
/// ⛔ <b>It is also STRICTER than what it replaced, which is the reason to adopt rather than a
/// bonus.</b> The old scan looked only at <c>el.Attributes()</c>. An attached property has two
/// spellings — the attribute form and the property-ELEMENT form
/// (<c>&lt;AutomationProperties.Name&gt;…&lt;/&gt;</c>) — and the element form is the one that goes
/// unnoticed, because the common case hides it. A correctly-named Expander written that way was
/// reported as an offender by the old scan and is handled by the rule.
/// </para>
/// <para>
/// ⚠ <b>The library reports; it does not assert.</b> A rule returns findings rather than throwing,
/// so the consumer keeps the choice of severity and test framework. The assertion below is
/// therefore ours, and so is the premise check — the rule cannot know what "enough" means here.
/// </para>
/// <para>
/// ⓘ <b>The rule catalogue is ONE rule today.</b> <c>XQ1001</c> is the whole of it; the
/// <c>IXamlRule</c> extension point is real and tested but the library of rules is not. Adopted
/// here for this guard and as somewhere to put rules that would otherwise be written inline —
/// not as a linter.
/// </para>
/// </remarks>
[TestClass]
public sealed class ExpanderAutomationNameTests
{
    [TestMethod]
    public void EveryExpanderInMarkup_DeclaresAnAutomationName()
    {
        string repoRoot = FindRepoRoot();
        string srcRoot = Path.Combine(repoRoot, "src");

        // bin/obj are skipped by default, and that is not a performance question: a build copies
        // markup into obj, so including them reports every violation twice and keeps reporting one
        // after the source is fixed.
        XamlScanContext context = XamlScanContext.Load(srcRoot);
        XamlRuleResult result = new ExpanderAutomationNameRule().Analyze(context);

        // ⛔ The premise, asserted rather than assumed. A scan that found no Expanders would report
        // success having measured nothing — the exact shape of a guard that passes because its
        // input vanished (a moved folder, a renamed element, a broken glob). `Inspected` exists on
        // the result for precisely this, so the check survives the rule moving out of this repo.
        Assert.IsTrue(
            result.Inspected > 0,
            "This scan found no <Expander> elements at all under src/. Either the markup moved or "
            + "the scan is looking in the wrong place; a green result here would mean nothing.");

        Assert.AreEqual(
            0, result.Findings.Count,
            $"{result.Findings.Count} of {result.Inspected} Expander(s) declare no automation name. "
            + "Each one's header part will announce 'Avalonia.Controls.Grid' to a screen reader, "
            + "because the inherited-name style in Themes/AccessibilityNames.axaml has no name to "
            + $"copy and applies nothing. Offenders:{Environment.NewLine}"
            + string.Join(Environment.NewLine, result.Findings));
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
