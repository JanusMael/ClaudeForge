using System.Xml;
using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Accessibility;

/// <summary>
/// Enforces invariant <b>I20</b> — every interactive control in EVERY
/// <c>src/**/*.axaml</c> file MUST have <c>AutomationProperties.Name</c> so
/// screen readers (Windows Narrator / NVDA / JAWS, macOS VoiceOver, Linux
/// Orca) can announce the control to blind and low-vision users.
///
/// <para>
/// <b>Scope is the whole repo, deliberately.</b> This test used to locate its
/// scan root via a private <c>FindViewsDirectory()</c> that appended
/// <c>src/ClaudeForge/Views</c> — a hardcoded single-app path from when this
/// repo held one app.  Every AXAML file outside that one folder was invisible
/// to the guard: <c>src/OpenCodeForge/Views/</c>,
/// <c>src/OpenCode.Avalonia/Permissions/</c>,
/// <c>src/ClaudeForge.Avalonia/</c>, <c>src/ClaudeForge/Controls/</c> and
/// <c>src/LayeredEditors.Avalonia/</c> could ship an unnamed control and the
/// suite stayed green.  The scan is now repo-wide, and
/// <see cref="AxamlScan_CoversEveryUiProject_NotOneHardcodedDirectory"/>
/// fails if a future refactor narrows it again.
/// </para>
///
/// <para>
/// This is the same class of blind spot as
/// <c>LocalizationParityTests.FindLocalizationDirectory()</c>, which hardcodes
/// <c>src/ClaudeForge/Localization</c> and is why
/// <c>src/ClaudeForge.Avalonia/Localization/Strings.resx</c> has user-facing
/// strings, no locale siblings, and no failing test.  That one is tracked
/// separately as "Problem 8" in <c>docs/OPENCODEFORGE-PLAN.md</c>; widening it
/// is NOT part of this change.
/// </para>
///
/// <para>
/// <b>Operating principle: incremental backfill via baseline.</b> Adding
/// <c>AutomationProperties.Name</c> to every pre-existing control is a
/// substantial mechanical change.  Rather than block all other work behind
/// that backfill, this test asserts the per-file unnamed-control count is at
/// or BELOW a snapshot baseline.  PRs that:
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

        // ── Added 2026-08-27: navigational containers the list had never covered ──
        //
        // ⛔⛔ The omission was not cosmetic. src/ClaudeForge/Views/MainWindow.axaml sat at a
        // baseline of 0 — "fully named" — while its TreeView, the primary navigation control of
        // the whole application, had no AutomationProperties.Name at all. Confirmed through UI
        // Automation on the running app: the nav tree reported an empty Name, so a screen reader
        // announced nothing for it. A guard reporting zero unnamed controls on a file whose most
        // important control is unnamed is worse than no guard, because the zero is quoted as
        // evidence.
        //
        // These six are all focusable and all announced as controls in their own right.
        "TreeView",
        "TabControl",
        "TabItem",
        "Expander",
        "MenuItem",
        "HyperlinkButton",

        // ⚠ DELIBERATELY NOT ADDED, measured counts as of this commit:
        //   ItemsControl (67)        — a bare repeater, not a control. It takes no focus and is
        //                             not announced; naming all 67 would be pure noise, and the
        //                             noise is what makes a baseline stop being read.
        //   ScrollViewer (31)        — same: scrolling is a viewport behaviour, not a control a
        //                             reader announces by name.
        //   SelectableTextBlock (8)  — announced by its CONTENT. A Name would either duplicate
        //                             the text or, worse, shadow it.
        //   TreeViewItem             — generated from ItemsSource in both apps, so there is no
        //                             element in the markup to annotate. That case is covered by
        //                             ItemsSourceBoundTabsTests' sibling reasoning: the container's
        //                             name comes from the bound item's ToString().
    };

    /// <summary>
    /// Top-level project directories under <c>src/</c> that hold AXAML and
    /// must therefore be represented in the scan.  This list is the tripwire
    /// against the scan silently narrowing back to one hardcoded folder: if a
    /// refactor drops a project from coverage,
    /// <see cref="AxamlScan_CoversEveryUiProject_NotOneHardcodedDirectory"/>
    /// names it.  Genuinely removing or renaming a UI project means updating
    /// this list in the same commit — deliberately, not by accident.
    /// </summary>
    private static readonly string[] ProjectsThatMustContributeAxaml =
    {
        "ClaudeForge",
        "ClaudeForge.Avalonia",
        "LayeredEditors.Avalonia",
        "OpenCode.Avalonia",
        "OpenCodeForge",
    };

    /// <summary>
    /// Per-file count of interactive controls that did NOT have
    /// <c>AutomationProperties.Name</c> when the baseline was snapshotted.
    /// Backfill PRs MUST decrement these toward zero; new unnamed controls
    /// FAIL the test.
    ///
    /// <para>
    /// Keys are repo-relative paths with <c>/</c> separators, NOT bare file
    /// names.  Bare names cannot work repo-wide: <c>MainWindow.axaml</c>
    /// exists in both <c>src/ClaudeForge/Views/</c> and
    /// <c>src/OpenCodeForge/Views/</c>, and <c>PropertyEditorWrapper.axaml</c>
    /// in both <c>src/ClaudeForge/Controls/</c> and
    /// <c>src/LayeredEditors.Avalonia/Controls/</c>.  Keying by bare name
    /// would silently let one file's debt authorise the other's.
    /// </para>
    ///
    /// <para>
    /// A missing entry means "the file should be at zero" — so a NEW AXAML
    /// file added anywhere under <c>src/</c> automatically gets the strict
    /// rule (no unnamed controls allowed).  This is the desired ratchet
    /// behaviour.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> Baseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // ── src/ClaudeForge/Views ────────────────────────────────────────
            // Original snapshot 2026-05-15, since backfilled to zero.  Kept at
            // 0 for self-documentation; a missing entry means the same thing.
            ["src/ClaudeForge/Views/AboutDialog.axaml"] = 0,
            ["src/ClaudeForge/Views/AboutEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/BackupRestoreView.axaml"] = 0,
            ["src/ClaudeForge/Views/EffectiveSettingsView.axaml"] = 0,
            ["src/ClaudeForge/Views/EnabledPluginsEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/EnvironmentEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/EssentialsView.axaml"] = 0,
            ["src/ClaudeForge/Views/HooksEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/MainWindow.axaml"] = 0,
            ["src/ClaudeForge/Views/MarketplacesEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/McpServersEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/MemoryEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/PermissionsEditorView.axaml"] = 0,
            ["src/ClaudeForge/Views/ProfilesView.axaml"] = 0,
            ["src/ClaudeForge/Views/SaveChangesDialog.axaml"] = 0,
            ["src/ClaudeForge/Views/SettingsGroupEditorView.axaml"] = 0,

            // ── Newly visible when the scan widened ──────────────────────────
            // MEASURED, not assumed: snapshot 2026-08-20, the commit that
            // replaced the hardcoded src/ClaudeForge/Views root with a
            // repo-wide src/**/*.axaml scan.  These are pre-existing debt the
            // old root simply never looked at.  Decrement as backfill lands;
            // do NOT raise them.
            //
            // All three are genuine debt, not ControlTemplate-part noise: the
            // PropertyEditorWrapper counts are real user-facing controls inside
            // the per-type editor DataTemplates (the boolean toggle, the string
            // TextBox, the enum ComboBox, the list add/remove Buttons), and the
            // ModelPicker one is a glyph-only "▾" dropdown Button that a screen
            // reader would otherwise announce as the bare character.
            ["src/ClaudeForge/Controls/ModelPicker.axaml"] = 1,
            ["src/ClaudeForge/Controls/PropertyEditorWrapper.axaml"] = 48,
            ["src/LayeredEditors.Avalonia/Controls/PropertyEditorWrapper.axaml"] = 6,

            // Everything else the widened scan newly reached already scores 0
            // and so needs no entry — including all of src/OpenCodeForge/,
            // src/OpenCode.Avalonia/Permissions/OpenCodePermissionEditorView.axaml
            // (verified 0, not assumed), src/ClaudeForge.Avalonia/Permissions/,
            // the remaining src/ClaudeForge/Controls|Resources|Views files, and
            // src/LayeredEditors.Avalonia/Themes/.  They are held at the strict
            // zero default by the missing-entry rule.
        };

    [TestMethod]
    public void EveryViewsAxamlFile_AtOrBelowBaseline_UnnamedInteractiveControlCount()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> axamlFiles = ScanAxamlFiles(repoRoot);

        AssertScanIsNonVacuous(repoRoot, axamlFiles);

        Dictionary<string, int> actual = new(StringComparer.Ordinal);
        foreach (string path in axamlFiles)
        {
            actual[RepoRelative(repoRoot, path)] = CountUnnamedInteractiveControls(path);
        }

        List<string> failures = new();

        // (1) Existing files: count must be ≤ baseline.
        foreach ((string file, int count) in actual.OrderBy(kv => kv.Key, StringComparer.Ordinal))
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
        foreach (string file in Baseline.Keys.OrderBy(k => k, StringComparer.Ordinal))
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
                "     Every UI project this scan covers now has a resx of its own, so there is no " +
                "literal-text exemption to fall back on — see docs/OPENCODEFORGE-PLAN.md.\n" +
                "  2. Reuse an existing button-label key when the visible label is itself a good " +
                "screen-reader announcement.\n" +
                "  3. See CLAUDE.md \"Accessibility — screen-reader names\" and AGENTS.md invariant I20.\n");
        }
    }

    /// <summary>
    /// Guards the SCAN itself, not the AXAML.  A guard that silently stops
    /// looking is worse than no guard: the suite stays green and the coverage
    /// gap is invisible.  This asserts the scan spans many directories across
    /// every UI project — so re-hardcoding a single folder fails here loudly
    /// instead of quietly shrinking what
    /// <see cref="EveryViewsAxamlFile_AtOrBelowBaseline_UnnamedInteractiveControlCount"/>
    /// inspects.
    /// </summary>
    [TestMethod]
    public void AxamlScan_CoversEveryUiProject_NotOneHardcodedDirectory()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> axamlFiles = ScanAxamlFiles(repoRoot);

        AssertScanIsNonVacuous(repoRoot, axamlFiles);

        HashSet<string> projects = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in axamlFiles)
        {
            // "src/<Project>/..." → "<Project>"
            string[] segments = RepoRelative(repoRoot, path).Split('/');
            if (segments.Length >= 2)
            {
                projects.Add(segments[1]);
            }
        }

        List<string> missing = ProjectsThatMustContributeAxaml
            .Where(p => !projects.Contains(p))
            .ToList();

        Assert.IsTrue(
            missing.Count == 0,
            $"The AXAML scan found no files under these src/ projects: {string.Join(", ", missing)}. " +
            $"It reached only: {string.Join(", ", projects.OrderBy(p => p, StringComparer.Ordinal))}. " +
            "Either the scan narrowed (regression — it must cover src/**/*.axaml, not one hardcoded " +
            "folder), or a UI project was legitimately renamed/removed, in which case update " +
            "ProjectsThatMustContributeAxaml in the same commit.");
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
        string repoRoot = FindRepoRoot();
        int total = 0;
        foreach (string path in ScanAxamlFiles(repoRoot))
        {
            int count = CountUnnamedInteractiveControls(path);
            Console.WriteLine(
                $"[AxamlAccessibilityCoverage] {count,4} unnamed  {RepoRelative(repoRoot, path)}");
            total += count;
        }

        Console.WriteLine(
            $"[AxamlAccessibilityCoverage] Total unnamed interactive controls remaining: {total}");
        // No assertion — informational only.
        Assert.IsTrue(total >= 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every <c>*.axaml</c> under <c>src/</c>, recursively, excluding build
    /// output.  Ordered so failure text and the tracker read the same way run
    /// to run.
    /// </summary>
    private static IReadOnlyList<string> ScanAxamlFiles(string repoRoot)
    {
        string srcDir = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcDir))
        {
            throw new InvalidOperationException(
                $"Expected a src/ directory at '{srcDir}' (repo root resolved from " +
                $"AppContext.BaseDirectory = '{AppContext.BaseDirectory}').");
        }

        return Directory.GetFiles(srcDir, "*.axaml", SearchOption.AllDirectories)
                        .Where(p => !IsUnderBuildOutput(repoRoot, p))
                        .OrderBy(p => RepoRelative(repoRoot, p), StringComparer.Ordinal)
                        .ToList();
    }

    /// <summary>
    /// True when any path segment below the repo root is <c>bin</c> or
    /// <c>obj</c>.  Checked segment-wise rather than by substring so a real
    /// source folder that merely contains those letters is not skipped.
    /// </summary>
    private static bool IsUnderBuildOutput(string repoRoot, string absolutePath)
    {
        string[] segments = Path.GetRelativePath(repoRoot, absolutePath)
                                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                              || s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Repo-relative path with <c>/</c> separators, so Baseline keys and
    /// failure messages read identically on Windows and on Linux CI.
    /// </summary>
    private static string RepoRelative(string repoRoot, string absolutePath) =>
        Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');

    /// <summary>
    /// Shared floor for both guards: the scan must reach many files across
    /// many directories.  Deliberately well below the real counts so ordinary
    /// churn does not trip it, while a collapse back to one folder does.
    /// </summary>
    private static void AssertScanIsNonVacuous(string repoRoot, IReadOnlyList<string> axamlFiles)
    {
        Assert.IsTrue(axamlFiles.Count >= 30,
            $"Expected at least 30 AXAML files under {Path.Combine(repoRoot, "src")}, got " +
            $"{axamlFiles.Count}. The scan likely resolved the wrong path or narrowed its glob.");

        int directories = axamlFiles
            .Select(p => Path.GetDirectoryName(RepoRelative(repoRoot, p)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.IsTrue(directories > 1,
            $"The AXAML scan found files in only {directories} directory. This test exists because " +
            "the scan root was once hardcoded to src/ClaudeForge/Views; a single-directory result " +
            "means that regression is back.");

        Assert.IsTrue(directories >= 8,
            $"Expected AXAML across at least 8 directories under src/, found {directories}. " +
            "The scan has narrowed — it must cover src/**/*.axaml.");
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
    /// Walks up from the test's runtime base directory to the repo root
    /// (identified by sibling <c>src/</c> and <c>tests/</c> directories).
    /// Necessary because the AXAML sources aren't bundled in the test
    /// assembly and are accessed via filesystem path during test execution.
    /// Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.
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
