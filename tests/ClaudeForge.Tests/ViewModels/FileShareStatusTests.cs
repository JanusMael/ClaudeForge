using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Status;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Guards F3's two sibling surfaces — *Share log* (About) and *Share* on a backup row — which had
/// the identical silent-success shape and now report through the centre pill.
/// </summary>
/// <remarks>
/// ⭐ <b>Both go through one mapper on purpose.</b> `FileShareStatus.Describe` is the only switch;
/// a second copy would be a second chance to answer the same outcome differently, and the two
/// surfaces are in different assemblies so nothing else would notice.
/// </remarks>
public sealed class FileShareStatusTests
{
    private const string Revealed = "revealed-sentence";
    private const string Unavailable = "unavailable-sentence";
    private const string Failed = "failed-sentence";

    private static (string? Text, bool IsFailure) Describe(ShareOutcome outcome)
        => FileShareStatus.Describe(outcome, Revealed, Unavailable, Failed);

    // ─────────────────────────────────────────────────────────────────────────
    // The mapper
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RevealedInFileManager_IsTheSuccessAndDoesNotStick()
    {
        (string? text, bool isFailure) = Describe(ShareOutcome.RevealedInFileManager);

        MessageAssert.Equal(Revealed, text, "The revealed sentence is the one success a file share has.");
        Assert.False(isFailure, "A success pill auto-clears; it must not wait to be dismissed.");
    }

    [Fact]
    public void Unavailable_IsNotAFailure()
    {
        (string? text, bool isFailure) = Describe(ShareOutcome.Unavailable);

        Assert.Equal(Unavailable, text);
        Assert.False(isFailure,
            "⚠ Nothing was attempted, so nothing went wrong. Reporting absence as failure would " +
            "leave a pill on screen demanding a dismiss for a non-event.");
    }

    [Fact]
    public void Failed_Sticks()
    {
        (string? text, bool isFailure) = Describe(ShareOutcome.Failed);

        Assert.Equal(Failed, text);
        Assert.True(isFailure, "A failure pill stays until the user dismisses it.");
    }

    [Theory]
    [InlineData(ShareOutcome.CopiedToClipboard)]
    [InlineData(ShareOutcome.OpenedInBrowser)]
    [InlineData(ShareOutcome.OpenedMailClient)]
    public void AnOutcomeAFileShareCannotProduce_IsReportedAsFailure(ShareOutcome impossible)
    {
        (string? text, bool isFailure) = Describe(impossible);

        // ⛔ Deliberate, and the opposite of what "it's a success member" would suggest.
        // ShareFileAsync reveals a file on all three platforms; it cannot write a clipboard, open
        // a browser or reach a mail client. An implementation returning one is not honouring its
        // contract, and telling the user "revealed in your file manager" when it was not is the
        // exact class of lie F3 removed.
        MessageAssert.Equal(Failed, text,
            $"{impossible} cannot arise from ShareFileAsync, so it must not borrow the success " +
            "sentence — that would assert something the service did not do.");
        Assert.True(isFailure, "A contract violation must not clear itself quietly.");
    }

    [Fact]
    public void EveryOutcome_IsAnswered()
    {
        // Premise before claim: if ShareOutcome ever gains a member, this loop reaches it and the
        // mapper still has to return something non-empty for it.
        //
        // ⚠ ONE named exemption, asserted rather than skipped: Cancelled deliberately has no
        // sentence (see Cancelled_SaysNothing_AndIsNotAFailure). Every other member — including
        // any added later — still has to produce one, so a new member that returns null fails here.
        foreach (ShareOutcome outcome in Enum.GetValues<ShareOutcome>())
        {
            (string? text, bool isFailure) = Describe(outcome);

            if (outcome == ShareOutcome.Cancelled)
            {
                MessageAssert.Null(text, "Cancelled must say nothing.");
                Assert.False(isFailure, "A cancel is not a failure.");
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(text),
                $"{outcome} produced no sentence; an empty pill says as little as no pill.");
        }
    }

    [Fact]
    public void Cancelled_SaysNothing_AndIsNotAFailure()
    {
        // ⛔ Not the catch-all. Cancelled arrived with the required CancellationToken; left to the
        // `_ =>` arm it would be reported as a sticky FAILURE the moment a user cancelled a share,
        // and it would compile cleanly doing so.
        (string? text, bool isFailure) = Describe(ShareOutcome.Cancelled);

        MessageAssert.Null(text, "The user cancelled and knows it; there is nothing to say.");
        Assert.False(isFailure, "A cancel is not a failure, so nothing should stick until dismissed.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Share log — About
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class OutcomeShareService(ShareOutcome outcome) : IShareService
    {
        public int FileCalls { get; private set; }

        public ValueTask<ShareOutcome> ShareTextAsync(string title, string text, string? uri, CancellationToken cancellationToken)
            => ValueTask.FromResult(outcome);

        public ValueTask<ShareOutcome> ShareFileAsync(string title, string filePath, CancellationToken cancellationToken)
        {
            FileCalls++;
            return ValueTask.FromResult(outcome);
        }
    }

    [Fact]
    public async Task ShareLog_ReportsTheOutcome_RatherThanCompletingSilently()
    {
        OutcomeShareService svc = new(ShareOutcome.RevealedInFileManager);
        AboutEditorViewModel vm = new(ClaudeEnvironment.Empty, 
            AboutProduct.ClaudeCode,
            shareService: svc,
            logPathProvider: static () => @"C:/logs/claudeforge.log");
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareLogCommand.ExecuteAsync(null);

        MessageAssert.Equal(1, svc.FileCalls, "Setup check: the command must have reached the service.");
        MessageAssert.NotNull(captured,
            "⛔ This is the defect: Share log awaited the service and returned, telling the user " +
            "nothing in either direction.");
        Assert.Equal(Strings.StatusShareLogRevealed, captured.Value.Text);
        Assert.False(captured.Value.IsFailure);
    }

    [Fact]
    public async Task ShareLog_WhenTheServiceCannotAct_SaysSo()
    {
        OutcomeShareService svc = new(ShareOutcome.Unavailable);
        AboutEditorViewModel vm = new(ClaudeEnvironment.Empty, 
            AboutProduct.ClaudeCode,
            shareService: svc,
            logPathProvider: static () => @"C:/logs/claudeforge.log");
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareLogCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.Equal(Strings.StatusShareLogUnavailable, captured.Value.Text);
        Assert.False(captured.Value.IsFailure, "Absence is not failure.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Share archive — backup row
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShareBackup_ReportsTheOutcome_InTheHostsWords()
    {
        OutcomeShareService svc = new(ShareOutcome.RevealedInFileManager);
        BackupRestoreViewModel vm = new(
            new StubDialogServiceForShare(), BackupPageTestOptions.Create(), svc);
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareBackupCommand.ExecuteAsync(MakeRow());

        MessageAssert.NotNull(captured,
            "Share on a backup row reported nothing before F3's sibling pass, even though " +
            "OnTerminalStatus was already wired for backup and restore outcomes.");
        MessageAssert.Equal(Strings.StatusShareArchiveRevealed, captured.Value.Text,
            "⚠ Read off ClaudeForge's own resx through BackupPageText — the shell has no strings " +
            "of its own, and a hardcoded English literal here is what that seam exists to stop.");
        Assert.False(captured.Value.IsFailure);
    }

    [Fact]
    public async Task ShareBackup_WithNoShareServiceWired_SaysUnavailable()
    {
        BackupRestoreViewModel vm = new(
            new StubDialogServiceForShare(), BackupPageTestOptions.Create());
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareBackupCommand.ExecuteAsync(MakeRow());

        MessageAssert.NotNull(captured,
            "ⓘ This is OpenCodeForge's live path — it wires no share service, so the button did " +
            "nothing and said nothing. It now says why.");
        Assert.Equal(Strings.StatusShareArchiveUnavailable, captured.Value.Text);
        Assert.False(captured.Value.IsFailure);
    }

    [Fact]
    public async Task ShareBackup_WithNoRow_StaysSilent()
    {
        OutcomeShareService svc = new(ShareOutcome.RevealedInFileManager);
        BackupRestoreViewModel vm = new(
            new StubDialogServiceForShare(), BackupPageTestOptions.Create(), svc);
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareBackupCommand.ExecuteAsync(null);

        MessageAssert.Equal(0, svc.FileCalls, "No row must forward nothing to the service.");
        MessageAssert.Null(captured,
            "⚠ Deliberately the one silent path. No row selected is not an outcome — there is no " +
            "archive the user asked about, so there is nothing to report. A missing SERVICE is " +
            "different, and that one does speak.");
    }

    private static BackupRowViewModel MakeRow()
    {
        return new BackupRowViewModel(new AgentForge.Core.Backup.BackupEntry
        {
            ArchivePath = "/fake/backup-2026.zip",
            FileName = "backup-2026.zip",
            SizeBytes = 1024,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = null,
        });
    }

    /// <summary>
    /// A dialog service that answers nothing, because sharing never opens a dialog.
    /// </summary>
    /// <remarks>
    /// ⓘ This is the twenty-first private copy of the same stub in this assembly — every test
    /// class that needs an <see cref="IDialogService"/> declares its own. Not consolidated here
    /// because that is a change to twenty other files and has nothing to do with sharing; noted
    /// so the next person sees a pattern rather than an oversight.
    /// </remarks>
    private sealed class StubDialogServiceForShare : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null,
                                           IReadOnlyList<FilePickerFilter>? filters = null)
            => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null)
            => Task.FromResult<string?>(null);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
            => Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message,
                                            string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel")
            => Task.FromResult<bool?>(false);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
            => Task.FromResult(false);
    }
}
