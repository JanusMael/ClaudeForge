using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.AgentForge.Sdk.Backup;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests;

/// <summary>
/// Proves <see cref="AgentConfigClientCore"/> answers scope questions from the ladder
/// <i>its product</i> supplies, rather than from Claude's.
/// <para>
/// <b>The specific leak this closes.</b> <c>EditableScopes</c> returned a hardcoded
/// <c>[ConfigScope.User]</c> when no workspace was loaded — product-neutral code naming
/// Claude's lowest rung. For a product whose lowest editable rung is called something else,
/// or sits at a different ordinal, that is a wrong answer with no symptom: the editor simply
/// targets a layer the product does not have.
/// </para>
/// <para>
/// <b>Why the unloaded client is the right place to assert.</b> Once a workspace exists,
/// <c>EditableScopes</c> derives from the documents actually discovered, and discovery still
/// stamps default-ladder scopes (<c>ConfigFileDiscoverer</c> knows only Claude's layouts — a
/// documented deferral). The no-workspace branch is therefore the one place a custom ladder
/// is observable today, which is also precisely where the hardcoded fallback lived.
/// </para>
/// </summary>
[TestClass]
public sealed class ProductScopeLadderTests
{
    /// <summary>Six rungs, two of them read-only — the shape Spike S1 measured for OpenCode.</summary>
    private static readonly ScopeLadder _sixRungs = new(
        "opencode-shaped",
        new ScopeRung("Mdm", IsReadOnly: true),
        new ScopeRung("Managed", IsReadOnly: true),
        new ScopeRung("Inline", IsReadOnly: false),
        new ScopeRung("Project", IsReadOnly: false),
        new ScopeRung("Custom", IsReadOnly: false),
        new ScopeRung("Global", IsReadOnly: false));

    [TestMethod]
    public void UnloadedClient_OffersItsOwnLowestEditableScope_NotClaudesUser()
    {
        using LadderClient client = new(_sixRungs);

        IReadOnlyList<ConfigScope> editable = client.EditableScopes;

        Assert.AreEqual(1, editable.Count);
        Assert.AreEqual("global", editable[0].Id,
            "The client must offer its own ladder's lowest editable rung. Getting \"user\" "
            + "here means the hardcoded [ConfigScope.User] fallback is back, and this "
            + "product does not have a User scope at all.");
        Assert.AreNotEqual(ConfigScope.User, editable[0],
            "Same ordinal (3 vs 5) is not the point — a scope from another ladder must not "
            + "compare equal to Claude's.");
    }

    [TestMethod]
    public void UnloadedClient_WithClaudesLadder_StillOffersUser()
    {
        // The counter-direction, so the test above cannot pass merely by returning something
        // other than User. A product whose ladder IS the default must be unaffected by 4f.
        using LadderClient client = new(ScopeLadder.Default);

        CollectionAssert.AreEqual(new[] { ConfigScope.User }, client.EditableScopes.ToArray());
    }

    [TestMethod]
    public void AProductWithNoEditableRung_StillOffersOne()
    {
        // An all-policy ladder is a coherent product statement (every layer set by MDM), and
        // the UI still asks for a scope to bind to. Returning an empty list here would leave
        // the editor with nothing to target, which surfaces as an unexplained blank page
        // rather than a read-only one.
        using LadderClient client = new(new ScopeLadder(
            "all-policy",
            new ScopeRung("Mdm", IsReadOnly: true),
            new ScopeRung("Managed", IsReadOnly: true)));

        Assert.AreEqual(1, client.EditableScopes.Count);
        Assert.AreEqual("managed", client.EditableScopes[0].Id);
    }

    /// <summary>
    /// A client that exists only to state a ladder. Deliberately not <c>TestConfigClient</c>:
    /// that one inherits <see cref="ScopeLadder.Default"/> on purpose, because its discovery
    /// stamps default-ladder scopes and a disagreeing ladder would make it incoherent. This
    /// one is never loaded, so it has no documents to disagree with.
    /// </summary>
    private sealed class LadderClient(ScopeLadder scopes) : AgentConfigClientCore(
        scopes.DefaultEditableScope, schemaRegistry: null)
    {
        protected override ProductDescriptor Product => SchemaRegistry.ClaudeCodeProduct;

        protected override IMergePolicy MergePolicy => new TestMergePolicy();

        protected override ScopeLadder Scopes { get; } = scopes;

        /// <summary>
        /// Discovers nothing. Every assertion here is about the no-workspace branch, and an
        /// empty list keeps it that way regardless of what is on the developer's disk — the
        /// real discoverers read <c>~/.claude</c>, which would make these tests depend on the
        /// machine they run on.
        /// </summary>
        protected override IReadOnlyList<DiscoveredFile> DiscoverFiles(string? projectRoot) => [];

        protected override IBackupClient CreateBackupClient() =>
            new BackupClient(BackupEngine.Default, [Product]);
    }
}
