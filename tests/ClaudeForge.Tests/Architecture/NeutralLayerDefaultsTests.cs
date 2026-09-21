using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Nothing in the neutral layer resolves to Claude's data by DEFAULT.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This is the one class of layering violation that has already shipped a real defect, and
/// the one nothing was watching for.</b> <c>AgentConfigClientCore.FootprintService</c> returned
/// <c>new FootprintService()</c>, whose catalog defaults to Claude's <c>~/.claude</c> categories,
/// and neither OpenCode client overrode it — so both reported <b>Claude's</b> disk footprint as
/// their own, and a delete would have removed the other agent's data. The two-app plan predicted
/// it in writing, naming the call site and the trigger, and it shipped anyway, because a
/// prediction in a document is not a mechanism.
/// </para>
/// <para>
/// ⚠ <b><c>AssemblyLayeringTests</c> cannot catch this, and that is a coverage gap rather than a
/// failure of that guard.</b> All three of its methods are about assembly REFERENCES. Claude-shaped
/// code inside a neutral assembly declares no reference at all, so it is silent on every one of
/// these. See <c>docs/EXTRACTION-VERIFICATION.md</c> §2 and §5.
/// </para>
/// <para>
/// ⭐ <b>The rule is about DEFAULTS, not names.</b> Claude-named symbols are legitimate throughout
/// the neutral layer — <c>SchemaRegistry</c>'s Claude product descriptor names Claude's paths
/// because it describes Claude, and that is correct. What is forbidden is a caller reaching Claude
/// data <i>without having said so</i>: a <c>??</c> fallback, or a constructor chaining to one.
/// </para>
/// <para>
/// ⭐ <b>Closing the constructor shape is what makes <c>new T()</c> safe everywhere.</b> The
/// FootprintService defect was second-order — a neutral type constructing another neutral type
/// whose own default was Claude — and no text search can see that. It becomes unrepresentable
/// rather than detectable once no neutral type has a Claude-defaulting parameterless path at all.
/// </para>
/// </remarks>
[TestClass]
public sealed class NeutralLayerDefaultsTests
{
    /// <summary>A <c>??</c> falling back to a Claude-named symbol.</summary>
    private static readonly Regex NullCoalescingToClaude = new(
        @"\?\?[^;]*\bClaude", RegexOptions.Compiled);

    /// <summary>A constructor chaining through a Claude-named symbol.</summary>
    private static readonly Regex ConstructorChainToClaude = new(
        @":\s*(this|base)\s*\([^)]*Claude", RegexOptions.Compiled);

    /// <summary>String literals, so a path or a message never reads as a symbol.</summary>
    private static readonly Regex StringLiteral = new(
        "@?\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);

    /// <summary>
    /// Known sites, by repo-relative path. ⛔ <b>This list SHRINKS. Adding to it is a decision,
    /// and it needs the reason written next to it.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ✅ <b>EMPTY, as of 2026-09-21 — and it held <c>FootprintService.cs</c> for a long time.</b>
    /// That entry recorded a neutral type whose paths AND catalog both defaulted to Claude's, with
    /// the honest note that fixing it was a real refactor rather than a tidy-up. Both arguments are
    /// now REQUIRED, so <c>new FootprintService()</c> does not compile and the default it warned
    /// about is unrepresentable rather than merely detected.
    /// </para>
    /// <para>
    /// ⚠ <b>The second-order form it also warned about is gone with it.</b>
    /// <c>AgentConfigClientCore</c> built one with <c>new FootprintService()</c> and carried no
    /// Claude token at all, so it appeared in no scan; it now exposes <c>ArtifactPaths</c> and
    /// <c>FootprintCategories</c> seams that default to <see langword="null"/>, and a client
    /// wanting a footprint says so.
    /// </para>
    /// <para>
    /// ⓘ Keeping the array rather than deleting it: the guard's shape is the point, and the next
    /// genuine exception needs somewhere to be justified in front of a reader.
    /// </para>
    /// </remarks>
    private static readonly string[] KnownSites = [];

    [TestMethod]
    public void NoNeutralSourceResolvesToClaudeDataByDefault()
    {
        string repoRoot = FindRepoRoot();
        string[] projects = NeutralProjectDirectories(repoRoot);

        Assert.AreNotEqual(0, projects.Length,
            "Found no packable project under src/, so the neutral layer was never scanned. "
            + "PackageMetadataTests explains what <IsPackable> is doing here — this guard reuses "
            + "it rather than keeping a second list of the eleven.");

        List<string> offenders = [];
        HashSet<string> allowedSitesHit = new(StringComparer.OrdinalIgnoreCase);
        int filesScanned = 0;

        foreach (string directory in projects)
        {
            foreach (string file in EnumerateSource(directory))
            {
                filesScanned++;
                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string code = StripCommentsAndStrings(lines[i]);
                    if (code.Length == 0)
                    {
                        continue;
                    }

                    string? shape =
                        NullCoalescingToClaude.IsMatch(code) ? "?? fallback to Claude data"
                        : ConstructorChainToClaude.IsMatch(code) ? "constructor chaining to a Claude default"
                        : null;

                    if (shape is null)
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(repoRoot, file);

                    if (KnownSites.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    {
                        allowedSitesHit.Add(relative);
                        continue;
                    }

                    offenders.Add($"{relative}:{i + 1} — {shape}: {lines[i].Trim()}");
                }
            }
        }

        Assert.AreNotEqual(0, filesScanned,
            "Scanned no source files under the neutral projects, so this guard proves nothing.");

        // ⭐ A ratchet that does not check its own entries rots into a list of things nobody can
        // remove, because nobody can tell which are still real. An entry that no longer matches
        // has been fixed, and the fix is only finished when the exemption goes with it.
        string[] stale = [.. KnownSites.Except(allowedSitesHit, StringComparer.OrdinalIgnoreCase)];

        Assert.AreEqual(0, stale.Length,
            "These KnownSites entries no longer match anything, which means they were fixed. "
            + "Delete them — this list is a ratchet and only shrinks. Stale: "
            + string.Join(", ", stale));

        Assert.AreEqual(0, offenders.Count,
            "These neutral-layer sites resolve to Claude's data when the caller does not choose. "
            + "A second product then reaches Claude's files with Claude named nowhere near the "
            + "call site — that is how both OpenCode clients came to report Claude's disk "
            + "footprint as their own, where a delete would have removed the other agent's data. "
            + "Take the value from the caller instead; the app that means ~/.claude can say so. "
            + "Offenders: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// The two sites the extraction audit named are still required-argument, not defaulted.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Asserted by reflection because the source scan above would not see them come back in
    /// every form.</b> A parameterless constructor can be reintroduced without the word "Claude"
    /// appearing on its line, and a re-added optional parameter is a signature change rather than
    /// a fallback expression. ⓘ One of these was nearly missed while being fixed: a
    /// <c>SchemaSnapshotService</c> call site spelled <c>new()</c>, target-typed, so it did not
    /// contain the type's name and no search for that name found it. The compiler did.
    /// </remarks>
    [TestMethod]
    public void TheTwoAuditedSitesStillRequireTheirCallerToNameTheHome()
    {
        ConstructorInfo[] snapshotCtors = typeof(SchemaSnapshotService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.AreEqual(0, snapshotCtors.Count(c => c.GetParameters().Length == 0),
            "SchemaSnapshotService has a parameterless constructor again. The one that was "
            + "removed defaulted to {ClaudeHome}/cache, which is Claude's answer — OpenCode's "
            + "schemas do not belong beneath it. Let the caller name the directory.");

        Assert.AreNotEqual(0, snapshotCtors.Length,
            "SchemaSnapshotService has no public constructor at all, so the assertion above is "
            + "passing for the wrong reason.");

        MethodInfo run = typeof(RestoreSidecarCleanup)
            .GetMethod(nameof(RestoreSidecarCleanup.Run), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "RestoreSidecarCleanup.Run is gone; re-read this guard before deleting it.");

        ParameterInfo home = run.GetParameters().First();

        Assert.IsFalse(home.IsOptional,
            $"RestoreSidecarCleanup.Run's '{home.Name}' parameter is optional again. It used to "
            + "default to PlatformPaths.ClaudeHome(ClaudeEnvironment.Empty) when null, so the caller could delete files "
            + "from Claude's home without ever naming it — and ClaudeForge's own console message "
            + "resolved that path a second time, independently of the walk it described.");
    }

    /// <summary>
    /// Everything outside comments and string literals on one line.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Both removals matter and for different reasons.</b> Doc comments legitimately name
    /// <c>PlatformPaths.ClaudeHome</c> in a <c>&lt;see cref&gt;</c> — including the very comment
    /// explaining why the default was removed, which would otherwise make this test fail on its
    /// own fix. And a string literal holding a path or a message is data, not a symbol.
    /// </remarks>
    private static string StripCommentsAndStrings(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith('*')
            || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        string withoutStrings = StringLiteral.Replace(line, "\"\"");

        int comment = withoutStrings.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? withoutStrings[..comment] : withoutStrings;
    }

    /// <summary>Every <c>.cs</c> under a directory, excluding build output.</summary>
    private static IEnumerable<string> EnumerateSource(string directory)
        => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>
    /// The directories of the packable projects — the shared, product-neutral layer.
    /// </summary>
    /// <remarks>
    /// Derived from <c>IsPackable</c> rather than from a list of the eleven, for the reason
    /// <c>PackageMetadataTests</c> gives: a list and the csproj files drift, and the test then
    /// defends the list.
    /// </remarks>
    private static string[] NeutralProjectDirectories(string repoRoot)
        => Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => XDocument.Load(f).Descendants()
                .Where(e => e.Name.LocalName == "IsPackable")
                .Select(e => e.Value.Trim())
                .LastOrDefault() == "true")
            .Select(f => Path.GetDirectoryName(f)!)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Matches <c>PackageMetadataTests.FindRepoRoot()</c>.</summary>
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
