using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using System.Reflection;
using Avalonia.Headless;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.AgentForge.Sdk.Internal;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Companion to
/// <see cref="TransactionalReloadTests"/>: that file pinned the H-1 "do
/// not corrupt the workspace on a malformed reload" contract; this one
/// pins three further race surfaces:
///
/// <list type="number">
///   <item>
///     <b>H-1 recovery</b> — a malformed reload that bails out must not
///     leave the system unable to reload again.  After the bail, a
///     subsequent valid file change must swap the SDK clients normally.
///   </item>
///   <item>
///     <b>Reload concurrency</b> — multiple
///     <see cref="MainWindowViewModel.LoadAllWorkspacesAsync"/> calls in
///     rapid succession must converge to a single final state without
///     deadlock.  ⚠ <b>Not <c>_reloadPending</c></b>, which this item used to
///     credit: that guards <c>ReloadCoreAsync</c>, a method this test never
///     calls.  <c>LoadAllWorkspacesAsync</c> serialises overlapping calls
///     itself — see its remarks for why it serialises rather than coalescing.
///   </item>
///   <item>
///     <b>H-2 persistent tool VMs</b> — the long-running tool VMs
///     (Backup, Profiles, About-Code, About-Desktop) must keep the same
///     instance across a reload, since they own state (in-flight
///     backups, file pickers, version probes) that must not be torn
///     down by an unrelated workspace swap.
///   </item>
/// </list>
///
/// All three are race-class regressions that single-purpose unit tests
/// could not catch — only the headless harness lets us drive
/// <see cref="MainWindowViewModel"/> end-to-end with a real Avalonia
/// dispatcher and a sandboxed file system.
/// <para>
/// ⚠⚠ <b>Every test here was inert until 2026-08-18</b> — <c>return Session.Dispatch(async
/// …)</c> yields <c>Task&lt;Task&gt;</c>, whose inner task MSTest never awaited, so no
/// assertion could fail. See <see cref="TransactionalReloadTests"/> for the full write-up.
/// Now converted and canaried.
/// </para>
/// <para>
/// ⚠ <b>Two then failed for real, and they are two <i>different</i> pre-existing defects:</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>Malformed-reload recovery</b> — there was no bail to recover from, because
///     <c>ConfigFileLoader.LoadAsync</c> swallowed the parse failure into an empty document.
///     ✅ <b>Fixed</b> via <c>SettingsDocument.LoadFailure</c>; see
///     <see cref="TransactionalReloadTests"/>. This test is live again.
///   </item>
///   <item>
///     <b>Use-after-dispose under concurrent reload</b> — one reload reached
///     <c>ClaudeCodeSdk?.Dispose()</c> while another was inside
///     <c>BuildNavigationTreeAsync</c>, so <c>PermissionsAccessor.GetDefaultModeAt</c> threw
///     <c>ObjectDisposedException</c>. Reachable in the app because
///     <c>OpenProjectAsync</c> sets <c>IsLoading</c> but never checks it, and awaits a folder
///     dialog first — so a file-watcher reload can begin while that dialog is open.
///     ✅ <b>Fixed</b> by serialising overlapping calls inside
///     <see cref="MainWindowViewModel.LoadAllWorkspacesAsync"/> rather than in its three
///     callers.
///   </item>
/// </list>
/// <para>
/// <b>Both fixes were canaried.</b> Bypassing the serialisation returns
/// <c>ObjectDisposedException</c> to two tests here; disabling the parse-failure bail turns
/// three red across this class and <see cref="TransactionalReloadTests"/>. Nothing in this
/// class is <c>[Ignore]</c>d any more.
/// </para>
/// </summary>
[TestClass]
public sealed class ReloadHardeningTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_h3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        string ccDir = Path.Combine(_sandbox, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(Path.Combine(ccDir, "settings.json"), "{}");

        string dtDir = Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!;
        Directory.CreateDirectory(dtDir);
        File.WriteAllText(PlatformPaths.DesktopConfigPath, "{}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
            }
        }
        catch
        {
            /* best effort */
        }
    }

    private string CcSettingsPath => Path.Combine(_sandbox, ".claude", "settings.json");

    private static MainWindowViewModel BuildViewModel()
    {
        return new MainWindowViewModel(new SchemaRegistry(new HttpClient()), new NullDialogService());
    }

    // ── H-1 recovery: malformed reload must not break subsequent reloads ──

    [TestMethod]
    public async Task LoadAllWorkspacesAsync_AfterMalformedBail_RecoversOnNextValidReload()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Initial valid load.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            AgentConfigClientCore? initialCc = vm.ClaudeCodeSdk;
            Assert.IsNotNull(initialCc);

            // Step 1: write malformed JSON, reload bails (H-1 contract).
            await File.WriteAllTextAsync(CcSettingsPath, """{"model": invalid""");
            await vm.LoadAllWorkspacesAsync();
            Assert.AreSame(initialCc, vm.ClaudeCodeSdk,
                "Precondition: malformed reload must preserve the existing SDK reference (H-1).");
            Assert.IsNotNull(vm.StatusMessage);

            // Step 2: external editor finishes the truncate-then-rewrite —
            // the file is now valid again.  A subsequent reload must
            // succeed, swapping the SDK reference.  Without recovery,
            // some piece of internal state (e.g. _reloadPending or a
            // disposed-flag) might prevent the second attempt.
            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"sonnet"}""");
            await vm.LoadAllWorkspacesAsync();

            Assert.IsNotNull(vm.ClaudeCodeSdk);
            Assert.AreNotSame(initialCc, vm.ClaudeCodeSdk,
                "Recovery contract: after the malformed bail, the next valid reload MUST swap the SDK reference.");
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    // ── Reload concurrency: rapid back-to-back reloads must converge ──

    [TestMethod]
    public async Task LoadAllWorkspacesAsync_ConcurrentCalls_ConvergeWithoutDeadlock()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Trigger several reloads in rapid succession. LoadAllWorkspacesAsync chains
            // overlapping calls, so each of t1..t3 performs a FULL load in call order rather
            // than being coalesced away — deliberately, because OpenProjectAsync mutates
            // ProjectRoot before calling and a joined load would never open the new project.
            // Before that serialisation existed, one call's ClaudeCodeSdk.Dispose() landed
            // while another was inside BuildNavigationTreeAsync and this threw
            // ObjectDisposedException.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();

            // Mutate the file each time so a successful reload would
            // produce a new SDK reference.  If the guard deadlocks or
            // drops a call, one of the awaits below would hang or
            // throw; the test runner enforces a hard timeout via the
            // dispatcher.
            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"a"}""");
            Task t1 = vm.LoadAllWorkspacesAsync();
            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"b"}""");
            Task t2 = vm.LoadAllWorkspacesAsync();
            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"c"}""");
            Task t3 = vm.LoadAllWorkspacesAsync();

            await Task.WhenAll(t1, t2, t3);

            // After all settle, the SDK client must reflect the LAST
            // valid file content.  We can't assert "model='c'" because
            // the SDK doesn't expose model directly via the test seam,
            // but we CAN assert the SDK is non-null and the workspace
            // root contains the latest write.
            Assert.IsNotNull(vm.ClaudeCodeSdk);
            IReadOnlyList<DirtyDocumentSnapshot> doc = vm.ClaudeCodeSdk!.SnapshotDirtyDocuments();
            // SnapshotDirtyDocuments is empty on a freshly-loaded
            // workspace (no in-memory edits).  The relevant assertion
            // is structural: the post-reload state is consistent and
            // queryable.  The lack of deadlock is itself the assertion.
            Assert.IsNotNull(doc);
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    // ── H-2 persistent tool VMs ──────────────────────────────────────────

    [TestMethod]
    public async Task PersistentToolVms_BackupVm_SurvivesReload_SameInstance()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            BackupRestoreViewModel? firstBackup = vm.GetBackupVmForTesting();
            Assert.IsNotNull(firstBackup,
                "Precondition: BackupRestoreViewModel must be constructed during nav-tree build.");

            // Trigger a reload that would, pre-H-2, dispose-and-recreate
            // every tool VM in the nav tree.  The H-2 fix caches Backup
            // / Profiles / About in MWVM fields and reuses them.
            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"sonnet"}""");
            await vm.LoadAllWorkspacesAsync();

            BackupRestoreViewModel? secondBackup = vm.GetBackupVmForTesting();
            Assert.AreSame(firstBackup, secondBackup,
                "H-2 contract: BackupRestoreViewModel reference MUST survive workspace reload.  " +
                "A fresh instance would lose any in-flight backup CTS, file watchers, and the " +
                "user's pre-reload Backup-tab state.");
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task PersistentToolVms_ProfilesVm_SurvivesReload_SameInstance()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            ProfilesViewModel? firstProfiles = vm.GetProfilesVmForTesting();
            Assert.IsNotNull(firstProfiles);

            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"sonnet"}""");
            await vm.LoadAllWorkspacesAsync();

            Assert.AreSame(firstProfiles, vm.GetProfilesVmForTesting(),
                "H-2 contract: ProfilesViewModel reference MUST survive workspace reload.");
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task PersistentToolVms_AboutVms_SurviveReload_SameInstance()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // About VMs (Code + Desktop) own version probes and a
            // PathWasAddedOrAlreadyPresent flag that drives the
            // ShowClaudeCodePathWarning banner.  Pre-H-2, a reload
            // would lose the "user clicked Add to PATH" state
            // mid-session.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            AboutEditorViewModel? firstAboutCode = vm.GetAboutCodeVmForTesting();
            AboutEditorViewModel? firstAboutDesktop = vm.GetAboutDesktopVmForTesting();
            Assert.IsNotNull(firstAboutCode);
            Assert.IsNotNull(firstAboutDesktop);

            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"sonnet"}""");
            await vm.LoadAllWorkspacesAsync();

            Assert.AreSame(firstAboutCode, vm.GetAboutCodeVmForTesting(),
                "H-2 contract: Claude-Code AboutEditorViewModel reference MUST survive reload.");
            Assert.AreSame(firstAboutDesktop, vm.GetAboutDesktopVmForTesting(),
                "H-2 contract: Claude-Desktop AboutEditorViewModel reference MUST survive reload.");
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task PersistentToolVms_EssentialsVm_SurvivesReload_SameInstance()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Essentials VM owns the synthetic-search amber
            // callout state (one-time, dismissed on first edit) plus any
            // mid-keystroke int / string list value the user is editing.
            // A reload that ditches the VM would reset the callout and lose
            // the in-flight value mid-typing.  Pin the H-2 contract.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            EssentialsViewModel? firstEssentials = vm.GetEssentialsVmForTesting();
            Assert.IsNotNull(firstEssentials,
                "Precondition: EssentialsViewModel must be constructed during nav-tree build.");

            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"sonnet"}""");
            await vm.LoadAllWorkspacesAsync();

            Assert.AreSame(firstEssentials, vm.GetEssentialsVmForTesting(),
                "H-2 contract: EssentialsViewModel reference MUST survive workspace reload.");
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task PersistentToolVms_StaySameAcrossThreeReloads()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Symmetric guard — verify the persistence holds across
            // multiple reload cycles, not just one.  Catches a class
            // of bug where the cache is populated on first build and
            // discarded on second, which a single-reload test would miss.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            BackupRestoreViewModel? initial = vm.GetBackupVmForTesting();
            Assert.IsNotNull(initial);

            for (int i = 0; i < 3; i++)
            {
                await File.WriteAllTextAsync(CcSettingsPath, $$"""{"model":"v{{i}}"}""");
                await vm.LoadAllWorkspacesAsync();
                Assert.AreSame(initial, vm.GetBackupVmForTesting(),
                    $"BackupVm reference must persist across reload #{i + 1}.");
            }
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    // ── Test doubles ────────────────────────────────────────────────────

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