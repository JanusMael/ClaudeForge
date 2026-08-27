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
/// ⚠ <b>Scope is the <c>LE.*</c> namespace only, deliberately.</b> Those keys are declared by this
/// repo, in one file, so "referenced but never declared" is decidable here. Theme keys owned by
/// Avalonia or Semi (<c>ThemeForegroundBrush</c>, <c>SemiColor*</c>, …) come from packages this
/// test cannot enumerate, and asserting on them would either need a hardcoded allowlist that goes
/// stale on every package bump or would fail on perfectly valid references.
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
