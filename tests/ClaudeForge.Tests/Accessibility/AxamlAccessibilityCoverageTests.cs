using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Enforces invariant <b>I20</b> — every interactive control in the app's AXAML
/// MUST have <c>AutomationProperties.Name</c> so screen readers (Windows
/// Narrator / NVDA / JAWS, macOS VoiceOver, Linux Orca) can announce the
/// control to blind and low-vision users.
///
/// <para>
/// <b>Scope: every AXAML file in the three view-bearing assemblies</b>, walked
/// recursively — <c>src/ClaudeForge</c>, <c>src/ClaudeForge.Avalonia</c> and
/// <c>src/LayeredEditors.Avalonia</c>.  This previously scanned
/// <c>src/ClaudeForge/Views</c> alone, flat, which left 16 of the repository's
/// 43 AXAML files unexamined — including everything under <c>Controls/</c>,
/// where the great majority of the remaining debt turned out to live.
/// </para>
///
/// <para>
/// <b>Operating principle: incremental backfill via baseline.</b> This test
/// asserts the per-file unnamed-control count is at or BELOW a snapshot
/// baseline.  PRs that:
/// </para>
/// <list type="bullet">
///   <item>Add a NEW unnamed control to a file → test fails (count grew).</item>
///   <item>Backfill existing controls → test passes (count shrank); author
///   should decrement the baseline entry to lock the new floor.</item>
///   <item>Add a new AXAML file with un-named controls → test fails
///   (no baseline entry → expected 0 → any unnamed count exceeds it).</item>
///   <item>Rename or delete a baseline file → test fails with a clear
///   "Baseline entry X no longer exists" message so the dictionary stays
///   in sync with the filesystem.</item>
/// </list>
///
/// <para>
/// <b>Naming convention</b> (locked by AGENTS.md I20 + CLAUDE.md
/// Accessibility section):
/// </para>
/// <list type="bullet">
///   <item>New string keys: <c>AutoName&lt;Context&gt;</c> /
///   <c>AutoHelp&lt;Context&gt;</c> in <c>Strings.resx</c>.</item>
///   <item>Values are clean text — no emoji, no <c>_</c> Alt-mnemonic
///   prefix.  Mirror to <c>Strings.zh-CN.resx</c> with TODO comment,
///   add the Designer property.</item>
///   <item>Where the visible label IS a good screen-reader announcement
///   (e.g. a Button whose Content is the plain word "Delete"), REUSE
///   the existing label key rather than inventing a new one.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class AxamlAccessibilityCoverageTests
{
    /// <summary>
    /// Element names that present a discoverable, focusable control to the
    /// user.  Static-only "decoration" elements (Border, TextBlock without
    /// click handler, Image) are intentionally NOT in this set — their
    /// accessibility story is the surrounding control they live inside.
    /// </summary>
    /// <remarks>
    /// <c>MenuItem</c> and <c>RepeatButton</c> were added when the scan
    /// widened.  A menu is a primary interactive surface and a context-menu
    /// entry is exactly the kind of control a screen-reader user reaches for,
    /// so leaving it out meant every <c>MenuFlyout</c> in the app was
    /// unasserted.
    /// </remarks>
    private static readonly HashSet<string> InteractiveControlElements = new(StringComparer.Ordinal)
    {
        "Button",
        "ToggleButton",
        "RepeatButton",
        "TextBox",
        "ComboBox",
        "CheckBox",
        "ToggleSwitch",
        "RadioButton",
        "Slider",
        "NumericUpDown",
        "DataGrid",
        "ListBox",
        "AutoCompleteBox",
        "DatePicker",
        "TimePicker",
        "CalendarDatePicker",
        "MenuItem",
    };

    /// <summary>The assemblies that carry AXAML, relative to the repo root.</summary>
    private static readonly string[] ScannedRoots =
    [
        Path.Combine("src", "ClaudeForge"),
        Path.Combine("src", "ClaudeForge.Avalonia"),
        Path.Combine("src", "LayeredEditors.Avalonia"),
    ];

    /// <summary>
    /// Generated resource dictionaries — carried verbatim from Fluent / Simple
    /// by `theme-audit compat` and never hand-edited.  They define brushes, not
    /// controls, so they have nothing to name.
    /// </summary>
    private static readonly string[] ExcludedFragments =
    [
        Path.Combine("Resources", "Compat") + Path.DirectorySeparatorChar,
    ];

    /// <summary>
    /// Each entry is the count of interactive controls in that file that do NOT
    /// have <c>AutomationProperties.Name</c>.  Backfill PRs MUST decrement these
    /// toward zero; new unnamed controls FAIL the test.
    ///
    /// <para>
    /// A missing entry means "the file should be at zero" — so a NEW AXAML file
    /// automatically gets the strict rule.  This is the desired ratchet.
    /// </para>
    ///
    /// <para>
    /// Keys are repo-relative paths with forward slashes, because the widened
    /// scan is recursive and a bare filename is no longer unique —
    /// <c>PropertyEditorWrapper.axaml</c> exists in two assemblies (see
    /// <see href="https://github.com/JanusMael/ClaudeForge/issues/46"/>).
    /// </para>
    /// </summary>
    /// <remarks>
    /// The two entries below are pre-existing debt that the previous flat
    /// <c>Views/</c>-only scan could not see; the backfill is tracked in
    /// <see href="https://github.com/JanusMael/ClaudeForge/issues/45"/>.
    /// Everything else in all three assemblies is at zero.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, int> Baseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/ClaudeForge/Controls/PropertyEditorWrapper.axaml"] = 48,
            ["src/LayeredEditors.Avalonia/Controls/PropertyEditorWrapper.axaml"] = 6,
        };

    [TestMethod]
    public void EveryAxamlFile_AtOrBelowBaseline_UnnamedInteractiveControlCount()
    {
        Dictionary<string, int> actual = ScanAll();

        Assert.IsTrue(actual.Count >= 30,
            $"Expected to find at least 30 AXAML files across {string.Join(", ", ScannedRoots)}, " +
            $"got {actual.Count}. FindRepoRoot likely resolved the wrong path.");

        List<string> failures = [];

        // (1) Existing files: count must be ≤ baseline.
        foreach ((string file, int count) in actual.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            int expected = Baseline.GetValueOrDefault(file, 0);
            if (count > expected)
            {
                failures.Add(
                    $"  • {file}: {count} unnamed interactive controls (baseline = {expected}). " +
                    "Add AutomationProperties.Name to the new control(s), OR if a control genuinely " +
                    "cannot have a Name, raise the baseline (and explain why in a comment).");
            }
        }

        // (2) Baseline entries that no longer exist on disk: author renamed
        // or deleted the file and forgot to update the dictionary.
        foreach (string file in Baseline.Keys.Where(f => !actual.ContainsKey(f)))
        {
            failures.Add(
                $"  • Baseline entry '{file}' no longer exists on disk. " +
                "Remove it from the Baseline dictionary in AxamlAccessibilityCoverageTests.cs.");
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "AXAML accessibility coverage regression(s) — invariant I20 violation:\n\n" +
                string.Join('\n', failures) +
                "\n\nFix:\n" +
                "  1. Add AutomationProperties.Name=\"{x:Static loc:Strings.AutoNameXxx}\" to the " +
                "control, with a matching resx key in Strings.resx + Strings.zh-CN.resx + Designer.cs.\n" +
                "  2. Reuse an existing label key when the visible label is itself a good " +
                "screen-reader announcement.\n" +
                "  3. See CLAUDE.md \"Accessibility — screen-reader names\" and AGENTS.md invariant I20.\n");
        }
    }

    [TestMethod]
    public void Baseline_ConvergesToZero_FullBackfillTracker()
    {
        // Diagnostic-only test that reports the total unnamed-control debt
        // remaining across all AXAML files.  Never fails — it's an
        // observability surface so a stocktake of accessibility progress is
        // one test-run away.  When the total reaches zero, delete this test
        // and the Baseline dictionary; the per-file zero default in the
        // companion test becomes the strict rule everywhere.
        int total = 0;
        foreach ((string file, int count) in ScanAll().OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (count > 0)
            {
                Console.WriteLine($"[AxamlAccessibilityCoverage]   {file,-70} {count,4} unnamed");
            }

            total += count;
        }

        Console.WriteLine(
            $"[AxamlAccessibilityCoverage] Total unnamed interactive controls remaining: {total}");
        // No assertion — informational only.
        Assert.IsTrue(total >= 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every AXAML file under <see cref="ScannedRoots"/>, keyed by repo-relative
    /// path with forward slashes, mapped to its unnamed-interactive-control count.
    /// </summary>
    private static Dictionary<string, int> ScanAll()
    {
        string root = FindRepoRoot();
        Dictionary<string, int> actual = new(StringComparer.Ordinal);

        foreach (string relativeRoot in ScannedRoots)
        {
            string directory = Path.Combine(root, relativeRoot);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*.axaml", SearchOption.AllDirectories))
            {
                if (IsExcluded(path))
                {
                    continue;
                }

                string key = Path.GetRelativePath(root, path).Replace('\\', '/');
                actual[key] = CountUnnamedInteractiveControls(path);
            }
        }

        return actual;
    }

    private static bool IsExcluded(string path)
    {
        string obj = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        string bin = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
        if (path.Contains(obj, StringComparison.Ordinal) || path.Contains(bin, StringComparison.Ordinal))
        {
            return true;
        }

        return ExcludedFragments.Any(fragment => path.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// Counts interactive controls in <paramref name="axamlPath"/> that do
    /// NOT have an <c>AutomationProperties.Name</c> attribute (regardless of
    /// where the attribute appears on a multi-line element declaration).
    /// </summary>
    private static int CountUnnamedInteractiveControls(string axamlPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(axamlPath, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            // Malformed AXAML at the XML layer is its own problem; surface
            // a clear failure rather than silently undercounting.
            throw new InvalidOperationException(
                $"Failed to parse AXAML as XML at {axamlPath}: {ex.Message}", ex);
        }

        int unnamed = 0;
        foreach (XElement el in doc.Descendants())
        {
            if (!InteractiveControlElements.Contains(el.Name.LocalName))
            {
                continue;
            }

            // Attached-property attribute appears in the source as
            // `AutomationProperties.Name="..."` — a single XML attribute
            // whose LocalName literally contains a dot.  XDocument reads
            // this without namespace mangling because attached-property
            // attributes are unprefixed in the default xmlns.
            bool hasName = el.Attributes()
                             .Any(a => a.Name.LocalName == "AutomationProperties.Name");

            if (!hasName)
            {
                unnamed++;
            }
        }

        return unnamed;
    }

    /// <summary>
    /// Walks up from the test's runtime base directory to the repo root — the
    /// directory holding <c>ClaudeForge.slnx</c>.  Necessary because the AXAML
    /// sources aren't bundled in the test assembly and are accessed via
    /// filesystem path during test execution.
    /// </summary>
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "ClaudeForge.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (the directory holding ClaudeForge.slnx) by walking up " +
            $"from AppContext.BaseDirectory = '{AppContext.BaseDirectory}'.");
    }
}
