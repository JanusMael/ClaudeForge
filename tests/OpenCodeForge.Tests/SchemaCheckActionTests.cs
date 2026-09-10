using System.Net;
using System.Text;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The <em>Check for schema updates</em> action: what it reports, and that the nav badges
/// move without the tree being rebuilt.
/// </summary>
/// <remarks>
/// ⭐ <b>The live re-badge is the whole reason <see cref="NavigationNodeViewModel.Badge"/> is
/// observable rather than <c>init</c>.</b> A test that only checked the returned summary would
/// stay green if the badges never moved, and the badge is the part a user actually sees.
/// </remarks>
[TestClass]
public sealed class SchemaCheckActionTests
{
    private string _sandbox = string.Empty;

    public required TestContext TestContext { get; set; }

    private static string Doc(string marker) =>
        $$"""
          {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
              "servedFor": { "type": "string", "description": "{{marker}}" }
            }
          }
          """;

    /// <summary>Refuses every request.</summary>
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated network unavailable");
    }

    /// <summary>
    /// Down while the window loads, up by the time the user presses the button.
    /// </summary>
    /// <remarks>
    /// ⚠ One failure is enough to cover the load: the registry latches
    /// <c>_networkUnavailable</c> after a connectivity-shaped error and stops probing for the
    /// rest of the session, so the second schema never attempts a fetch.
    /// <c>RefreshAsync</c> clearing that latch is precisely what makes an explicit retry work.
    /// </remarks>
    private sealed class ComesUpHandler : HttpMessageHandler
    {
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) <= 1)
            {
                throw new HttpRequestException("Simulated network unavailable");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Doc(request.RequestUri!.ToString()), Encoding.UTF8, "application/json"),
            });
        }
    }

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "occheck-" + Guid.NewGuid().ToString("N"));
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

    private static IReadOnlyList<NavigationNodeViewModel> SectionHeaders(MainWindowViewModel vm) =>
        [.. vm.Navigation.Where(n => n.Children.Count > 0)];

    /// <summary>
    /// ⭐ A check that reaches upstream moves the badges on the nodes already in the tree.
    /// </summary>
    [TestMethod]
    public async Task ASuccessfulCheck_MovesTheBadgesInPlace()
    {
        MainWindowViewModel vm = await InitializedAsync(new ComesUpHandler());
        IReadOnlyList<NavigationNodeViewModel> before = SectionHeaders(vm);

        Assert.AreEqual(2, before.Count, "Premise: both sections loaded.");
        foreach (NavigationNodeViewModel header in before)
        {
            Assert.AreEqual(Strings.SchemaBadgeBundled, header.Badge,
                "Premise: the window loaded while the network was down, so every badge starts "
                + "on the bundled copy. Without that the assertion below could pass without "
                + "anything having changed.");
        }

        string summary = await vm.CheckForSchemaUpdatesAsync(TestContext.CancellationTokenSource.Token);

        // Same node objects, not a rebuilt tree — that is the property being pinned.
        IReadOnlyList<NavigationNodeViewModel> after = SectionHeaders(vm);
        CollectionAssert.AreEqual(
            before.ToList(), after.ToList(),
            "The tree was rebuilt. The badge is observable precisely so it does not have to be.");

        foreach (NavigationNodeViewModel header in after)
        {
            Assert.IsNotNull(header.Badge);
            StringAssert.StartsWith(header.Badge, "fetched", StringComparison.Ordinal,
                $"'{header.Title}' still shows its load-time badge after a successful check.");
        }

        StringAssert.Contains(summary, "Reload", StringComparison.OrdinalIgnoreCase,
            "An Updated result must tell the user the editors do not yet reflect the new copy.");
    }

    /// <summary>
    /// A check that cannot reach upstream says so, rather than reporting no updates.
    /// </summary>
    [TestMethod]
    public async Task ACheckWithNoNetwork_ReportsUnavailable()
    {
        MainWindowViewModel vm = await InitializedAsync(new OfflineHandler());

        string summary = await vm.CheckForSchemaUpdatesAsync(TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(Strings.SchemaCheckUnavailable, summary);
    }

    // ── The summariser's severity order ──────────────────────────────────

    private static SchemaRefreshResult Result(string id, SchemaRefreshStatus status) =>
        new(new ProductDescriptor(id, id, "https://example.invalid/s.json", id + ".json",
                ArchiveFolder: id),
            status, null, null);

    [TestMethod]
    public void FailedOutranksEverything()
    {
        string line = MainWindowViewModel.SummariseSchemaCheck(
        [
            Result("a", SchemaRefreshStatus.Updated),
            Result("b", SchemaRefreshStatus.Failed),
        ]);

        StringAssert.Contains(line, "b", StringComparison.Ordinal);
        Assert.AreNotEqual(
            string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.SchemaCheckUpdatedFmt, "a"),
            line,
            "A run where something failed must not be reported as a plain update.");
    }

    [TestMethod]
    public void UpdatedOutranksUnavailableAndUpToDate()
    {
        string line = MainWindowViewModel.SummariseSchemaCheck(
        [
            Result("a", SchemaRefreshStatus.Unchanged),
            Result("b", SchemaRefreshStatus.Updated),
            Result("c", SchemaRefreshStatus.Unavailable),
        ]);

        StringAssert.Contains(line, "b", StringComparison.Ordinal,
            "The actionable outcome is the one that changed, and it names which product.");
    }

    [TestMethod]
    public void EverythingUnchanged_IsUpToDate()
    {
        string line = MainWindowViewModel.SummariseSchemaCheck(
        [
            Result("a", SchemaRefreshStatus.Unchanged),
            Result("b", SchemaRefreshStatus.Unchanged),
        ]);

        Assert.AreEqual(Strings.SchemaCheckUpToDate, line);
    }
}
