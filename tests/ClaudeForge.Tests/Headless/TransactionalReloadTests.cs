using System.Reflection;
using Avalonia.Headless;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// transactional-reload regression tests.
///
/// an external editor that
/// truncates-then-rewrites <c>settings.json</c> can briefly leave it as
/// invalid JSON; the file watcher fires, our reload runs, and a parse
/// failure must not corrupt the in-memory workspace.  These tests
/// exercise the contract directly via <see cref="MainWindowViewModel.LoadAllWorkspacesAsync"/>
/// (now <c>internal</c> for the H-3 headless harness) on a real
/// <see cref="PlatformPaths.TestUserProfileOverride"/> sandbox.
/// </summary>
[TestClass]
public sealed class TransactionalReloadTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_h1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);

        // Sandbox CC + DT path resolution into the temp dir so the test
        // doesn't read or mutate the user's real ~/.claude.
        PlatformPaths.TestUserProfileOverride = _sandbox;

        // Seed both products with a valid empty settings file so the
        // initial LoadAllWorkspacesAsync succeeds.
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
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch
        {
            /* best effort — file-system indexer may hold transient locks */
        }
    }

    private string CcSettingsPath => Path.Combine(_sandbox, ".claude", "settings.json");
    private string DtSettingsPath => PlatformPaths.DesktopConfigPath;

    /// <summary>
    /// Construct a real <see cref="MainWindowViewModel"/> against the
    /// sandboxed paths.  Caller invokes <see cref="MainWindowViewModel.LoadAllWorkspacesAsync"/>
    /// directly to drive the system under test.
    /// </summary>
    private static MainWindowViewModel BuildViewModel()
    {
        SchemaRegistry schemaRegistry = new(new HttpClient());
        NullDialogService dialog = new();
        return new MainWindowViewModel(schemaRegistry, dialog);
    }

    // ── H-1 contract tests ─────────────────────────────────────────────

    [TestMethod]
    public Task LoadAllWorkspacesAsync_ValidReload_SwapsSdkClients()
    {
        return Session.Dispatch(async () =>
        {
            // Sanity baseline: the happy-path reload still produces fresh
            // SDK clients.  Without this we can't tell whether subsequent
            // failure cases are revealing a regression vs an unrelated
            // construction issue.
            MainWindowViewModel vm = BuildViewModel();

            await vm.LoadAllWorkspacesAsync();
            ClaudeConfigClientCore? firstCc = vm.ClaudeCodeSdk;
            ClaudeConfigClientCore? firstDt = vm.ClaudeDesktopSdk;
            Assert.IsNotNull(firstCc);
            Assert.IsNotNull(firstDt);

            // Mutate the file (still valid JSON) and reload.
            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"sonnet"}""");
            await vm.LoadAllWorkspacesAsync();

            Assert.IsNotNull(vm.ClaudeCodeSdk);
            Assert.IsNotNull(vm.ClaudeDesktopSdk);
            Assert.AreNotSame(firstCc, vm.ClaudeCodeSdk,
                "Valid reload must produce a fresh CC SDK client.");
            Assert.AreNotSame(firstDt, vm.ClaudeDesktopSdk,
                "Valid reload must produce a fresh DT SDK client.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task LoadAllWorkspacesAsync_MalformedJson_KeepsExistingWorkspace()
    {
        return Session.Dispatch(async () =>
        {
            // Initial load succeeds with valid JSON.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            ClaudeConfigClientCore? origCc = vm.ClaudeCodeSdk;
            ClaudeConfigClientCore? origDt = vm.ClaudeDesktopSdk;
            Assert.IsNotNull(origCc);
            Assert.IsNotNull(origDt);

            // Simulate the external-editor truncate-then-rewrite race:
            // settings.json is briefly invalid JSON.  A file-watcher fire
            // would call LoadAllWorkspacesAsync — that call must catch the
            // JsonException internally and bail BEFORE swapping any SDKs.
            await File.WriteAllTextAsync(CcSettingsPath, """{"model": invalid""");

            await vm.LoadAllWorkspacesAsync();

            // SDK references unchanged after a failed
            // reload.  If LoadAllWorkspacesAsync had partially executed
            // ClaudeCodeSdk would have been disposed and replaced
            // with a fresh (empty) client — origCc would no longer be the
            // same reference.
            Assert.AreSame(origCc, vm.ClaudeCodeSdk,
                "Malformed JSON parse must NOT replace the in-memory CC SDK.");
            Assert.AreSame(origDt, vm.ClaudeDesktopSdk,
                "Malformed JSON parse must NOT replace the in-memory DT SDK either.");

            // Verify the user-facing status message reflects the failure.
            Assert.IsNotNull(vm.StatusMessage);
            StringAssert.Contains(vm.StatusMessage!, "settings.json",
                "StatusMessage should name the offending file.");
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task LoadAllWorkspacesAsync_OneProductValidOneMalformed_KeepsBoth()
    {
        return Session.Dispatch(async () =>
        {
            // Setup: both products initially valid; load succeeds.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            ClaudeConfigClientCore? origCc = vm.ClaudeCodeSdk;
            ClaudeConfigClientCore? origDt = vm.ClaudeDesktopSdk;

            // Now: CC is updated to a NEW valid value (would normally
            // trigger an SDK swap), AND DT is malformed.  The
            // transactional contract is all-or-nothing — if EITHER fails,
            // BOTH stay at their existing references.
            await File.WriteAllTextAsync(CcSettingsPath, """{"model":"sonnet"}""");
            await File.WriteAllTextAsync(DtSettingsPath, """{"mcpServers": invalid""");

            await vm.LoadAllWorkspacesAsync();

            Assert.AreSame(origCc, vm.ClaudeCodeSdk,
                "Even though CC is valid, DT's parse failure must roll back the swap.");
            Assert.AreSame(origDt, vm.ClaudeDesktopSdk,
                "DT must stay at its existing SDK reference.");
        }, CancellationToken.None);
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