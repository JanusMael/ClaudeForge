using System.Reflection;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;
using SchemaRegistry = Bennewitz.Ninja.AgentForge.Core.Schema.SchemaRegistry;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// One <c>SchemaRegistry</c> per launch, shared by both clients and by page building.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>This app built THREE.</b> Its own, created inside <c>InitializeAsync</c>, plus a
/// private one inside each client: <c>AgentConfigClientCore</c> builds a registry whenever it is
/// handed <see langword="null"/>, and both OpenCode clients passed exactly that. Nothing failed
/// — every registry resolved the same schemas — which is why it survived a phase. The costs were
/// a duplicate conditional GET per schema on every launch, and, offline, one 3s
/// <c>FetchTimeout</c> per registry instead of one per schema.
/// </para>
/// <para>
/// ⭐ <b>The correctness half is the provenance badge.</b> The nav badge reports the registry the
/// PAGES were built from. With three registries it could read <c>Fetched</c> while the registry
/// the save path validates against had fallen back to bundled, and no surface anywhere would
/// disagree. Sharing is what makes the badge speak for save-validation too.
/// </para>
/// <para>
/// ⚠ <b>Reflection over a protected member, deliberately.</b> A registry does not expose its
/// identity, and <c>AgentConfigClientCore.SchemaRegistryInstance</c> exists for derived clients
/// rather than for tests — making it public so this test could read it would add shipping surface
/// that only a test wants. The property is looked up by name and its absence fails the test
/// rather than skipping it, so a rename surfaces here instead of silently emptying the assertion.
/// </para>
/// </remarks>
[TestClass]
public sealed class SharedSchemaRegistryTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // ⛔ Required, not hygiene: the parameterless view-model constructor resolves this app's
        // schema cache directory from $OPENCODE_CONFIG_DIR. Without the redirect it would be
        // computed against the developer's own OpenCode install.
        _sandbox = Path.Combine(Path.GetTempPath(), "ocf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", "1");

        // ⛔⛔ NOT optional, and it is why this file may construct the app's real view-model at
        // all. That constructor calls SchemaRegistry.CreateWithNetwork — the app's whole point
        // here — so without this pin these two tests would resolve schemas against whatever
        // upstream serves today, which is exactly what
        // ProductionSchemaRegistryTests.NoTestResolvesSchemasAgainstTheLiveInternet forbids.
        // Bundled resolves in memory: no request, no disk write, no dependence on the network.
        //
        // ⚠ Process-global, which is safe only because this suite is sequential by design. It is
        // cleared in Cleanup for the same reason DebugFlags.ResetForTesting clears it.
        SchemaRegistry.ProcessSourceOverride = SchemaSourceOverride.Bundled;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", null);
        SchemaRegistry.ProcessSourceOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    /// <summary>
    /// Both of the app's clients validate against the same registry instance.
    /// </summary>
    /// <remarks>
    /// ⚠ The app's constructor, not the test-seam one. A view-model built from ready-made
    /// sections cannot share anything it was not given, so asserting against that overload would
    /// pass while the shipped composition root stayed broken.
    /// </remarks>
    [TestMethod]
    public void TheAppsTwoClientsShareOneSchemaRegistry()
    {
        using MainWindowViewModel vm = new();

        Assert.AreEqual(2, vm.Sections.Count,
            "This app hosts exactly two products; a different count means the composition root "
            + "changed and this assertion no longer covers what it claims.");

        SchemaRegistry first = RegistryOf(vm.Sections[0].Client);
        SchemaRegistry second = RegistryOf(vm.Sections[1].Client);

        Assert.AreSame(second, first,
            "Each client built its own SchemaRegistry, so every schema is fetched once per client "
            + "and an offline launch pays a FetchTimeout per client. Pass the registry into "
            + "OpenCodeClient / OpenCodeTuiClient from MainWindowViewModel's constructor chain.");
    }

    /// <summary>
    /// The registry the pages are built from is the one the clients already hold.
    /// </summary>
    /// <remarks>
    /// ⛔ The sibling above would stay green if <c>InitializeAsync</c> built a THIRD registry for
    /// page building and left the clients sharing a second one — which is exactly the state this
    /// app shipped in. This asserts the other half: no new registry appears at initialize time.
    /// </remarks>
    [TestMethod]
    public async Task InitializeBuildsPagesFromTheClientsOwnRegistry()
    {
        using MainWindowViewModel vm = new();
        SchemaRegistry clientRegistry = RegistryOf(vm.Sections[0].Client);

        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        FieldInfo field = typeof(MainWindowViewModel).GetField(
            "_registry", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "MainWindowViewModel._registry is gone; this guard no longer reaches what it "
                + "claims to check.");

        Assert.AreSame(clientRegistry, field.GetValue(vm),
            "InitializeAsync built its own registry instead of reusing the one the constructor "
            + "handed the clients, so the provenance badge can report a source the save path "
            + "never used.");
    }

    /// <summary>The test context, for the cancellation token MSTest supplies per test.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The registry a client validates against, read off the protected member that exposes it to
    /// derived clients.
    /// </summary>
    private static SchemaRegistry RegistryOf(AgentConfigClientCore client)
    {
        PropertyInfo property = typeof(AgentConfigClientCore).GetProperty(
            "SchemaRegistryInstance", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "AgentConfigClientCore.SchemaRegistryInstance is gone or was renamed; this guard "
                + "cannot see what it claims to check.");

        return (SchemaRegistry)property.GetValue(client)!;
    }
}
