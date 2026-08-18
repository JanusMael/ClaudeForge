using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using System.Reflection;
using Avalonia.Headless;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
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
/// <para>
/// ⚠⚠ <b>These tests could not fail until 2026-08-18.</b> Each was
/// <c>return Session.Dispatch(async () =&gt; …)</c>, which binds
/// <c>Dispatch&lt;T&gt;(Func&lt;T&gt;)</c> with <c>T = Task</c> and yields
/// <c>Task&lt;Task&gt;</c>. MSTest awaited only the outer task, so every assertion inside was
/// unobserved. They now return a value from the lambda so it binds
/// <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;)</c>, and were canaried with a deliberate
/// <c>Assert.Fail</c> to prove they can fail.
/// </para>
/// <para>
/// ⚠ <b>Two of the three then failed for real, and the defect is pre-existing — not a
/// Phase 1–4 regression.</b> <c>ConfigFileLoader.LoadAsync</c> catches <c>JsonException</c>
/// and returns an empty <c>JsonObject</c> (see its own comment: "a subsequent save will
/// overwrite the file's current contents"). So <c>LoadAllWorkspacesAsync</c>'s PHASE 1 never
/// observes a parse failure and always proceeds to the destructive swap it labels
/// "no throw points past here". The contract asserted below has therefore never held at any
/// point in this repo's history.
/// </para>
/// <para>
/// <b>It also conflicts with a contract pinned elsewhere.</b>
/// <c>ConfigFileLoaderTests</c> asserts the opposite resilience guarantee — a corrupt file
/// must degrade to an empty-root document rather than crash. Both cannot hold as written;
/// resolving that is a deliberate decision, so the two failing tests are
/// <c>[Ignore]</c>d with the diagnosis rather than deleted or weakened.
/// </para>
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
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
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
    public async Task LoadAllWorkspacesAsync_ValidReload_SwapsSdkClients()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Sanity baseline: the happy-path reload still produces fresh
            // SDK clients.  Without this we can't tell whether subsequent
            // failure cases are revealing a regression vs an unrelated
            // construction issue.
            MainWindowViewModel vm = BuildViewModel();

            await vm.LoadAllWorkspacesAsync();
            AgentConfigClientCore? firstCc = vm.ClaudeCodeSdk;
            AgentConfigClientCore? firstDt = vm.ClaudeDesktopSdk;
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
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task LoadAllWorkspacesAsync_MalformedJson_KeepsExistingWorkspace()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Initial load succeeds with valid JSON.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            AgentConfigClientCore? origCc = vm.ClaudeCodeSdk;
            AgentConfigClientCore? origDt = vm.ClaudeDesktopSdk;
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
            return true;
        }, CancellationToken.None);

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public async Task LoadAllWorkspacesAsync_OneProductValidOneMalformed_KeepsBoth()
    {
        bool ran = await Session.Dispatch(async () =>
        {
            // Setup: both products initially valid; load succeeds.
            MainWindowViewModel vm = BuildViewModel();
            await vm.LoadAllWorkspacesAsync();
            AgentConfigClientCore? origCc = vm.ClaudeCodeSdk;
            AgentConfigClientCore? origDt = vm.ClaudeDesktopSdk;

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