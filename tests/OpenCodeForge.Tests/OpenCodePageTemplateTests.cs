using System.Xml.Linq;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using Bennewitz.Ninja.OpenCode.Avalonia.Artifacts;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// Every PAGE the navigation tree can select has a view registered for it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>The same silent-failure shape as the specialised editors, one level up.</b>
/// <c>MainWindow.axaml</c> binds the selected node's <c>Editor</c> straight into a
/// <c>ContentControl</c>, and <see cref="NavigationNodeViewModel.Editor"/> is typed
/// <see langword="object"/> — so a page whose view-model has no <c>App.axaml</c> template renders
/// as the <b>type name</b>. It compiles, it runs, it logs nothing.
/// </para>
/// <para>
/// Before the artifacts page the window named <c>SettingsPageHost</c> directly, which made this
/// impossible to get wrong and also made a non-schema page impossible to add. Trading one for the
/// other is only safe with this guard in place.
/// </para>
/// <para>
/// Walks the REAL navigation tree rather than a hardcoded list, so a third kind of page is covered
/// the day it is added. Asserts over parsed attribute values, never over file text, so a type named
/// in an XML comment cannot count as a registration.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodePageTemplateTests
{
    private string _sandbox = string.Empty;

    public required TestContext TestContext { get; set; }

    [TestInitialize]
    public void Setup()
    {
        // Redirected so nothing here can read or write the developer's own configuration.
        _sandbox = Path.Combine(Path.GetTempPath(), "ocpage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_CONFIG_DIR", _sandbox);
        Environment.SetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG", "1");
    }

    [TestCleanup]
    public void Cleanup()
    {
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

    /// <summary>The type names <c>App.axaml</c> declares templates for, without their prefix.</summary>
    private static HashSet<string> TemplatedTypeNames()
    {
        string appAxaml = Path.Combine(RepoRoot(), "src", "OpenCodeForge", "App.axaml");
        Assert.IsTrue(File.Exists(appAxaml), $"'{appAxaml}' not found.");

        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        return XDocument
            .Load(appAxaml)
            .Descendants(avalonia + "DataTemplate")
            .Select(t => (string?)t.Attribute(xaml + "DataType") ?? (string?)t.Attribute("DataType"))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Split(':')[^1].Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static MainWindowViewModel BuildViewModel() => new(
        new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
            OpenCodePageLayout.Config, () => Strings.SectionOpenCode),
        new HostedSection(OpenCodeProducts.Tui, new OpenCodeTuiClient(),
            OpenCodePageLayout.Tui, () => Strings.SectionOpenCodeTui));

    private static IEnumerable<NavigationNodeViewModel> Flatten(
        IEnumerable<NavigationNodeViewModel> nodes)
    {
        foreach (NavigationNodeViewModel node in nodes)
        {
            yield return node;
            foreach (NavigationNodeViewModel child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    [TestMethod]
    public async Task EveryPageTheNavigationTreeCanSelect_HasADataTemplate()
    {
        File.WriteAllText(Path.Combine(_sandbox, "opencode.json"), """{ "logLevel": "INFO" }""");

        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        HashSet<string> templated = TemplatedTypeNames();
        List<string> pageTypes = [.. Flatten(vm.Navigation)
            .Select(n => n.Editor)
            .Where(e => e is not null)
            .Select(e => e!.GetType().Name)
            .Distinct(StringComparer.Ordinal)];

        Assert.IsTrue(pageTypes.Count > 0,
            "No page view-models were found in the navigation tree, so this test checked nothing.");

        List<string> missing = [.. pageTypes.Where(t => !templated.Contains(t))];

        Assert.AreEqual(0, missing.Count,
            $"{missing.Count} page view-model(s) reachable from the navigation tree have no "
            + "Application.DataTemplates entry in src/OpenCodeForge/App.axaml, so selecting them "
            + "renders the type name instead of the page:\n  "
            + string.Join("\n  ", missing.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The two page kinds are genuinely different types, which is the whole reason the window
    /// had to stop naming one host directly.
    /// </summary>
    [TestMethod]
    public async Task TheNavigationTreeHoldsMoreThanOneKindOfPage()
    {
        File.WriteAllText(Path.Combine(_sandbox, "opencode.json"), """{ "logLevel": "INFO" }""");

        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        List<Type> kinds = [.. Flatten(vm.Navigation)
            .Select(n => n.Editor)
            .Where(e => e is not null)
            .Select(e => e!.GetType())
            .Distinct()];

        CollectionAssert.Contains(kinds, typeof(SettingsGroupEditorViewModel));
        CollectionAssert.Contains(kinds, typeof(OpenCodeArtifactsPageViewModel));
    }

    /// <summary>
    /// The artifacts page appears even when every settings section fails to load.
    /// </summary>
    /// <remarks>
    /// ⭐ Deliberate, not incidental: a user whose configuration is too broken for OpenCode to
    /// start is precisely the user who needs to see which files it would have read. Building it
    /// inside the per-section loop would have made it disappear exactly then.
    /// </remarks>
    [TestMethod]
    public async Task TheArtifactsPageIsPresentEvenWithNoWorkingSections()
    {
        MainWindowViewModel vm = new();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        NavigationNodeViewModel? artifacts = Flatten(vm.Navigation)
            .FirstOrDefault(n => n.NodeId == MainWindowViewModel.ArtifactsNodeId);

        Assert.IsNotNull(artifacts, "The artifacts page must always be reachable.");
        Assert.IsInstanceOfType<OpenCodeArtifactsPageViewModel>(artifacts.Editor);
    }
}
