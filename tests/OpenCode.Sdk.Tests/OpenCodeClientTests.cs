using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// Proves the clients actually <b>wire</b> their four seams, rather than declaring them.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here goes through the public surface and real files, because the seams
/// themselves (<c>Product</c>, <c>MergePolicy</c>, <c>Scopes</c>, <c>DiscoverFiles</c>) are
/// protected, and a test that reached them by reflection would prove the members exist rather
/// than that anything consumes them. A client that declared OpenCode's ladder while the
/// engine kept using Claude's would pass a reflection test and fail these.
/// </para>
/// <para>
/// Two scopes are constructed deliberately in the precedence tests. This repo's recurring
/// coverage finding is that almost every fixture holds one document at one scope, and a
/// single scope has nothing to resolve against — a merge test built that way proves nothing.
/// </para>
/// </remarks>
[TestClass]
public class OpenCodeClientTests
{
    private string _sandbox = string.Empty;
    private string _projectRoot = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "occ-" + Guid.NewGuid().ToString("N"));
        _projectRoot = Path.Combine(_sandbox, "proj");
        Directory.CreateDirectory(_projectRoot);
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
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

    private void WriteGlobal(string json)
    {
        string dir = Path.Combine(_sandbox, ".config", "opencode");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "opencode.json"), json);
    }

    private void WriteProject(string json)
        => File.WriteAllText(Path.Combine(_projectRoot, "opencode.json"), json);

    private static OpenCodeClient NewClient()
        => new(OpenCodeClient.GlobalScope, OpenCodeEnvironment.Empty);

    /// <summary>
    /// Arrays are read as <see cref="JsonArray"/> and unwrapped here, because the SDK's
    /// <c>GetEffective&lt;T&gt;</c> converts scalars and passes <c>JsonNode</c> shapes through
    /// but does not deserialize collections — <c>GetEffective&lt;string[]&gt;</c> returns
    /// <see langword="null"/>, not an empty array.
    /// <para>
    /// ⚠ That is a deliberate limit rather than a gap to fill in passing: the obvious fix is
    /// reflection-based <c>JsonSerializer.Deserialize</c>, which is exactly what produced the
    /// <c>IL2026</c> trim warning that broke the Release publish for three phases while every
    /// Debug test passed over it.
    /// </para>
    /// </summary>
    private static string[] Strings(JsonArray? array)
    {
        Assert.IsNotNull(array, "Expected an array at this path.");
        return [.. array.Select(n => n!.GetValue<string>())];
    }

    /// <summary>
    /// The scopes the client offers come from OpenCode's ladder and the files actually found —
    /// not from Claude's four rungs.
    /// </summary>
    [TestMethod]
    public async Task EditableScopes_ComeFromOpenCodesLadder()
    {
        WriteGlobal("{}");
        WriteProject("{}");

        using OpenCodeClient client = NewClient();
        await client.OpenAsync(_projectRoot, CancellationToken.None);

        string[] names = client.EditableScopes.Select(s => s.ToString()).ToArray();

        CollectionAssert.Contains(names, "Global");
        CollectionAssert.Contains(names, "Project");
        CollectionAssert.DoesNotContain(names, "User",
            "'User' is a Claude rung. Seeing it here means the client is running on "
            + "ScopeLadder.Default — the exact way a second product silently inherits "
            + "Claude's scope model.");
        CollectionAssert.DoesNotContain(names, "Local");
    }

    /// <summary>
    /// The <c>Scopes</c> seam itself, which nothing else here reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>Added because a canary exposed a hole.</b> Replacing the client's ladder with
    /// <c>ScopeLadder.Default</c> — Claude's — failed <b>zero</b> tests. Every other test
    /// opens the client against real files, and <c>EditableScopes</c> then derives from the
    /// <i>discovered documents</i>, whose scopes discovery takes from OpenCode's ladder
    /// directly. So the seam was declared, consumed by the core, and never observed.
    /// </para>
    /// <para>
    /// <c>Scopes</c> is read in exactly one place: the fallback
    /// <c>Scopes.DefaultEditableScope</c>, used before the workspace exists or when nothing
    /// editable was found. Asking before opening is therefore the only way to see it. The two
    /// ladders give different answers — OpenCode's lowest writable rung is Global, Claude's is
    /// User — which is what makes this able to fail.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void BeforeOpening_TheFallbackScopeComesFromOpenCodesLadder()
    {
        using OpenCodeClient client = NewClient();

        CollectionAssert.AreEqual(
            new[] { "Global" },
            client.EditableScopes.Select(s => s.ToString()).ToArray(),
            "Before a workspace exists the client falls back to its ladder's default "
            + "editable scope. 'User' here would mean it is running on Claude's ladder.");
    }

    /// <summary>
    /// The precedence S1 measured, resolved end to end through the merge engine: the project
    /// layer wins a scalar over the global one.
    /// </summary>
    [TestMethod]
    public async Task ProjectOutranksGlobal_ForAScalar()
    {
        WriteGlobal("""{ "model": "from-global" }""");
        WriteProject("""{ "model": "from-project" }""");

        using OpenCodeClient client = NewClient();
        await client.OpenAsync(_projectRoot, CancellationToken.None);

        Assert.AreEqual("from-project", client.GetEffective<string>("model"),
            "S1 measured the project config outranking the lower layers. If the global value "
            + "wins, discovery order or the ladder is inverted.");
    }

    /// <summary>
    /// The union half of the merge policy, exercised through a real client rather than by
    /// asking the policy object directly. <c>instructions</c> accumulates, lowest layer first.
    /// </summary>
    [TestMethod]
    public async Task Instructions_UnionAcrossScopes_LowestLayerFirst()
    {
        WriteGlobal("""{ "instructions": ["global.md"] }""");
        WriteProject("""{ "instructions": ["project.md"] }""");

        using OpenCodeClient client = NewClient();
        await client.OpenAsync(_projectRoot, CancellationToken.None);

        string[] instructions = Strings(client.GetEffective<JsonArray>("instructions"));

        CollectionAssert.AreEqual(
            new[] { "global.md", "project.md" },
            instructions,
            "instructions unions lowest-priority-first (S1). Either a missing union or the "
            + "wrong order shows up here and nowhere else — and order matters because "
            + "OpenCode evaluates the last matching rule.");
    }

    /// <summary>
    /// The replace half, and the one with a data-losing failure mode: a higher layer's
    /// <c>disabled_providers</c> replaces outright. Unioning here silently re-enables a
    /// provider the user turned off.
    /// </summary>
    [TestMethod]
    public async Task DisabledProviders_Replace_TheyDoNotAccumulate()
    {
        WriteGlobal("""{ "disabled_providers": ["anthropic"] }""");
        WriteProject("""{ "disabled_providers": ["openai"] }""");

        using OpenCodeClient client = NewClient();
        await client.OpenAsync(_projectRoot, CancellationToken.None);

        string[] providers = Strings(client.GetEffective<JsonArray>("disabled_providers"));

        CollectionAssert.AreEqual(new[] { "openai" }, providers,
            "The project layer replaces this key outright (S1). A union would resurrect "
            + "'anthropic' — a provider a higher layer deliberately stopped disabling.");
    }

    /// <summary>
    /// The TUI client is a separate product against a separate file, and opening it does not
    /// pick up the main config sitting beside it.
    /// </summary>
    [TestMethod]
    public async Task TuiClient_ReadsTuiJson_NotTheMainConfig()
    {
        string dir = Path.Combine(_sandbox, ".config", "opencode");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "opencode.json"), """{ "model": "main" }""");
        File.WriteAllText(Path.Combine(dir, "tui.json"), """{ "theme": "tui-theme" }""");

        using OpenCodeTuiClient client = new(OpenCodeClient.GlobalScope, OpenCodeEnvironment.Empty);
        await client.OpenAsync(_projectRoot, CancellationToken.None);

        Assert.AreEqual("tui-theme", client.GetEffective<string>("theme"));
        Assert.IsNull(client.GetEffective<string>("model"),
            "The TUI client must not see the main config's keys — they are separate products "
            + "with separate schemas and no key overlap.");
    }

    /// <summary>
    /// A project context changes nothing for the TUI client: <c>tui.json</c> has no project
    /// form, so its single scope is the global one either way.
    /// </summary>
    [TestMethod]
    public async Task TuiClient_IgnoresProjectContext()
    {
        string dir = Path.Combine(_sandbox, ".config", "opencode");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tui.json"), """{ "theme": "global" }""");
        File.WriteAllText(Path.Combine(_projectRoot, "tui.json"), """{ "theme": "project" }""");

        using OpenCodeTuiClient client = new(OpenCodeClient.GlobalScope, OpenCodeEnvironment.Empty);
        await client.OpenAsync(_projectRoot, CancellationToken.None);

        Assert.AreEqual("global", client.GetEffective<string>("theme"));
        CollectionAssert.AreEqual(
            new[] { "Global" },
            client.EditableScopes.Select(s => s.ToString()).ToArray());
    }
}
