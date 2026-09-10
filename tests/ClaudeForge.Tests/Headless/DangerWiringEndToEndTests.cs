using System.Reflection;
using Avalonia.Headless;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;
using LibVm = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// The danger table reaches the real settings pages of the real window — not just the factory.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>This exists because a canary measured the gap.</b> Breaking
/// <c>MainWindowViewModel</c>'s wiring — passing <see langword="null"/> to
/// <c>NavigationTreeBuilder.BuildGroups</c> instead of the section's table — reddened
/// <b>nothing</b>. <c>ClaudeEditorDangerWiringTests</c> drives the factory directly and
/// <c>DangerSurfaceMarkupTests</c> reads the markup; between them sits the wiring that connects
/// the two, and it was unguarded. Every row in the app would have rendered with no severity while
/// the whole suite stayed green.
/// </para>
/// <para>
/// ⚠ Guards BOTH directions. Asserting only that Claude Code has a table would pass a change that
/// hands the same table to Claude Desktop, which is the specific mislabel the per-section wiring
/// exists to prevent.
/// </para>
/// </remarks>
[TestClass]
public sealed class DangerWiringEndToEndTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_dangerwiring_" + Guid.NewGuid().ToString("N"));
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
        DebugFlags.ResetForTesting();
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    /// <summary>Every settings-group editor beneath the nav node titled <paramref name="header"/>.</summary>
    private static List<SettingsGroupEditorViewModel> GroupsUnder(
        MainWindowViewModel vm, string header)
    {
        NavigationNodeViewModel? section = vm.NavigationTree.FirstOrDefault(
            n => string.Equals(n.Title, header, StringComparison.Ordinal));
        Assert.IsNotNull(section, $"no navigation header titled '{header}'");
        NavigationNodeViewModel root = section!;

        List<SettingsGroupEditorViewModel> found = [];
        Walk(root, found);
        return found;

        static void Walk(NavigationNodeViewModel node, List<SettingsGroupEditorViewModel> sink)
        {
            if (node.Editor is SettingsGroupEditorViewModel group)
            {
                sink.Add(group);
            }

            foreach (NavigationNodeViewModel child in node.Children)
            {
                Walk(child, sink);
            }
        }
    }

    [TestMethod]
    public async Task ClaudeCodesSettingsPages_CarryItsDangerTable()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        List<SettingsGroupEditorViewModel> groups = GroupsUnder(vm, "Claude Code");
        Assert.IsTrue(groups.Count > 0,
            "no Claude Code settings pages were built — the scan has lost its subject and would "
            + "pass without checking anything");

        List<LibVm.PropertyEditorViewModel> editors = [.. groups.SelectMany(g => g.Editors)];
        Assert.IsTrue(editors.Count > 0, "the pages built no editors");

        List<string> unwired = [.. editors
            .Where(e => e.DangerClassifier is null)
            .Select(e => e.Path)];

        Assert.IsTrue(unwired.Count == 0,
            $"{unwired.Count} of {editors.Count} Claude Code editor(s) reached the window without a "
            + $"danger classifier: {string.Join(", ", unwired.Take(10))}. The rows render with no "
            + "severity at all, which looks exactly like a product with nothing dangerous in it.");
    }

    [TestMethod]
    public async Task ClaudeDesktopsSettingsPages_CarryNoTable()
    {
        MainWindowViewModel vm = BuildViewModel();
        await vm.LoadAllWorkspacesAsync();

        List<SettingsGroupEditorViewModel> groups = GroupsUnder(vm, "Claude Desktop");
        Assert.IsTrue(groups.Count > 0, "no Claude Desktop settings pages were built");

        List<LibVm.PropertyEditorViewModel> editors = [.. groups.SelectMany(g => g.Editors)];
        Assert.IsTrue(editors.Count > 0, "the pages built no editors");

        List<string> wired = [.. editors
            .Where(e => e.DangerClassifier is not null)
            .Select(e => e.Path)];

        Assert.IsTrue(wired.Count == 0,
            $"{wired.Count} Claude Desktop editor(s) carry a danger classifier: "
            + $"{string.Join(", ", wired.Take(10))}. Nobody has triaged this product's keys, so a "
            + "table here can only be another product's — silent on most rows and confidently "
            + "wrong on any name that collides (env is in both schemas).");
    }

    private static MainWindowViewModel BuildViewModel()
    {
        return new MainWindowViewModel(new SchemaRegistry(), new NullDialogService());
    }

    /// <summary>Inert dialog service — these tests never open anything.</summary>
    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null) =>
            Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel") => Task.FromResult<bool?>(false);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(false);
    }
}
