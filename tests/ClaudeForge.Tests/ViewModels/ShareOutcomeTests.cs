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
[TestClass]
public sealed class ShareOutcomeTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // DefaultShareService — the outcome has to be measured, never assumed
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A launcher that reports success without starting anything.</summary>
    private static Process? Launched(ProcessStartInfo _) => new();

    /// <summary>A launcher that reports the process did not start, as <c>Process.Start</c> does.</summary>
    private static Process? NotLaunched(ProcessStartInfo _) => null;

    [TestMethod]
    public async Task ShareText_WithNothingToShare_IsUnavailableRatherThanSuccess()
    {
        DefaultShareService svc = new(NotLaunched);

        ShareOutcome outcome = await svc.ShareTextAsync("title", string.Empty);

        Assert.AreEqual(ShareOutcome.Unavailable, outcome,
            "An empty payload reaches no platform branch. Reporting anything else would be the " +
            "original defect restated: claiming an action that did not happen.");
    }

    [TestMethod]
    public async Task ShareText_WithUri_WhenLaunchSucceeds_IsOpenedInBrowser()
    {
        DefaultShareService svc = new(Launched);

        ShareOutcome outcome = await svc.ShareTextAsync("title", "body", "https://example.invalid/");

        Assert.AreEqual(ShareOutcome.OpenedInBrowser, outcome,
            "Every platform hands a caller-supplied URI to the default browser.");
    }

    [TestMethod]
    public async Task ShareText_WithUri_WhenLaunchFails_IsFailed()
    {
        DefaultShareService svc = new(NotLaunched);

        ShareOutcome outcome = await svc.ShareTextAsync("title", "body", "https://example.invalid/");

        Assert.AreEqual(ShareOutcome.Failed, outcome,
            "TryStart returned false, so Failed must be what reaches the caller. ⛔ This is the " +
            "assertion the whole change exists for: TryStart used to return void and swallow " +
            "the failure, which is why a broken share looked identical to a working one.");
    }

    [TestMethod]
    public async Task ShareFile_WhenPathIsNotOnDisk_IsUnavailableNotFailed()
    {
        DefaultShareService svc = new(Launched);
        string missing = Path.Combine(Path.GetTempPath(), $"share-outcome-{Guid.NewGuid():N}.txt");

        ShareOutcome outcome = await svc.ShareFileAsync("title", missing);

        Assert.AreEqual(ShareOutcome.Unavailable, outcome,
            "Nothing was attempted, so this is not a failure. The two stay distinct because the " +
            "status pill keeps a failure on screen until dismissed and clears a non-failure.");
    }

    [TestMethod]
    public async Task ShareFile_WhenLaunchFails_IsFailed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"share-outcome-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "payload");
        try
        {
            DefaultShareService svc = new(NotLaunched);

            ShareOutcome outcome = await svc.ShareFileAsync("title", path);

            // Premise first: the file really is on disk, so this exercises the launch arm and
            // not the missing-path arm above, which returns Unavailable for a different reason.
            Assert.IsTrue(File.Exists(path), "Setup failed: the file under test must exist.");
            Assert.AreEqual(ShareOutcome.Failed, outcome,
                "An existing file whose file manager would not start is a failure, not an absence.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ShareFile_WhenLaunchSucceeds_IsRevealedInFileManager()
    {
        string path = Path.Combine(Path.GetTempPath(), $"share-outcome-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "payload");
        try
        {
            DefaultShareService svc = new(Launched);

            ShareOutcome outcome = await svc.ShareFileAsync("title", path);

            Assert.AreEqual(ShareOutcome.RevealedInFileManager, outcome,
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
    [TestMethod]
    public void TheDefaultOutcome_IsFailed_SoAnUnsetValueIsLoud()
    {
        string? zeroMember = Enum.GetName(default(ShareOutcome));

        Assert.AreEqual(nameof(ShareOutcome.Failed), zeroMember,
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
        public Task<ShareOutcome> ShareTextAsync(string title, string text, string? uri = null)
            => Task.FromResult(outcome);

        public Task<ShareOutcome> ShareFileAsync(string title, string filePath)
            => Task.FromResult(outcome);
    }

    private static async Task<(string Text, bool IsFailure)> ShareAndCapture(ShareOutcome outcome)
    {
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: new OutcomeShareService(outcome));
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        Assert.IsNotNull(captured,
            $"Sharing with outcome {outcome} emitted nothing at all — which is F3 itself: the " +
            "command completing without acknowledging anything in either direction.");
        return captured.Value;
    }

    [TestMethod]
    [DataRow(ShareOutcome.CopiedToClipboard, false)]
    [DataRow(ShareOutcome.OpenedInBrowser, false)]
    [DataRow(ShareOutcome.OpenedMailClient, false)]
    [DataRow(ShareOutcome.RevealedInFileManager, false)]
    [DataRow(ShareOutcome.Unavailable, false)]
    [DataRow(ShareOutcome.Failed, true)]
    public async Task EveryOutcome_EmitsANonEmptySentence_WithTheRightSeverity(
        ShareOutcome outcome, bool expectedIsFailure)
    {
        (string text, bool isFailure) = await ShareAndCapture(outcome);

        Assert.IsFalse(string.IsNullOrWhiteSpace(text),
            $"Outcome {outcome} must carry a sentence; an empty pill says as little as no pill.");
        Assert.AreEqual(expectedIsFailure, isFailure,
            $"Outcome {outcome} must be reported at the right severity. ⚠ Only Failed sticks " +
            "until dismissed; Unavailable is not a failure, because nothing was attempted.");
    }

    [TestMethod]
    public async Task NoTwoOutcomes_ShareASentence()
    {
        // ⭐ This is the assertion the per-outcome rows cannot make. Each of those compares one
        // outcome against the resx key the code itself names, so a copy-paste that wired two
        // outcomes to the SAME key passes every one of them. A user would then be told the
        // configuration went to the clipboard when it went to their mail client.
        Dictionary<string, ShareOutcome> seen = new(StringComparer.Ordinal);

        foreach (ShareOutcome outcome in Enum.GetValues<ShareOutcome>())
        {
            (string text, _) = await ShareAndCapture(outcome);

            Assert.IsFalse(seen.TryGetValue(text, out ShareOutcome earlier),
                $"{outcome} and {earlier} both report \"{text}\". Every outcome exists because it " +
                "is a different thing to tell the user; two sharing a sentence collapses that.");
            seen[text] = outcome;
        }

        Assert.AreEqual(Enum.GetValues<ShareOutcome>().Length, seen.Count,
            "Every declared outcome must have been reached and reported.");
    }

    [TestMethod]
    public async Task WhenTheServiceThrows_TheFailureReachesTheUser()
    {
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: new ThrowingShareService());
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        Assert.IsNotNull(captured,
            "⛔ The catch used to log and return, under a comment about not surfacing a dialog. " +
            "It is the pill that was missing, not the dialog that was wanted.");
        Assert.IsTrue(captured.Value.IsFailure,
            "A throw is a failure, and a failure pill sticks until the user dismisses it.");
        Assert.AreEqual(Strings.StatusShareConfigFailed, captured.Value.Text,
            "The failure sentence comes from resx like every other user-visible string.");
    }

    [TestMethod]
    public async Task WithNoShareServiceWired_TheUserIsToldSoRatherThanNothing()
    {
        EffectiveSettingsViewModel vm = new(MakeClient(), shareService: null);
        (string Text, bool IsFailure)? captured = null;
        vm.OnTerminalStatus = (text, isFailure) => captured = (text, isFailure);

        await vm.ShareConfigCommand.ExecuteAsync(null);

        Assert.IsNotNull(captured,
            "The button is clickable with no service wired, so pressing it must say something. " +
            "Returning quietly is the defect, not the guard against it.");
        Assert.AreEqual(Strings.StatusShareConfigUnavailable, captured.Value.Text,
            "Nothing was attempted, so this is the unavailable sentence.");
        Assert.IsFalse(captured.Value.IsFailure,
            "Absence is not failure: this pill auto-clears rather than waiting to be dismissed.");
    }

    private sealed class ThrowingShareService : IShareService
    {
        public Task<ShareOutcome> ShareTextAsync(string title, string text, string? uri = null)
            => throw new IOException("fake share failure");

        public Task<ShareOutcome> ShareFileAsync(string title, string filePath)
            => throw new IOException("fake share failure");
    }
}
