using System.Net;
using System.Text;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The nav-header badge that says which copy of a schema a section's pages were built from.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Why the badge exists at all.</b> Network-first loading means a section's shape comes
/// either from the binary or from a download moments old, and nothing on screen — or in a
/// screenshot attached to a bug report — distinguished them. "The editor shows a field I do not
/// have" and "the editor is missing a field I do have" are both explained by provenance and by
/// nothing else.
/// </para>
/// <para>
/// ⚠ <b>Every test here controls the network.</b> Asserting "fetched" or "bundled" against the
/// real one would make the result a property of the machine the suite runs on — which is exactly
/// why <c>InitializeAsync</c> gained a registry parameter.
/// </para>
/// </remarks>
[TestClass]
public sealed class SchemaProvenanceBadgeTests
{
    private string _sandbox = string.Empty;

    public required TestContext TestContext { get; set; }

    /// <summary>Refuses every request, so the load falls back to the bundled copy.</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network unavailable");
    }

    /// <summary>Serves each schema back as a minimal but valid document.</summary>
    /// <remarks>
    /// The body must differ per URL, or two sections would produce the same digest and
    /// <see cref="TwoSections_GetTheirOwnFingerprints"/> could not tell a per-schema record from
    /// one shared entry.
    /// </remarks>
    private sealed class PerUrlHandler : HttpMessageHandler
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
        _sandbox = Path.Combine(Path.GetTempPath(), "ocbadge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", "1");
        File.WriteAllText(Path.Combine(_sandbox, "opencode.json"), "{}");
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", null);
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

    private static MainWindowViewModel BuildViewModel() => new(
        new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
            OpenCodePageLayout.Config, () => Strings.SectionOpenCode,
            OpenCodeDangerTable.Config),
        new HostedSection(OpenCodeProducts.Tui, new OpenCodeTuiClient(),
            OpenCodePageLayout.Tui, () => Strings.SectionOpenCodeTui,
            OpenCodeDangerTable.Tui));

    private async Task<MainWindowViewModel> InitializedAsync(HttpMessageHandler handler)
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(
            TestContext.CancellationTokenSource.Token,
            new SchemaRegistry(new HttpClient(handler)));
        return vm;
    }

    /// <summary>The two section headers, in nav order.</summary>
    private static IReadOnlyList<NavigationNodeViewModel> SectionHeaders(MainWindowViewModel vm) =>
        [.. vm.Navigation.Where(n => n.Children.Count > 0)];

    [TestMethod]
    public async Task WithNoNetwork_TheBadgeSaysBundled()
    {
        MainWindowViewModel vm = await InitializedAsync(new OfflineHandler());
        IReadOnlyList<NavigationNodeViewModel> headers = SectionHeaders(vm);

        Assert.IsTrue(headers.Count > 0, "Premise: at least one section must have loaded.");

        foreach (NavigationNodeViewModel header in headers)
        {
            Assert.AreEqual(Strings.SchemaBadgeBundled, header.Badge,
                $"'{header.Title}' should report the bundled copy when the network refuses.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(header.BadgeTooltip),
                "A badge with no hover text leaves the fingerprint unreachable.");
        }
    }

    [TestMethod]
    public async Task WithAReachableNetwork_TheBadgeSaysFetched()
    {
        MainWindowViewModel vm = await InitializedAsync(new PerUrlHandler());
        IReadOnlyList<NavigationNodeViewModel> headers = SectionHeaders(vm);

        Assert.IsTrue(headers.Count > 0, "Premise: at least one section must have loaded.");

        foreach (NavigationNodeViewModel header in headers)
        {
            Assert.AreNotEqual(Strings.SchemaBadgeBundled, header.Badge,
                $"'{header.Title}' reported the bundled copy while the network was answering.");
            Assert.IsNotNull(header.Badge);
            StringAssert.StartsWith(header.Badge, "fetched", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ⭐ Each section reports its OWN schema's fingerprint.
    /// </summary>
    /// <remarks>
    /// The provenance is stored per schema file name. One shared entry — or a lookup keyed by
    /// something both products share — would give both sections the same digest, and the badge
    /// would then confidently attribute the TUI schema's identity to the config one. The served
    /// bodies differ per URL so this can distinguish the two cases.
    /// </remarks>
    [TestMethod]
    public async Task TwoSections_GetTheirOwnFingerprints()
    {
        MainWindowViewModel vm = await InitializedAsync(new PerUrlHandler());
        IReadOnlyList<NavigationNodeViewModel> headers = SectionHeaders(vm);

        Assert.AreEqual(2, headers.Count, "Premise: both sections must have loaded.");
        Assert.AreNotEqual(
            headers[0].BadgeTooltip,
            headers[1].BadgeTooltip,
            "Both sections reported the same provenance, so the badge is not per-schema — one "
            + "section is being described by the other's schema.");
    }

    /// <summary>
    /// A row that owns no schema carries no badge — Essentials and Artifacts are not sections.
    /// </summary>
    /// <remarks>
    /// ⚠ Absence is the honest answer. Rendering "bundled" here would state something nobody
    /// established, and these two rows sit at the top of the tree where they are read first.
    /// <para>
    /// ⓘ <b>This does NOT exercise the null-provenance branch in <c>ApplyProvenanceBadge</c>,
    /// and a canary proved it.</b> These rows never reach that method — only section headers
    /// do — so making the null case render "bundled" leaves this test green. What it actually
    /// pins is that the schemaless rows are never given a badge by some later change, which is
    /// worth pinning on its own; it is simply not coverage of that branch.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task RowsWithNoSchemaCarryNoBadge()
    {
        MainWindowViewModel vm = await InitializedAsync(new PerUrlHandler());

        List<NavigationNodeViewModel> schemaless =
            [.. vm.Navigation.Where(n => n.Children.Count == 0)];

        Assert.IsTrue(schemaless.Count > 0,
            "Premise: the tree should hold the Essentials and Artifacts rows.");

        foreach (NavigationNodeViewModel row in schemaless)
        {
            Assert.IsTrue(string.IsNullOrEmpty(row.Badge),
                $"'{row.Title}' owns no schema but claims provenance '{row.Badge}'.");
        }
    }

    /// <summary>
    /// The tooltip carries the short fingerprint, which is what a bug report needs.
    /// </summary>
    [TestMethod]
    public async Task TheTooltipCarriesTheFingerprint()
    {
        MainWindowViewModel vm = await InitializedAsync(new PerUrlHandler());
        NavigationNodeViewModel header = SectionHeaders(vm)[0];

        using SchemaRegistry probe = new(new HttpClient(new PerUrlHandler()));
        _ = await probe.GetSchemaAsync(
            OpenCodeProducts.Config.SchemaUrl,
            OpenCodeProducts.Config.SchemaFileName,
            TestContext.CancellationTokenSource.Token);

        string expected = probe.ProvenanceFor(OpenCodeProducts.Config.SchemaFileName)!.ShortSha;

        Assert.IsNotNull(header.BadgeTooltip);
        StringAssert.Contains(header.BadgeTooltip, expected, StringComparison.Ordinal,
            "The tooltip omits the fingerprint, so two installs cannot be compared from a "
            + "screenshot — which is the case the badge exists for.");
    }
}
