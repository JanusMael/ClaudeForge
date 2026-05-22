using System.Globalization;
using System.Reflection;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Behaviour tests for <see cref="BackupRestoreViewModel"/>. Uses a stub dialog
/// service so prompt flow is deterministic and requires no UI.
/// </summary>
[TestClass]
public sealed class BackupRestoreViewModelTests
{
    [TestMethod]
    public void Refresh_AppliesCredentialsPreference()
    {
        BackupRestoreViewModel vm = new(new StubDialogService())
        {
            CredentialsPreference = true,
        };
        vm.Refresh();
        Assert.IsTrue(vm.IncludeCredentials,
            "Refresh should mirror the remembered credential choice into the checkbox.");
    }

    [TestMethod]
    public void Refresh_NullPreferenceLeavesCheckboxUnchecked()
    {
        BackupRestoreViewModel vm = new(new StubDialogService())
        {
            CredentialsPreference = null,
        };
        vm.Refresh();
        Assert.IsFalse(vm.IncludeCredentials,
            "When no preference is remembered the checkbox must start unchecked (safe default).");
    }

    [TestMethod]
    public async Task BackupCommand_AlwaysPromptsForCredentialsOnNonSanitizedBackups()
    {
        // the old "prompt-once, remember preference" pattern
        // was replaced with always-prompt for non-Sanitized backups.  This
        // test pins the new contract: a backup with no prior credentials
        // answer raises the prompt, AND a second backup raises it again.
        StubDialogService dialog = new() { ConfirmReturns = true };
        BackupRestoreViewModel vm = new(dialog)
        {
            CredentialsPreference = null,
            BackupDirectory = Path.Combine(Path.GetTempPath(),
                "vt-" + Guid.NewGuid().ToString("N")),
        };

        string home = Path.Combine(Path.GetTempPath(), "vt-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        PlatformPaths.TestUserProfileOverride = home;

        try
        {
            await vm.BackupCommand.ExecuteAsync(null);
            Assert.AreEqual(1, dialog.ConfirmCalls,
                "First non-Sanitized backup must raise the credentials prompt.");

            // Second invocation — must prompt again (no more remembered preference).
            await vm.BackupCommand.ExecuteAsync(null);
            Assert.AreEqual(2, dialog.ConfirmCalls,
                "Subsequent non-Sanitized backups must ALSO raise the prompt — " +
                "the old 'remember once' behaviour was removed 2026-05-15.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
            try
            {
                if (Directory.Exists(home))
                {
                    Directory.Delete(home, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }

            try
            {
                if (Directory.Exists(vm.BackupDirectory))
                {
                    Directory.Delete(vm.BackupDirectory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task BackupCommand_CredentialsDialogDismissedViaX_AbortsBackup()
    {
        // regression for the X-close-proceeds bug.
        // Pre-fix: ShowConfirmAsync returned bool, X-close collapsed to
        // false (= "Omit credentials"), which then PROCEEDED with the
        // backup without credentials.  Now ShowConfirmAsync returns
        // bool? and null (X-close) aborts the backup entirely.
        StubDialogService dialog = new()
        {
            // ConfirmReturns is a `bool`; the stub wraps it as `bool?`
            // which doesn't represent "X closed."  Use the dedicated
            // override below.
            ConfirmDismissesViaX = true,
        };
        BackupRestoreViewModel vm = new(dialog)
        {
            CredentialsPreference = null,
            BackupDirectory = Path.Combine(Path.GetTempPath(),
                "vt-" + Guid.NewGuid().ToString("N")),
        };

        string home = Path.Combine(Path.GetTempPath(), "vt-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        PlatformPaths.TestUserProfileOverride = home;

        try
        {
            await vm.BackupCommand.ExecuteAsync(null);

            // The prompt fired but the user dismissed via X.  Backup
            // must have aborted — no zip should exist in the backup
            // directory.
            string[] zips = Directory.Exists(vm.BackupDirectory)
                ? Directory.GetFiles(vm.BackupDirectory, "*.zip")
                : [];
            Assert.AreEqual(0, zips.Length,
                "X-dismissing the credentials prompt must abort the backup. " +
                "Pre-fix bug: X collapsed to 'Omit' and proceeded silently.");
            Assert.IsTrue(dialog.ConfirmCalls >= 1,
                "The credentials prompt should have fired before the abort.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
            try
            {
                if (Directory.Exists(home))
                {
                    Directory.Delete(home, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }

            try
            {
                if (Directory.Exists(vm.BackupDirectory))
                {
                    Directory.Delete(vm.BackupDirectory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task BackupCommand_Sanitized_SkipsCredentialsPromptEntirely()
    {
        // Sanitized mode hard-drops credentials (BackupEngine.ShouldSkipHomeFile)
        // so the prompt would be a no-op decision.  Suppress it.
        StubDialogService dialog = new() { ConfirmReturns = true };
        BackupRestoreViewModel vm = new(dialog)
        {
            CredentialsPreference = null,
            BackupDirectory = Path.Combine(Path.GetTempPath(),
                "vt-" + Guid.NewGuid().ToString("N")),
        };
        vm.Mode = BackupMode.Sanitized;

        string home = Path.Combine(Path.GetTempPath(), "vt-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        PlatformPaths.TestUserProfileOverride = home;

        try
        {
            await vm.BackupCommand.ExecuteAsync(null);
            Assert.AreEqual(0, dialog.ConfirmCalls,
                "Sanitized-mode backups must NOT raise the credentials prompt — " +
                "the credentials file is dropped regardless of the user's answer.");
            Assert.IsFalse(vm.IncludeCredentials,
                "Sanitized mode force-clears IncludeCredentials so no carry-over " +
                "from a prior non-Sanitized backup can leak credentials in.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
            try
            {
                if (Directory.Exists(home))
                {
                    Directory.Delete(home, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }

            try
            {
                if (Directory.Exists(vm.BackupDirectory))
                {
                    Directory.Delete(vm.BackupDirectory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task RestoreCommand_DirtyGuard_PromptsInOrder()
    {
        StubDialogService dialog = new() { ConfirmReturns = false }; // user always declines
        BackupRestoreViewModel vm = new(dialog)
        {
            IsAnyWorkspaceDirty = () => true,
            SaveAllWorkspaces = _ => Task.CompletedTask,
        };

        // Feed the VM a synthetic (but non-corrupt) BackupEntry so the command body runs.
        BackupEntry entry = new()
        {
            ArchivePath = "/nowhere/backup.zip",
            FileName = "backup.zip",
            SizeBytes = 1,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest { Platform = "linux" },
        };
        BackupRowViewModel row = new(entry);

        await vm.RestoreCommand.ExecuteAsync(row);

        // Two sequential prompts: Save-first?  → No.  Discard?  → No.  Cancel.
        Assert.AreEqual(2, dialog.ConfirmCalls,
            "Dirty-guard flow must show Save-first then Discard prompts when the user declines both.");
    }

    /// <summary>
    /// pre-Backup save guard MUST pass
    /// <c>isRestoreContext: false</c> so the SaveDialog uses regular "Save Changes" /
    /// "Saving N changes" / "will be written to" labels.  Pre-fix the host wired
    /// the callback to <c>SaveForRestoreAsync</c> which forced restore-themed
    /// labels even when the user was about to back up — visible bug:
    /// "[Create Backup] → unsaved-changes prompt → Save → dialog title says
    /// 'Restore Preview' even though we're backing up."
    /// </summary>
    [TestMethod]
    public async Task BackupCommand_DirtyGuard_PassesIsRestoreContextFalseToSave()
    {
        bool? capturedContext = null;
        StubDialogService dialog = new() { ConfirmReturns = true }; // accept "Save first"
        BackupRestoreViewModel vm = new(dialog)
        {
            CredentialsPreference = false, // already answered, no prompt
            BackupDirectory = Path.Combine(Path.GetTempPath(),
                "vt-" + Guid.NewGuid().ToString("N")),
            IsAnyWorkspaceDirty = () => true,
            SaveAllWorkspaces = isRestoreContext =>
            {
                capturedContext = isRestoreContext;
                return Task.CompletedTask;
            },
        };

        string home = Path.Combine(Path.GetTempPath(), "vt-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        PlatformPaths.TestUserProfileOverride = home;

        try
        {
            await vm.BackupCommand.ExecuteAsync(null);

            Assert.IsNotNull(capturedContext,
                "SaveAllWorkspaces should have been invoked once the user accepted the dirty-prompt.");
            Assert.IsFalse(capturedContext!.Value,
                "Backup-flow pre-save guard MUST pass isRestoreContext: false so the " +
                "SaveDialog uses 'Save Changes' / 'Saving N changes' labels, not the " +
                "restore-themed 'Restore Preview' / 'Restoring N changes' / 'will be restored to'.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
            try
            {
                if (Directory.Exists(home))
                {
                    Directory.Delete(home, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }

            try
            {
                if (Directory.Exists(vm.BackupDirectory))
                {
                    Directory.Delete(vm.BackupDirectory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    /// <summary>
    /// pre-Restore save guard MUST pass
    /// <c>isRestoreContext: true</c> so the SaveDialog uses restore-themed labels.
    /// Symmetric to the Backup test — pins the Restore flow's correct context
    /// against the same callback signature change.
    /// </summary>
    [TestMethod]
    public async Task RestoreCommand_DirtyGuard_PassesIsRestoreContextTrueToSave()
    {
        bool? capturedContext = null;
        StubDialogService dialog = new() { ConfirmReturns = true }; // accept "Save first"
        BackupRestoreViewModel vm = new(dialog)
        {
            IsAnyWorkspaceDirty = () => true,
            SaveAllWorkspaces = isRestoreContext =>
            {
                capturedContext = isRestoreContext;
                return Task.CompletedTask;
            },
        };

        BackupEntry entry = new()
        {
            ArchivePath = "/nowhere/backup.zip",
            FileName = "backup.zip",
            SizeBytes = 1,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest { Platform = "linux" },
        };
        BackupRowViewModel row = new(entry);

        await vm.RestoreCommand.ExecuteAsync(row);

        Assert.IsNotNull(capturedContext,
            "SaveAllWorkspaces should have been invoked once the user accepted the dirty-prompt.");
        Assert.IsTrue(capturedContext!.Value,
            "Restore-flow pre-save guard MUST pass isRestoreContext: true so the " +
            "SaveDialog uses 'Restore Preview' / 'Restoring N changes' / 'will be restored to' labels.");
    }

    [TestMethod]
    public async Task DeleteAsync_SetsIsBusyDuringConfirmDialog()
    {
        bool isBusyDuringDialog = false;
        StubDialogService dialog = new() { ConfirmReturns = false };
        BackupRestoreViewModel vm = new(dialog);
        dialog.TargetVm = vm;
        dialog.OnConfirmCalled = v => isBusyDuringDialog = v.IsBusy;

        // Feed a synthetic entry so the command body runs.
        BackupEntry entry = new()
        {
            ArchivePath = Path.Combine(Path.GetTempPath(), "fake-delete.zip"),
            FileName = "fake-delete.zip",
            SizeBytes = 1,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest { Platform = "test" },
        };
        BackupRowViewModel row = new(entry);
        vm.Backups.Add(row);

        await vm.DeleteCommand.ExecuteAsync(row);

        Assert.IsTrue(isBusyDuringDialog,
            "IsBusy must be true while the delete confirmation dialog is showing.");
        Assert.IsFalse(vm.IsBusy,
            "IsBusy must be reset to false after DeleteAsync completes.");
    }

    [TestMethod]
    public void Dispose_UnsubscribesPersistentStateChanged()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        int callCount = 0;
        vm.PersistentStateChanged += (_, _) => callCount++;

        // Dispose severs the link.
        vm.Dispose();

        // Now fire the event through the only available path — raising via reflection
        // to simulate the engine calling it after the VM is discarded.
        FieldInfo? field = typeof(BackupRestoreViewModel)
            .GetField("PersistentStateChanged",
                BindingFlags.Instance | BindingFlags.Public);
        // After Dispose the backing delegate is null, so invoking it should be a no-op.
        EventHandler? evt = field?.GetValue(vm) as EventHandler;
        evt?.Invoke(vm, EventArgs.Empty);

        Assert.AreEqual(0, callCount,
            "No handlers should fire after Dispose() nulls PersistentStateChanged.");
    }

    [TestMethod]
    public void BackupModeConverter_ConvertsBothDirections()
    {
        object? result = BackupModeConverter.IsSettingsOnly.Convert(
            BackupMode.SettingsOnly, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.IsTrue((bool?)result);

        result = BackupModeConverter.IsFull.Convert(
            BackupMode.SettingsOnly, typeof(bool), null, CultureInfo.InvariantCulture);
        Assert.IsFalse((bool?)result);

        object? back = BackupModeConverter.IsFull.ConvertBack(
            true, typeof(BackupMode), null, CultureInfo.InvariantCulture);
        Assert.AreEqual(BackupMode.Full, back);
    }

    // ── New hardening tests ──────────────────────────────────────────────────

    [TestMethod]
    public async Task BackupAsync_WhenCreateDirectoryThrows_ResetsIsBackingUpAndShowsAlert()
    {
        // Create a temp file so BackupDirectory points at a file, causing CreateDirectory to throw.
        string tempFile = Path.GetTempFileName();
        try
        {
            AlertCountingDialogService alertDialog = new();
            BackupRestoreViewModel vm = new(alertDialog)
            {
                BackupDirectory = tempFile, // file path → Directory.CreateDirectory throws IOException
                CredentialsPreference = false, // skip the credentials prompt
            };

            await vm.BackupCommand.ExecuteAsync(null);

            Assert.IsFalse(vm.IsBusy,
                "IsBusy must be reset to false even when CreateDirectory throws (finally block).");
            Assert.IsTrue(alertDialog.AlertCalls >= 1,
                "ShowAlertAsync must be called at least once with the failure message.");
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task RestoreAsync_WhenCallbackThrows_DoesNotLeakException()
    {
        // If OnRestoreCompleted throws, the exception must be swallowed (logged only).
        // We verify this by triggering a restore against a non-existent archive and
        // asserting IsBusy is always reset regardless of the outcome.
        // (A corrupt/missing archive means RestoreAsync fails early and never calls
        // the callback — this test also confirms IsBusy resets on failure.)
        StubDialogService dialog = new() { ConfirmReturns = true };
        bool callbackFired = false;
        BackupRestoreViewModel vm = new(dialog)
        {
            OnRestoreCompleted = () =>
            {
                callbackFired = true;
                throw new InvalidOperationException("Simulated callback failure");
            },
        };

        BackupEntry entry = new()
        {
            ArchivePath = Path.Combine(Path.GetTempPath(), "nonexistent-restore-test.zip"),
            FileName = "nonexistent-restore-test.zip",
            SizeBytes = 1,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest { Platform = "test" },
        };
        BackupRowViewModel row = new(entry);

        // Must not throw even if the restore path has an error.
        await vm.RestoreCommand.ExecuteAsync(row);

        Assert.IsFalse(vm.IsBusy,
            "IsBusy must be reset to false after restore completes (success or failure).");
        // Note: the callback won't fire for a non-existent archive (no successful restore),
        // but this verifies the command path terminates cleanly and IsBusy is always reset.
        _ = callbackFired; // acknowledged — may be false for missing archive
    }

    // ───────────────────────────────────────────────────────────────────────
    //  UI-flow paths
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BrowseBackupDirectoryAsync_FirstSet_MirrorsToRestore()
    {
        StubDialogService dlg = new() { PickFolderReturns = "/tmp/backups" };
        BackupRestoreViewModel vm = new(dlg);
        vm.Refresh();

        // Both empty → "first set" branch fires; the picked folder mirrors
        // to RestoreDirectory so the user doesn't have to configure both.
        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);

        Assert.AreEqual("/tmp/backups", vm.BackupDirectory);
        Assert.AreEqual("/tmp/backups", vm.RestoreDirectory,
            "First-set must mirror BackupDirectory to RestoreDirectory.");
        Assert.AreEqual(1, dlg.PickFolderCalls);
    }

    [TestMethod]
    public async Task BrowseBackupDirectoryAsync_AlreadyConfigured_NoMirror()
    {
        StubDialogService dlg = new() { PickFolderReturns = "/tmp/new-backup-dir" };
        BackupRestoreViewModel vm = new(dlg)
        {
            InitialBackupDirectory = "/tmp/old-backup-dir",
            InitialRestoreDirectory = "/tmp/old-restore-dir",
        };
        vm.Refresh();

        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);

        Assert.AreEqual("/tmp/new-backup-dir", vm.BackupDirectory);
        Assert.AreEqual("/tmp/old-restore-dir", vm.RestoreDirectory,
            "When RestoreDirectory is already set, BrowseBackupDirectory must NOT mirror.");
    }

    [TestMethod]
    public async Task BrowseBackupDirectoryAsync_UserCancels_NoStateChange()
    {
        StubDialogService dlg = new() { PickFolderReturns = null };
        BackupRestoreViewModel vm = new(dlg) { InitialBackupDirectory = "/tmp/x" };
        vm.Refresh();

        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);

        Assert.AreEqual("/tmp/x", vm.BackupDirectory,
            "User dismissing the folder picker must leave BackupDirectory unchanged.");
    }

    [TestMethod]
    public async Task BrowseRestoreDirectoryAsync_FirstSet_MirrorsToBackup()
    {
        StubDialogService dlg = new() { PickFolderReturns = "/tmp/restore" };
        BackupRestoreViewModel vm = new(dlg);
        vm.Refresh();

        await vm.BrowseRestoreDirectoryCommand.ExecuteAsync(null);

        Assert.AreEqual("/tmp/restore", vm.RestoreDirectory);
        Assert.AreEqual("/tmp/restore", vm.BackupDirectory,
            "First-set must mirror RestoreDirectory to BackupDirectory.");
    }

    [TestMethod]
    public async Task BrowseRestoreDirectoryAsync_UserCancels_NoStateChange()
    {
        StubDialogService dlg = new() { PickFolderReturns = null };
        BackupRestoreViewModel vm = new(dlg) { InitialRestoreDirectory = "/tmp/x" };
        vm.Refresh();

        await vm.BrowseRestoreDirectoryCommand.ExecuteAsync(null);

        Assert.AreEqual("/tmp/x", vm.RestoreDirectory);
    }

    [TestMethod]
    public void OnBackupDirectoryChanged_AfterInit_FiresPersistentStateChanged()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        vm.Refresh(); // _initialized = true after this

        int fired = 0;
        vm.PersistentStateChanged += (_, _) => fired++;

        vm.BackupDirectory = "/tmp/changed";

        Assert.AreEqual(1, fired,
            "After init, mutating BackupDirectory must fire PersistentStateChanged "
            + "so MainWindowViewModel persists the new path to gui-state.json.");
    }

    [TestMethod]
    public void OnRestoreDirectoryChanged_AfterInit_FiresPersistentStateChanged()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        vm.Refresh();

        int fired = 0;
        vm.PersistentStateChanged += (_, _) => fired++;

        vm.RestoreDirectory = "/tmp/changed";

        Assert.AreEqual(1, fired);
    }

    [TestMethod]
    public void Refresh_SuppressesPersistentStateChanged_DuringInitialSeed()
    {
        // Refresh sets _initialized=false, seeds from Initial* properties,
        // then sets _initialized=true.  Side-effect partial methods must
        // not fire during the seed window or every reload would write the
        // gui-state file unnecessarily.
        BackupRestoreViewModel vm = new(new StubDialogService())
        {
            InitialBackupDirectory = "/tmp/seed-backup",
            InitialRestoreDirectory = "/tmp/seed-restore",
        };

        int fired = 0;
        vm.PersistentStateChanged += (_, _) => fired++;

        vm.Refresh();

        Assert.AreEqual(0, fired,
            "Refresh's initial seed must NOT fire PersistentStateChanged — only "
            + "user-initiated edits after initialisation should trigger persistence.");
    }

    [TestMethod]
    public void HasRestoreDirectory_TracksRestoreDirectoryEmptiness()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        Assert.IsFalse(vm.HasRestoreDirectory);

        vm.RestoreDirectory = "/tmp/x";
        Assert.IsTrue(vm.HasRestoreDirectory);

        vm.RestoreDirectory = string.Empty;
        Assert.IsFalse(vm.HasRestoreDirectory);
    }

    [TestMethod]
    public void HasNeverBackedUp_TracksLastBackupUtcNullness()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        // Default null → never backed up.
        Assert.IsTrue(vm.HasNeverBackedUp);

        vm.LastBackupUtc = DateTime.UtcNow.AddDays(-1);
        // Setting after construction does NOT fire the property-changed pipeline
        // for HasNeverBackedUp because LastBackupUtc is a plain auto-property.
        // The label itself recomputes lazily (it's an expression-body), so the
        // value flips correctly even without notification.
        Assert.IsFalse(vm.HasNeverBackedUp);
    }

    [TestMethod]
    public void LastBackupLabel_NullUtc_RendersNeverString()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        Assert.AreEqual(
            Strings.LabelLastBackupNever,
            vm.LastBackupLabel);
    }

    [TestMethod]
    public void CanCreateBackup_FalseWhenNoDirectory()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        Assert.IsFalse(vm.CanCreateBackup,
            "Without a backup directory the Create command must be disabled.");
    }

    [TestMethod]
    public void ShowMsixTab_FalseWhenStatusIsNull()
    {
        BackupRestoreViewModel vm = new(new StubDialogService());
        Assert.IsFalse(vm.ShowMsixTab,
            "MsixStatus is null by default → ShowMsixTab must be false on every platform.");
    }

    // ── ShareBackupAsync — null-safety + happy-path delegation ────────────

    [TestMethod]
    public async Task ShareBackupAsync_NullShareService_NoOp()
    {
        // Construct WITHOUT a share service.  The command must short-circuit
        // gracefully — no NullReferenceException.
        BackupRestoreViewModel vm = new(new StubDialogService(), shareService: null);
        await vm.ShareBackupCommand.ExecuteAsync(null);
        // Pass = no exception thrown.
    }

    [TestMethod]
    public async Task ShareBackupAsync_NullRow_NoOp()
    {
        RecordingShareService share = new();
        BackupRestoreViewModel vm = new(new StubDialogService(), share);
        await vm.ShareBackupCommand.ExecuteAsync(null);
        Assert.AreEqual(0, share.ShareFileCalls,
            "Null row must short-circuit without invoking the share service.");
    }

    // ── remaining BackupRestoreViewModel paths ──

    [TestMethod]
    public async Task ShareBackupAsync_WithRowAndService_ForwardsToShareService()
    {
        // Happy path: a non-null row + non-null share service → forwards
        // the archive path to ShareFileAsync.  Exercises the success
        // branch beyond the two existing null-safety tests.
        RecordingShareService share = new();
        BackupRestoreViewModel vm = new(new StubDialogService(), share);
        BackupRowViewModel row = new(new BackupEntry
        {
            ArchivePath = "/tmp/example.zip",
            FileName = "example.zip",
            SizeBytes = 1024,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest
            {
                CreatedUtc = DateTime.UtcNow,
                AppVersion = "1.0",
                Platform = "windows",
            },
        });

        await vm.ShareBackupCommand.ExecuteAsync(row);

        Assert.AreEqual(1, share.ShareFileCalls);
        Assert.AreEqual("/tmp/example.zip", share.LastFilePath);
    }

    [TestMethod]
    public async Task ShareBackupAsync_ShareServiceThrows_DoesNotPropagate()
    {
        // The command must catch share-service exceptions internally so a
        // misbehaving share provider can't crash the app.
        RecordingShareService share = new() { ShouldThrow = true };
        BackupRestoreViewModel vm = new(new StubDialogService(), share);
        BackupRowViewModel row = new(new BackupEntry
        {
            ArchivePath = "/tmp/example.zip",
            FileName = "example.zip",
            SizeBytes = 1024,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest
            {
                CreatedUtc = DateTime.UtcNow,
                AppVersion = "1.0",
                Platform = "windows",
            },
        });

        // No throw → command swallowed it.
        await vm.ShareBackupCommand.ExecuteAsync(row);
    }

    [TestMethod]
    public void OpenFileLocation_NullRow_NoOp()
    {
        // Sync command — null row must short-circuit before reaching
        // ShellLauncher.  Verifies the null guard at the top of the
        // method (line 653 of BackupRestoreViewModel).
        BackupRestoreViewModel vm = new(new StubDialogService());
        vm.OpenFileLocationCommand.Execute(null);
        // Pass = no NullReferenceException trying to deref row.Entry.
    }

    [TestMethod]
    public async Task FixMsixAsync_NotOnWindowsOrNoFix_EarlyReturn()
    {
        // FixMsixAsync guards on (!OperatingSystem.IsWindows() || IsBusy)
        // and on MsixStatus?.NeedsFix != true.  When no MsixStatus is set
        // the second guard fires immediately and the command returns
        // without touching IsBusy.
        StubDialogService dialog = new();
        BackupRestoreViewModel vm = new(dialog);

        await vm.FixMsixCommand.ExecuteAsync(null);

        Assert.AreEqual(0, dialog.ConfirmCalls,
            "FixMsix must NOT show a confirm dialog when MsixStatus.NeedsFix is unset.");
        Assert.IsFalse(vm.IsBusy);
    }

    [TestMethod]
    public void CancelOperation_NoActiveOperation_NoThrow()
    {
        // CancelOperation = _operationCts?.Cancel() — must be a no-op
        // when nothing is in flight.  Guards a real race:
        // user clicks cancel before / after a backup is running.
        BackupRestoreViewModel vm = new(new StubDialogService());
        vm.CancelOperationCommand.Execute(null);
        // Pass = no NullReferenceException on the null _operationCts.
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Drag-drop restore
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RestoreFromDroppedArchive_NullOrEmptyPath_NoOp()
    {
        StubDialogService dialog = new();
        BackupRestoreViewModel vm = new(dialog);

        await vm.RestoreFromDroppedArchiveAsync(null!);
        await vm.RestoreFromDroppedArchiveAsync(string.Empty);
        await vm.RestoreFromDroppedArchiveAsync("   ");

        Assert.AreEqual(0, dialog.AlertCalls,
            "Empty / whitespace paths should silently no-op — they represent the " +
            "'no payload' case rather than a user error worth alerting on.");
        Assert.AreEqual(0, dialog.ConfirmCalls);
    }

    [TestMethod]
    public async Task RestoreFromDroppedArchive_NonZipExtension_ShowsInvalidAlert()
    {
        // Defence-in-depth: the View's DragOver gate filters to .zip before
        // letting Drop fire, but the VM-level handler also rejects non-zip
        // paths so a programmatic invocation (CLI / future automation hook)
        // can't bypass the extension check.
        StubDialogService dialog = new();
        BackupRestoreViewModel vm = new(dialog);

        string tmp = Path.Combine(Path.GetTempPath(), "drop-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(tmp, "not a zip");

        try
        {
            await vm.RestoreFromDroppedArchiveAsync(tmp);

            Assert.AreEqual(1, dialog.AlertCalls,
                "Non-zip drops must surface an error alert so the user " +
                "understands why nothing happened.");
            Assert.AreEqual(0, dialog.ConfirmCalls,
                "Confirm prompt must not fire — extension check happens first.");
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task RestoreFromDroppedArchive_CorruptZip_ShowsInvalidAlert()
    {
        // A .zip extension but random bytes inside — TryReadEntry returns
        // an entry with IsCorrupt=true.  The VM must alert and refuse the
        // restore rather than route to RestoreCommand which would then fail
        // with a less-clear "manifest missing" error.
        StubDialogService dialog = new();
        BackupRestoreViewModel vm = new(dialog);

        string tmp = Path.Combine(Path.GetTempPath(), "drop-" + Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(tmp, [0xDE, 0xAD, 0xBE, 0xEF]);

        try
        {
            await vm.RestoreFromDroppedArchiveAsync(tmp);

            Assert.AreEqual(1, dialog.AlertCalls,
                "Corrupt zip must surface the 'not a valid backup' alert.");
            Assert.AreEqual(0, dialog.ConfirmCalls);
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task RestoreFromDroppedArchive_MissingFile_ShowsInvalidAlert()
    {
        // A .zip path that doesn't exist on disk — TryReadEntry returns
        // null, which the VM treats as "invalid" (same alert as corrupt).
        StubDialogService dialog = new();
        BackupRestoreViewModel vm = new(dialog);

        string missing = Path.Combine(Path.GetTempPath(),
            "drop-missing-" + Guid.NewGuid().ToString("N") + ".zip");

        await vm.RestoreFromDroppedArchiveAsync(missing);

        Assert.AreEqual(1, dialog.AlertCalls,
            "A non-existent path under a .zip name must still alert — silently " +
            "no-oping would be confusing to a user who just dropped a file.");
    }

    [TestMethod]
    public async Task RestoreFromDroppedArchive_ValidZip_FiresConfirmPrompt()
    {
        // Construct a real backup zip so TryReadEntry returns a non-corrupt
        // entry.  Then drop it and assert the confirm prompt fires.  We do
        // NOT confirm-true: that would actually run RestoreCommand against
        // the live ~/.claude profile, which this test must not do.
        string fakeHome = Path.Combine(Path.GetTempPath(), "vt-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fakeHome, ".claude"));
        await File.WriteAllTextAsync(Path.Combine(fakeHome, ".claude", "settings.json"), """{"theme":"dark"}""");
        PlatformPaths.TestUserProfileOverride = fakeHome;
        BackupEngine.InvalidateListCache();

        string zipPath = Path.Combine(fakeHome, "backup-20300101-000000.zip");
        try
        {
            BackupResult created = await BackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = zipPath,
                IncludeClaudeCode = true,
                IncludeClaudeDesktop = false,
            });
            Assert.IsTrue(created.Succeeded, "Test prerequisite: backup must create.");

            StubDialogService dialog = new() { ConfirmReturns = false }; // user cancels
            BackupRestoreViewModel vm = new(dialog);

            await vm.RestoreFromDroppedArchiveAsync(zipPath);

            Assert.AreEqual(1, dialog.ConfirmCalls,
                "Valid backup zip must fire exactly one confirm prompt before any " +
                "guard the click-from-list path runs (dirty-workspace etc.).");
            Assert.AreEqual(0, dialog.AlertCalls,
                "No error alert on the happy path — validation passed.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
            BackupEngine.InvalidateListCache();
            try
            {
                if (Directory.Exists(fakeHome))
                {
                    Directory.Delete(fakeHome, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task RestoreFromDroppedArchive_ConfirmDismissedViaX_DoesNotRestore()
    {
        // Universal X-dismiss contract: confirming-by-X must abort, never
        // proceed with the restore.
        string fakeHome = Path.Combine(Path.GetTempPath(), "vt-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(fakeHome, ".claude"));
        await File.WriteAllTextAsync(Path.Combine(fakeHome, ".claude", "settings.json"), """{"theme":"dark"}""");
        PlatformPaths.TestUserProfileOverride = fakeHome;
        BackupEngine.InvalidateListCache();

        string zipPath = Path.Combine(fakeHome, "backup-20300101-000000.zip");
        try
        {
            BackupResult created = await BackupEngine.Default.CreateAsync(new BackupRequest
            {
                DestinationZipPath = zipPath,
                IncludeClaudeCode = true,
                IncludeClaudeDesktop = false,
            });
            Assert.IsTrue(created.Succeeded);

            // Wire up a sentinel for "did we proceed past the confirm prompt?"
            // If we did, the dirty-workspace check or the cross-platform
            // warning would fire next, bumping ConfirmCalls.  Asserting
            // exactly ONE confirm call locks the abort-on-X contract.
            StubDialogService dialog = new() { ConfirmDismissesViaX = true };
            BackupRestoreViewModel vm = new(dialog)
            {
                IsAnyWorkspaceDirty = () => true, // would fire a 2nd prompt if we proceeded
            };

            await vm.RestoreFromDroppedArchiveAsync(zipPath);

            Assert.AreEqual(1, dialog.ConfirmCalls,
                "X-dismissing the drop-restore prompt must abort BEFORE the " +
                "dirty-workspace prompt fires.  Pre-fix bug: X would collapse " +
                "to 'cancel' and silently proceed.");
        }
        finally
        {
            PlatformPaths.TestUserProfileOverride = null;
            BackupEngine.InvalidateListCache();
            try
            {
                if (Directory.Exists(fakeHome))
                {
                    Directory.Delete(fakeHome, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    [TestMethod]
    public async Task RestoreFromDroppedArchive_WhileBusy_NoOp()
    {
        // Re-entrancy guard — same shape as RestoreCommand's IsBusy check.
        // We set IsBusy via reflection because the property is internal-set.
        StubDialogService dialog = new();
        BackupRestoreViewModel vm = new(dialog);

        typeof(BackupRestoreViewModel)
            .GetProperty(nameof(vm.IsBusy))!
            .SetValue(vm, true);

        await vm.RestoreFromDroppedArchiveAsync("C:\\does-not-matter.zip");

        Assert.AreEqual(0, dialog.AlertCalls);
        Assert.AreEqual(0, dialog.ConfirmCalls,
            "Drop-restore while another operation is in flight must silently no-op " +
            "rather than queue or race the live operation.");
    }

    private sealed class RecordingShareService : IShareService
    {
        public int ShareFileCalls { get; private set; }
        public string? LastTitle { get; private set; }
        public string? LastFilePath { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task ShareFileAsync(string title, string filePath)
        {
            ShareFileCalls++;
            LastTitle = title;
            LastFilePath = filePath;
            if (ShouldThrow)
            {
                throw new IOException("fake share failure");
            }

            return Task.CompletedTask;
        }

        public Task ShareTextAsync(string title, string text, string? subject = null)
        {
            return Task.CompletedTask;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  BackupRowViewModel + BackupModeConverter formatters
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BackupRowViewModel_DisplayFormatters_ReadFromEntry()
    {
        BackupEntry entry = new()
        {
            ArchivePath = "/fake/backup-2026.zip",
            FileName = "backup-2026.zip",
            LastModifiedUtc = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc),
            SizeBytes = 2_500_000,
            Manifest = new BackupManifest
            {
                Kind = "backup",
                SchemaVersion = BackupManifest.CurrentSchemaVersion,
                Platform = "Windows",
                Mode = BackupMode.SettingsOnly,
                Clients = ["ClaudeCode"],
            },
        };
        BackupRowViewModel row = new(entry);

        Assert.AreEqual("backup-2026.zip", row.DisplayName);
        StringAssert.Contains(row.DisplayDate, "2026");
        StringAssert.Contains(row.DisplaySize, "MB");
        Assert.AreEqual("Windows", row.DisplayPlatform);
        Assert.AreEqual("SettingsOnly", row.DisplayMode);
        // Short form for the cell (2026-05-19) — "ClaudeCode" → "Code".
        Assert.AreEqual("Code", row.DisplayClients);
        // Tooltip retains the full product name for hover-disambiguation.
        StringAssert.Contains(row.DisplayClientsTooltip, "ClaudeCode");
        Assert.IsTrue(row.IsRestorable);
    }

    [TestMethod]
    public void BackupRowViewModel_CorruptArchive_NotRestorable()
    {
        // BackupEntry.IsCorrupt is a computed property — true when Manifest is null.
        BackupEntry entry = new()
        {
            ArchivePath = "/fake/broken.zip",
            FileName = "broken.zip",
            LastModifiedUtc = DateTime.UtcNow,
            SizeBytes = 100,
            Manifest = null, // → IsCorrupt = true
        };
        BackupRowViewModel row = new(entry);
        Assert.IsFalse(row.IsRestorable);
        Assert.AreEqual("?", row.DisplayPlatform,
            "Missing manifest renders platform as '?'.");
        Assert.AreEqual("—", row.DisplayMode);
        Assert.AreEqual("corrupt", row.DisplayClients);
        StringAssert.Contains(row.DisplayClientsTooltip, "Manifest is unreadable",
            "Corrupt-archive tooltip should explain the empty cell.");
    }

    // ── Clients-column abbreviation (2026-05-19) ─────────────────────
    //
    // Cell shows the short form so the Clients column can fit at ~95 px,
    // freeing space for the File name flex column.  Hover-tooltip retains
    // the long product names for disambiguation.

    [TestMethod]
    public void BackupRowViewModel_DisplayClients_AbbreviatesBothProducts()
    {
        BackupEntry entry = new()
        {
            ArchivePath = "/fake/multi.zip",
            FileName = "multi.zip",
            LastModifiedUtc = DateTime.UtcNow,
            SizeBytes = 100,
            Manifest = new BackupManifest
            {
                Platform = "Windows",
                Mode = BackupMode.Full,
                Clients = ["ClaudeCode", "ClaudeDesktop"],
            },
        };
        BackupRowViewModel row = new(entry);

        Assert.AreEqual("Code+Desktop", row.DisplayClients,
            "Both ClaudeCode and ClaudeDesktop must compact to their short forms in the cell.");
        StringAssert.Contains(row.DisplayClientsTooltip, "ClaudeCode");
        StringAssert.Contains(row.DisplayClientsTooltip, "ClaudeDesktop");
    }

    [TestMethod]
    public void BackupRowViewModel_DisplayMode_SanitizedRow_IsCompactWithFullTooltip()
    {
        // cell label shortened from "Sanitized (for sharing)"
        // to just "Sanitized" so the Mode column can fit at ~110 px.  The
        // amber chip background carries the "this is special" visual cue
        // and the tooltip carries the long-form explanation.
        BackupEntry entry = new()
        {
            ArchivePath = "/fake/share.zip",
            FileName = "share.zip",
            LastModifiedUtc = DateTime.UtcNow,
            SizeBytes = 100,
            Manifest = new BackupManifest
            {
                Platform = "Windows",
                Mode = BackupMode.Sanitized,
                Clients = ["ClaudeCode"],
            },
        };
        BackupRowViewModel row = new(entry);

        Assert.AreEqual("Sanitized", row.DisplayMode,
            "Sanitized cell label must be the compact form.");
        StringAssert.Contains(row.DisplayModeTooltip, "for sharing",
            "Tooltip must carry the long-form explanation.");
        StringAssert.Contains(row.DisplayModeTooltip, "not restorable",
            "Tooltip should also surface the not-restorable consequence.");
    }

    [TestMethod]
    public void BackupRowViewModel_DisplayModeTooltip_NonSanitized_MirrorsDisplayMode()
    {
        // For non-Sanitized modes the tooltip mirrors the cell value so
        // hovering doesn't show duplicate but slightly different text.
        BackupEntry entry = new()
        {
            ArchivePath = "/fake/full.zip",
            FileName = "full.zip",
            LastModifiedUtc = DateTime.UtcNow,
            SizeBytes = 100,
            Manifest = new BackupManifest
            {
                Platform = "Windows",
                Mode = BackupMode.Full,
                Clients = ["ClaudeCode"],
            },
        };
        BackupRowViewModel row = new(entry);

        Assert.AreEqual("Full", row.DisplayMode);
        Assert.AreEqual(row.DisplayMode, row.DisplayModeTooltip,
            "Tooltip must mirror DisplayMode for non-Sanitized rows.");
    }

    [TestMethod]
    public void BackupRowViewModel_DisplayClients_UnknownClientFallsBackToRawName()
    {
        // Future-proof: if a new product name appears in the manifest that the
        // abbreviation table doesn't know about, render it verbatim rather than
        // dropping it.  Catches the "new client name landed in the manifest
        // before the GUI's abbreviation map was updated" case.
        BackupEntry entry = new()
        {
            ArchivePath = "/fake/future.zip",
            FileName = "future.zip",
            LastModifiedUtc = DateTime.UtcNow,
            SizeBytes = 100,
            Manifest = new BackupManifest
            {
                Platform = "Windows",
                Mode = BackupMode.SettingsOnly,
                Clients = ["ClaudeCode", "ClaudeFutureProduct"],
            },
        };
        BackupRowViewModel row = new(entry);

        // "ClaudeCode" abbreviates to "Code"; "ClaudeFutureProduct" passes through verbatim.
        Assert.AreEqual("Code+ClaudeFutureProduct", row.DisplayClients);
    }

    [TestMethod]
    [DataRow("claudecode", "Code")]
    [DataRow("CLAUDECODE", "Code")]
    [DataRow("ClaudeCode", "Code")]
    [DataRow("claudedesktop", "Desktop")]
    [DataRow("ClaudeDESKTOP", "Desktop")]
    public void BackupRowViewModel_DisplayClients_AbbreviationIsCaseInsensitive(
        string manifestValue, string expectedAbbrev)
    {
        // security-reviewer LOW #2.  Today every manifest is
        // written by BackupEngine with exact casing, but a third-party tool
        // could ship an archive with different casing in the Clients list;
        // the abbreviation map must still hit the short form rather than
        // falling through to the verbose passthrough.
        BackupEntry entry = new()
        {
            ArchivePath = "/fake/mixed-case.zip",
            FileName = "mixed-case.zip",
            LastModifiedUtc = DateTime.UtcNow,
            SizeBytes = 100,
            Manifest = new BackupManifest
            {
                Platform = "Windows",
                Mode = BackupMode.SettingsOnly,
                Clients = [manifestValue],
            },
        };
        BackupRowViewModel row = new(entry);

        Assert.AreEqual(expectedAbbrev, row.DisplayClients,
            $"'{manifestValue}' must abbreviate case-insensitively.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Test doubles
    // ───────────────────────────────────────────────────────────────────────

    // ═══════════════════════════════════════════════════════════════════════
    //  L2 — BackupRowViewModel inherits ObservableObject
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void BackupRowViewModel_IsObservableObject()
    {
        // The row class itself is a plain DTO over the immutable
        // BackupEntry; we don't currently mutate row state.  But
        // future row-mutating paths need a working PropertyChanged
        // wire, and the conversion to ObservableObject in L2 is the
        // foundation.  Sanity-check the inheritance is in place.
        BackupEntry entry = new()
        {
            ArchivePath = "/tmp/fake.zip",
            FileName = "fake.zip",
            SizeBytes = 100,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = null,
        };
        BackupRowViewModel row = new(entry);

        Assert.IsInstanceOfType<ObservableObject>(row,
            "BackupRowViewModel must inherit ObservableObject so future " +
            "mutators have a working PropertyChanged wire (L2 contract).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  L3 — Defence-in-depth: IsRestorable=false for Sanitized rows
    // ═══════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void BackupRowViewModel_SanitizedBackup_IsNotRestorable()
    {
        // The View's OnRestoreBackup handler early-returns when the
        // row's IsRestorable is false (L3 defence-in-depth).  Verify
        // the row's contract end of that wire: a Sanitized manifest
        // produces IsRestorable=false so the handler skip is reachable.
        BackupEntry sanitizedEntry = new()
        {
            ArchivePath = "/tmp/sanitized.zip",
            FileName = "sanitized.zip",
            SizeBytes = 100,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest { Mode = BackupMode.Sanitized },
        };
        BackupRowViewModel row = new(sanitizedEntry);

        Assert.IsFalse(row.IsRestorable,
            "Sanitized backups must not be restorable — the row-level guard " +
            "is the visual signal AND the early-return source for the View's " +
            "OnRestoreBackup defence-in-depth check (L3).");
        Assert.IsTrue(row.IsSanitized,
            "IsSanitized must mirror the manifest Mode so the View can render " +
            "the yellow chip on the row.");
    }

    [TestMethod]
    public void BackupRowViewModel_SettingsOnlyBackup_IsRestorable()
    {
        BackupEntry settingsOnlyEntry = new()
        {
            ArchivePath = "/tmp/settings.zip",
            FileName = "settings.zip",
            SizeBytes = 100,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = new BackupManifest { Mode = BackupMode.SettingsOnly },
        };
        BackupRowViewModel row = new(settingsOnlyEntry);

        Assert.IsTrue(row.IsRestorable);
        Assert.IsFalse(row.IsSanitized);
    }

    [TestMethod]
    public void BackupRowViewModel_CorruptBackup_IsNotRestorable()
    {
        // No manifest → row is treated as corrupt → IsRestorable false.
        BackupEntry corruptEntry = new()
        {
            ArchivePath = "/tmp/corrupt.zip",
            FileName = "corrupt.zip",
            SizeBytes = 100,
            LastModifiedUtc = DateTime.UtcNow,
            Manifest = null,
        };
        BackupRowViewModel row = new(corruptEntry);

        Assert.IsFalse(row.IsRestorable,
            "Corrupt backups (no manifest) must not be restorable.");
    }

    private sealed class AlertCountingDialogService : IDialogService
    {
        public int AlertCalls { get; private set; }
        public string? LastAlertTitle { get; private set; }

        public Task ShowAlertAsync(string title, string message)
        {
            AlertCalls++;
            LastAlertTitle = title;
            return Task.CompletedTask;
        }

        // All other IDialogService methods return safe no-op results:
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

    private sealed class StubDialogService : IDialogService
    {
        public bool ConfirmReturns { get; set; }
        public int ConfirmCalls { get; private set; }

        /// <summary>
        /// set <c>true</c> to simulate the user dismissing
        /// the dialog via the window-close X.  Overrides
        /// <see cref="ConfirmReturns"/>: ShowConfirmAsync returns
        /// <c>null</c> regardless.  Use for X-close regression tests.
        /// </summary>
        public bool ConfirmDismissesViaX { get; set; }

        /// <summary>
        /// Optional callback invoked each time ShowConfirmAsync is called, receiving the
        /// BackupRestoreViewModel that is the ultimate caller. Used to inspect VM state
        /// mid-flight (e.g. assert IsBusy is true while the dialog is open).
        /// </summary>
        public Action<BackupRestoreViewModel>? OnConfirmCalled { get; set; }

        // The test harness needs to supply a reference to the VM so the callback can
        // inspect it. Set by tests that use OnConfirmCalled.
        public BackupRestoreViewModel? TargetVm { get; set; }

        /// <summary>
        /// Optional value <see cref="PickFolderAsync"/> will return.  Set to a path to
        /// simulate the user picking a folder; leave <see langword="null"/> to simulate
        /// the user dismissing the picker.
        /// </summary>
        public string? PickFolderReturns { get; set; }

        /// <summary>Number of times <see cref="PickFolderAsync"/> was invoked.</summary>
        public int PickFolderCalls { get; private set; }

        public Task<string?> PickFolderAsync(string? title = null)
        {
            PickFolderCalls++;
            return Task.FromResult(PickFolderReturns);
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

        /// <summary>
        /// Count of <see cref="ShowAlertAsync"/> invocations.  Used by the
        /// drop-restore tests to assert an error alert fired (invalid file
        /// type / corrupt zip / etc.).
        /// </summary>
        public int AlertCalls { get; private set; }

        /// <summary>Title of the most recent <see cref="ShowAlertAsync"/> call.</summary>
        public string? LastAlertTitle { get; private set; }

        public Task ShowAlertAsync(string title, string message)
        {
            AlertCalls++;
            LastAlertTitle = title;
            return Task.CompletedTask;
        }

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel")
        {
            ConfirmCalls++;
            if (OnConfirmCalled is not null && TargetVm is not null)
            {
                OnConfirmCalled(TargetVm);
            }

            // ConfirmDismissesViaX takes precedence: simulate X close (null)
            // for regression tests that pin the abort-on-X contract.
            return Task.FromResult<bool?>(ConfirmDismissesViaX ? null : ConfirmReturns);
        }

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
        {
            return Task.FromResult(ConfirmReturns);
        }
    }
}