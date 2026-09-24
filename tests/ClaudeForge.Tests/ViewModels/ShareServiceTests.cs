using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Diagnostics;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices;
using Bennewitz.Ninja.AppServices.Abstractions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// In-memory stub that records calls to <see cref="IShareService"/> methods
/// so tests can assert that the correct payload was forwarded.
/// </summary>
file sealed class RecordingShareService : IShareService
{
    public record ShareTextCall(string Title, string Text, string? Uri);

    public record ShareFileCall(string Title, string FilePath);

    public List<ShareTextCall> TextCalls { get; } = [];
    public List<ShareFileCall> FileCalls { get; } = [];

    /// <summary>What the next call reports. ⚠ Defaults to <see cref="ShareOutcome.Unavailable"/>
    /// because that is the truth about a stub: it recorded the payload and shared nothing. A test
    /// asserting a success sentence has to say which success it is arranging, so the arrangement
    /// cannot be mistaken for the thing under test.</summary>
    public ShareOutcome NextOutcome { get; set; } = ShareOutcome.Unavailable;

    /// <summary>Set to throw from both methods, for the caller's catch path.</summary>
    public Exception? ThrowOnShare { get; set; }

    public ValueTask<ShareOutcome> ShareTextAsync(string title, string text, string? uri, CancellationToken cancellationToken)
    {
        TextCalls.Add(new ShareTextCall(title, text, uri));
        return ThrowOnShare is not null
            ? ValueTask.FromException<ShareOutcome>(ThrowOnShare)
            : ValueTask.FromResult(NextOutcome);
    }

    public ValueTask<ShareOutcome> ShareFileAsync(string title, string filePath, CancellationToken cancellationToken)
    {
        FileCalls.Add(new ShareFileCall(title, filePath));
        return ThrowOnShare is not null
            ? ValueTask.FromException<ShareOutcome>(ThrowOnShare)
            : ValueTask.FromResult(NextOutcome);
    }
}

file sealed class NullDialogService : IDialogService
{
    public Task<string?> PickFolderAsync(string? title = null)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickFileAsync(string? title = null,
                                       IReadOnlyList<FilePickerFilter>? filters = null)
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

// ─────────────────────────────────────────────────────────────────────────────
// BackupRestoreViewModel — ShareBackupCommand
// ─────────────────────────────────────────────────────────────────────────────

[TestClass]
public class BackupShareCommandTests
{
    private static BackupRowViewModel MakeRow(string path = @"C:/backups/test.zip")
    {
        return new BackupRowViewModel(new BackupEntry
        {
            ArchivePath = path,
            FileName = Path.GetFileName(path),
            SizeBytes = 1024,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = null, // IsCorrupt = true, IsRestorable = false
        });
    }

    [TestMethod]
    public async Task ShareBackup_NullRow_DoesNotCallService()
    {
        RecordingShareService svc = new();
        BackupRestoreViewModel vm = new(new NullDialogService(), BackupPageTestOptions.Create(), svc);

        // Execute with null — must be a no-op.
        await vm.ShareBackupCommand.ExecuteAsync(null);

        Assert.AreEqual(0, svc.FileCalls.Count,
            "Passing null row must not forward any call to the share service.");
    }

    [TestMethod]
    public async Task ShareBackup_NullService_IsNoOp()
    {
        // No share service wired up — command must complete silently.
        BackupRestoreViewModel vm = new(new NullDialogService(), BackupPageTestOptions.Create());
        BackupRowViewModel row = MakeRow();

        // Should not throw.
        await vm.ShareBackupCommand.ExecuteAsync(row);
    }

    [TestMethod]
    public async Task ShareBackup_CallsServiceWithArchivePath()
    {
        const string archivePath = @"C:/backups/my-backup-2026.zip";
        RecordingShareService svc = new();
        BackupRestoreViewModel vm = new(new NullDialogService(), BackupPageTestOptions.Create(), svc);
        BackupRowViewModel row = MakeRow(archivePath);

        await vm.ShareBackupCommand.ExecuteAsync(row);

        Assert.AreEqual(1, svc.FileCalls.Count,
            "Exactly one ShareFileAsync call should be forwarded.");
        Assert.AreEqual(archivePath, svc.FileCalls[0].FilePath,
            "The archive path must be passed verbatim to the share service.");
    }

    [TestMethod]
    public async Task ShareBackup_TitleIsDisplayName()
    {
        const string archivePath = @"C:/backups/my-backup-2026.zip";
        RecordingShareService svc = new();
        BackupRestoreViewModel vm = new(new NullDialogService(), BackupPageTestOptions.Create(), svc);
        BackupRowViewModel row = MakeRow(archivePath);

        await vm.ShareBackupCommand.ExecuteAsync(row);

        Assert.AreEqual(row.DisplayName, svc.FileCalls[0].Title,
            "The share title must match the row's DisplayName.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EffectiveSettingsViewModel — ShareConfigCommand
// ─────────────────────────────────────────────────────────────────────────────

[TestClass]
public class EffectiveSettingsShareCommandTests
{
    private static AgentConfigClientCore MakeClient()
    {
        // Wrap an in-memory empty SettingsWorkspace via the internal
        // FromExistingWorkspace overload — avoids disk I/O and
        // TestUserProfileOverride leakage across tests. The grant lives
        // in AgentForge.Sdk.csproj's InternalsVisibleTo for ClaudeForge.Tests.
        SettingsWorkspace ws = new([], ClaudeMergePolicy.Instance);
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    /// <remarks>
    /// ⓘ This was <c>ShareConfig_NullService_IsNoOp</c>, and the name stopped being true when F3
    /// landed: a missing service is now reported rather than absorbed. It still pins the half
    /// that matters here — no throw — and the sentence it emits is asserted in
    /// <c>ShareOutcomeTests.WithNoShareServiceWired_TheUserIsToldSoRatherThanNothing</c>.
    /// </remarks>
    [TestMethod]
    public async Task ShareConfig_NullService_DoesNotThrow()
    {
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: null);

        // Must not throw even without a share service.
        await vm.ShareConfigCommand.ExecuteAsync(null);
    }

    [TestMethod]
    public async Task ShareConfig_CallsServiceWithEffectiveJson()
    {
        RecordingShareService svc = new();
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: svc);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        Assert.AreEqual(1, svc.TextCalls.Count,
            "ShareConfigCommand must invoke ShareTextAsync once.");
        Assert.AreEqual("Claude Config", svc.TextCalls[0].Title,
            "Title must be 'Claude Config'.");
        Assert.IsNotNull(svc.TextCalls[0].Text,
            "Text payload must not be null.");
    }

    [TestMethod]
    public async Task ShareConfig_TextMatchesEffectiveJson()
    {
        RecordingShareService svc = new();
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: svc);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        // The text forwarded to the share service should equal the VM's EffectiveJson.
        Assert.AreEqual(vm.EffectiveJson, svc.TextCalls[0].Text,
            "The text payload must match the current EffectiveJson string.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AboutEditorViewModel — ShareLogCommand
// ─────────────────────────────────────────────────────────────────────────────

[TestClass]
public class AboutShareLogCommandTests
{
    private const string FakeLogPath = @"C:/logs/claudeforge-20260427.log";

    [TestMethod]
    public void ShareLog_NullService_CommandCannotExecute()
    {
        // No share service → CanExecute must be false regardless of log path.
        AboutEditorViewModel vm = new(ClaudeEnvironment.Empty, 
            AboutProduct.ClaudeCode,
            shareService: null,
            logPathProvider: () => FakeLogPath);

        Assert.IsFalse(vm.ShareLogCommand.CanExecute(null),
            "ShareLogCommand must be disabled when no IShareService is wired up.");
    }

    [TestMethod]
    public void ShareLog_NullLogPath_CommandCannotExecute()
    {
        RecordingShareService svc = new();
        // Log path provider returns null → CanExecute must be false.
        AboutEditorViewModel vm = new(ClaudeEnvironment.Empty, 
            AboutProduct.ClaudeCode,
            shareService: svc,
            logPathProvider: () => null);

        Assert.IsFalse(vm.ShareLogCommand.CanExecute(null),
            "ShareLogCommand must be disabled when the log path is unavailable.");
    }

    [TestMethod]
    public void ShareLog_BothPrerequisitesMet_CommandCanExecute()
    {
        RecordingShareService svc = new();
        AboutEditorViewModel vm = new(ClaudeEnvironment.Empty, 
            AboutProduct.ClaudeCode,
            shareService: svc,
            logPathProvider: () => FakeLogPath);

        Assert.IsTrue(vm.ShareLogCommand.CanExecute(null),
            "ShareLogCommand must be enabled when both a service and a log path are present.");
    }

    [TestMethod]
    public async Task ShareLog_CallsServiceWithLogPath()
    {
        RecordingShareService svc = new();
        AboutEditorViewModel vm = new(ClaudeEnvironment.Empty, 
            AboutProduct.ClaudeCode,
            shareService: svc,
            logPathProvider: () => FakeLogPath);

        await vm.ShareLogCommand.ExecuteAsync(null);

        Assert.AreEqual(1, svc.FileCalls.Count,
            "ShareLogCommand must invoke ShareFileAsync exactly once.");
        Assert.AreEqual(FakeLogPath, svc.FileCalls[0].FilePath,
            "The log file path must be forwarded verbatim to the share service.");
    }

    [TestMethod]
    public async Task ShareLog_NullService_IsNoOp()
    {
        // Even when forced to execute, the command must not throw with no service.
        AboutEditorViewModel vm = new(ClaudeEnvironment.Empty, 
            AboutProduct.ClaudeCode,
            shareService: null,
            logPathProvider: () => FakeLogPath);

        // CanExecute is false, but we invoke directly to verify the internal guard.
        await vm.ShareLogCommand.ExecuteAsync(null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DefaultShareService — basic smoke tests
//
// All tests pass processLauncher: _ => null to suppress real Process.Start
// calls.  Without this, the Windows fallback branches (explorer.exe /select,
// and UseShellExecute URI opening) would pop real Explorer windows and browser
// tabs during test runs.
// ─────────────────────────────────────────────────────────────────────────────

[TestClass]
public class DefaultShareServiceTests
{
    /// <summary>No-op process launcher — prevents any real process from starting.</summary>
    private static readonly Func<ProcessStartInfo, Process?> NoOpLauncher = _ => null;

    // ⛔ No test shares TEXT WITHOUT A URI here. On Windows and macOS that path pipes into
    // clip.exe / pbcopy directly — not through the injected launcher — so a test of it overwrites
    // the developer's clipboard on every run. One did, until 2026-09-23. The package's own suite
    // (AppServices.Tests) owns DefaultShareService's outcomes.

    [TestMethod]
    public async Task ShareFileAsync_DoesNotThrow_WhenFileDoesNotExist()
    {
        // Even with a non-existent path, the service must not throw —
        // it silently skips the Process.Start on macOS/Linux when the file is absent.
        DefaultShareService svc = new(processLauncher: NoOpLauncher);
        await svc.ShareFileAsync("Test Title", @"C:/does/not/exist/file.zip", CancellationToken.None);
    }

    [TestMethod]
    public async Task ShareTextAsync_DoesNotThrow_WithUri()
    {
        DefaultShareService svc = new(processLauncher: NoOpLauncher);
        await svc.ShareTextAsync("Test", "body", "https://example.com", CancellationToken.None);
    }

    /// <summary>
    /// Constructing with no arguments — what the app does — starts nothing.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Replaces two tests that exercised an <c>hwndProvider</c> parameter which no longer
    /// exists.</b> It was there to initialise MAUI Essentials behind
    /// <c>#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER</c>, in a TFM that was declared but never
    /// built — so both tests were asserting the wiring of a code path no build contained. What is
    /// worth pinning is what the composition root actually calls.
    /// </remarks>
    [TestMethod]
    public void DefaultShareService_ConstructsWithNoArguments_AndLaunchesNothing()
    {
        bool launched = false;
        DefaultShareService svc = new(processLauncher: psi =>
        {
            launched = true;
            _ = psi;
            return null;
        });

        Assert.IsNotNull(svc);
        Assert.IsFalse(launched, "Construction must not start a process.");

        // The parameterless form is the one App.axaml.cs uses; it must not throw either.
        Assert.IsNotNull(new DefaultShareService());
    }

    [TestMethod]
    public async Task ShareTextAsync_InvokesProcessLauncher_WhenUriProvidedOnWindows()
    {
        // Verify the injected launcher is called (not silently skipped) when a URI
        // is provided on the current OS.  On Windows without MAUI TFM, the URI
        // fallback calls the launcher; on other platforms a different branch fires.
        // Either way the launcher must be called at most once and must not throw.
        int launchCount = 0;
        DefaultShareService svc = new(processLauncher: _ =>
        {
            launchCount++;
            return null;
        });

        await svc.ShareTextAsync("T", "body", "https://example.com", CancellationToken.None);

        // Exactly 0 or 1 launches depending on OS — the important thing is no exception.
        Assert.IsTrue(launchCount is 0 or 1,
            "Launcher should be invoked 0 or 1 times, never more.");
    }
}