using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Bennewitz.Ninja.OpenCodeForge.ViewModels;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// What "first runnable build" is supposed to mean: the app composes real settings pages from the
/// real bundled schemas, against configuration on disk, with no reference to the other product.
/// </summary>
/// <remarks>
/// These are view-model level rather than GUI tests. Avalonia headless cannot instantiate this
/// app's views — the shared headless harness is deliberately stripped of application resource
/// dictionaries — so the window itself is verified by running the app, not from here.
/// </remarks>
[TestClass]
public sealed class FirstRunnableBuildTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // A real directory with real files, redirected via OPENCODE_CONFIG_DIR so nothing here
        // can read or write the developer's own configuration.
        _sandbox = Path.Combine(Path.GetTempPath(), "ocf-" + Guid.NewGuid().ToString("N"));
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

    private void WriteGlobalConfig(string json) =>
        File.WriteAllText(Path.Combine(_sandbox, "opencode.json"), json);

    private static MainWindowViewModel BuildViewModel() => new(
        new HostedSection(OpenCodeProducts.Config, new OpenCodeClient(),
            OpenCodePageLayout.Config, () => Strings.SectionOpenCode),
        new HostedSection(OpenCodeProducts.Tui, new OpenCodeTuiClient(),
            OpenCodePageLayout.Tui, () => Strings.SectionOpenCodeTui));

    // ── the headline claim ───────────────────────────────────────────────────

    /// <summary>
    /// Both sections load and produce settings pages carrying real editors. This is the assertion
    /// that the extraction phases paid off: every page here is the shell's neutral group editor.
    /// </summary>
    [TestMethod]
    public async Task Initialize_BuildsSettingsPagesForBothSections()
    {
        WriteGlobalConfig("""{ "model": "anthropic/claude-sonnet-4-5", "logLevel": "INFO" }""");

        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(string.Empty, vm.Status,
            $"Both sections should have loaded cleanly. Status was: {vm.Status}");
        Assert.AreEqual(2, vm.Navigation.Count, "One navigation header per hosted section.");

        foreach (var header in vm.Navigation)
        {
            Assert.IsTrue(header.Children.Count > 0,
                $"Section '{header.Title}' produced no settings pages, so the schema either did "
                + "not load or bucketed into nothing.");
            Assert.IsTrue(
                header.Children.All(c => c.Editor is SettingsGroupEditorViewModel),
                $"Every page under '{header.Title}' must be the shell's neutral group editor.");
        }

        Assert.IsNotNull(vm.SelectedNode, "A page should be selected on load.");
    }

    /// <summary>
    /// Pages carry actual property editors, not empty shells. A layout that buckets nothing would
    /// still produce page nodes, so counting pages alone proves very little.
    /// </summary>
    [TestMethod]
    public async Task Pages_CarryRealPropertyEditors()
    {
        WriteGlobalConfig("{}");

        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        int editors = vm.Navigation
            .SelectMany(h => h.Children)
            .Select(c => (SettingsGroupEditorViewModel)c.Editor!)
            .Sum(e => e.Editors.Count);

        Assert.IsTrue(editors > 20,
            $"Expected the real schemas to produce many editors across both sections; got {editors}.");
    }

    /// <summary>
    /// A value in the file reaches the editor that edits it — the end-to-end path from disk,
    /// through this product's scope ladder and merge policy, into the neutral page.
    /// </summary>
    [TestMethod]
    public async Task AValueOnDisk_ReachesItsEditor()
    {
        WriteGlobalConfig("""{ "username": "sandbox-user" }""");

        MainWindowViewModel vm = BuildViewModel();
        await vm.InitializeAsync(TestContext.CancellationTokenSource.Token);

        bool found = vm.Navigation
            .SelectMany(h => h.Children)
            .Select(c => (SettingsGroupEditorViewModel)c.Editor!)
            .SelectMany(e => e.Editors)
            .Any(e => e.Path == "username");

        Assert.IsTrue(found,
            "No editor was built for 'username', so the schema walk and the page layout disagree "
            + "about a key that is both in the schema and in the file.");
    }

    // ── independence from the other product ──────────────────────────────────

    /// <summary>
    /// ⚠ The point of the whole exercise: this app must not reference the other product, directly
    /// or transitively. A ProjectReference would compile fine and silently couple the two apps.
    /// </summary>
    [TestMethod]
    public void TheAppReferencesNoClaudeAssembly()
    {
        List<string> offenders =
        [
            .. typeof(MainWindowViewModel).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => n.StartsWith("ClaudeForge", StringComparison.Ordinal))
        ];

        Assert.IsTrue(offenders.Count == 0,
            "OpenCodeForge must not reference any ClaudeForge assembly: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The window-state file must not land under the other product's home directory, and must
    /// follow the config-dir redirection.
    /// </summary>
    [TestMethod]
    public void WindowState_IsWrittenBesideThisProductsConfig()
    {
        Services.WindowStateService.Save(new Services.WindowState(1000, 700, IsMaximized: false));

        string expected = Path.Combine(_sandbox, "cache", "OpenCodeForge-gui-state.json");
        Assert.IsTrue(File.Exists(expected),
            $"Expected state at '{expected}'. If this fails, the path either ignored "
            + "OPENCODE_CONFIG_DIR or was written somewhere else entirely.");

        Services.WindowState loaded = Services.WindowStateService.Load();
        Assert.AreEqual(1000, loaded.Width);
        Assert.AreEqual(700, loaded.Height);

        StringAssert.DoesNotMatch(expected, new System.Text.RegularExpressions.Regex(@"\.claude"),
            "State must never be written under the other product's home directory.");
    }

    /// <summary>
    /// A corrupt state file must not stop the app from opening. It is the least important file the
    /// app owns, and the failure mode of getting this wrong is an app that will not start.
    /// </summary>
    [TestMethod]
    public void WindowState_SurvivesACorruptFile()
    {
        string path = Path.Combine(_sandbox, "cache", "OpenCodeForge-gui-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json at all ");

        Assert.AreEqual(Services.WindowState.Default, Services.WindowStateService.Load());
    }

    // ── localization ─────────────────────────────────────────────────────────

    /// <summary>
    /// Every resource key resolves. The resource base name is a literal that must match
    /// RootNamespace plus the folder, and a mismatch fails at runtime rather than at build — a key
    /// returning its own name is what that looks like.
    /// </summary>
    [TestMethod]
    public void EveryStringResolves()
    {
        (string Name, string Value)[] all =
        [
            (nameof(Strings.AppTitle), Strings.AppTitle),
            (nameof(Strings.HeaderTabProperties), Strings.HeaderTabProperties),
            (nameof(Strings.HeaderTabEffective), Strings.HeaderTabEffective),
            (nameof(Strings.HeaderTabJsonAll), Strings.HeaderTabJsonAll),
            (nameof(Strings.HeaderTabJsonActive), Strings.HeaderTabJsonActive),
            (nameof(Strings.SectionOpenCode), Strings.SectionOpenCode),
            (nameof(Strings.SectionOpenCodeTui), Strings.SectionOpenCodeTui),
        ];

        foreach ((string name, string value) in all)
        {
            Assert.AreNotEqual(name, value,
                $"'{name}' resolved to its own key name, which is what a missing resource or a "
                + "wrong resource base name looks like at runtime.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"'{name}' resolved to blank.");
        }
    }

    /// <summary>
    /// ⚠ No user-facing string this app can render may name the other product. The editor
    /// library's fallback tooltips are the specific trap: unwired, a host inherits them, and one
    /// of them used to name a competitor.
    /// </summary>
    [TestMethod]
    public void NoReachableStringNamesTheOtherProduct()
    {
        List<string> reachable =
        [
            Strings.AppTitle, Strings.HeaderTabProperties, Strings.HeaderTabEffective,
            Strings.HeaderTabJsonAll, Strings.HeaderTabJsonActive,
            Strings.SectionOpenCode, Strings.SectionOpenCodeTui,
            WrapperStrings.TipUndocumented, WrapperStrings.TipReadOnly,
            WrapperStrings.TipResetToInherited, WrapperStrings.TipShowSuggestions,
            WrapperStrings.TipNewSetting, WrapperStrings.LabelOverridden, WrapperStrings.LabelReset,
        ];

        List<string> offenders =
        [
            .. reachable.Where(s =>
                s.Contains("Claude", StringComparison.OrdinalIgnoreCase)
                || s.Contains("Anthropic", StringComparison.OrdinalIgnoreCase))
        ];

        Assert.IsTrue(offenders.Count == 0,
            "A string this app can render names the other product: " + string.Join(" | ", offenders));
    }

    /// <summary>Required by MSTest for the cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;
}
