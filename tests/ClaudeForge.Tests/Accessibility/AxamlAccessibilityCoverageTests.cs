using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Enforces invariant <b>I20</b> — every interactive control in
/// <c>src/ClaudeForge/Views/*.axaml</c> MUST have
/// <c>AutomationProperties.Name</c> so screen readers (Windows Narrator /
/// NVDA / JAWS, macOS VoiceOver, Linux Orca) can announce the control to
/// blind and low-vision users.
///
/// <para>
/// <b>Operating principle: incremental backfill via baseline.</b> Adding
/// <c>AutomationProperties.Name</c> to ~140 existing controls is a
/// substantial mechanical change.  Rather than block all other work
/// behind that backfill, this test asserts the per-file unnamed-control
/// count is at or BELOW a snapshot baseline taken 2026-05-15.  PRs that:
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
    private static readonly HashSet<string> InteractiveControlElements = new(StringComparer.Ordinal)
    {
        "Button",
        "ToggleButton",
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
    };

    /// <summary>
    /// Snapshot baseline taken 2026-05-15 when invariant I20 landed.  Each
    /// entry is the count of interactive controls in that file that did NOT
    /// have <c>AutomationProperties.Name</c> at that moment.  Backfill PRs
    /// MUST decrement these toward zero; new unnamed controls FAIL the
    /// test.
    ///
    /// <para>
    /// A missing entry means "the file should be at zero" — so a NEW
    /// AXAML file added to <c>Views/</c> automatically gets the strict
    /// rule (no unnamed controls allowed).  This is the desired ratchet
    /// behaviour.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> Baseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // Snapshot taken via XDocument scan 2026-05-15.  Sorted alphabetically.
            // Decrement each entry as backfill PRs land.  Set to 0 when a file is
            // fully named; entries at 0 can stay in the baseline for self-
            // documentation or be removed (missing entry → strict 0 default).
            ["AboutDialog.axaml"] = 0, 
            ["AboutEditorView.axaml"] = 0, 
            ["BackupRestoreView.axaml"] = 0,
            ["EffectiveSettingsView.axaml"] = 0,
            ["EnabledPluginsEditorView.axaml"] = 0, 
            ["EnvironmentEditorView.axaml"] = 0,
            ["EssentialsView.axaml"] = 0, 
            ["HooksEditorView.axaml"] = 0,
            ["MainWindow.axaml"] = 0, 
            ["MarketplacesEditorView.axaml"] = 0,
            ["McpServersEditorView.axaml"] = 0,
            ["MemoryEditorView.axaml"] = 0,
            ["PermissionsEditorView.axaml"] = 0,
            ["ProfilesView.axaml"] = 0,
            ["SaveChangesDialog.axaml"] = 0, 
            ["SettingsGroupEditorView.axaml"] = 0, 
            // WelcomeView.axaml has no interactive controls, so no entry needed.
        };

    [TestMethod]
    public void EveryViewsAxamlFile_AtOrBelowBaseline_UnnamedInteractiveControlCount()
    {
        string viewsDir = FindViewsDirectory();
        string[] axamlFiles = Directory.GetFiles(viewsDir, "*.axaml");

        Assert.IsTrue(axamlFiles.Length >= 10,
            $"Expected to find at least 10 AXAML files under {viewsDir}, got {axamlFiles.Length}. " +
            "FindViewsDirectory likely resolved the wrong path.");

        Dictionary<string, int> actual = new(StringComparer.Ordinal);
        foreach (string path in axamlFiles)
        {
            string fileName = Path.GetFileName(path);
            actual[fileName] = CountUnnamedInteractiveControls(path);
        }

        List<string> failures = new();

        // (1) Existing files: count must be ≤ baseline.
        foreach ((string file, int count) in actual)
        {
            int expected = Baseline.TryGetValue(file, out int b) ? b : 0;
            if (count > expected)
            {
                failures.Add(
                    $"  • {file}: {count} unnamed interactive controls (baseline = {expected}). " +
                    $"Add AutomationProperties.Name to the new control(s), OR if a control genuinely " +
                    $"cannot have a Name, raise the baseline (and explain why in a comment).");
            }
        }

        // (2) Baseline entries that no longer exist on disk: author renamed
        // or deleted the file and forgot to update the dictionary.
        foreach (string file in Baseline.Keys)
        {
            if (!actual.ContainsKey(file))
            {
                failures.Add(
                    $"  • Baseline entry '{file}' no longer exists on disk. " +
                    $"Remove from Baseline dictionary in AxamlAccessibilityCoverageTests.cs.");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "AXAML accessibility coverage regression(s) — invariant I20 violation:\n\n" +
                string.Join('\n', failures) +
                "\n\nFix:\n" +
                "  1. Add AutomationProperties.Name=\"{x:Static loc:Strings.AutoNameXxx}\" to the " +
                "control, with a matching resx key in Strings.resx + Strings.zh-CN.resx + Designer.cs.\n" +
                "  2. Reuse an existing button-label key when the visible label is itself a good " +
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
        string viewsDir = FindViewsDirectory();
        int total = 0;
        foreach (string path in Directory.GetFiles(viewsDir, "*.axaml").OrderBy(p => p))
        {
            int count = CountUnnamedInteractiveControls(path);
            Console.WriteLine($"[AxamlAccessibilityCoverage]   {Path.GetFileName(path),-40} {count,4} unnamed");
            total += count;
        }

        Console.WriteLine(
            $"[AxamlAccessibilityCoverage] Total unnamed interactive controls remaining: {total}");
        // No assertion — informational only.
        Assert.IsTrue(total >= 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
    /// Walks up from the test's runtime base directory to the repo root,
    /// then down to <c>src/ClaudeForge/Views</c>.  Necessary because the
    /// AXAML sources aren't bundled in the test assembly and are accessed
    /// via filesystem path during test execution.
    /// </summary>
    private static string FindViewsDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "src", "ClaudeForge", "Views");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate src/ClaudeForge/Views/ by walking up from " +
            $"AppContext.BaseDirectory = '{AppContext.BaseDirectory}'.");
    }
}