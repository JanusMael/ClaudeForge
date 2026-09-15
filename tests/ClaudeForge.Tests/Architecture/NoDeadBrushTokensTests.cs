using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// A brush token this repository declares must be used by something.
/// </summary>
/// <remarks>
/// <para>
/// A dead token is not merely clutter. It carries a comment explaining a decision that no longer
/// applies, so the next reader takes it as current: <c>InstallBannerCodeBorderBrush</c> sat beside
/// the banner palette for long enough that "is the install banner missing a border?" became a
/// reasonable question to have to answer. <c>SuggestionGroupHeaderBrush</c> was worse — its comment
/// explained that Semi does not guarantee a Fluent key "so we own this token explicitly", which
/// reads as a live constraint on a picker that had since moved to inherited foregrounds.
/// </para>
/// <para>
/// ⛔⛔ <b>The compat shims are EXCLUDED, and without that this test is pure noise.</b>
/// <c>FluentKeys.Semi.axaml</c> (210 brush keys) and <c>SimpleKeys.Semi.axaml</c> (50) exist to
/// satisfy templates inside Semi, Fluent and third-party controls — references this scan cannot
/// see, because they are not in this repository at all. Measured: 167 of 307 declared brush keys
/// look unreferenced, and 165 of those are shim entries doing exactly their job.
/// </para>
/// <para>
/// ⛔ <b>Four key families are BUILT AT RUNTIME and can never be found by a text scan.</b>
/// <c>AppSeverityToBrushConverter</c> asks for <c>$"AppSeverity{severity}Brush"</c>; the change-kind,
/// common-action and scope converters do the same for their own shapes. A guard that did not know
/// this would declare nine live tokens dead, and the natural response — deleting them — removes the
/// colour and leaves the converter silently taking its fallback hex.
/// </para>
/// <para>
/// ⭐ <b>Those exemptions have to earn their keep, or they become an allow-list that outlives its
/// reason.</b> <see cref="EveryComputedFamilyExemptionIsStillEarned"/> checks that the named source
/// still constructs a key of that shape, so deleting a converter makes its exemption lapse and its
/// tokens correctly report dead.
/// </para>
/// <para>
/// ⚠ <b>Comments are stripped before the reference scan.</b> A key merely NAMED in prose is not a
/// use, and this is not hypothetical: while F2 was being written, new comments mentioning
/// <c>AppSeverityCriticalBrush</c> and <c>AppSeverityCautionBrush</c> made both look referenced
/// when neither is named anywhere in markup or code. Stripping changes no verdict today — the
/// family exemption already covers those two — and it closes the hiding place for free.
/// </para>
/// </remarks>
[TestClass]
public sealed class NoDeadBrushTokensTests
{
    /// <summary>
    /// Third-party theme shims. Their consumers live in packages, not in this repository.
    /// </summary>
    private static readonly string[] CompatShims =
    [
        "src/ClaudeForge/Resources/Compat/FluentKeys.Semi.axaml",
        "src/ClaudeForge/Resources/Compat/SimpleKeys.Semi.axaml",
    ];

    /// <param name="KeyPattern">The shape of key this family produces.</param>
    /// <param name="BuiltBy">
    /// The source file that constructs it. Named so the exemption can be re-verified rather than
    /// believed — see <see cref="EveryComputedFamilyExemptionIsStillEarned"/>.
    /// </param>
    /// <param name="ConstructionPattern">
    /// How the key is built in that file. Matched against the source so a converter that stops
    /// building keys stops exempting them.
    /// </param>
    private sealed record ComputedFamily(
        string KeyPattern, string BuiltBy, string ConstructionPattern);

    private static readonly ComputedFamily[] Families =
    [
        new(@"^AppSeverity\w+Brush$",
            "src/LayeredEditors.Avalonia/Converters/AppSeverityToBrushConverter.cs",
            @"\$""AppSeverity\{\w+\}Brush"""),
        new(@"^AppChangeKind\w+Brush$",
            "src/ClaudeForge/Converters/ChangeKindToBrushConverter.cs",
            @"\$""AppChangeKind\{\w+\}Brush"""),
        new(@"^common-action-brush-[\w-]+$",
            "src/ClaudeForge/Converters/CommonActionKindToBrushConverter.cs",
            @"\$""common-action-brush-\{"),
        new(@"^scope-brush-[\w-]+$",
            "src/ClaudeForge/Converters/ScopeToBrushConverter.cs",
            @"\$""scope-brush-\{"),
    ];

    /// <summary>
    /// Below this, the declaration scan has stopped finding the palette and would pass over
    /// anything. 69 keys are declared outside the shims today.
    /// </summary>
    private const int MinimumDeclarations = 50;

    private static readonly Regex Declaration =
        new(@"<(?<element>\w*Brush)\s+x:Key\s*=\s*""(?<key>[^""]+)""", RegexOptions.Compiled);

    [TestMethod]
    public void EveryDeclaredBrushTokenIsUsedBySomething()
    {
        string repoRoot = FindRepoRoot();
        Dictionary<string, SortedSet<string>> declarations = DeclarationsOutsideShims(repoRoot);

        Assert.IsTrue(declarations.Count >= MinimumDeclarations,
            $"only {declarations.Count} brush token(s) found outside the compat shims, expected at "
            + $"least {MinimumDeclarations}. The declaration pattern has stopped matching, so this "
            + "test is no longer checking anything.");

        string axaml = string.Join("\n", SourceFiles(repoRoot, "*.axaml").Select(f => StripXmlComments(f.Text)));
        string code = string.Join("\n", SourceFiles(repoRoot, "*.cs").Select(f => StripCsComments(f.Text)));

        List<string> dead = [];

        foreach ((string key, SortedSet<string> where) in declarations.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (Families.Any(f => Regex.IsMatch(key, f.KeyPattern)))
            {
                continue;
            }

            int declared = Regex.Matches(axaml, $@"x:Key\s*=\s*""{Regex.Escape(key)}""").Count;
            int uses = Regex.Matches(axaml, Regex.Escape(key)).Count - declared
                       + Regex.Matches(code, Regex.Escape(key)).Count;

            if (uses == 0)
            {
                dead.Add($"{key}  —  declared in {string.Join(", ", where)}");
            }
        }

        Assert.IsTrue(dead.Count == 0,
            $"{dead.Count} brush token(s) are declared and never used:\n  "
            + string.Join("\n  ", dead)
            + "\n\nDelete the declaration, or wire it up. A token nobody references still carries a "
            + "comment that reads as current, and the next person to touch that area has to prove "
            + "the absence means nothing. If the key is built at runtime, add it to Families with "
            + "the source that builds it.");
    }

    /// <summary>
    /// ⭐ Keeps the exemptions honest. An exemption whose converter is gone would go on hiding real
    /// dead tokens forever, and nothing else would ever say so.
    /// </summary>
    [TestMethod]
    public void EveryComputedFamilyExemptionIsStillEarned()
    {
        string repoRoot = FindRepoRoot();
        List<string> stale = [];

        foreach (ComputedFamily family in Families)
        {
            string path = Path.Combine(repoRoot, family.BuiltBy.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                stale.Add($"{family.KeyPattern}: {family.BuiltBy} no longer exists");
                continue;
            }

            // ⚠ Comments stripped first. These converters DOCUMENT their key shape in prose
            // ("Looks up scope-brush-{id} from application resources"), so matching the raw file
            // would let a doc comment keep the exemption alive after the code that built the key
            // was deleted — the exemption would then hide every token of that shape, permanently.
            if (!Regex.IsMatch(StripCsComments(File.ReadAllText(path)), family.ConstructionPattern))
            {
                stale.Add(
                    $"{family.KeyPattern}: {family.BuiltBy} no longer builds a key matching "
                    + $"/{family.ConstructionPattern}/");
            }
        }

        Assert.IsTrue(stale.Count == 0,
            $"{stale.Count} computed-family exemption(s) no longer describe the code:\n  "
            + string.Join("\n  ", stale)
            + "\n\nRemove the exemption. While it stands it hides every token of that shape from "
            + $"{nameof(EveryDeclaredBrushTokenIsUsedBySomething)}, including genuinely dead ones.");
    }

    /// <summary>
    /// The shim exclusion is by PATH, so a move or rename silently turns 260 shim entries into
    /// reported dead tokens. Failing here names the cause instead.
    /// </summary>
    [TestMethod]
    public void TheCompatShimsAreWhereTheExclusionExpects()
    {
        string repoRoot = FindRepoRoot();

        foreach (string shim in CompatShims)
        {
            string path = Path.Combine(repoRoot, shim.Replace('/', Path.DirectorySeparatorChar));

            Assert.IsTrue(File.Exists(path),
                $"{shim} is excluded from the dead-token scan but does not exist. If it moved, "
                + "update CompatShims — otherwise its entries will be reported as dead tokens, "
                + "which they are not: their consumers are templates inside theme packages.");

            Assert.IsTrue(Declaration.Matches(File.ReadAllText(path)).Count > 20,
                $"{shim} declares almost no brushes, so it is probably no longer the shim this "
                + "exclusion was written for");
        }
    }

    private static Dictionary<string, SortedSet<string>> DeclarationsOutsideShims(string repoRoot)
    {
        Dictionary<string, SortedSet<string>> declarations = [];

        foreach ((string relative, string text) in SourceFiles(repoRoot, "*.axaml"))
        {
            if (CompatShims.Contains(relative, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (Match m in Declaration.Matches(text))
            {
                string key = m.Groups["key"].Value;

                if (!declarations.TryGetValue(key, out SortedSet<string>? where))
                {
                    declarations[key] = where = new SortedSet<string>(StringComparer.Ordinal);
                }

                where.Add(relative);
            }
        }

        return declarations;
    }

    private static string StripXmlComments(string s) =>
        Regex.Replace(s, @"<!--.*?-->", " ", RegexOptions.Singleline);

    private static string StripCsComments(string s) =>
        Regex.Replace(
            Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline),
            @"//[^\n]*", " ");

    private static IEnumerable<(string Relative, string Text)> SourceFiles(
        string repoRoot, string pattern)
    {
        foreach (string path in Directory.EnumerateFiles(
                     Path.Combine(repoRoot, "src"), pattern, SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (
                Path.GetRelativePath(repoRoot, path).Replace('\\', '/'),
                File.ReadAllText(path));
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "could not locate the repository root (ClaudeForge.slnx)");
        return dir.FullName;
    }
}
