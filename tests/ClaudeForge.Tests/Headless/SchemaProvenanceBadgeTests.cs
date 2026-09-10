using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using Avalonia.Headless;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// The nav-header badge that says which copy of a schema each product section was built from.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>In this app the badge says more than it does in OpenCodeForge.</b> ClaudeForge builds
/// ONE registry and hands it to both SDK clients, so the copy reported here is also the copy
/// save-validation evaluates against. OpenCodeForge's two are separate instances that merely
/// agree, and its badge is careful to claim only that it describes the pages.
/// </para>
/// <para>
/// ⛔ <b>The case worth the most here is Claude Desktop's.</b> Its schema is hand-maintained —
/// the <c>$id</c> is a bare token — so its descriptor URL is <c>bundled://…</c> and no fetch is
/// ever attempted for it. The ordinary bundled tooltip tells the reader the app "tried to fetch
/// a newer copy and could not", which on that section is an untruth pointing at a network
/// problem they do not have.
/// </para>
/// <para>
/// ⚠ <b>Every test here controls the network.</b> Asserting "fetched" or "bundled" against the
/// real one would make the result a property of the machine the suite runs on.
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaProvenanceBadgeTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private string _sandbox = string.Empty;

    /// <summary>Refuses every request, so the load falls back to the bundled copy.</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network unavailable");
    }

    /// <summary>Serves a minimal but valid schema for whatever is asked for.</summary>
    private sealed class ServingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = $$"""
                            {
                              "$schema": "http://json-schema.org/draft-07/schema#",
                              "type": "object",
                              "properties": {
                                "servedFor": { "type": "string", "description": "{{request.RequestUri}}" }
                              }
                            }
                            """;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "cfbadge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        string ccDir = Path.Combine(_sandbox, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(Path.Combine(ccDir, "settings.json"), "{}");

        string dtDir = Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!;
        Directory.CreateDirectory(dtDir);
        File.WriteAllText(PlatformPaths.DesktopConfigPath, "{}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
            }
        }
        catch
        {
            /* best effort — the file-system indexer may hold transient locks */
        }
    }

    private static async Task<MainWindowViewModel> LoadedAsync(HttpMessageHandler handler)
    {
        MainWindowViewModel vm = new(new SchemaRegistry(new HttpClient(handler)), new NullDialogService());
        await vm.LoadAllWorkspacesAsync();
        return vm;
    }

    private static NavigationNodeViewModel Header(MainWindowViewModel vm, string nodeId)
    {
        NavigationNodeViewModel? header = vm.NavigationTree
            .FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.Ordinal));

        Assert.IsNotNull(header, $"Premise: the '{nodeId}' section header must be in the tree.");
        return header;
    }

    /// <summary>
    /// The digest a given product's BUNDLED copy hashes to, obtained from an offline registry.
    /// </summary>
    /// <remarks>
    /// Deterministic: the bundled bytes are in the binary, and the digest is taken after the
    /// strip and overlay, exactly as the badge's is.
    /// </remarks>
    private static async Task<string> BundledShaAsync(ProductDescriptor product)
    {
        using SchemaRegistry probe = new();
        _ = await probe.GetSettingsNodeAsync(product);
        return probe.ProvenanceFor(product.SchemaFileName)!.ShortSha;
    }

    [TestMethod]
    public async Task WithNoNetwork_BothSectionsSayBundled()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = await LoadedAsync(new OfflineHandler());

            foreach (string nodeId in new[] { MainWindowViewModel.NavIdClaudeCode, MainWindowViewModel.NavIdClaudeDesktop })
            {
                NavigationNodeViewModel header = Header(vm, nodeId);
                Assert.AreEqual(Strings.SchemaBadgeBundled, header.Badge,
                    $"'{nodeId}' should report the bundled copy when the network refuses.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(header.BadgeTooltip),
                    "A badge with no hover text leaves the fingerprint unreachable.");
            }

            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    /// <summary>
    /// With the network answering, Claude Code fetches and Claude Desktop does not — because
    /// Desktop has nowhere to fetch from, not because anything failed.
    /// </summary>
    [TestMethod]
    public async Task WithAReachableNetwork_ClaudeCodeFetches_DesktopStaysBundled()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Premise, asserted rather than assumed: the two products differ in exactly the
            // way this test is about. If Desktop ever gains a published schema, this test is
            // making a claim about the wrong thing and should say so here.
            StringAssert.StartsWith(SchemaRegistry.ClaudeCodeProduct.SchemaUrl, "https://",
                StringComparison.OrdinalIgnoreCase);
            Assert.IsFalse(
                SchemaRegistry.ClaudeDesktopProduct.SchemaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                "Premise: Claude Desktop's schema is hand-maintained and has no upstream URL.");

            MainWindowViewModel vm = await LoadedAsync(new ServingHandler());

            NavigationNodeViewModel cc = Header(vm, MainWindowViewModel.NavIdClaudeCode);
            Assert.IsNotNull(cc.Badge);
            StringAssert.StartsWith(cc.Badge, "fetched", StringComparison.Ordinal,
                "Claude Code reported the bundled copy while the network was answering.");

            NavigationNodeViewModel dt = Header(vm, MainWindowViewModel.NavIdClaudeDesktop);
            Assert.AreEqual(Strings.SchemaBadgeBundled, dt.Badge,
                "Claude Desktop has no upstream, so a reachable network cannot change its badge.");

            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    /// <summary>
    /// ⭐ Claude Desktop's bundled tooltip must not claim a download was attempted.
    /// </summary>
    /// <remarks>
    /// Both arms are asserted. Checking only that the tooltip equals the no-upstream text would
    /// pass just as well if the applier had been written to use that string unconditionally —
    /// so the sibling test below pins the other direction on the same run of the same code.
    /// </remarks>
    [TestMethod]
    public async Task ClaudeDesktop_SaysThereIsNothingToDownload_NotThatDownloadingFailed()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = await LoadedAsync(new ServingHandler());
            NavigationNodeViewModel dt = Header(vm, MainWindowViewModel.NavIdClaudeDesktop);

            string sha = await BundledShaAsync(SchemaRegistry.ClaudeDesktopProduct);
            string noUpstream = string.Format(
                CultureInfo.CurrentCulture, Strings.SchemaBadgeTooltipNoUpstreamFmt, sha);
            string failedFetch = string.Format(
                CultureInfo.CurrentCulture, Strings.SchemaBadgeTooltipBundledFmt, sha);

            Assert.AreNotEqual(noUpstream, failedFetch,
                "Premise: the two bundled tooltips must be different strings, or this test "
                + "cannot distinguish them.");

            Assert.AreEqual(noUpstream, dt.BadgeTooltip,
                "Claude Desktop's schema is hand-maintained and no fetch is ever attempted for "
                + "it. The failed-fetch tooltip would send the reader looking for a network "
                + "problem they do not have.");

            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    /// <summary>
    /// The other direction: a product that DOES have an upstream, which fell back to bundled,
    /// gets the tooltip that says the fetch was tried.
    /// </summary>
    [TestMethod]
    public async Task ClaudeCode_FallenBackToBundled_SaysTheFetchWasTried()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = await LoadedAsync(new OfflineHandler());
            NavigationNodeViewModel cc = Header(vm, MainWindowViewModel.NavIdClaudeCode);

            string sha = await BundledShaAsync(SchemaRegistry.ClaudeCodeProduct);
            string failedFetch = string.Format(
                CultureInfo.CurrentCulture, Strings.SchemaBadgeTooltipBundledFmt, sha);

            Assert.AreEqual(failedFetch, cc.BadgeTooltip,
                "Claude Code does have an upstream, so falling back to bundled IS a failed "
                + "fetch and the tooltip should say so.");

            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    /// <summary>The tooltip carries the short fingerprint, which is what a bug report needs.</summary>
    [TestMethod]
    public async Task TheTooltipCarriesTheFingerprint()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = await LoadedAsync(new OfflineHandler());
            NavigationNodeViewModel cc = Header(vm, MainWindowViewModel.NavIdClaudeCode);

            string sha = await BundledShaAsync(SchemaRegistry.ClaudeCodeProduct);

            Assert.IsNotNull(cc.BadgeTooltip);
            StringAssert.Contains(cc.BadgeTooltip, sha, StringComparison.Ordinal,
                "The tooltip omits the fingerprint, so two installs cannot be compared from a "
                + "screenshot — which is the case the badge exists for.");

            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    /// <summary>
    /// Rows that own no schema carry no badge.
    /// </summary>
    /// <remarks>
    /// ⚠ Absence is the honest answer for Essentials, Profiles, Backup and the rest — they are
    /// not product sections and rendering "bundled" on them would state something nobody
    /// established. This does not exercise the null-provenance branch in the applier: those
    /// rows never reach it.
    /// </remarks>
    [TestMethod]
    public async Task RowsWithNoSchemaCarryNoBadge()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = await LoadedAsync(new OfflineHandler());

            string[] sectionIds =
                [MainWindowViewModel.NavIdClaudeCode, MainWindowViewModel.NavIdClaudeDesktop];

            List<NavigationNodeViewModel> others =
                [.. vm.NavigationTree.Where(n => !sectionIds.Contains(n.NodeId, StringComparer.Ordinal))];

            Assert.IsTrue(others.Count > 0, "Premise: the tree holds rows besides the two sections.");

            foreach (NavigationNodeViewModel row in others)
            {
                Assert.IsTrue(string.IsNullOrEmpty(row.Badge),
                    $"'{row.Title}' owns no schema but claims provenance '{row.Badge}'.");
            }

            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    // ── Test doubles ────────────────────────────────────────────────────

    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null)
            => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null)
            => Task.FromResult<string?>(null);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
            => Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel")
            => Task.FromResult<bool?>(false);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(false);
    }
}
