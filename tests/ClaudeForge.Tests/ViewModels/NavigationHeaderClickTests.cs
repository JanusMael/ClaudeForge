using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for the navigation-header click contract on
/// <see cref="MainWindowViewModel.OnSelectedNodeChanged"/>:
/// selecting a node whose <see cref="NavigationNodeViewModel.Editor"/> is
/// <c>null</c> (the "Claude Code" / "Claude Desktop" header rows, the
/// visual separator) clears <see cref="MainWindowViewModel.ActiveEditor"/>
/// so the welcome view becomes visible.
/// <para>
/// This regression-protects the user-facing contract introduced in place of
/// the now-removed <c>--showWelcomeView</c> debug flag: clicking a header
/// is the supported path back to the welcome view, replacing the need for
/// a startup-only debug switch.
/// </para>
/// </summary>
[TestClass]
public sealed class NavigationHeaderClickTests
{
    private string _sandbox = null!;
    private MainWindowViewModel _vm = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _vm = new MainWindowViewModel(new SchemaRegistry(), new NullDialogService());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _vm.Dispose();
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    [TestMethod]
    public void SelectingHeaderNode_ClearsActiveEditor()
    {
        // Simulate: a leaf editor is currently selected.
        NavigationNodeViewModel leaf = new("Some leaf") { Editor = new object() };
        _vm.SelectedNode = leaf;
        Assert.IsNotNull(_vm.ActiveEditor, "Setup: selecting a leaf populates ActiveEditor.");

        // User clicks a header row (no Editor) — ActiveEditor must clear so the
        // welcome view's "ActiveEditor IsNull" binding becomes true.
        NavigationNodeViewModel header = new("Claude Code", "⚙", "section header");
        _vm.SelectedNode = header;

        Assert.IsNull(_vm.ActiveEditor,
            "Selecting a header (Editor==null) must clear ActiveEditor so the welcome view shows.");
    }

    [TestMethod]
    public void SelectingNullNode_LeavesActiveEditorIntact()
    {
        // Programmatic clears (e.g. between workspace reloads) must NOT yank the
        // user's editor away — they will receive a real selection in a moment.
        NavigationNodeViewModel leaf = new("Some leaf") { Editor = new object() };
        _vm.SelectedNode = leaf;
        object? beforeNull = _vm.ActiveEditor;

        _vm.SelectedNode = null;

        Assert.AreSame(beforeNull, _vm.ActiveEditor,
            "Setting SelectedNode=null is a programmatic clear and must leave ActiveEditor in place.");
    }

    [TestMethod]
    public void SelectingHeaderThenLeaf_RestoresActiveEditor()
    {
        NavigationNodeViewModel leaf = new("Some leaf") { Editor = new object() };
        NavigationNodeViewModel header = new("Claude Code", "⚙", "section header");

        _vm.SelectedNode = leaf;
        _vm.SelectedNode = header;
        Assert.IsNull(_vm.ActiveEditor, "Header click cleared editor.");

        _vm.SelectedNode = leaf;
        Assert.AreSame(leaf.Editor, _vm.ActiveEditor,
            "Selecting a leaf again must restore ActiveEditor.");
    }

    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task ShowAlertAsync(string title, string message)
        {
            return Task.CompletedTask;
        }

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel")
        {
            return Task.FromResult<bool?>(false);
        }

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
        {
            return Task.FromResult(false);
        }
    }
}