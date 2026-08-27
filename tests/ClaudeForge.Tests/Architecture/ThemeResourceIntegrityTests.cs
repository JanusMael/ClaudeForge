using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every <c>LE.*</c> theme resource an AXAML file asks for MUST actually be declared.
///
/// <para>
/// ⛔⛔ <b>Why this guard exists: two tokens were referenced nine times and defined zero times, and
/// nothing in the toolchain said a word.</b> <c>OpenCodeKeybindEditorView.axaml</c> named
/// <c>LE.DangerText</c> (7 references) and <c>LE.DangerBorder</c> (2) from the day the keybinds
/// editor landed, while <c>EditorColors.axaml</c> declared neither. The consequences, in order of
/// how much each one hid the problem:
/// </para>
/// <list type="number">
///   <item>
///   <b>An unresolvable <c>DynamicResource</c> is not a build error.</b> Avalonia's XAML compiler
///   resolves <c>StaticResource</c> at build time but defers <c>DynamicResource</c> to runtime, so
///   the reference compiled cleanly — including under the Release trim publish, which is this
///   repo's strictest AXAML gate.
///   </item>
///   <item>
///   <b>It is not a runtime error either, and it logs nothing.</b> The property is simply left at
///   its default. So the conflict banner, the incomplete banner, the per-row conflict notice, both
///   "held" banners, "capturing", and the incomplete-binding notice all rendered as ORDINARY TEXT
///   inside an INVISIBLE border — every danger signal in that editor, silently absent.
///   </item>
///   <item>
///   <b>A live UI Automation and screenshot pass over that very page missed it.</b> All nine
///   elements are conditional on a conflicting or incomplete keybind, and the sandbox config the
///   pass used had neither, so not one of them was ever on screen to be looked at.
///   </item>
/// </list>
///
/// <para>
/// That combination — invisible to the compiler, invisible to the trim gate, invisible to the log,
/// and invisible to a screenshot unless you happen to seed the triggering state — is precisely the
/// shape a static guard closes cheaply. The same family as this repo's other
/// "the prose claimed it, nothing checked it" findings: a colour that exists only in a reference
/// colours nothing.
/// </para>
///
/// <para>
/// ⚠ <b>Scope is the repo's own two namespaces, deliberately.</b> <c>LE.*</c> (declared by the
/// editor library in <c>EditorColors.axaml</c>) and <c>App*</c> (declared by each host app's
/// <c>App.axaml</c>) are both written here, so "referenced but never declared" is decidable. Theme
/// keys owned by Avalonia or Semi (<c>ThemeForegroundBrush</c>, <c>SemiColor*</c>, …) come from
/// packages this test cannot enumerate, and asserting on them would either need a hardcoded
/// allowlist that goes stale on every package bump or would fail on perfectly valid references.
/// </para>
///
/// <para>
/// ⛔⛔ <b>The <c>App*</c> namespace needs a DIFFERENT rule, and finding out why turned up a second
/// live defect.</b> <c>LE.*</c> keys have one declaration site, so a repo-wide set comparison
/// settles them. <c>App*</c> keys are declared PER APP, which means a token can be perfectly
/// declared somewhere in the repo and still be missing from the app that renders it. That is
/// exactly what had happened: <c>PropertyEditorWrapper</c>, in the shared editor library, asks for
/// <c>AppPropertyHeadingBackgroundBrush</c> and <c>AppPropertyHeadingBrush</c>; ClaudeForge
/// declared both, OpenCodeForge had no <c>Application.Resources</c> block at all, and that wrapper
/// wraps every property editor on every OpenCode settings page. Measured on screen: the property
/// names rendered as plain bold text still carrying the invisible chip's padding.
/// </para>
///
/// <para>
/// So <see cref="EveryAppTokenAsharedLibraryNeeds_IsDeclaredByEveryApp"/> asserts the invariant
/// that actually matters: <b>a token referenced by a SHARED library must be declared by EVERY app
/// that consumes it.</b> An app referencing its own tokens in its own views is unconstrained —
/// that case is already covered by the repo-wide check, and constraining it further would forbid
/// one app from having a token the other has no use for.
/// </para>
/// </summary>
[TestClass]
public sealed class ThemeResourceIntegrityTests
{
    /// <summary>
    /// <c>{DynamicResource LE.Foo}</c> / <c>{StaticResource LE.Foo}</c>, in markup-extension form
    /// or as a property-element <c>ResourceKey</c>. Captures the key without the <c>LE.</c> prefix
    /// left off, so failure text names exactly what to add to <c>EditorColors.axaml</c>.
    /// </summary>
    private static readonly Regex ReferencePattern = new(
        @"\{\s*(?:Dynamic|Static)Resource\s+(LE\.[A-Za-z0-9_]+)\s*\}",
        RegexOptions.Compiled);

    /// <summary>A resource declaration: <c>x:Key="LE.Foo"</c>.</summary>
    private static readonly Regex DefinitionPattern = new(
        @"x:Key\s*=\s*""(LE\.[A-Za-z0-9_]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// <c>{DynamicResource AppFoo}</c> — the host-supplied token namespace, declared per app in
    /// each <c>App.axaml</c>.
    /// </summary>
    private static readonly Regex AppReferencePattern = new(
        @"\{\s*(?:Dynamic|Static)Resource\s+(App[A-Za-z0-9_]+)\s*\}",
        RegexOptions.Compiled);

    /// <summary>An <c>App*</c> declaration: <c>x:Key="AppFoo"</c>.</summary>
    private static readonly Regex AppDefinitionPattern = new(
        @"x:Key\s*=\s*""(App[A-Za-z0-9_]+)""",
        RegexOptions.Compiled);

    /// <summary>A <c>&lt;ProjectReference Include="..\Foo\Foo.csproj" /&gt;</c> entry.</summary>
    private static readonly Regex ProjectReferencePattern = new(
        @"ProjectReference\s+Include\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// A <c>"LE.Foo"</c> string literal in C#, which is how a control or converter looks a brush
    /// up at runtime.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Scanning C# is what makes the unused-token direction honest.</b> Six of the eight
    /// tokens are referenced from code and never from markup —
    /// <c>BoolToStatusBrushConverter</c> resolves the tri-state trio and
    /// <c>LinkifiedTextBlock</c> the three link colours — so an AXAML-only scan reports all six as
    /// dead. The first draft of this class handled that with a hardcoded allowlist of "used from
    /// code" keys, which is a list that silently goes stale the moment a control stops using one:
    /// the token would then be genuinely dead and the allowlist would keep vouching for it.
    /// Reading the actual literals costs one more directory walk and cannot go stale.
    /// </remarks>
    private static readonly Regex CodeReferencePattern = new(
        @"""(LE\.[A-Za-z0-9_]+)""",
        RegexOptions.Compiled);

    [TestMethod]
    public void EveryReferencedThemeResource_IsActuallyDeclared()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> axamlFiles = ScanAxamlFiles(repoRoot);

        HashSet<string> declared = [];
        // key -> the files that ask for it, so a failure says where to look.
        Dictionary<string, SortedSet<string>> referencedBy = new(StringComparer.Ordinal);

        foreach (string file in axamlFiles)
        {
            string text = File.ReadAllText(file);
            string relative = RepoRelative(repoRoot, file);

            foreach (Match m in DefinitionPattern.Matches(text))
            {
                declared.Add(m.Groups[1].Value);
            }

            foreach (Match m in ReferencePattern.Matches(text))
            {
                string key = m.Groups[1].Value;
                if (!referencedBy.TryGetValue(key, out SortedSet<string>? files))
                {
                    files = new SortedSet<string>(StringComparer.Ordinal);
                    referencedBy[key] = files;
                }

                files.Add(relative);
            }
        }

        // A scan that finds nothing proves nothing. Both floors are far below the real counts so
        // ordinary churn does not trip them, while a broken regex or a collapsed scan root does.
        Assert.IsTrue(declared.Count >= 6,
            $"Expected at least 6 LE.* declarations across {axamlFiles.Count} AXAML files, found " +
            $"{declared.Count}. The scan or the declaration pattern is broken, not the repo.");
        Assert.IsTrue(referencedBy.Count >= 5,
            $"Expected at least 5 distinct LE.* references across {axamlFiles.Count} AXAML files, " +
            $"found {referencedBy.Count}. The scan or the reference pattern is broken.");

        List<string> undeclared = referencedBy.Keys
                                              .Where(k => !declared.Contains(k))
                                              .OrderBy(k => k, StringComparer.Ordinal)
                                              .ToList();

        if (undeclared.Count > 0)
        {
            IEnumerable<string> lines = undeclared.Select(k =>
                $"  {k}  ({referencedBy[k].Count} reference(s) in " +
                $"{string.Join(", ", referencedBy[k])})");

            Assert.Fail(
                $"{undeclared.Count} LE.* theme resource(s) are referenced but never declared:\n" +
                string.Join('\n', lines) +
                "\n\nAn unresolvable DynamicResource does not fail the build, does not throw, and " +
                "logs nothing — Avalonia leaves the property at its default, so the control just " +
                "renders unstyled. Declare each key in " +
                "src/LayeredEditors.Avalonia/Themes/EditorColors.axaml, or correct the reference. " +
                $"Declared keys are: {string.Join(", ", declared.OrderBy(k => k, StringComparer.Ordinal))}.");
        }
    }

    /// <summary>
    /// The inverse direction: a declared token nobody uses is dead theme surface.
    /// </summary>
    /// <remarks>
    /// ⚠ Reported as a failure rather than tolerated, matching how this repo already treats an
    /// unreferenced resx key (a build error via <c>Directory.Build.targets</c>). The reasoning is
    /// the same: a palette entry that no view asks for is either a leftover from a removed control
    /// or a typo'd counterpart to one of the undeclared keys above — and the second case is a live
    /// defect wearing the first case's clothes.
    /// </remarks>
    [TestMethod]
    public void EveryDeclaredThemeResource_IsActuallyUsed()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> axamlFiles = ScanAxamlFiles(repoRoot);

        HashSet<string> declared = [];
        HashSet<string> referenced = [];

        foreach (string file in axamlFiles)
        {
            string text = File.ReadAllText(file);

            foreach (Match m in DefinitionPattern.Matches(text))
            {
                declared.Add(m.Groups[1].Value);
            }

            foreach (Match m in ReferencePattern.Matches(text))
            {
                referenced.Add(m.Groups[1].Value);
            }
        }

        int fromCode = 0;
        foreach (string file in ScanCSharpFiles(repoRoot))
        {
            foreach (Match m in CodeReferencePattern.Matches(File.ReadAllText(file)))
            {
                if (referenced.Add(m.Groups[1].Value))
                {
                    fromCode++;
                }
            }
        }

        Assert.IsTrue(declared.Count >= 6,
            $"Expected at least 6 LE.* declarations, found {declared.Count}. The scan is broken.");
        Assert.IsTrue(fromCode >= 1,
            "Expected at least one LE.* brush to be resolved from C# (BoolToStatusBrushConverter "
            + "and LinkifiedTextBlock both do). Finding none means the C# scan or its pattern is "
            + "broken, which would make every code-only token look dead.");

        List<string> unused = declared
                              .Where(k => !referenced.Contains(k))
                              .OrderBy(k => k, StringComparer.Ordinal)
                              .ToList();

        Assert.AreEqual(0, unused.Count,
            $"{unused.Count} LE.* theme resource(s) are declared but referenced from neither AXAML " +
            $"nor C#: {string.Join(", ", unused)}. Either wire them up or delete them — and check " +
            "first whether one is a misspelling of a key some view references and cannot resolve, " +
            $"which {nameof(EveryReferencedThemeResource_IsActuallyDeclared)} would also be " +
            "reporting.");
    }

    /// <summary>
    /// Every <c>*.axaml</c> under <c>src/</c>, recursively, excluding build output.
    /// </summary>
    /// <remarks>
    /// Deliberately the same repo-wide shape as
    /// <c>AxamlAccessibilityCoverageTests.ScanAxamlFiles</c> rather than a path to one project —
    /// that test's own history is a hardcoded <c>src/ClaudeForge/Views</c> that left every other
    /// UI project unguarded, and the undeclared tokens this class exists for live in
    /// <c>src/OpenCode.Avalonia/</c>.
    /// </remarks>
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
    /// A token a shared library asks for must be declared by every app that consumes that library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>The defect this was written for:</b> <c>PropertyEditorWrapper</c> in
    /// <c>LayeredEditors.Avalonia</c> references <c>AppPropertyHeadingBackgroundBrush</c> and
    /// <c>AppPropertyHeadingBrush</c>. ClaudeForge declared both in its <c>App.axaml</c>
    /// ThemeDictionaries; OpenCodeForge had no <c>Application.Resources</c> block at all. That
    /// wrapper wraps every property editor on every settings page in BOTH apps, so every property
    /// heading in OpenCodeForge rendered with a null background — visible on screen only if you
    /// knew to compare the two apps side by side.
    /// </para>
    /// <para>
    /// ⚠ <b>Consumption is resolved through the real transitive project graph, not assumed.</b> The
    /// obvious shortcut — "every app must declare every <c>App*</c> token any library mentions" — is
    /// conservative today only because both apps happen to reference the same UI libraries. The
    /// moment a library is consumed by one app and not the other, that shortcut starts demanding
    /// tokens from an app that can never render them, and the natural way to quiet a wrong test is
    /// to declare a dead token. Walking the closure costs about fifteen lines and cannot be wrong
    /// in that direction.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void EveryAppTokenAsharedLibraryNeeds_IsDeclaredByEveryApp()
    {
        string repoRoot = FindRepoRoot();
        string srcDir = Path.Combine(repoRoot, "src");

        // An "app" is a project directory with an App.axaml; everything else under src/ is a
        // library. Both apps declare their tokens there, so this is also where to look for them.
        List<string> appDirs = Directory.GetFiles(srcDir, "App.axaml", SearchOption.AllDirectories)
                                        .Where(p => !IsUnderBuildOutput(repoRoot, p))
                                        .Select(p => Path.GetDirectoryName(p)!)
                                        .OrderBy(p => p, StringComparer.Ordinal)
                                        .ToList();

        Assert.IsTrue(appDirs.Count >= 2,
            $"Expected at least 2 app projects (ClaudeForge and OpenCodeForge) under {srcDir}, "
            + $"found {appDirs.Count}. A cross-app guard that sees one app proves nothing.");

        var failures = new List<string>();
        int checkedPairs = 0;

        foreach (string appDir in appDirs)
        {
            string appName = Path.GetFileName(appDir);

            // What this app declares, anywhere in its own AXAML.
            HashSet<string> declared = [];
            foreach (string file in Directory.GetFiles(appDir, "*.axaml", SearchOption.AllDirectories)
                                            .Where(p => !IsUnderBuildOutput(repoRoot, p)))
            {
                foreach (Match m in AppDefinitionPattern.Matches(File.ReadAllText(file)))
                {
                    declared.Add(m.Groups[1].Value);
                }
            }

            // What the libraries THIS app actually pulls in will ask for at render time.
            foreach (string libDir in TransitiveProjectDirs(repoRoot, appDir))
            {
                if (libDir.Equals(appDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(libDir, "*.axaml", SearchOption.AllDirectories)
                                                .Where(p => !IsUnderBuildOutput(repoRoot, p)))
                {
                    string relative = RepoRelative(repoRoot, file);
                    foreach (Match m in AppReferencePattern.Matches(File.ReadAllText(file)))
                    {
                        checkedPairs++;
                        string key = m.Groups[1].Value;
                        if (!declared.Contains(key))
                        {
                            failures.Add($"  {appName} does not declare {key}, required by {relative}");
                        }
                    }
                }
            }
        }

        Assert.IsTrue(checkedPairs >= 1,
            "Found no App* token references in any library either app consumes. The reference "
            + "pattern or the project-graph walk is broken, so this guard would pass vacuously.");

        if (failures.Count > 0)
        {
            Assert.Fail(
                $"{failures.Count} App* token(s) are referenced by a shared library but not "
                + $"declared by the app that renders it:\n{string.Join('\n', failures.Distinct())}"
                + "\n\nAn unresolvable DynamicResource does not fail the build, does not throw, and "
                + "logs nothing — the control keeps its default, so the styling is silently absent. "
                + "Declare the token in that app's App.axaml (in ThemeDictionaries when the value "
                + "must differ between Semi Light and Semi Dark).");
        }
    }

    /// <summary>
    /// Every project directory reachable from <paramref name="startDir"/> through
    /// <c>ProjectReference</c>, including itself.
    /// </summary>
    private static IReadOnlyCollection<string> TransitiveProjectDirs(string repoRoot, string startDir)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> queue = new();
        queue.Enqueue(startDir);

        while (queue.Count > 0)
        {
            string dir = queue.Dequeue();
            if (!seen.Add(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            foreach (string proj in Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                foreach (Match m in ProjectReferencePattern.Matches(File.ReadAllText(proj)))
                {
                    // csproj paths are Windows-separated; normalise so this resolves on Linux CI.
                    string rel = m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                    string? target = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(dir, rel)));
                    if (target is not null)
                    {
                        queue.Enqueue(target);
                    }
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Every <c>*.cs</c> under <c>src/</c>, so a brush resolved by a control or converter counts as
    /// used.
    /// </summary>
    private static IReadOnlyList<string> ScanCSharpFiles(string repoRoot)
        => Directory.GetFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
                    .Where(p => !IsUnderBuildOutput(repoRoot, p))
                    .OrderBy(p => RepoRelative(repoRoot, p), StringComparer.Ordinal)
                    .ToList();

    /// <summary>
    /// True when any path segment below the repo root is <c>bin</c> or <c>obj</c>. Checked
    /// segment-wise rather than by substring so a real source folder that merely contains those
    /// letters is not skipped.
    /// </summary>
    private static bool IsUnderBuildOutput(string repoRoot, string absolutePath)
    {
        string[] segments = Path.GetRelativePath(repoRoot, absolutePath)
                                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                              || s.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Repo-relative path with <c>/</c> separators, so failure messages read identically on
    /// Windows and on Linux CI.
    /// </summary>
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
