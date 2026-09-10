using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Architecture;

/// <summary>
/// Which <c>SchemaRegistry</c> each part of the repo is allowed to build: the app
/// assemblies must ask for the network by name, and the test suite must never reach it.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>A bare registry is OFFLINE</b>, and that default is deliberate — 34 test sites
/// construct one, and under network-first every one of them would otherwise resolve schemas
/// against whatever upstream is serving that day. The cost of inverting the default lands
/// here: an app that forgets to ask for the network still compiles, still passes every test,
/// and simply never fetches.
/// </para>
/// <para>
/// ⭐ <b>This is not hypothetical.</b> <c>src/ClaudeForge/App.axaml.cs</c> wrote
/// <c>SchemaRegistry schemaRegistry = new();</c> from the initial commit, and network-first
/// (<c>6260636</c>) updated OpenCodeForge's composition root without touching ClaudeForge's.
/// The shipped app therefore built its pages AND validated its saves against bundled schemas
/// for the whole of Phase 13, while the docs described a fetch it never made. Nothing failed;
/// there was nothing to fail.
/// </para>
/// <para>
/// ⚠ <b>Source text, not reflection.</b> The choice being guarded is which overload a
/// composition root calls, and a registry does not expose whether it holds an
/// <c>HttpClient</c> — deliberately, since that would be public surface existing only for a
/// test. Reflection cannot see the difference; the source can.
/// </para>
/// <para>
/// ⚠ Scans the two APP assemblies only. Libraries under <c>src/</c> take a registry from
/// their caller, and <c>tests/</c> is where the offline default is supposed to be used.
/// </para>
/// </remarks>
[TestClass]
public sealed class ProductionSchemaRegistryTests
{
    /// <summary>The app assemblies — i.e. every composition root this repo ships.</summary>
    private static readonly string[] AppProjectDirs = ["ClaudeForge", "OpenCodeForge"];

    /// <summary>
    /// <c>new SchemaRegistry()</c> with an empty argument list.
    /// </summary>
    private static readonly Regex BareExplicitNew = new(
        @"new\s+SchemaRegistry\s*\(\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// The target-typed form, <c>SchemaRegistry x = new();</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ Matched separately and it is the one that actually shipped. A guard written against
    /// <c>new SchemaRegistry()</c> alone would have stayed green over the real defect, because
    /// C# lets the type appear only on the left of the assignment.
    /// </remarks>
    private static readonly Regex BareTargetTypedNew = new(
        @"SchemaRegistry\s+\w+\s*=\s*new\s*\(\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex AsksForNetwork = new(
        @"SchemaRegistry\.CreateWithNetwork\s*\(",
        RegexOptions.Compiled);

    [TestMethod]
    public void NoAppAssemblyConstructsAnOfflineSchemaRegistry()
    {
        string repoRoot = FindRepoRoot();
        List<string> offences = [];
        int scanned = 0;

        foreach (string app in AppProjectDirs)
        {
            foreach (string file in EnumerateSources(repoRoot, app))
            {
                scanned++;
                string text = File.ReadAllText(file);

                if (BareExplicitNew.IsMatch(text) || BareTargetTypedNew.IsMatch(text))
                {
                    offences.Add(Path.GetRelativePath(repoRoot, file));
                }
            }
        }

        // Non-vacuity. A rename that empties the scan must fail here rather than pass by
        // finding nothing to object to — the failure mode AssemblyLayeringTests was shipped
        // with and only caught by canarying it.
        Assert.IsTrue(
            scanned > 0,
            $"Scanned no sources under {string.Join(", ", AppProjectDirs)}; the scan has been narrowed to nothing.");

        Assert.AreEqual(
            0,
            offences.Count,
            "An app composition root built a SchemaRegistry with no HttpClient, which means OFFLINE. "
            + "Use SchemaRegistry.CreateWithNetwork(). Offending files: "
            + string.Join(", ", offences));
    }

    [TestMethod]
    [DataRow("ClaudeForge")]
    [DataRow("OpenCodeForge")]
    public void EachAppAsksForTheNetworkByName(string app)
    {
        string repoRoot = FindRepoRoot();

        bool found = EnumerateSources(repoRoot, app)
            .Any(file => AsksForNetwork.IsMatch(File.ReadAllText(file)));

        Assert.IsTrue(
            found,
            $"'{app}' never calls SchemaRegistry.CreateWithNetwork(). Either it stopped asking for "
            + "the network, or this scan no longer reaches its sources — the first is the bug the "
            + "sibling test cannot see, because 'no bare registry' is also true of an app that "
            + "builds none at all.");
    }

    /// <summary>
    /// A registry handed a bare <c>new HttpClient()</c> — i.e. one that reaches the real
    /// internet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ Deliberately blind to <c>new HttpClient(handler)</c>. A stubbed handler is the
    /// established way to pin a branch (<c>FailingHttpHandler</c> for bundled, a serving
    /// handler for fetched) and those sites are exactly right.
    /// </para>
    /// <para>
    /// ⛔ <b>Both spellings, and the second is not optional.</b> The first draft matched only
    /// <c>new SchemaRegistry(new HttpClient())</c> and walked straight past
    /// <c>SchemaRegistry x = new(new HttpClient())</c> — two live sites survived a pass that
    /// reported success, in the same file whose sibling regex above carries a comment about
    /// exactly this trap. Target-typed <c>new</c> puts the type on the left of the assignment
    /// where a constructor-shaped pattern cannot see it.
    /// </para>
    /// </remarks>
    private static readonly Regex[] LiveNetworkRegistry =
    [
        new(@"new\s+SchemaRegistry\s*\(\s*new\s+HttpClient\s*\(\s*\)\s*\)", RegexOptions.Compiled),
        new(@"SchemaRegistry\s+\w+\s*=\s*new\s*\(\s*new\s+HttpClient\s*\(\s*\)\s*\)", RegexOptions.Compiled),
    ];

    /// <summary>
    /// No test resolves a schema against the live internet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔⛔ <b>24 sites across 19 files did, and none of them meant to.</b> They were written
    /// when an <c>HttpClient</c> was inert here — the HTTPS step sat below the bundled one and
    /// never ran — so passing one read as harmless boilerplate. Network-first turned every one
    /// of them into a live call, which is how they were measured: a probe registry built the
    /// same way reported <c>Source=Fetched</c>.
    /// </para>
    /// <para>
    /// ⭐ The damage is not flakiness alone. Those tests assert against whatever upstream
    /// serves on the day they run, so a schema change made by someone outside this repo can
    /// redden the suite — or, worse, keep it green over a real regression by supplying a shape
    /// the bundled copy no longer has.
    /// </para>
    /// <para>
    /// ⓘ The offline default IS how a test says "bundled". That is what the parameterless
    /// constructor is for, and it restores exactly the behaviour these sites had before
    /// network-first.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void NoTestResolvesSchemasAgainstTheLiveInternet()
    {
        string repoRoot = FindRepoRoot();
        string testsRoot = Path.Combine(repoRoot, "tests");
        Assert.IsTrue(Directory.Exists(testsRoot), $"Expected a tests root at '{testsRoot}'.");

        List<string> offences = [];
        int scanned = 0;

        foreach (string file in EnumerateSourcesUnder(testsRoot))
        {
            scanned++;

            // This file names the forbidden shape in a regex and in prose; matching itself
            // would make the guard permanently red for describing what it forbids.
            if (string.Equals(Path.GetFileName(file), "ProductionSchemaRegistryTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            if (LiveNetworkRegistry.Any(rx => rx.IsMatch(text)))
            {
                offences.Add(Path.GetRelativePath(repoRoot, file));
            }
        }

        Assert.IsTrue(scanned > 1, $"Scanned {scanned} test sources; the scan has narrowed to nothing.");

        Assert.AreEqual(
            0,
            offences.Count,
            "A test built a SchemaRegistry with a real HttpClient, so it resolves schemas over the "
            + "live internet. Use the parameterless constructor for bundled, or new HttpClient(handler) "
            + "to pin a branch. Offending files: "
            + string.Join(", ", offences));
    }

    /// <summary>
    /// Every <c>.cs</c> under one app project, excluding build output.
    /// </summary>
    private static IEnumerable<string> EnumerateSources(string repoRoot, string app)
    {
        string root = Path.Combine(repoRoot, "src", app);

        // A missing project directory is a scan that has silently narrowed, not an empty app.
        Assert.IsTrue(Directory.Exists(root), $"Expected an app project at '{root}'.");

        return EnumerateSourcesUnder(root);
    }

    /// <summary>Every <c>.cs</c> beneath <paramref name="root"/>, excluding build output.</summary>
    private static IEnumerable<string> EnumerateSourcesUnder(string root)
        => Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

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
