using Bennewitz.Ninja.AgentForge.Core.Platform;
using System.Diagnostics;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Guards F3 — <i>Share config succeeds silently</i> — at both halves of the fix: the service now
/// reports what it did, and the view-model turns that into a sentence on the centre status pill.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Before this, the defect was invisible by construction.</b> <c>ShareTextAsync</c> returned
/// a bare <see cref="Task"/>, so "the browser opened", "the clipboard was written" and "no branch
/// matched and nothing happened at all" were the same observation — to the caller, to the user,
/// and to a test. Windows text-sharing was the third of those in every shipped build. The
/// assertions below are on the <b>returned outcome</b> for exactly that reason: a test that
/// asserted only "did not throw" is what let it survive.
/// </para>
/// <para>
/// ⚠ <b>No test here runs <c>clip.exe</c> or <c>pbcopy</c>.</b> Those branches write the machine's
/// real clipboard, and a suite that clobbers the developer's clipboard would be its own defect.
/// The clipboard outcome is covered at the mapping layer instead, where it is the part F3 is
/// about.
/// </para>
/// <para>
/// ⓘ <b>The Linux <c>mailto:</c> branch is not covered at the service level</b>, because it is
/// unreachable on any other host and a test that quietly asserts nothing on Windows would read as
/// coverage without being any. <see cref="ShareOutcome.OpenedMailClient"/> is covered at the
/// mapping layer, which is host-independent.
/// </para>
/// </remarks>
public sealed class ShareOutcomeTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // DefaultShareService — the outcome has to be measured, never assumed
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A launcher that reports success without starting anything.</summary>
    private static Process? Launched(ProcessStartInfo _) => new();

    /// <summary>A launcher that reports the process did not start, as <c>Process.Start</c> does.</summary>
    private static Process? NotLaunched(ProcessStartInfo _) => null;

    [Fact]
    public async Task ShareText_WithNothingToShare_IsUnavailableRatherThanSuccess()
    {
        DefaultShareService svc = new(NotLaunched);

        ShareOutcome outcome = await svc.ShareTextAsync("title", string.Empty, uri: null, CancellationToken.None);

        MessageAssert.Equal(ShareOutcome.Unavailable, outcome,
            "An empty payload reaches no platform branch. Reporting anything else would be the " +
            "original defect restated: claiming an action that did not happen.");
    }

    [Fact]
    public async Task ShareText_WithUri_WhenLaunchSucceeds_IsOpenedInBrowser()
    {
        DefaultShareService svc = new(Launched);

        ShareOutcome outcome = await svc.ShareTextAsync("title", "body", "https://example.invalid/", CancellationToken.None);

        MessageAssert.Equal(ShareOutcome.OpenedInBrowser, outcome,
            "Every platform hands a caller-supplied URI to the default browser.");
    }

    [Fact]
    public async Task ShareText_WithUri_WhenLaunchFails_IsFailed()
    {
        DefaultShareService svc = new(NotLaunched);

        ShareOutcome outcome = await svc.ShareTextAsync("title", "body", "https://example.invalid/", CancellationToken.None);

        MessageAssert.Equal(ShareOutcome.Failed, outcome,
            "TryStart returned false, so Failed must be what reaches the caller. ⛔ This is the " +
            "assertion the whole change exists for: TryStart used to return void and swallow " +
            "the failure, which is why a broken share looked identical to a working one.");
    }

    [Fact]
    public async Task ShareFile_WhenPathIsNotOnDisk_IsUnavailableNotFailed()
    {
        DefaultShareService svc = new(Launched);
        string missing = Path.Combine(Path.GetTempPath(), $"share-outcome-{Guid.NewGuid():N}.txt");

        ShareOutcome outcome = await svc.ShareFileAsync("title", missing, CancellationToken.None);

        MessageAssert.Equal(ShareOutcome.Unavailable, outcome,
            "Nothing was attempted, so this is not a failure. The two stay distinct because the " +
            "status pill keeps a failure on screen until dismissed and clears a non-failure.");
    }

    [Fact]
    public async Task ShareFile_WhenLaunchFails_IsFailed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"share-outcome-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "payload");
        try
        {
            DefaultShareService svc = new(NotLaunched);

            ShareOutcome outcome = await svc.ShareFileAsync("title", path, CancellationToken.None);

            // Premise first: the file really is on disk, so this exercises the launch arm and
            // not the missing-path arm above, which returns Unavailable for a different reason.
            Assert.True(File.Exists(path), "Setup failed: the file under test must exist.");
            MessageAssert.Equal(ShareOutcome.Failed, outcome,
                "An existing file whose file manager would not start is a failure, not an absence.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ShareFile_WhenLaunchSucceeds_IsRevealedInFileManager()
    {
        string path = Path.Combine(Path.GetTempPath(), $"share-outcome-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "payload");
        try
        {
            DefaultShareService svc = new(Launched);

            ShareOutcome outcome = await svc.ShareFileAsync("title", path, CancellationToken.None);

            MessageAssert.Equal(ShareOutcome.RevealedInFileManager, outcome,
                "All three platforms reveal the file rather than opening a share sheet, and the " +
                "outcome must say so rather than claiming a share that is not available anywhere.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <remarks>
    /// ⓘ Read through <see cref="Enum.GetName{TEnum}(TEnum)"/> rather than as
    /// <c>(int)ShareOutcome.Failed == 0</c>, which MSTEST0032 rejects as a constant the compiler
    /// can fold — correctly, and the pinned decision is a real one either way.
    /// </remarks>
    [Fact]
    public void TheDefaultOutcome_IsFailed_SoAnUnsetValueIsLoud()
    {
        string? zeroMember = Enum.GetName(default(ShareOutcome));

        MessageAssert.Equal(nameof(ShareOutcome.Failed), zeroMember,
            "⚠ Deliberate. A fake or a half-written implementation returning default(ShareOutcome) " +
            "must land on a pill that sticks, not on a success that clears itself after six " +
            "seconds. Renumbering the enum puts the honest direction on the quiet side.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EffectiveSettingsViewModel — one sentence per outcome, and no two alike
    // ─────────────────────────────────────────────────────────────────────────

    private static AgentConfigClientCore MakeClient()
    {
        SettingsWorkspace ws = new([], ClaudeMergePolicy.Instance);
        return ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, ConfigScope.User, schemaRegistry: new SchemaRegistry());
    }

    private sealed class OutcomeShareService(ShareOutcome outcome) : IShareService
    {
        public ValueTask<ShareOutcome> ShareTextAsync(string title, string text, string? uri, CancellationToken cancellationToken)
            => ValueTask.FromResult(outcome);

        public ValueTask<ShareOutcome> ShareFileAsync(string title, string filePath, CancellationToken cancellationToken)
            => ValueTask.FromResult(outcome);
    }

    private static async Task<(string Text, bool IsFailure)> ShareAndCapture(ShareOutcome outcome)
    {
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: new OutcomeShareService(outcome));
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        MessageAssert.NotNull(captured,
            $"Sharing with outcome {outcome} emitted nothing at all — which is F3 itself: the " +
            "command completing without acknowledging anything in either direction.");
        return captured.Value;
    }

    [Theory]
    [InlineData(ShareOutcome.CopiedToClipboard, false)]
    [InlineData(ShareOutcome.OpenedInBrowser, false)]
    [InlineData(ShareOutcome.OpenedMailClient, false)]
    [InlineData(ShareOutcome.RevealedInFileManager, false)]
    [InlineData(ShareOutcome.Unavailable, false)]
    [InlineData(ShareOutcome.Failed, true)]
    public async Task EveryOutcome_EmitsANonEmptySentence_WithTheRightSeverity(
        ShareOutcome outcome, bool expectedIsFailure)
    {
        (string text, bool isFailure) = await ShareAndCapture(outcome);

        Assert.False(string.IsNullOrWhiteSpace(text),
            $"Outcome {outcome} must carry a sentence; an empty pill says as little as no pill.");
        MessageAssert.Equal(expectedIsFailure, isFailure,
            $"Outcome {outcome} must be reported at the right severity. ⚠ Only Failed sticks " +
            "until dismissed; Unavailable is not a failure, because nothing was attempted.");
    }

    [Fact]
    public async Task NoTwoOutcomes_ShareASentence()
    {
        // ⭐ This is the assertion the per-outcome rows cannot make. Each of those compares one
        // outcome against the resx key the code itself names, so a copy-paste that wired two
        // outcomes to the SAME key passes every one of them. A user would then be told the
        // configuration went to the clipboard when it went to their mail client.
        Dictionary<string, ShareOutcome> seen = new(StringComparer.Ordinal);

        // ⚠ Cancelled is excluded by NAME: it emits no sentence, so it has none to collide.
        //    ACancelledShare_EmitsNoStatusAtAll asserts that positively.
        foreach (ShareOutcome outcome in Enum.GetValues<ShareOutcome>().Where(o => o != ShareOutcome.Cancelled))
        {
            (string text, _) = await ShareAndCapture(outcome);

            Assert.False(seen.TryGetValue(text, out ShareOutcome earlier),
                $"{outcome} and {earlier} both report \"{text}\". Every outcome exists because it " +
                "is a different thing to tell the user; two sharing a sentence collapses that.");
            seen[text] = outcome;
        }

        MessageAssert.Equal(Enum.GetValues<ShareOutcome>().Length - 1, seen.Count,
            "Every declared outcome except Cancelled must have been reached and reported.");
    }

    [Fact]
    public async Task ACancelledShare_EmitsNoStatusAtAll()
    {
        // ⚠ The one outcome that must emit NOTHING — the reverse of F3, deliberately. F3 was a
        // share that did nothing yet completed silently; a cancel is the user's own act.
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: new OutcomeShareService(ShareOutcome.Cancelled));
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        MessageAssert.Null(captured, "A cancelled share must not raise a status pill.");
    }

    [Fact]
    public async Task CancellingTheCommand_WhileTheShareIsRunning_EmitsNoStatus()
    {
        // The realistic path: the service is still working when the user cancels, so the cancel
        // arrives as an OperationCanceledException on the command's own token, not as a returned
        // outcome. It must end the same way — silently — not in the catch-all failure branch.
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: new BlockingShareService());
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        Task running = vm.ShareConfigCommand.ExecuteAsync(null);
        vm.ShareConfigCommand.Cancel();
        await running;

        MessageAssert.Null(captured, "Cancelling the command must not be reported as a failure.");
    }

    /// <summary>Waits on the caller's token and nothing else, so the only way out is a cancel.</summary>
    private sealed class BlockingShareService : IShareService
    {
        public async ValueTask<ShareOutcome> ShareTextAsync(string title, string text, string? uri, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return ShareOutcome.Failed;
        }

        public ValueTask<ShareOutcome> ShareFileAsync(string title, string filePath, CancellationToken cancellationToken)
            => throw new NotSupportedException("Effective settings shares text, not a file.");
    }

    [Fact]
    public async Task WhenTheServiceThrows_TheFailureReachesTheUser()
    {
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: new ThrowingShareService());
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        MessageAssert.NotNull(captured,
            "⛔ The catch used to log and return, under a comment about not surfacing a dialog. " +
            "It is the pill that was missing, not the dialog that was wanted.");
        Assert.True(captured.Value.IsFailure,
            "A throw is a failure, and a failure pill sticks until the user dismisses it.");
        MessageAssert.Equal(Strings.StatusShareConfigFailed, captured.Value.Text,
            "The failure sentence comes from resx like every other user-visible string.");
    }

    [Fact]
    public async Task WithNoShareServiceWired_TheUserIsToldSoRatherThanNothing()
    {
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: null);
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        MessageAssert.NotNull(captured,
            "The button is clickable with no service wired, so pressing it must say something. " +
            "Returning quietly is the defect, not the guard against it.");
        MessageAssert.Equal(Strings.StatusShareConfigUnavailable, captured.Value.Text,
            "Nothing was attempted, so this is the unavailable sentence.");
        Assert.False(captured.Value.IsFailure,
            "Absence is not failure: this pill auto-clears rather than waiting to be dismissed.");
    }

    private sealed class ThrowingShareService : IShareService
    {
        public ValueTask<ShareOutcome> ShareTextAsync(string title, string text, string? uri, CancellationToken cancellationToken)
            => throw new IOException("fake share failure");

        public ValueTask<ShareOutcome> ShareFileAsync(string title, string filePath, CancellationToken cancellationToken)
            => throw new IOException("fake share failure");
    }
}
