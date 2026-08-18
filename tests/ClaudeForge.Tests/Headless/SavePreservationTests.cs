using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// End-to-end guard that a save driven through the <b>GUI's own</b> save path
/// preserves comments and formatting.
/// <para>
/// Phase 2's preservation guarantee is covered at the library level
/// (<c>AgentForge.Jsonc.Tests</c>) and at the loader level
/// (<c>ConfigFileLoaderPreservationTests</c>), but nothing exercised
/// <see cref="MainWindowViewModel"/>'s <c>SaveCoreAsync</c> — the path a user
/// actually triggers. <c>McpServersEditorRoundTripTests</c> comes closest and
/// explicitly only <i>mirrors</i> what that method does. The GUI leg carries real
/// wiring of its own: <c>SelectedConfigWriter()</c> resolving <c>--writer</c>,
/// <c>ClaudeCodeClient.FromExistingWorkspace</c> threading the writer in, and the
/// save-stamp header. This class covers that leg.
/// </para>
/// <para>
/// <b>Dispatch shape matters here.</b> These tests use the
/// <c>Dispatch&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt;, ct)</c> overload — the lambda
/// returns a value — because the far more common
/// <c>return Session.Dispatch(async () =&gt; { ... }, ct)</c> in this folder binds
/// to <c>Dispatch&lt;T&gt;(Func&lt;T&gt;, ct)</c> with <c>T = Task</c>. That returns
/// <c>Task&lt;Task&gt;</c>, the test framework awaits only the outer task, and every
/// assertion inside the lambda is silently unobserved — such a test cannot fail.
/// Awaiting once does not fix it either; the inner task must be unwrapped, which is
/// what returning a value from the lambda achieves. <b>Both tests below were
/// canaried with a deliberate failure to confirm they really do go red.</b>
/// </para>
/// </summary>
[TestClass]
public sealed class SavePreservationTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    /// <summary>
    /// A settings file with the trivia the JSONC writer exists to protect: a
    /// leading line comment, a block comment, a comment inside a nested object,
    /// a blank line, and a trailing comma (legal JSONC, illegal JSON — so a
    /// reader using default <see cref="JsonDocumentOptions"/> throws on it).
    /// </summary>
    private const string CommentedSettings =
        """
        {
          // Top-of-file comment that must survive a save.
          "model": "sonnet",

          /* Block comment above the value the test edits. */
          "cleanupPeriodDays": 90,
          "env": {
            // Comment inside a nested object.
            "FOO": "bar"
          },
          "permissions": {
            "defaultMode": "acceptEdits",
          }
        }
        """;

    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_save_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;

        string ccDir = Path.Combine(_sandbox, ".claude");
        Directory.CreateDirectory(ccDir);
        File.WriteAllText(CcSettingsPath, CommentedSettings);

        // Desktop must load too — LoadAllWorkspacesAsync is transactional across
        // both products, so a missing/invalid Desktop config would roll back the
        // swap and leave ClaudeCodeSdk null.
        string dtDir = Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!;
        Directory.CreateDirectory(dtDir);
        File.WriteAllText(PlatformPaths.DesktopConfigPath, "{}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        // DebugFlags is process-global static state; leaking --writer into the next
        // test would silently change how it saves.
        DebugFlags.ResetForTesting();
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

    [TestMethod]
    public async Task GuiSave_PreservesCommentsFormattingAndKeyOrder()
    {
        string after = await Session.Dispatch(
            async () =>
            {
                MainWindowViewModel vm = BuildViewModel();
                await vm.LoadAllWorkspacesAsync();
                Assert.IsNotNull(vm.ClaudeCodeSdk, "Precondition: the CC SDK client must be loaded.");

                // The load must not have collapsed the commented file to empty. This is
                // the data-loss bug Phase 2 fixed: a comment made LoadAsync throw, the
                // throw became an empty JsonObject, and the next save wrote that over
                // the user's file.
                Assert.AreEqual("sonnet", vm.ClaudeCodeSdk.GetEffective<string>("model"),
                    "The commented file must load with its values intact, not as empty.");

                vm.ClaudeCodeSdk.SetValue("cleanupPeriodDays", 45, ConfigScope.User);
                await vm.SaveForBackupOrRestoreAsync(isRestoreContext: false);

                return await File.ReadAllTextAsync(CcSettingsPath);
            },
            CancellationToken.None);

        Assert.AreEqual(45, TopLevelInt(after, "cleanupPeriodDays"),
            "The edit must actually reach disk, or the preservation assertions below are vacuous.");

        StringAssert.Contains(after, "// Top-of-file comment that must survive a save.",
            "The GUI save path must preserve line comments.");
        StringAssert.Contains(after, "/* Block comment above the value the test edits. */",
            "The GUI save path must preserve block comments.");
        StringAssert.Contains(after, "// Comment inside a nested object.",
            "A comment inside a nested object must survive editing a sibling key.");

        Assert.AreEqual(2, ChangedLineCount(CommentedSettings, after),
            "A one-value edit must rewrite exactly two meaningful lines (old value out, new "
            + "value in). More than that means the document was re-serialized rather than edited.");

        CollectionAssert.AreEqual(
            new[] { "model", "cleanupPeriodDays", "env", "permissions" },
            TopLevelKeys(after).Where(k => k != "//").ToArray(),
            "Key order must be preserved exactly.");
    }

    [TestMethod]
    public async Task GuiSave_WithWriterLegacy_IsLossy_SoTheHatchesCostIsMeasured()
    {
        // The contrast case. --writer legacy is a one-release escape hatch whose cost
        // should be documented rather than discovered, and this is the only test that
        // proves the flag is honoured through the GUI's own composition
        // (DebugFlags -> SelectedConfigWriter -> FromExistingWorkspace -> save) rather
        // than merely parsed. When the hatch is removed, delete this test with it.
        DebugFlags.Initialize(["--writer", "legacy"]);
        Assert.AreEqual("legacy", DebugFlags.ConfigWriterName, "Precondition: the flag must parse.");

        string after = await Session.Dispatch(
            async () =>
            {
                MainWindowViewModel vm = BuildViewModel();
                await vm.LoadAllWorkspacesAsync();
                Assert.IsNotNull(vm.ClaudeCodeSdk);

                vm.ClaudeCodeSdk.SetValue("cleanupPeriodDays", 45, ConfigScope.User);
                await vm.SaveForBackupOrRestoreAsync(isRestoreContext: false);

                return await File.ReadAllTextAsync(CcSettingsPath);
            },
            CancellationToken.None);

        Assert.AreEqual(45, TopLevelInt(after, "cleanupPeriodDays"),
            "The legacy writer must still write the value — it is lossy, not broken.");
        Assert.IsFalse(after.Contains("// Top-of-file comment", StringComparison.Ordinal),
            "--writer legacy re-serializes the whole document, so comments are expected to be "
            + "lost. If this now passes, the hatch no longer differs from the default and the "
            + "preservation test above may be passing for the wrong reason.");
    }

    [TestMethod]
    public async Task GuiSave_WritesEveryProductsChanges_NotJustTheFirstSection()
    {
        // Written because nothing covered it. Phase 4d replaced MainWindowViewModel's two
        // named SDK fields with a list of ProductSection, so save / validate / snapshot /
        // subscribe / dispose / export all became `foreach`. A canary that made every one of
        // those loops cover only the FIRST open section — silently one-product — passed all
        // 2,814 tests. Every other test in the suite exercises one product at a time.
        (string Cc, string Dt) after = await Session.Dispatch(
            async () =>
            {
                MainWindowViewModel vm = BuildViewModel();
                await vm.LoadAllWorkspacesAsync();

                Assert.AreEqual(2, vm.Sections.Count(s => s.Client is not null),
                    "Precondition: both product sections must be open, or a one-product save "
                    + "would satisfy the assertions below by default.");

                // One edit per product, then ONE save through the GUI's own path.
                vm.ClaudeCodeSdk!.SetValue("cleanupPeriodDays", 45, ConfigScope.User);
                vm.ClaudeDesktopSdk!.SetValue(
                    "preferences", new JsonObject { ["theme"] = "dark" }, ConfigScope.User);

                await vm.SaveForBackupOrRestoreAsync(isRestoreContext: false);

                return (await File.ReadAllTextAsync(CcSettingsPath),
                        await File.ReadAllTextAsync(PlatformPaths.DesktopConfigPath));
            },
            CancellationToken.None);

        Assert.AreEqual(45, TopLevelInt(after.Cc, "cleanupPeriodDays"),
            "Claude Code's edit must reach disk.");
        StringAssert.Contains(after.Dt, "\"theme\"",
            "Claude Desktop's edit must reach disk too. If only Claude Code's did, a lifecycle "
            + "loop is covering one section instead of all of them — which is exactly what the "
            + "two named fields this list replaced made easy to get wrong.");
    }

    [TestMethod]
    public async Task HasUnsavedChanges_TrueWhenOnlyTheLastSectionIsDirty()
    {
        // The counter-direction, and the cheaper half of the same hole: a "first product
        // only" read of dirtiness leaves Save disabled for a Desktop-only edit, because
        // Claude Code is first in the list and clean. Asserting on the LAST section makes
        // ordering load-bearing in the test rather than incidental.
        bool dirty = await Session.Dispatch(
            async () =>
            {
                MainWindowViewModel vm = BuildViewModel();
                await vm.LoadAllWorkspacesAsync();
                Assert.IsFalse(vm.HasUnsavedChanges, "Precondition: a fresh load is clean.");

                vm.Sections[^1].Client!.SetValue(
                    "preferences", new JsonObject { ["theme"] = "dark" }, ConfigScope.User);

                return vm.HasUnsavedChanges;
            },
            CancellationToken.None);

        Assert.IsTrue(dirty,
            "An edit to the last product section must mark the window dirty. If it does not, "
            + "the dirty check stops at the first section and the user cannot save the change.");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static MainWindowViewModel BuildViewModel()
    {
        return new MainWindowViewModel(new SchemaRegistry(new HttpClient()), new ConfirmingDialogService());
    }

    private static JsonDocumentOptions ReadOpts() => new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string[] TopLevelKeys(string json)
    {
        JsonNode? n = JsonNode.Parse(json, documentOptions: ReadOpts());
        return n is JsonObject o ? o.Select(kv => kv.Key).ToArray() : [];
    }

    private static int TopLevelInt(string json, string key)
    {
        JsonNode? n = JsonNode.Parse(json, documentOptions: ReadOpts());
        return n is JsonObject o && o.TryGetPropertyValue(key, out JsonNode? v) && v is not null
            ? v.GetValue<int>()
            : -1;
    }

    /// <summary>
    /// Lines that differ, by plain LCS edit distance. Two normalisations, both
    /// structural consequences of a <i>correct</i> minimal edit rather than losses:
    /// the save stamp carries a to-the-second timestamp, and appending a key
    /// necessarily puts a comma on the previously-last line. Neither can disguise a
    /// whole-document re-serialization, which moves every line including its
    /// indentation.
    /// </summary>
    private static int ChangedLineCount(string a, string b)
    {
        string[] x = Meaningful(a), y = Meaningful(b);
        int[,] lcs = new int[x.Length + 1, y.Length + 1];
        for (int i = x.Length - 1; i >= 0; i--)
        {
            for (int j = y.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = x[i] == y[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        return x.Length + y.Length - (2 * lcs[0, 0]);
    }

    private static string[] Meaningful(string s) =>
        s.Replace("\r\n", "\n", StringComparison.Ordinal)
         .Split('\n')
         .Where(l => !l.TrimStart().StartsWith("\"//\":", StringComparison.Ordinal))
         .Select(l => l.TrimEnd().TrimEnd(','))
         .ToArray();

    // ── Test doubles ────────────────────────────────────────────────────

    /// <summary>
    /// Answers "yes" to save confirmations. The shared <c>NullDialogService</c> copies
    /// in this folder return <see langword="false"/>, which aborts <c>SaveCoreAsync</c>
    /// before it writes anything — a save test using one would assert against an
    /// unmodified file and pass for the wrong reason.
    /// </summary>
    private sealed class ConfirmingDialogService : IDialogService
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
            return Task.FromResult<bool?>(true);
        }

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt)
        {
            return Task.FromResult(true);
        }
    }
}
