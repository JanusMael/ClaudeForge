using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using System.ComponentModel;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for the editing-context surfacing properties on
/// <see cref="MainWindowViewModel"/> — the values that drive the Welcome
/// page's "What you're editing now" panel and the persistent status row at
/// the top of MainWindow.
/// <para>
/// All properties are pure functions of <see cref="MainWindowViewModel.ProjectRoot"/>;
/// the test fixture changes that one observable and asserts the derived
/// values + change notifications stay consistent.
/// </para>
/// </summary>
public sealed class EditingContextSummaryTests : IDisposable
{
    private string _sandbox = null!;
    private MainWindowViewModel _vm = null!;

    public EditingContextSummaryTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
            TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Build a platform-correct project-root path for the test fixture.
    /// <para>
    /// Previously these tests hard-coded <c>"C:\\repos\\demo"</c>, which
    /// works on Windows but fails on Linux/macOS — <see cref="Path.GetFileName(string)"/>
    /// only treats backslash as a directory separator on Windows.  On
    /// Linux a string like <c>"C:\\repos\\demo"</c> is just a flat name
    /// containing literal backslashes, so <c>ProjectFolderName</c>
    /// returns the whole string and the assertions fail.
    /// </para>
    /// <para>
    /// This helper returns a path that uses the host OS's directory
    /// separator, so the production <see cref="Path"/> APIs behave the
    /// way the test author intended on every platform.
    /// </para>
    /// </summary>
    private static string MakeProjectRoot(string leaf, bool trailingSeparator = false) =>
        OperatingSystem.IsWindows()
            ? $@"C:\repos\{leaf}{(trailingSeparator ? @"\" : "")}"
            : $"/repos/{leaf}{(trailingSeparator ? "/" : "")}";

    // -----------------------------------------------------------------------
    // No-project mode
    // -----------------------------------------------------------------------

    [Fact]
    public void NoProject_IsProjectOpenIsFalse()
    {
        _vm.ProjectRoot = null;
        Assert.False(_vm.IsProjectOpen);
    }

    [Fact]
    public void NoProject_FolderNameIsEmpty()
    {
        _vm.ProjectRoot = null;
        Assert.Equal(string.Empty, _vm.ProjectFolderName);
    }

    [Fact]
    public void NoProject_ClaudeDirPathIsEmpty()
    {
        _vm.ProjectRoot = null;
        Assert.Equal(string.Empty, _vm.ProjectClaudeDirPath);
    }

    [Fact]
    public void NoProject_SummaryUsesNoProjectString()
    {
        _vm.ProjectRoot = null;
        Assert.Equal(Strings.TextEditingContextNoProject, _vm.EditingContextSummary);
    }

    [Fact]
    public void NoProject_IconIsHouse()
    {
        _vm.ProjectRoot = null;
        Assert.Equal("🏠", _vm.EditingContextIcon);
    }

    [Fact]
    public void UserSettingsPath_AlwaysAvailable()
    {
        // UserSettingsPath should always have a value, with or without a project open.
        _vm.ProjectRoot = null;
        Assert.False(string.IsNullOrEmpty(_vm.UserSettingsPath));
        Assert.EndsWith("settings.json", _vm.UserSettingsPath);
    }

    // -----------------------------------------------------------------------
    // Project-open mode
    // -----------------------------------------------------------------------

    [Fact]
    public void ProjectOpen_IsProjectOpenIsTrue()
    {
        _vm.ProjectRoot = MakeProjectRoot("demo");
        Assert.True(_vm.IsProjectOpen);
    }

    [Fact]
    public void ProjectOpen_FolderNameIsLeaf()
    {
        _vm.ProjectRoot = MakeProjectRoot("demo");
        Assert.Equal("demo", _vm.ProjectFolderName);
    }

    [Fact]
    public void ProjectOpen_FolderName_TrailingSlashStripped()
    {
        _vm.ProjectRoot = MakeProjectRoot("demo", trailingSeparator: true);
        MessageAssert.Equal("demo", _vm.ProjectFolderName,
            "Trailing separators must not produce an empty leaf name.");
    }

    [Fact]
    public void ProjectOpen_ClaudeDirPathIsProjectDotClaude()
    {
        _vm.ProjectRoot = MakeProjectRoot("demo");
        Assert.True(_vm.ProjectClaudeDirPath.EndsWith(Path.Combine("demo", ".claude")),
            $"Expected ProjectClaudeDirPath to end with 'demo{Path.DirectorySeparatorChar}.claude' but got '{_vm.ProjectClaudeDirPath}'.");
    }

    [Fact]
    public void ProjectOpen_SummaryIncludesProjectName()
    {
        _vm.ProjectRoot = MakeProjectRoot("demo");
        MessageAssert.Contains("demo", _vm.EditingContextSummary,
            "Project-mode summary must include the project leaf name.");
    }

    [Fact]
    public void ProjectOpen_IconIsFolder()
    {
        _vm.ProjectRoot = MakeProjectRoot("demo");
        Assert.Equal("📁", _vm.EditingContextIcon);
    }

    // -----------------------------------------------------------------------
    // Change-notification — open / close / reopen
    // -----------------------------------------------------------------------

    [Fact]
    public void ProjectRootChange_RaisesPropertyChangedForDerivedProperties()
    {
        List<string?> seen = new();
        ((INotifyPropertyChanged)_vm).PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        _vm.ProjectRoot = MakeProjectRoot("demo");

        // The [NotifyPropertyChangedFor] attributes on _projectRoot must
        // surface as PropertyChanged events for every derived UI property
        // — otherwise the status row and Welcome panel go stale on open/close.
        Assert.Contains(nameof(MainWindowViewModel.IsProjectOpen), seen);
        Assert.Contains(nameof(MainWindowViewModel.ProjectFolderName), seen);
        Assert.Contains(nameof(MainWindowViewModel.ProjectClaudeDirPath), seen);
        Assert.Contains(nameof(MainWindowViewModel.EditingContextSummary), seen);
        Assert.Contains(nameof(MainWindowViewModel.EditingContextIcon), seen);
    }

    [Fact]
    public void ProjectRoot_OpenCloseReopen_TransitionsThroughBothStates()
    {
        // Closed
        _vm.ProjectRoot = null;
        Assert.False(_vm.IsProjectOpen);

        // Open A
        _vm.ProjectRoot = MakeProjectRoot("alpha");
        Assert.True(_vm.IsProjectOpen);
        Assert.Equal("alpha", _vm.ProjectFolderName);

        // Close
        _vm.ProjectRoot = null;
        Assert.False(_vm.IsProjectOpen);
        Assert.Equal(string.Empty, _vm.ProjectFolderName);

        // Open B
        _vm.ProjectRoot = MakeProjectRoot("beta");
        Assert.True(_vm.IsProjectOpen);
        Assert.Equal("beta", _vm.ProjectFolderName);
    }

    // -----------------------------------------------------------------------
    // Test double
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