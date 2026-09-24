using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Bennewitz.Ninja.ScopedEditors.Abstractions;
using Bennewitz.Ninja.ScopedEditors.AvaloniaUI.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Every theme resource a view asks for MUST actually be declared by the app that renders it.
///
/// <para>
/// ⚠ <b>plans/00005 moved the editor library — and its <c>LE.*</c> tokens — into the
/// <c>Bennewitz.Ninja.ScopedEditors</c> package.</b> The two tests that held <c>LE.*</c> references
/// and declarations to each other checked that library's INTERNAL consistency, so they left with
/// it; no test in that repository replaces them yet (see PROGRESS.md). What stays here is the
/// contract that crosses the boundary: the <c>App*</c> tokens the package asks the app for. The
/// history below is why both kinds of check exist.
/// </para>
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
public sealed class ThemeResourceIntegrityTests
{
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

    /// <summary>Any <c>App*</c> string literal — a complete key or the fixed half of a built one.</summary>
    private static readonly Regex AppLiteralPattern = new(
        @"^App[A-Z][A-Za-z0-9]*$",
        RegexOptions.Compiled);

    /// <summary>A <c>&lt;ProjectReference Include="..\Foo\Foo.csproj" /&gt;</c> entry.</summary>
    private static readonly Regex ProjectReferencePattern = new(
        @"ProjectReference\s+Include\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Every <c>App*</c> token the consumed <c>ScopedEditors.AvaloniaUI</c> package asks for is
    /// declared by ClaudeForge — learned from the package's own metadata, not from a list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>This is the contract that crosses the package boundary, and nothing else checks it.</b>
    /// The shared <c>PropertyEditorWrapper</c> asks for <c>AppPropertyHeadingBackgroundBrush</c> and
    /// <c>AppPropertyHeadingBrush</c>, and the app must declare both — the defect this class was
    /// widened for. Since plans/00005 that wrapper ships in a PACKAGE: there is no source to scan and
    /// no project reference to walk, so <see cref="EveryAppTokenAsharedLibraryNeeds_IsDeclaredByEveryApp"/>
    /// cannot see what it needs. A token the package starts asking for would render as styling that
    /// is silently absent — no build error, no exception, no log line.
    /// </para>
    /// <para>
    /// ⭐ <b>Read from the compiled package.</b> Compiled AXAML keeps every resource key as a string
    /// literal, so the assembly's user-string heap names exactly the keys it looks up. Measured at
    /// <c>2026.3.924</c>: the two heading brushes, and the fragment <c>AppSeverity</c>.
    /// </para>
    /// <para>
    /// ⚠ <b>A fragment is the literal half of a key built at runtime</b> —
    /// <c>AppSeverityToBrushConverter.KeyFor</c> interpolates <c>$"AppSeverity{severity}Brush"</c>.
    /// It is accepted only when the package's own key builder completes it; those keys are
    /// <c>AppSeverityTokenCoverageTests</c>'. Any other <c>App*</c> literal must be declared, so a new
    /// computed family fails here until something covers it, rather than passing unnoticed.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryAppTokenTheEditorPackageRequests_IsDeclaredByClaudeForge()
    {
        string repoRoot = FindRepoRoot();
        IReadOnlyList<string> literals = AppLiteralsIn(typeof(AppSeverityToBrushConverter).Assembly);

        HashSet<string> declared = new(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(Path.Combine(repoRoot, "src", "ClaudeForge"), "*.axaml",
                                                   SearchOption.AllDirectories)
                                        .Where(p => !IsUnderBuildOutput(repoRoot, p)))
        {
            foreach (Match m in AppDefinitionPattern.Matches(File.ReadAllText(file)))
            {
                declared.Add(m.Groups[1].Value);
            }
        }

        string[] built = [.. Enum.GetValues<AppSeverity>().Select(AppSeverityToBrushConverter.KeyFor)];

        List<string> satisfied = [.. literals.Where(declared.Contains)];
        List<string> unexplained = [.. literals
            .Where(l => !declared.Contains(l))
            .Where(l => !built.Any(k => k.StartsWith(l, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        // Premise before claim: a reader that found nothing, or found only fragments, proves nothing.
        Assert.True(satisfied.Count > 0,
            $"Found {literals.Count} App* literal(s) in {typeof(AppSeverityToBrushConverter).Assembly.GetName().Name}, "
            + "none of them a key ClaudeForge declares. The metadata read or the declaration scan is "
            + "broken, so this guard would pass without checking anything.");

        MessageAssert.Equal(0, unexplained.Count,
            "The editor package asks for App* token(s) ClaudeForge does not declare:\n  "
            + string.Join("\n  ", unexplained)
            + "\n\nDeclare each in src/ClaudeForge/App.axaml (both theme variants). An unresolvable "
            + "DynamicResource is silently absent styling. If one is the literal half of a key the "
            + "package builds at runtime, cover that family the way AppSeverityTokenCoverageTests does.");
    }

    /// <summary>
    /// Every <c>App*</c> string literal in <paramref name="assembly"/>, read from its user-string
    /// heap — where compiled AXAML keeps resource keys and C# keeps its literals.
    /// </summary>
    private static IReadOnlyList<string> AppLiteralsIn(Assembly assembly)
    {
        using FileStream stream = File.OpenRead(assembly.Location);
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();

        List<string> result = [];
        if (metadata.GetHeapSize(HeapIndex.UserString) <= 1)
        {
            return result;
        }

        for (UserStringHandle handle = MetadataTokens.UserStringHandle(1);
             !handle.IsNil;
             handle = metadata.GetNextHandle(handle))
        {
            string value = metadata.GetUserString(handle);
            if (AppLiteralPattern.IsMatch(value))
            {
                result.Add(value);
            }
        }

        return result;
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
    [Fact]
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

        // TWO-APP GUARD NARROWED — plans/00003 Phase 0. ⛔ Inconclusive rather than lowered to 1:
        // this guard's own words are "a cross-app guard that sees one app proves nothing", and
        // that is still true — so it declines to report a measurement it cannot take, rather than
        // passing vacuously. Restore the assertion when OpenCodeForge rejoins.
        if (appDirs.Count < 2)
        {
            Assert.Skip(
                "Needs two app projects to compare; found "
                + appDirs.Count
                + " under "
                + srcDir
                + ". Restore OpenCodeForge to this tree and this guard runs again.");
        }

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

        Assert.True(checkedPairs >= 1,
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
