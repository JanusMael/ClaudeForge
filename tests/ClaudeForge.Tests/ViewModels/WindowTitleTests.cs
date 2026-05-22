using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MainWindowViewModel.WindowTitle"/> — the titlebar
/// string format
/// <code>
/// "ClaudeForge — &lt;branch|folder|No Project Loaded&gt;[ *]"
/// </code>
/// </summary>
[TestClass]
public sealed class WindowTitleTests
{
    private string _sandbox = null!;
    private MainWindowViewModel _vm = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "wt-" + Guid.NewGuid().ToString("N"));
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
            try
            {
                Directory.Delete(_sandbox, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Prefix: app name only
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WindowTitle_AlwaysStartsWithAppTitle()
    {
        _vm.ProjectRoot = null;
        StringAssert.StartsWith(_vm.WindowTitle, "ClaudeForge",
            $"Expected '{Strings.AppTitle}' prefix; got '{_vm.WindowTitle}'.");

        _vm.ProjectRoot = _sandbox;
        StringAssert.StartsWith(_vm.WindowTitle, "ClaudeForge");
    }

    // -----------------------------------------------------------------------
    // Project indicator suffix
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WindowTitle_NoProject_AppendsNoProjectLoaded()
    {
        _vm.ProjectRoot = null;
        Assert.AreEqual($"ClaudeForge — {Strings.TitleNoProjectLoaded}", _vm.WindowTitle);
    }

    [TestMethod]
    public void WindowTitle_NonGitFolder_AppendsFolderName()
    {
        string projectDir = Path.Combine(_sandbox, "my-project");
        Directory.CreateDirectory(projectDir);

        _vm.ProjectRoot = projectDir;
        Assert.AreEqual("ClaudeForge — my-project", _vm.WindowTitle);
    }

    [TestMethod]
    public void WindowTitle_GitRepoOnBranch_AppendsBranchName()
    {
        string projectDir = Path.Combine(_sandbox, "git-project");
        Directory.CreateDirectory(projectDir);
        string gitDir = Path.Combine(projectDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature/auth\n");

        _vm.ProjectRoot = projectDir;
        Assert.AreEqual("ClaudeForge — git-project - feature/auth", _vm.WindowTitle);
    }

    // -----------------------------------------------------------------------
    // Unsaved-changes asterisk (W4)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WindowTitle_HasUnsavedChanges_AppendsAsterisk()
    {
        _vm.ProjectRoot = null;
        _vm.HasUnsavedChanges = true;
        StringAssert.EndsWith(_vm.WindowTitle, " *");

        _vm.HasUnsavedChanges = false;
        Assert.IsFalse(_vm.WindowTitle.EndsWith(" *"),
            "Title must NOT have trailing asterisk when HasUnsavedChanges is false.");
    }

    [TestMethod]
    public void WindowTitle_GitRepoWithUnsavedChanges_BranchThenAsterisk()
    {
        string projectDir = Path.Combine(_sandbox, "dirty-repo");
        Directory.CreateDirectory(projectDir);
        string gitDir = Path.Combine(projectDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");

        _vm.ProjectRoot = projectDir;
        _vm.HasUnsavedChanges = true;

        Assert.AreEqual("ClaudeForge — dirty-repo - main *", _vm.WindowTitle,
            "Format must be 'AppTitle — indicator *' (asterisk AFTER the indicator, not before).");
    }

    // -----------------------------------------------------------------------
    // Change notification
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WindowTitle_FiresPropertyChanged_OnProjectRootChange()
    {
        List<string> fired = new();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                fired.Add(e.PropertyName);
            }
        };

        _vm.ProjectRoot = Path.Combine(_sandbox, "trigger-change");
        Directory.CreateDirectory(_vm.ProjectRoot);

        Assert.IsTrue(fired.Contains(nameof(MainWindowViewModel.WindowTitle)),
            "Changing ProjectRoot must raise PropertyChanged for WindowTitle so the " +
            "Window's bound Title actually re-renders.  Pre-fix: only HasUnsavedChanges " +
            "had the NotifyPropertyChangedFor wiring.");
    }

    [TestMethod]
    public void WindowTitle_FiresPropertyChanged_OnHasUnsavedChangesFlip()
    {
        List<string> fired = new();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
            {
                fired.Add(e.PropertyName);
            }
        };

        _vm.HasUnsavedChanges = true;
        Assert.IsTrue(fired.Contains(nameof(MainWindowViewModel.WindowTitle)));
    }

    // -----------------------------------------------------------------------
    // Test scaffolding — minimal IDialogService stub mirroring the pattern
    // used by sibling MWVM test fixtures (EditingContextSummaryTests etc.).
    // -----------------------------------------------------------------------

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