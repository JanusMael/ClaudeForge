using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using Microsoft.Extensions.Time.Testing;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// The post-save window during which a config-file-watcher event is treated as the echo of our
/// own write rather than an external edit.
/// <para>
/// Before <see cref="MainWindowViewModel"/> took a <see cref="TimeProvider"/>, neither half of
/// this behaviour was asserted anywhere: the only way to express "an event outside the window is
/// NOT suppressed" was to sleep for the whole two seconds, so nobody did. Advancing a
/// <see cref="FakeTimeProvider"/> makes both halves assertable and the whole class runs in
/// milliseconds.
/// </para>
/// </summary>
[TestClass]
public sealed class SelfWriteSuppressionWindowTests
{
    private string _sandbox = null!;
    private FakeTimeProvider _time = null!;
    private SchemaRegistry _schemaRegistry = null!;
    private MainWindowViewModel _vm = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        _time = new FakeTimeProvider();

        // Held in a field so Cleanup can drain the registry's fire-and-forget disk-cache sync
        // before the sandbox is deleted — see the AGENTS.md §3 entry on draining before delete.
        _schemaRegistry = new SchemaRegistry();
        _vm = new MainWindowViewModel(_schemaRegistry, new NullDialogService(), timeProvider: _time);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_vm.LastAutomaticReload is { } reload)
        {
            // Wait for it to finish, not to succeed; reading Exception marks a fault observed.
            await reload.ContinueWith(static t => _ = t.Exception,
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        await _schemaRegistry.WhenDiskCacheIdleAsync();

        _vm.Dispose();
        _schemaRegistry.Dispose();
        PlatformPaths.TestUserProfileOverride = null;
        TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
    }

    [TestMethod]
    public void BeforeAnySelfWrite_NothingIsSuppressed()
    {
        // The default deadline is DateTimeOffset.MinValue, so a watcher event on a freshly
        // constructed view-model must read as a genuine external edit.
        Assert.IsFalse(_vm.IsWithinSelfWriteSuppressionWindow(),
            "With no save having happened, a watcher event is an external edit and must reload.");
    }

    [TestMethod]
    public void ImmediatelyAfterStamping_TheWindowIsOpen()
    {
        _vm.StampSelfWriteSuppressionWindow();

        Assert.IsTrue(_vm.IsWithinSelfWriteSuppressionWindow(),
            "The watcher re-firing on our own write must not trigger a reload.");
    }

    [TestMethod]
    public void JustInsideTheWindow_IsStillSuppressed()
    {
        _vm.StampSelfWriteSuppressionWindow();

        _time.Advance(MainWindowViewModel.SelfWriteSuppressionWindow - TimeSpan.FromMilliseconds(1));

        Assert.IsTrue(_vm.IsWithinSelfWriteSuppressionWindow(),
            "One millisecond before the deadline is still inside the window.");
    }

    [TestMethod]
    public void AtTheDeadline_SuppressionHasLapsed()
    {
        _vm.StampSelfWriteSuppressionWindow();

        _time.Advance(MainWindowViewModel.SelfWriteSuppressionWindow);

        // The comparison is strict (now < deadline), so the boundary itself is already outside.
        // This is the half that was previously unassertable without a real two-second sleep.
        Assert.IsFalse(_vm.IsWithinSelfWriteSuppressionWindow(),
            "At the deadline the window has closed and an external edit must reload again.");
    }

    [TestMethod]
    public void WellAfterTheWindow_SuppressionHasLapsed()
    {
        _vm.StampSelfWriteSuppressionWindow();

        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.IsFalse(_vm.IsWithinSelfWriteSuppressionWindow());
    }

    [TestMethod]
    public void StampingAgain_ExtendsTheWindowFromTheNewNow()
    {
        // A second save inside an open window must push the deadline out, not leave it where the
        // first save put it — otherwise a rapid save-save pair stops suppressing partway through
        // the second write.
        _vm.StampSelfWriteSuppressionWindow();
        _time.Advance(MainWindowViewModel.SelfWriteSuppressionWindow - TimeSpan.FromMilliseconds(1));

        _vm.StampSelfWriteSuppressionWindow();
        _time.Advance(MainWindowViewModel.SelfWriteSuppressionWindow - TimeSpan.FromMilliseconds(1));

        Assert.IsTrue(_vm.IsWithinSelfWriteSuppressionWindow(),
            "The re-stamp must restart the window from the second save, not keep the first deadline.");
    }

    /// <summary>
    /// Same do-nothing stub the sibling view-model fixtures declare; the constructor requires an
    /// <see cref="IDialogService"/> and no test here opens a dialog.
    /// </summary>
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
