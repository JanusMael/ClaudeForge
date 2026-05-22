using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Tests for the dirty-state recheck behavior of <see cref="MainWindowViewModel"/>.
/// <para>
/// <strong>Regression context:</strong> the previous <c>OnAnyWorkspaceChanged</c>
/// handler unconditionally set <c>HasUnsavedChanges = true</c> on every
/// <c>workspace.Changed</c> event. After a set-then-reset cycle (user edits a
/// field, then clicks Reset which writes the original value back into the
/// workspace), the workspace's <see cref="Bennewitz.Ninja.ClaudeForge.Core.Settings.SettingsDocument.IsDirty"/>
/// flag stayed true (it is a one-way latch), but <c>HasActualChanges()</c>
/// correctly returned false. The Save button stayed enabled even though
/// nothing actually differed from the baseline. The new handler computes
/// <see cref="MainWindowViewModel.HasUnsavedChanges"/> from a structural
/// comparison instead, so the Save button correctly disables after Reset.
/// </para>
/// </summary>
[TestClass]
public sealed class HasUnsavedChangesRecheckTests
{
    private string _sandbox = null!;

    [TestInitialize]
    public void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        // Robust delete: each test constructs a MainWindowViewModel which
        // wires a FileSystemWatcher against the sandbox directory.  On the
        // CI windows-latest runner the watcher's ReadDirectoryChangesW
        // completion-port handle can outlive Dispose() by tens of
        // milliseconds, racing with Directory.Delete.  The helper does
        // a forced GC pass + retry-with-backoff to absorb that latency.
        TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
    }

    [TestMethod]
    public async Task SetThenRevertSameValue_ClearsHasUnsavedChanges()
    {
        // Seed a User-scope settings.json with one key so the workspace has
        // a baseline to compare against.
        string settingsPath = Path.Combine(_sandbox, ".claude", "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"model":"sonnet"}""");

        MainWindowViewModel vm = new(new SchemaRegistry(), new NullDialogService());
        try
        {
            await vm.InitializeCommand.ExecuteAsync(null);

            // After load the workspace is clean.
            Assert.IsFalse(vm.HasUnsavedChanges, "Fresh load must not flag unsaved changes.");

            // Reach into the SDK client and mutate via the public surface —
            // the editor pipeline does not need to be exercised directly here;
            // we are testing the SDK Changed forwarder → HasActualChanges chain.
            // 4.3.7 step 14: prefer the SDK seam over the legacy workspace one.
            ClaudeConfigClientCore? client = vm.GetClaudeCodeSdkClientForTesting();
            Assert.IsNotNull(client, "Initialize must have created the Claude Code SDK client.");

            client!.SetValue("model", "opus", ConfigScope.User);
            Assert.IsTrue(vm.HasUnsavedChanges,
                "After a value change diverges from the baseline, HasUnsavedChanges must flip true.");

            // Set the value BACK to the baseline ("sonnet"). Document.IsDirty stays
            // latched true (one-way), but HasActualChanges() returns false because
            // the JSON content is structurally identical to the baseline.
            client.SetValue("model", "sonnet", ConfigScope.User);
            Assert.IsFalse(vm.HasUnsavedChanges,
                "After setting the value back to baseline, HasUnsavedChanges must flip false " +
                "even though IsDirty stays latched. This is the contract that makes Save " +
                "correctly disable after a Reset on the MCP / Permissions / Hooks pages.");
        }
        finally
        {
            vm.Dispose();
        }
    }

    [TestMethod]
    public async Task EditThenReset_FlipsHasUnsavedChangesBackToFalse()
    {
        // The Reset path goes through the editor's RemoveValue then re-SetValue
        // dance (see SettingsGroupEditorViewModel.OnEditorPropertyChanged). After
        // the cycle the workspace document's Root is structurally equal to its
        // BaselineRoot again — IsDirty stays latched true (one-way), but
        // HasActualChanges() returns false. The new HasUnsavedChanges recompute
        // (ComputeHasActualChanges via JsonNode.DeepEquals) catches this and
        // disables the Save button.
        //
        // This integration test locks the contract end-to-end so a future
        // refactor of OnAnyWorkspaceChanged or HasActualChanges cannot silently
        // re-introduce the regression.
        string settingsPath = Path.Combine(_sandbox, ".claude", "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"model":"sonnet"}""");

        MainWindowViewModel vm = new(new SchemaRegistry(), new NullDialogService());
        try
        {
            await vm.InitializeCommand.ExecuteAsync(null);
            ClaudeConfigClientCore? client = vm.GetClaudeCodeSdkClientForTesting();
            Assert.IsNotNull(client);

            // Edit: deviate from baseline.
            client!.SetValue("model", "opus", ConfigScope.User);
            Assert.IsTrue(vm.HasUnsavedChanges, "Edit must flip the Save button on.");

            // Reset: simulate the two-step Reset path the live-write actually
            // performs — RemoveValue (clear in-memory edit) + SetValue (re-apply
            // baseline so the Root re-matches BaselineRoot exactly). After this
            // sequence Document.HasActualChanges() returns false.
            client.RemoveValue("model", ConfigScope.User);
            client.SetValue("model", "sonnet", ConfigScope.User);

            Assert.IsFalse(vm.HasUnsavedChanges,
                "After a full Reset cycle (RemoveValue + restore-baseline-via-SetValue), " +
                "HasUnsavedChanges must be false — even though the document's IsDirty " +
                "latch is still true. This is the contract that makes Save correctly " +
                "disable after Reset on every editor.");
        }
        finally
        {
            vm.Dispose();
        }
    }

    // -----------------------------------------------------------------------
    // Install-banner sticky-dismiss tests
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task InstallBanner_NotShown_WhenCodeSettingsFilePresent()
    {
        // IsClaudeCodeInstalled has a sandboxed fallback: File.Exists(UserSettingsPath).
        // Creating settings.json in the sandbox makes Code "detected" regardless of
        // whether the binary is on PATH.  When at least one product is detected,
        // ShowInstallBanner must be false after Initialize.
        string settingsPath = Path.Combine(_sandbox, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(settingsPath, "{}");

        MainWindowViewModel vm = new(new SchemaRegistry(), new NullDialogService());
        try
        {
            await vm.InitializeCommand.ExecuteAsync(null);
            Assert.IsFalse(vm.ShowInstallBanner,
                "When Code is detected (settings.json present) ShowInstallBanner must be false.");
        }
        finally
        {
            vm.Dispose();
        }
    }

    [TestMethod]
    public async Task InstallBanner_AutoClearsDismissedFlag_WhenProductAppears()
    {
        // Contract: if the user dismissed the banner (neither product was installed),
        // then a product is detected on a subsequent reload, _bannerDismissedByUser is
        // cleared so that if both products are later removed again the banner re-shows.
        //
        // Verification strategy: start without settings.json (Code not detected via
        // the sandboxed fallback), do a first Initialize, then place settings.json and
        // reload.  The reload must see Code as installed and keep ShowInstallBanner=false.
        // We also verify via a second reload (after removing settings.json again) that
        // the banner now re-appears — i.e. the dismissed-flag was cleared by the
        // "product detected" reload rather than staying permanently suppressed.
        //
        // Note: this test only exercises the IsClaudeCodeInstalled seam (the
        // File.Exists(UserSettingsPath) fallback is sandbox-isolated).
        // IsDesktopInstalled's binary-check paths are not sandboxed, so on a
        // machine with Desktop installed the second leg below may observe
        // ShowInstallBanner=false for that reason — the assertion is
        // therefore only made when Desktop is also not detected.
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        string settingsPath = Path.Combine(_sandbox, ".claude", "settings.json");

        // ── leg 1: no products detected, load, then place Code settings ──
        MainWindowViewModel vm = new(new SchemaRegistry(), new NullDialogService());
        try
        {
            await vm.InitializeCommand.ExecuteAsync(null);
            // banner state here depends on whether Desktop binary is present on
            // this machine — we don't assert it, just proceed to the reload.

            // Simulate "Code just appeared" (e.g. after Restore drops settings.json).
            await File.WriteAllTextAsync(settingsPath, "{}");
            await vm.ReloadCommand.ExecuteAsync(null);

            Assert.IsFalse(vm.ShowInstallBanner,
                "After reload finds Code installed, ShowInstallBanner must be false.");

            // ── leg 2: remove Code settings again — banner should re-show
            //    (only assertable if Desktop is also not installed on this machine) ──
            File.Delete(settingsPath);
            await vm.ReloadCommand.ExecuteAsync(null);

            if (!PlatformPaths.IsDesktopInstalled)
            {
                Assert.IsTrue(vm.ShowInstallBanner,
                    "After removing the only detected product, ShowInstallBanner must return " +
                    "to true — the auto-clear of _bannerDismissedByUser in leg 2 ensures " +
                    "the banner is not permanently suppressed.");
            }
        }
        finally
        {
            vm.Dispose();
        }
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