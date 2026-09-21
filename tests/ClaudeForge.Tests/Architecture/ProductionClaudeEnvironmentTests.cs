using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// The shipped app reads its <c>ClaudeEnvironment</c> from the process, never a defaulted one.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>A defaulted environment means the DEFAULT HOME</b>, and that default is deliberate:
/// hundreds of library call sites and test fixtures legitimately pass
/// <c>ClaudeEnvironment.Empty</c>, because a sandbox override outranks it anyway. The cost of a
/// safe default lands here — an app composition root that forgets
/// <see cref="ClaudeEnvironment.FromProcess"/> still compiles, still passes every test, and
/// simply ignores <c>CLAUDE_CONFIG_DIR</c> for every user who set it.
/// </para>
/// <para>
/// ⭐ <b>That is not hypothetical, and it is the same shape twice over.</b>
/// <c>ProductionSchemaRegistryTests</c> exists because <c>App.axaml.cs</c> wrote a bare
/// <c>SchemaRegistry</c> from the initial commit and the shipped app never fetched a schema for
/// a whole phase. The environment is the identical trap: the app **documented**
/// <c>CLAUDE_CONFIG_DIR</c> to the user at <c>EnvVarTooltipConverter.cs</c> and then ignored it
/// everywhere, which is one of the two defects plans/00002 exists to fix. Nothing failed, because
/// there was nothing to fail.
/// </para>
/// <para>
/// ⚠ <b>Source text, not reflection.</b> The choice being guarded is which factory a composition
/// root calls, and a <c>ClaudeEnvironment</c> whose <c>ConfigDir</c> is <c>null</c> is
/// indistinguishable at runtime from one on a machine where the variable is unset. Reflection
/// cannot see the difference; the source can.
/// </para>
/// <para>
/// ⚠ <b>Scans the APP assemblies only.</b> Libraries under <c>src/</c> take the environment from
/// their caller — that is the entire design — and <c>tests/</c> is where the defaulted value is
/// supposed to be used.
/// </para>
/// </remarks>
[TestClass]
public sealed class ProductionClaudeEnvironmentTests
{
    /// <summary>The app assemblies — i.e. every composition root this repo ships.</summary>
    // TWO-APP GUARD NARROWED — plans/00003 Phase 0. "OpenCodeForge" belongs here when it rejoins;
    // its equivalent is OpenCodeEnvironment, so it needs its own token rather than this one.
    private static readonly string[] AppProjectDirs = ["ClaudeForge"];

    /// <summary><c>ClaudeEnvironment.Empty</c> — the explicitly-defaulted value.</summary>
    private static readonly Regex ExplicitEmpty = new(
        @"ClaudeEnvironment\.Empty", RegexOptions.Compiled);

    /// <summary><c>new ClaudeEnvironment()</c> with an empty argument list.</summary>
    private static readonly Regex BareExplicitNew = new(
        @"new\s+ClaudeEnvironment\s*\(\s*\)", RegexOptions.Compiled);

    /// <summary>
    /// The target-typed form, <c>ClaudeEnvironment x = new();</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Matched separately, and it is the spelling that actually shipped the sibling defect.</b>
    /// A guard written against <c>new T()</c> alone stays green over the real thing, because C#
    /// lets the type appear only on the left of the assignment. In this repository's own
    /// conversion, <b>38 of 39</b> client-construction sites were written this way.
    /// </remarks>
    private static readonly Regex BareTargetTypedNew = new(
        @"ClaudeEnvironment\s+\w+\s*=\s*new\s*\(\s*\)", RegexOptions.Compiled);

    private static readonly Regex FromProcess = new(
        @"ClaudeEnvironment\.FromProcess\s*\(", RegexOptions.Compiled);

    [TestMethod]
    public void NoAppCompositionRootBuildsADefaultedClaudeEnvironment()
    {
        string repoRoot = FindRepoRoot();
        List<string> violations = [];
        int fromProcessSites = 0;
        int filesScanned = 0;

        foreach (string appDir in AppProjectDirs)
        {
            string dir = Path.Combine(repoRoot, "src", appDir);
            Assert.IsTrue(Directory.Exists(dir), $"Expected an app project at '{dir}'.");

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                filesScanned++;
                int lineNumber = 0;
                foreach (string raw in File.ReadAllLines(file))
                {
                    lineNumber++;
                    string code = StripComment(raw);
                    if (code.Length == 0)
                    {
                        continue;
                    }

                    fromProcessSites += FromProcess.Matches(code).Count;

                    if (ExplicitEmpty.IsMatch(code) || BareExplicitNew.IsMatch(code)
                        || BareTargetTypedNew.IsMatch(code))
                    {
                        violations.Add(
                            $"{Path.GetFileName(file)}:{lineNumber}  {code.Trim()}");
                    }
                }
            }
        }

        Assert.IsTrue(filesScanned > 20, $"Only {filesScanned} app files scanned — src/ moved.");

        // ⛔ The half that stops this passing while guarding nothing. If the app stopped calling
        // FromProcess entirely — the exact defect — the violation list could still be empty,
        // because "reads no environment at all" is not "reads a defaulted one".
        Assert.IsTrue(
            fromProcessSites > 0,
            "No app source calls ClaudeEnvironment.FromProcess() at all. Either the composition "
            + "root stopped reading the environment — which silently ignores CLAUDE_CONFIG_DIR for "
            + "every user who set it — or this scan no longer matches the code, in which case it "
            + "is green and guarding nothing.");

        Assert.AreEqual(
            0, violations.Count,
            $"{violations.Count} app site(s) build a DEFAULTED ClaudeEnvironment. That resolves the "
            + "default home, so CLAUDE_CONFIG_DIR is ignored — silently, with nothing failing. The "
            + "composition root must use ClaudeEnvironment.FromProcess() and pass the value down:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Strip a line comment, leaving code. A <c>//</c> inside a string literal is respected.
    /// </summary>
    /// <remarks>
    /// ⛔ Load-bearing rather than tidiness: of 17 raw <c>ClaudeEnvironment.Empty</c> matches
    /// across production sources, <b>nine were doc comments</b> explaining the rule. A scan that
    /// did not strip them would be reporting its own documentation as a defect.
    /// </remarks>
    private static string StripComment(string raw)
    {
        string trimmed = raw.TrimStart();
        if (trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        bool inString = false;
        for (int i = 0; i < raw.Length - 1; i++)
        {
            if (raw[i] == '"' && (i == 0 || raw[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && raw[i] == '/' && raw[i + 1] == '/')
            {
                return raw[..i];
            }
        }

        return raw;
    }

    /// <summary>Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.</summary>
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
