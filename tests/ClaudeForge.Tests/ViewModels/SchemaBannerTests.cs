using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// Integration tests for the post-reload schema-violation banner
/// surfaced by <see cref="MainWindowViewModel.SchemaErrors"/> +
/// <see cref="MainWindowViewModel.HasSchemaErrors"/>.
/// </summary>
/// <remarks>
/// These tests sandbox <c>~/.claude/</c> via
/// <see cref="PlatformPaths.TestUserProfileOverride"/> and seed an
/// out-of-schema property under the real Claude Code settings schema; the
/// SDK client's <c>ValidateAsync</c> wires the result into the VM at the
/// end of <c>LoadAllWorkspacesAsync</c>.  No specialised fakes — we want the
/// integration with the real schema-validation pipeline.
/// </remarks>
public sealed class SchemaBannerTests : IDisposable
{
    private string _sandbox = null!;

    public SchemaBannerTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        Directory.CreateDirectory(Path.Combine(_sandbox, ".claude"));
        PlatformPaths.TestUserProfileOverride = _sandbox;
    }

    private void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        if (!Directory.Exists(_sandbox))
        {
            return;
        }

        try
        {
            Directory.Delete(_sandbox, recursive: true);
        }
        catch (IOException)
        {
            /* schema cache may briefly hold a lock — temp dir is OS-cleaned */
        }
        catch (UnauthorizedAccessException)
        {
            /* same */
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Setting <see cref="MainWindowViewModel.SchemaErrors"/> to a non-empty
    /// list flips <see cref="MainWindowViewModel.HasSchemaErrors"/> to true
    /// and updates the banner text and the show-details command's
    /// CanExecute.  This is the property contract the banner AXAML binds to.
    /// </summary>
    [Fact]
    public void SchemaErrors_Setter_DrivesHasSchemaErrors_BannerText_AndCanExecute()
    {
        using MainWindowViewModel vm = new(ClaudeEnvironment.Empty, new SchemaRegistry(), new NullDialogService());

        Assert.False(vm.HasSchemaErrors, "Empty list must report no errors.");
        Assert.False(vm.ShowSchemaErrorsCommand.CanExecute(null),
            "Show-details command must be disabled when there are no errors.");

        vm.SchemaErrors =
        [
            new SchemaValidationError("settings.json", "/permissions/gestate",
                "Property 'gestate' is not allowed."),
            new SchemaValidationError("settings.json", "/model",
                "Value must be one of: sonnet, opus."),
        ];

        Assert.True(vm.HasSchemaErrors, "Non-empty list must flip HasSchemaErrors true.");
        Assert.True(vm.ShowSchemaErrorsCommand.CanExecute(null),
            "Show-details command must enable when errors are present.");
        MessageAssert.Contains("2", vm.SchemaErrorsBannerText,
            "Banner headline must include the error count.");
    }

    /// <summary>
    /// Clearing <see cref="MainWindowViewModel.SchemaErrors"/> back to an
    /// empty list (e.g. user fixed the file externally and reloaded)
    /// re-disables the banner without leaving the command stuck enabled.
    /// </summary>
    [Fact]
    public void SchemaErrors_ClearedToEmpty_RestoresClean()
    {
        using MainWindowViewModel vm = new(ClaudeEnvironment.Empty, new SchemaRegistry(), new NullDialogService());

        vm.SchemaErrors =
            [new SchemaValidationError("settings.json", "/x", "y")];
        Assert.True(vm.HasSchemaErrors);

        vm.SchemaErrors = [];

        Assert.False(vm.HasSchemaErrors,
            "Resetting to an empty list must hide the banner.");
        Assert.False(vm.ShowSchemaErrorsCommand.CanExecute(null),
            "Resetting to an empty list must disable the show-details command.");
    }

    /// <summary>
    ///  an out-of-schema
    /// property in <c>~/.claude/settings.json</c> must populate
    /// <see cref="MainWindowViewModel.SchemaErrors"/> after
    /// <c>InitializeCommand</c> runs (which calls
    /// <c>LoadAllWorkspacesAsync</c> internally).
    /// </summary>
    [Fact]
    public async Task PostReload_ValidationFindsOutOfSchemaKey_PopulatesBanner()
    {
        // Seed a settings.json with an unknown key under permissions.
        // The Claude Code schema constrains permissions to known sub-keys
        // (allow, deny, ask, defaultMode, …); "gestate" is rejected.
        string settingsPath = Path.Combine(_sandbox, ".claude", "settings.json");
        await File.WriteAllTextAsync(settingsPath, """
                                                   {
                                                       "permissions": {
                                                           "gestate": [],
                                                           "allow": ["Read"]
                                                       }
                                                   }
                                                   """);

        MainWindowViewModel vm = new(ClaudeEnvironment.Empty, new SchemaRegistry(), new NullDialogService());
        try
        {
            await vm.InitializeCommand.ExecuteAsync(null);

            // Validation runs at the end of LoadAllWorkspacesAsync; the SDK
            // client emits at least one violation for the invalid key.
            Assert.True(vm.HasSchemaErrors,
                "An out-of-schema property in the seeded settings must surface as a banner.");
            Assert.True(vm.SchemaErrors.Any(e =>
                    e.InstancePath.Contains("gestate", StringComparison.OrdinalIgnoreCase)),
                "At least one validation error must reference the offending 'gestate' property.");
        }
        finally
        {
            (vm as IDisposable)?.Dispose();
        }
    }

    // ── Test plumbing ────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IDialogService"/> stub — only used to construct the
    /// VM; none of these tests actually drive a dialog.
    /// </summary>
    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null)
        {
            return Task.FromResult<string?>(null);
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

        public Task ShowAlertAsync(string title, string message)
        {
            return Task.CompletedTask;
        }

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<bool?> ShowConfirmAsync(string title, string message,
                                            string confirmLabel = "Confirm",
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