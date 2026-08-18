using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests;

/// <summary>
/// A concrete <see cref="AgentConfigClientCore"/> for tests that need <i>a</i>
/// client in order to exercise product-neutral behaviour — the workspace and
/// scope model, save / validate, the threading and cancellation contracts,
/// backup, MCP servers, env.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>AgentForge.Sdk</c> ships no concrete client: the two
/// real ones are Claude's and live in <c>ClaudeForge.Sdk.Claude</c>. Before this
/// type, every test in this project that needed a live client constructed a
/// <c>ClaudeCodeClient</c>, which meant a project named <c>AgentForge.*</c> had a
/// <c>ProjectReference</c> to a product — the exact inversion
/// <c>AssemblyLayeringTests</c> exists to prevent, and one it does not currently
/// catch (it scans <c>src/</c> only). Routing these tests through a local subclass
/// removes the reference.
/// </para>
/// <para>
/// <b>What this is NOT.</b> It is not a product-neutral client, though it is closer
    /// than it was. The <c>bool IsClaudeCode</c> that used to select between exactly two
    /// Claude schemas is gone — <see cref="Product"/> is a <see cref="ProductDescriptor"/>,
    /// so this type could name any product's schema. What still ties it to Claude is
    /// <see cref="ConfigFileDiscoverer"/>, which only knows Claude's file layouts and
    /// remains a documented deferral. So this client discovers Claude's files and
    /// validates against Claude's schema; what it buys is that the <i>assembly
    /// dependency</i> is honest and the remaining Claude assumption sits in one
    /// labelled file instead of spread across a dozen test fixtures.
/// </para>
/// <para>
/// <b>When to use the real client instead.</b> If a test asserts something about
/// Claude specifically — discovery order, a Claude accessor, schema-declared hook
/// vocabulary, permission rule syntax — it belongs in
/// <c>ClaudeForge.Sdk.Claude.Tests</c> against the real client. Using this type
/// there would assert against a copy of the thing under test.
/// </para>
/// </remarks>
internal sealed class TestConfigClient : AgentConfigClientCore
{
    // Overload rather than a defaulted parameter: ConfigScope is a struct as of Phase 3,
    // and a default parameter value must be a compile-time constant.
    public TestConfigClient()
        : this(ConfigScope.User)
    {
    }

    public TestConfigClient(ConfigScope defaultScope)
        : base(defaultScope, schemaRegistry: null)
    {
    }

    public TestConfigClient(ConfigScope defaultScope, SchemaRegistry schemaRegistry)
        : base(defaultScope, schemaRegistry)
    {
    }

    private TestConfigClient(
        ConfigScope defaultScope,
        SchemaRegistry schemaRegistry,
        SettingsWorkspace preLoaded)
        : base(defaultScope, schemaRegistry, preLoaded)
    {
    }

    /// <summary>
    /// Wrap an already-loaded workspace, skipping the disk load
    /// <see cref="AgentConfigClientCore.OpenAsync"/> would do. Mirrors the real
    /// clients' <c>FromExistingWorkspace</c> so tests of the wrap path read the same.
    /// </summary>
    public static TestConfigClient FromExistingWorkspace(
        SettingsWorkspace workspace,
        ConfigScope defaultScope,
        SchemaRegistry schemaRegistry)
    {
        return new TestConfigClient(defaultScope, schemaRegistry, workspace);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot)
    {
        // Settings before .mcp.json, matching the real clients: when both declare
        // mcpServers, save order has to favour the settings file.
        IReadOnlyList<DiscoveredFile> settings =
            ConfigFileDiscoverer.DiscoverClaudeCodeSettings(projectRoot, profileName: null);
        IReadOnlyList<DiscoveredFile> mcp =
            ConfigFileDiscoverer.DiscoverMcpFiles(projectRoot, profileName: null);
        return [.. settings, .. mcp];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Claude Code's descriptor because the alternative bundled schema is Claude
    /// Desktop's, not a neutral one — see the class remarks. Nothing about these tests
    /// depends on which of the two is chosen beyond the schema being loadable.
    /// </remarks>
    protected override ProductDescriptor Product => SchemaRegistry.ClaudeCodeProduct;

    /// <inheritdoc/>
    /// <remarks>
    /// A local policy, not Claude's: <c>ClaudeMergePolicy</c> lives in
    /// <c>ClaudeForge.Sdk.Claude</c>, and referencing it from here would be the assembly
    /// inversion this whole type exists to avoid. It unions any all-array path, which is
    /// what these tests' merge expectations were written against.
    /// </remarks>
    protected override IMergePolicy MergePolicy => TestMergePolicy.Instance;

    /// <inheritdoc/>
    protected override IBackupClient CreateBackupClient()
    {
        return new BackupClient(
            engine: BackupEngine.Default,
            includeClaudeCode: true,
            includeClaudeDesktop: false);
    }
}
