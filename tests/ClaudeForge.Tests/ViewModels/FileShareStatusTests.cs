using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Status;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

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
[TestClass]
public sealed class FileShareStatusTests
{
    private const string Revealed = "revealed-sentence";
    private const string Unavailable = "unavailable-sentence";
    private const string Failed = "failed-sentence";

    private static (string Text, bool IsFailure) Describe(ShareOutcome outcome)
        => FileShareStatus.Describe(outcome, Revealed, Unavailable, Failed);

    // ─────────────────────────────────────────────────────────────────────────
    // The mapper
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void RevealedInFileManager_IsTheSuccessAndDoesNotStick()
    {
        (string text, bool isFailure) = Describe(ShareOutcome.RevealedInFileManager);

        Assert.AreEqual(Revealed, text, "The revealed sentence is the one success a file share has.");
        Assert.IsFalse(isFailure, "A success pill auto-clears; it must not wait to be dismissed.");
    }

    [TestMethod]
    public void Unavailable_IsNotAFailure()
    {
        (string text, bool isFailure) = Describe(ShareOutcome.Unavailable);

        Assert.AreEqual(Unavailable, text);
        Assert.IsFalse(isFailure,
            "⚠ Nothing was attempted, so nothing went wrong. Reporting absence as failure would " +
            "leave a pill on screen demanding a dismiss for a non-event.");
    }

    [TestMethod]
    public void Failed_Sticks()
    {
        (string text, bool isFailure) = Describe(ShareOutcome.Failed);

        Assert.AreEqual(Failed, text);
        Assert.IsTrue(isFailure, "A failure pill stays until the user dismisses it.");
    }

    [TestMethod]
    [DataRow(ShareOutcome.CopiedToClipboard)]
    [DataRow(ShareOutcome.OpenedInBrowser)]
    [DataRow(ShareOutcome.OpenedMailClient)]
    public void AnOutcomeAFileShareCannotProduce_IsReportedAsFailure(ShareOutcome impossible)
    {
        (string text, bool isFailure) = Describe(impossible);

        // ⛔ Deliberate, and the opposite of what "it's a success member" would suggest.
        // ShareFileAsync reveals a file on all three platforms; it cannot write a clipboard, open
        // a browser or reach a mail client. An implementation returning one is not honouring its
        // contract, and telling the user "revealed in your file manager" when it was not is the
        // exact class of lie F3 removed.
        Assert.AreEqual(Failed, text,
            $"{impossible} cannot arise from ShareFileAsync, so it must not borrow the success " +
            "sentence — that would assert something the service did not do.");
        Assert.IsTrue(isFailure, "A contract violation must not clear itself quietly.");
    }

    [TestMethod]
    public void EveryOutcome_IsAnswered()
    {
        // Premise before claim: if ShareOutcome ever gains a member, this loop reaches it and the
        // mapper still has to return something non-empty for it.
        foreach (ShareOutcome outcome in Enum.GetValues<ShareOutcome>())
        {
            (string text, _) = Describe(outcome);

            Assert.IsFalse(string.IsNullOrWhiteSpace(text),
                $"{outcome} produced no sentence; an empty pill says as little as no pill.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Share log — About
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class OutcomeShareService(ShareOutcome outcome) : IShareService
    {
        public int FileCalls { get; private set; }

        public Task<ShareOutcome> ShareTextAsync(string title, string text, string? uri = null)
            => Task.FromResult(outcome);

        public Task<ShareOutcome> ShareFileAsync(string title, string filePath)
        {
            FileCalls++;
            return Task.FromResult(outcome);
        }
    }

    [TestMethod]
    public async Task ShareLog_ReportsTheOutcome_RatherThanCompletingSilently()
    {
        OutcomeShareService svc = new(ShareOutcome.RevealedInFileManager);
        AboutEditorViewModel vm = new(
            AboutProduct.ClaudeCode,
            shareService: svc,
            logPathProvider: static () => @"C:/logs/claudeforge.log");
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareLogCommand.ExecuteAsync(null);

        Assert.AreEqual(1, svc.FileCalls, "Setup check: the command must have reached the service.");
        Assert.IsNotNull(captured,
            "⛔ This is the defect: Share log awaited the service and returned, telling the user " +
            "nothing in either direction.");
        Assert.AreEqual(Strings.StatusShareLogRevealed, captured.Value.Text);
        Assert.IsFalse(captured.Value.IsFailure);
    }

    [TestMethod]
    public async Task ShareLog_WhenTheServiceCannotAct_SaysSo()
    {
        OutcomeShareService svc = new(ShareOutcome.Unavailable);
        AboutEditorViewModel vm = new(
            AboutProduct.ClaudeCode,
            shareService: svc,
            logPathProvider: static () => @"C:/logs/claudeforge.log");
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareLogCommand.ExecuteAsync(null);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Strings.StatusShareLogUnavailable, captured.Value.Text);
        Assert.IsFalse(captured.Value.IsFailure, "Absence is not failure.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Share archive — backup row
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ShareBackup_ReportsTheOutcome_InTheHostsWords()
    {
        OutcomeShareService svc = new(ShareOutcome.RevealedInFileManager);
        BackupRestoreViewModel vm = new(
            new StubDialogServiceForShare(), BackupPageTestOptions.Create(), svc);
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareBackupCommand.ExecuteAsync(MakeRow());

        Assert.IsNotNull(captured,
            "Share on a backup row reported nothing before F3's sibling pass, even though " +
            "OnTerminalStatus was already wired for backup and restore outcomes.");
        Assert.AreEqual(Strings.StatusShareArchiveRevealed, captured.Value.Text,
            "⚠ Read off ClaudeForge's own resx through BackupPageText — the shell has no strings " +
            "of its own, and a hardcoded English literal here is what that seam exists to stop.");
        Assert.IsFalse(captured.Value.IsFailure);
    }

    [TestMethod]
    public async Task ShareBackup_WithNoShareServiceWired_SaysUnavailable()
    {
        BackupRestoreViewModel vm = new(
            new StubDialogServiceForShare(), BackupPageTestOptions.Create());
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareBackupCommand.ExecuteAsync(MakeRow());

        Assert.IsNotNull(captured,
            "ⓘ This is OpenCodeForge's live path — it wires no share service, so the button did " +
            "nothing and said nothing. It now says why.");
        Assert.AreEqual(Strings.StatusShareArchiveUnavailable, captured.Value.Text);
        Assert.IsFalse(captured.Value.IsFailure);
    }

    [TestMethod]
    public async Task ShareBackup_WithNoRow_StaysSilent()
    {
        OutcomeShareService svc = new(ShareOutcome.RevealedInFileManager);
        BackupRestoreViewModel vm = new(
            new StubDialogServiceForShare(), BackupPageTestOptions.Create(), svc);
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareBackupCommand.ExecuteAsync(null);

        Assert.AreEqual(0, svc.FileCalls, "No row must forward nothing to the service.");
        Assert.IsNull(captured,
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
