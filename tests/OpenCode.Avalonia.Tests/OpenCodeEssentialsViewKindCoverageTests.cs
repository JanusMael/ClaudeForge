using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Avalonia.Essentials;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Tests;

/// <summary>
/// Every card kind the page actually builds has a surface in the page's markup.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>A card whose kind the view cannot draw renders as an empty rectangle.</b> The title,
/// the body and the severity dot all still appear — only the editor is missing — so it reads as
/// "this setting has no controls yet" rather than as a bug. It compiles, it runs, it logs nothing,
/// and no existing test looks at it. Exactly the shape as the missing-DataTemplate trap one level
/// up, which is why the same treatment applies.
/// </para>
/// <para>
/// ⚠ <b>This is the test that fires when the remaining fourteen cards land.</b> The page today
/// draws two of the six kinds, on purpose: writing the other four surfaces blind would ship markup
/// nobody has seen render. The moment a Bool / Int / EnumString / StringList card is added, this
/// fails and names the kind, instead of leaving a blank card in the app.
/// </para>
/// <para>
/// Reads parsed ATTRIBUTE VALUES, never file text, so a kind named in one of the file's comments
/// cannot count as a surface — and the comments here do name all six.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeEssentialsViewKindCoverageTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ocesskind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, ".config", "opencode"));
        PlatformPaths.TestUserProfileOverride = _root;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private static string RepoRoot()
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

    /// <summary>The kinds the markup has an <c>IsVisible</c> surface for.</summary>
    private static HashSet<string> KindsWithASurface()
    {
        string view = Path.Combine(
            RepoRoot(), "src", "OpenCode.Avalonia", "Essentials", "OpenCodeEssentialsView.axaml");
        Assert.IsTrue(File.Exists(view), $"'{view}' not found.");

        Regex reference = new(
            @"EssentialsCardKindConverters\.Is(\w+)", RegexOptions.CultureInvariant);

        return
        [
            .. XDocument
                .Load(view)
                .Descendants()
                .SelectMany(e => e.Attributes())
                .Select(a => a.Value)
                .SelectMany(v => reference.Matches(v).Select(m => m.Groups[1].Value)),
        ];
    }

    private OpenCodeEssentialsViewModel BuildPage() => new(
        client: null,
        new OpenCodeEnvironment(ProjectConfigDisabled: true),
        new SchemaPageLayout
        {
            PropertyToPage = new Dictionary<string, string>(StringComparer.Ordinal),
            PageOrder = [],
            FallbackPage = "Advanced",
        });

    [TestMethod]
    public void EveryKindThePageBuilds_HasASurfaceInTheMarkup()
    {
        HashSet<string> drawn = KindsWithASurface();
        List<string> used =
        [
            .. BuildPage().Cards.Select(c => c.Kind.ToString()).Distinct(StringComparer.Ordinal),
        ];

        Assert.IsTrue(used.Count > 0,
            "No cards were built, so this test checked nothing.");

        List<string> missing = [.. used.Where(k => !drawn.Contains(k))];

        Assert.AreEqual(0, missing.Count,
            $"{missing.Count} card kind(s) are built by OpenCodeEssentialsViewModel but have no "
            + "editor surface in OpenCodeEssentialsView.axaml, so those cards render with a title "
            + "and no controls:\n  " + string.Join("\n  ", missing.Order(StringComparer.Ordinal))
            + "\nAdd the surface to the view, next to the existing ones.");
    }

    /// <summary>
    /// The reverse direction: markup for a kind nothing builds.
    /// </summary>
    /// <remarks>
    /// Not a defect on its own — but it means an <c>IsVisible</c> binding nobody has ever seen
    /// evaluate true, which is how markup written ahead of its cards quietly rots. Reported so the
    /// pair stays deliberate.
    /// </remarks>
    [TestMethod]
    public void TheMarkupDrawsNoKindThePageNeverBuilds()
    {
        HashSet<string> drawn = KindsWithASurface();
        HashSet<string> used =
        [
            .. BuildPage().Cards.Select(c => c.Kind.ToString()),
        ];

        Assert.IsTrue(drawn.Count > 0,
            "No kind converters were found in the markup, so this test checked nothing — the "
            + "binding syntax it scans for has probably changed.");

        List<string> unused = [.. drawn.Where(k => !used.Contains(k))];

        Assert.AreEqual(0, unused.Count,
            "OpenCodeEssentialsView.axaml draws a surface for card kind(s) the page never builds: "
            + string.Join(", ", unused.Order(StringComparer.Ordinal))
            + ". Either add the card or drop the markup.");
    }

    /// <summary>
    /// Every name the markup scans for is a real converter, so a rename cannot make this test
    /// vacuously pass.
    /// </summary>
    [TestMethod]
    public void EveryKindNamedInTheMarkup_IsARealCardKind()
    {
        foreach (string kind in KindsWithASurface())
        {
            Assert.IsTrue(Enum.TryParse(kind, out EssentialsCardKind _),
                $"The markup references EssentialsCardKindConverters.Is{kind}, which is not an "
                + "EssentialsCardKind — so the surface it guards can never become visible.");
        }
    }

    /// <summary>
    /// The two EnumString <em>flavours</em> each have a surface, not just the kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>A canary with a deliberately empty prediction found this hole, and the prediction was
    /// right.</b> Deleting the free-form <c>AutoCompleteBox</c> from the markup reddened
    /// <b>nothing</b>: the test above scans for <c>EssentialsCardKindConverters.Is*</c>, and both
    /// flavours live inside one container keyed on <c>IsEnumString</c> — so the kind still had "a
    /// surface" while three cards (<c>model</c>, <c>small_model</c>, <c>default_agent</c>) rendered
    /// with no editor at all. Exactly the blank-card failure one level down from the one that test
    /// exists to prevent.
    /// </para>
    /// <para>
    /// The flavours are discriminated by two card properties rather than by a converter, because
    /// <c>AllowsFreeForm</c> is not part of the kind — which is why the converter scan cannot see
    /// them and this check is separate rather than folded in.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void BothEnumStringFlavoursThePageBuilds_HaveASurfaceInTheMarkup()
    {
        HashSet<string> bound = FlavourBindingsInTheMarkup();
        IReadOnlyList<EssentialsCardViewModel> cards = [.. BuildPage().Cards];

        Assert.IsTrue(cards.Any(c => c.Kind == EssentialsCardKind.EnumString),
            "The page builds no EnumString card, so this test checked nothing.");

        List<string> missing = [];

        if (cards.Any(c => c.IsStrictEnumString) && !bound.Contains(nameof(EssentialsCardViewModel.IsStrictEnumString)))
        {
            missing.Add(nameof(EssentialsCardViewModel.IsStrictEnumString));
        }

        if (cards.Any(c => c.IsFreeFormEnumString) && !bound.Contains(nameof(EssentialsCardViewModel.IsFreeFormEnumString)))
        {
            missing.Add(nameof(EssentialsCardViewModel.IsFreeFormEnumString));
        }

        Assert.AreEqual(0, missing.Count,
            "OpenCodeEssentialsView.axaml has no editor bound to: "
            + string.Join(", ", missing)
            + " — yet the page builds cards of that flavour, so they render with a title and no "
            + "control. The kind-level test cannot see this: both flavours sit inside one "
            + "IsEnumString container, so the KIND still has a surface.");
    }

    /// <summary>The reverse direction, as for the kinds.</summary>
    [TestMethod]
    public void TheMarkupBindsNoEnumStringFlavourThePageNeverBuilds()
    {
        HashSet<string> bound = FlavourBindingsInTheMarkup();
        IReadOnlyList<EssentialsCardViewModel> cards = [.. BuildPage().Cards];

        Assert.IsTrue(bound.Count > 0,
            "No flavour binding was found in the markup, so the syntax this scans for has "
            + "probably changed and the test above is now vacuous too.");

        List<string> unused = [];

        if (bound.Contains(nameof(EssentialsCardViewModel.IsStrictEnumString))
            && !cards.Any(c => c.IsStrictEnumString))
        {
            unused.Add(nameof(EssentialsCardViewModel.IsStrictEnumString));
        }

        if (bound.Contains(nameof(EssentialsCardViewModel.IsFreeFormEnumString))
            && !cards.Any(c => c.IsFreeFormEnumString))
        {
            unused.Add(nameof(EssentialsCardViewModel.IsFreeFormEnumString));
        }

        Assert.AreEqual(0, unused.Count,
            "OpenCodeEssentialsView.axaml binds an editor to " + string.Join(", ", unused)
            + ", which no card ever satisfies — markup nobody has seen evaluate true. Either add "
            + "the card or drop the control.");
    }

    /// <summary>Which flavour properties the markup binds an <c>IsVisible</c> to.</summary>
    /// <remarks>
    /// Parsed attribute values, never file text, so the names in this file's own comments — and
    /// they appear there — cannot count as a surface.
    /// </remarks>
    private static HashSet<string> FlavourBindingsInTheMarkup()
    {
        string view = Path.Combine(
            RepoRoot(), "src", "OpenCode.Avalonia", "Essentials", "OpenCodeEssentialsView.axaml");
        Assert.IsTrue(File.Exists(view), $"'{view}' not found.");

        Regex binding = new(
            @"^\s*\{\s*Binding\s+(IsStrictEnumString|IsFreeFormEnumString)\s*\}\s*$",
            RegexOptions.CultureInvariant);

        return
        [
            .. XDocument
                .Load(view)
                .Descendants()
                .SelectMany(e => e.Attributes())
                .Where(a => string.Equals(a.Name.LocalName, "IsVisible", StringComparison.Ordinal))
                .Select(a => binding.Match(a.Value))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value),
        ];
    }
}
