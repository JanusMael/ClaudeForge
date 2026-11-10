using System.Reflection;
using System.Xml.Linq;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AssemblyQuality;
using Bennewitz.Ninja.AssemblyQuality.Rules;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// The six <c>Bennewitz.Ninja.AssemblyQuality</c> rules over the compiled <c>src/</c> assemblies:
/// what a consumer of the published packages actually binds against. plans/00010, step Q.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>A clean result has to mean something</b>, so every rule is held to three things at once
/// (plans/00010 decision 8): zero findings, nothing <c>Skipped</c>, and a non-zero
/// <c>Inspected</c>. The first alone passes a scan that loaded nothing; the second alone passes a
/// scan that examined only what it could load.
/// </para>
/// <para>
/// ⛔ <b>No literal assembly list.</b> The subjects are discovered the way
/// <c>PackageMetadataTests</c> discovers them: every project directly under <c>src/</c>, split by
/// its own <c>IsPackable</c>. A new project joins the scan by existing. BNAQ1005's allow-list is
/// the set of project names under <c>src/</c> and <c>tests/</c>, never copied from
/// <c>AssemblyInfo.InternalsVisibleTo.cs</c>: an allow-list built from the file it checks cannot
/// fail.
/// </para>
/// <para>
/// ⚠ <b>The subjects are pinned</b> before any rule runs, because a scan pointed at the wrong
/// assembly reports clean too. Each loaded assembly must be the very one the compiler bound for a
/// type this test names: <see cref="IAgentConfigClient"/> for a packable library and the app's
/// <c>Program</c> for a product. In package mode the libraries arrive from the packages, which is
/// the point: the canary then runs these rules over the published shape.
/// </para>
/// <para>
/// What each rule replaces or adds to:
/// </para>
/// <list type="table">
///   <item><term>BNAQ1001</term><description>Every packable assembly. Replaced the Sdk-only
///   <c>PublicSurfaceContractTests.EveryAsyncMethod_HasRequiredCancellationToken</c>.</description></item>
///   <item><term>BNAQ1002</term><description><c>AgentForge.Sdk</c> only, by decision 6:
///   <c>JsonNode</c> is the settings model's currency in Core, Abstractions and the Shell. Replaced
///   <c>NoPublicApi_LeaksSystemTextJsonNodes</c>.</description></item>
///   <item><term>BNAQ1003</term><description>The reflection half of
///   <see cref="AssemblyLayeringTests"/>, which keeps its csproj-XML half: the compiler drops an
///   unused reference, and this rule cannot see one.</description></item>
///   <item><term>BNAQ1004</term><description>Every assembly, internal types included. The
///   <c>.Avalonia</c> segments it found are why step R renamed them.</description></item>
///   <item><term>BNAQ1005</term><description>Adds the allow-list half that
///   <see cref="SharedFriendGrantsTests"/> never had.</description></item>
///   <item><term>BNAQ1006</term><description>Near-vacuous in project mode, and meaningful in the
///   package canary, where published binaries meet newer dependencies.</description></item>
/// </list>
/// </remarks>
public sealed class AssemblyQualityTests
{
    /// <summary>Name prefixes that identify a product. Mirrors <see cref="AssemblyLayeringTests"/>.</summary>
    private static readonly string[] ProductPrefixes = ["ClaudeForge", "OpenCode"];

    private const string SdkAssemblyName = "AgentForge.Sdk";

    private static readonly Lazy<Subjects> Scan = new(Discover);

    [Fact]
    public void BNAQ1001_NoPackedAssemblyDefaultsACancellationToken()
    {
        AssertClean(new CancellationTokenRule(), Scan.Value.Packable);
    }

    [Fact]
    public void BNAQ1002_TheSdkLeaksNoJsonNodeOrNewtonsoftType()
    {
        Assembly sdk = Assert.Single(Scan.Value.Packable, a => a.GetName().Name == SdkAssemblyName);
        AssertClean(new SurfaceLeakRule(), [sdk]);
    }

    [Fact]
    public void BNAQ1003_SharedAssembliesReferenceNoProduct()
    {
        AssertClean(new ForbiddenReferenceRule(ProductPrefixes), Scan.Value.Packable);
    }

    [Fact]
    public void BNAQ1003_ProductsReferenceNoOtherProduct()
    {
        // Each product family may reference itself and the shared layer, never another product.
        foreach (IGrouping<string, Assembly> family in Scan.Value.Products.GroupBy(FamilyOf))
        {
            string[] others = [.. ProductPrefixes.Where(p => p != family.Key)];
            AssertClean(new ForbiddenReferenceRule(others), [.. family]);
        }
    }

    [Fact]
    public void BNAQ1004_NoNamespaceSegmentShadowsAReferencedRoot()
    {
        AssertClean(NamespaceShadowRule.IncludingInternalTypes(), Scan.Value.All);
    }

    [Fact]
    public void BNAQ1005_EveryFriendGrantNamesAProjectInThisRepository()
    {
        AssertClean(new FriendGrantRule(Scan.Value.ProjectNames), Scan.Value.All);
    }

    [Fact]
    public void BNAQ1006_EveryReferenceResolvesAndEveryInternalIsStillGranted()
    {
        AssertClean(new FriendReferenceRule(), Scan.Value.All);
    }

    [Fact]
    public void TheScanCoversBothLayers_SoNoRuleAboveIsVacuous()
    {
        Subjects s = Scan.Value;
        Assert.True(s.Packable.Count >= 2, "Fewer than two packable src projects: " + Describe(s.Packable));
        Assert.True(s.Products.Count >= 1, "No product src project: " + Describe(s.Products));
        Assert.Contains(s.Packable, a => a.GetName().Name == SdkAssemblyName);
        Assert.Equal(s.All.Count, s.Packable.Count + s.Products.Count);
    }

    // ── the scan ──────────────────────────────────────────────────────────────

    private sealed record Subjects(
        IReadOnlyList<Assembly> Packable,
        IReadOnlyList<Assembly> Products,
        IReadOnlyList<Assembly> All,
        IReadOnlyList<string> ProjectNames);

    private static Subjects Discover()
    {
        string root = FindRepositoryRoot();
        string src = Path.Combine(root, "src");

        List<Assembly> packable = [];
        List<Assembly> products = [];
        IEnumerable<string> srcProjects = Directory.GetDirectories(src)
            .Order(StringComparer.Ordinal)
            .Select(dir => Directory.GetFiles(dir, "*.csproj"))
            .Where(projects => projects.Length > 0)
            .Select(projects => Assert.Single(projects));
        foreach (string project in srcProjects)
        {
            (IsPackable(project) ? packable : products).Add(Load(Path.GetFileNameWithoutExtension(project)));
        }

        // ⚠ Pin the subjects to what the compiler bound, not to what a name resolved to.
        Assembly sdk = typeof(IAgentConfigClient).Assembly;
        Assert.Same(sdk, Assert.Single(packable, a => a.GetName().Name == sdk.GetName().Name));
        Assembly app = typeof(Program).Assembly;
        Assert.Same(app, Assert.Single(products, a => a.GetName().Name == app.GetName().Name));

        // Every project the repository builds may be named in a grant; nothing else may.
        string[] projectNames =
        [
            .. new[] { "src", "tests" }
                .Select(d => Path.Combine(root, d))
                .SelectMany(d => Directory.GetDirectories(d))
                .SelectMany(d => Directory.GetFiles(d, "*.csproj"))
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Order(StringComparer.Ordinal),
        ];

        return new Subjects(packable, products, [.. packable, .. products], projectNames);
    }

    private static bool IsPackable(string csproj)
    {
        string? value = XDocument.Load(csproj).Descendants()
            .Where(e => e.Name.LocalName == "IsPackable")
            .Select(e => e.Value.Trim())
            .LastOrDefault();

        // PackageMetadataTests requires every src project to say which it is, so silence is a
        // failure there; it must not quietly classify a project here.
        Assert.True(value is "true" or "false", csproj + " does not state <IsPackable> explicitly.");
        return value == "true";
    }

    private static Assembly Load(string name)
    {
        Assembly assembly = Assembly.Load(new AssemblyName(name));
        Assert.Equal(name, assembly.GetName().Name);
        return assembly;
    }

    private static string FamilyOf(Assembly assembly)
    {
        string name = assembly.GetName().Name!;
        return Assert.Single(ProductPrefixes, p => name.StartsWith(p, StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("No directory holding src/ and tests/ above " + AppContext.BaseDirectory);
    }

    // ── the assertion ─────────────────────────────────────────────────────────

    private static void AssertClean(IAssemblyRule rule, IReadOnlyList<Assembly> subjects)
    {
        Assert.NotEmpty(subjects);

        AssemblyRuleResult result = rule.Analyze(AssemblyScanContext.Of([.. subjects]));
        string scope = rule.Id + " over " + Describe(subjects);

        Assert.True(result.Inspected > 0,
            scope + " inspected nothing, so a clean result would mean nothing.");

        // ⚠ Assert.Empty shortens each entry and cuts off what would not load and why.
        Assert.True(result.Skipped.Count == 0,
            scope + " skipped " + result.Skipped.Count + ":\n  " + string.Join("\n  ", result.Skipped));

        Assert.True(result.Findings.Count == 0,
            scope + " (" + rule.Summary + ") found " + result.Findings.Count + ":\n  "
            + string.Join("\n  ", result.Findings.Select(f => f.ToString())));
    }

    private static string Describe(IEnumerable<Assembly> assemblies) =>
        string.Join(", ", assemblies.Select(a => a.GetName().Name));
}
