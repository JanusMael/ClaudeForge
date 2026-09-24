using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MainWindowViewModel.WindowTitle"/> — the titlebar
/// string format
/// <code>
/// "ClaudeForge — &lt;branch|folder|No Project Loaded&gt;[ *]"
/// </code>
/// </summary>
public sealed class WindowTitleTests : IDisposable
{
    private string _sandbox = null!;
    private MainWindowViewModel _vm = null!;

    public WindowTitleTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "wt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _vm = new MainWindowViewModel(ClaudeEnvironment.Empty, new SchemaRegistry(), new NullDialogService());
    }

    private void Cleanup()
    {
        _vm.Dispose();
        PlatformPaths.TestUserProfileOverride = null;
        if (Directory.Exists(_sandbox))
        {
            try
            {
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // Prefix: app name only
    // -----------------------------------------------------------------------

    [Fact]
    public void WindowTitle_AlwaysStartsWithAppTitle()
    {
        _vm.ProjectRoot = null;
        MessageAssert.StartsWith("ClaudeForge", _vm.WindowTitle,
            $"Expected '{Strings.AppTitle}' prefix; got '{_vm.WindowTitle}'.");

        _vm.ProjectRoot = _sandbox;
        OrdinalAssert.StartsWith("ClaudeForge", _vm.WindowTitle);
    }

    // -----------------------------------------------------------------------
    // Project indicator suffix
    // -----------------------------------------------------------------------

    [Fact]
    public void WindowTitle_NoProject_AppendsNoProjectLoaded()
    {
        _vm.ProjectRoot = null;
        Assert.Equal($"ClaudeForge — {Strings.TitleNoProjectLoaded}", _vm.WindowTitle);
    }

    [Fact]
    public void WindowTitle_NonGitFolder_AppendsFolderName()
    {
        string projectDir = Path.Combine(_sandbox, "my-project");
        Directory.CreateDirectory(projectDir);

        _vm.ProjectRoot = projectDir;
        Assert.Equal("ClaudeForge — my-project", _vm.WindowTitle);
    }

    [Fact]
    public void WindowTitle_GitRepoOnBranch_AppendsBranchName()
    {
        string projectDir = Path.Combine(_sandbox, "git-project");
        Directory.CreateDirectory(projectDir);
        string gitDir = Path.Combine(projectDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/feature/auth\n");

        _vm.ProjectRoot = projectDir;
        Assert.Equal("ClaudeForge — git-project - feature/auth", _vm.WindowTitle);
    }

    // -----------------------------------------------------------------------
    // Unsaved-changes asterisk (W4)
    // -----------------------------------------------------------------------

    [Fact]
    public void WindowTitle_HasUnsavedChanges_AppendsAsterisk()
    {
        _vm.ProjectRoot = null;
        _vm.HasUnsavedChanges = true;
        OrdinalAssert.EndsWith(" *", _vm.WindowTitle);

        _vm.HasUnsavedChanges = false;
        Assert.False(_vm.WindowTitle.EndsWith(" *"),
            "Title must NOT have trailing asterisk when HasUnsavedChanges is false.");
    }

    [Fact]
    public void WindowTitle_GitRepoWithUnsavedChanges_BranchThenAsterisk()
    {
        string projectDir = Path.Combine(_sandbox, "dirty-repo");
        Directory.CreateDirectory(projectDir);
        string gitDir = Path.Combine(projectDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");

        _vm.ProjectRoot = projectDir;
        _vm.HasUnsavedChanges = true;

        MessageAssert.Equal("ClaudeForge — dirty-repo - main *", _vm.WindowTitle,
            "Format must be 'AppTitle — indicator *' (asterisk AFTER the indicator, not before).");
    }

    // -----------------------------------------------------------------------
    // Change notification
    // -----------------------------------------------------------------------

    [Fact]
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

        Assert.True(fired.Contains(nameof(MainWindowViewModel.WindowTitle)),
            "Changing ProjectRoot must raise PropertyChanged for WindowTitle so the " +
            "Window's bound Title actually re-renders.  Pre-fix: only HasUnsavedChanges " +
            "had the NotifyPropertyChangedFor wiring.");
    }

    [Fact]
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
        OrdinalAssert.Contains(nameof(MainWindowViewModel.WindowTitle), fired);
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