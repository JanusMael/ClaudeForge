using System.Diagnostics;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Backup;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Status;
using Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.TestSupport;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.AgentForge.AvaloniaUI.Shell.Tests.ViewModels;

/// <summary>
/// What the backup page does with what its HOST supplies, beyond the copied suite in
/// <see cref="BackupRestoreViewModelTests"/> (plans/00008 step 4).
/// </summary>
/// <remarks>
/// On <c>main</c>, <c>ClaudeForge.Tests</c> reached the agent-running warning only when a real
/// <c>claude</c> process happened to be running on the machine — so its coverage of that branch
/// depended on who ran the suite. Here the host names THIS test process, which is running by
/// construction, so the branch is exercised every time and its outcome asserted.
/// </remarks>
public sealed class BackupRestoreViewModelHostTests
{
    private static BackupRowViewModel SameMachineRow() =>
        new(new BackupEntry
        {
            ArchivePath = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".zip"),
            FileName = "missing.zip",
            SizeBytes = 1,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest { Platform = "test" },
        });

    [Fact]
    public async Task Restore_WhileAHostNamedAgentIsRunning_WarnsWithTheHostsWording()
    {
        string self = Process.GetCurrentProcess().ProcessName;
        BackupPageOptions options = TestBackupPage.Options() with { AgentProcessNames = [self] };
        AcceptingDialogService dialog = new();
        BackupRestoreViewModel vm = new(dialog, options);

        List<string?> statuses = [];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BackupRestoreViewModel.StatusMessage))
            {
                statuses.Add(vm.StatusMessage);
            }
        };

        await vm.RestoreCommand.ExecuteAsync(SameMachineRow());

        string expectedPrefix = options.Text.StatusAgentRunningFmt.Split('{')[0];
        Assert.Contains(statuses, s => s is not null && s.StartsWith(expectedPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Restore_WhenTheHostNamesNoAgents_NeverWarns()
    {
        BackupPageOptions options = TestBackupPage.Options() with { AgentProcessNames = [] };
        BackupRestoreViewModel vm = new(new AcceptingDialogService(), options);

        List<string?> statuses = [];
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BackupRestoreViewModel.StatusMessage))
            {
                statuses.Add(vm.StatusMessage);
            }
        };

        await vm.RestoreCommand.ExecuteAsync(SameMachineRow());

        string runningPrefix = options.Text.StatusAgentRunningFmt.Split('{')[0];
        Assert.DoesNotContain(statuses, s => s is not null && s.StartsWith(runningPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Share_WithNoShareService_ReportsUnavailable_InTheHostsWording()
    {
        BackupRestoreViewModel vm = new(new AcceptingDialogService(), TestBackupPage.Options());
        List<(string Text, bool IsFailure)> reported = [];
        vm.OnTerminalStatus = (text, isFailure) => reported.Add((text, isFailure));

        await vm.ShareBackupCommand.ExecuteAsync(SameMachineRow());

        (string text, bool isFailure) = Assert.Single(reported);
        Assert.Equal(TestBackupPage.Text().StatusShareArchiveUnavailable, text);
        (string? expectedText, bool expectedFailure) = FileShareStatus.Describe(
            ShareOutcome.Unavailable, "r", TestBackupPage.Text().StatusShareArchiveUnavailable, "f");
        Assert.Equal(expectedText, text);
        Assert.Equal(expectedFailure, isFailure);
    }

    [Fact]
    public void OnNavigatedTo_RereadsThePage()
    {
        // Arriving on the page runs the same refresh as the Refresh button. Its observable effect
        // without touching disk: the remembered credentials choice is mirrored into the checkbox.
        BackupRestoreViewModel vm = new(new AcceptingDialogService(), TestBackupPage.Options())
        {
            CredentialsPreference = true,
        };
        Assert.False(vm.IncludeCredentials);

        vm.OnNavigatedTo();

        Assert.True(vm.IncludeCredentials);
    }

    /// <summary>Accepts every confirmation and picks nothing.</summary>
    private sealed class AcceptingDialogService : IDialogService
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
                                            string cancelLabel = "Cancel") =>
            Task.FromResult<bool?>(true);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(true);
    }
}
