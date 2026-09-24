using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.FileIO;

/// <summary>
/// End-to-end proof that Phase 2 delivered: load a hand-formatted, commented config,
/// change one value, save, and check that only that value moved.
/// </summary>
/// <remarks>
/// The library has its own unit tests, but they exercise <c>JsoncEditor</c> directly. These
/// go through the real <see cref="ConfigFileLoader"/> path — read, parse, diff against the
/// baseline, render, atomic write — because that is the path a user's file actually takes,
/// and every one of those steps had to cooperate for the formatting to survive.
/// </remarks>
public sealed class ConfigFileLoaderPreservationTests : IDisposable
{
    private string _sandbox = null!;

    public ConfigFileLoaderPreservationTests() => Init();

    private void Init()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), $"cfl-preserve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sandbox);
    }

    private void Cleanup()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient lock; leave it for the OS reaper rather than failing the test.
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private async Task<(string Path, SettingsDocument Doc)> LoadFixtureAsync(string content)
    {
        string path = Path.Combine(_sandbox, "settings.json");
        await File.WriteAllTextAsync(path, content);

        DiscoveredFile file = new(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                                  Exists: true, IsReadOnly: false);
        return (path, await ConfigFileLoader.LoadAsync(file));
    }

    /// <summary>
    /// The headline promise. Before Phase 2 this file came back re-serialized: comments
    /// gone, blank line gone, tabs turned into two spaces.
    /// </summary>
    [Fact]
    public async Task EditingOneValue_LeavesCommentsBlankLinesAndTabsIntact()
    {
        const string original = "{\n"
                                + "\t// pinned deliberately — see the team decision doc\n"
                                + "\t\"model\": \"sonnet\",\n"
                                + "\n"
                                + "\t/* the permissions block is reviewed quarterly */\n"
                                + "\t\"permissions\": {\n"
                                + "\t\t\"defaultMode\": \"ask\"\n"
                                + "\t}\n"
                                + "}";

        (string path, SettingsDocument doc) = await LoadFixtureAsync(original);

        doc.Root["model"] = "opus";
        await ConfigFileLoader.SaveAsync(doc);

        string after = await File.ReadAllTextAsync(path);

        MessageAssert.Equal(original.Replace("\"sonnet\"", "\"opus\""), after,
                        "Only the edited value's span should differ from the original.");
        Assert.Contains("// pinned deliberately", after);
        Assert.Contains("/* the permissions block is reviewed quarterly */", after);
    }

    [Fact]
    public async Task EditingANestedValue_DoesNotDisturbTheSurroundingObject()
    {
        const string original = "{\n"
                                + "  \"permissions\": {\n"
                                + "    // keep this note\n"
                                + "    \"defaultMode\": \"ask\",\n"
                                + "    \"allow\": []\n"
                                + "  }\n"
                                + "}";

        (string path, SettingsDocument doc) = await LoadFixtureAsync(original);

        // Mutating through the object model the way SettingsWorkspace does for a nested
        // path: replace the whole "permissions" object with an edited clone. The writer's
        // diff must still narrow that down to the one leaf that changed.
        System.Text.Json.Nodes.JsonObject permissions =
            (System.Text.Json.Nodes.JsonObject)doc.Root["permissions"]!.DeepClone();
        permissions["defaultMode"] = "acceptEdits";
        doc.Root["permissions"] = permissions;

        await ConfigFileLoader.SaveAsync(doc);

        string after = await File.ReadAllTextAsync(path);

        MessageAssert.Equal(original.Replace("\"ask\"", "\"acceptEdits\""), after,
                        "Replacing the parent object in memory must still produce a leaf-level "
                        + "edit on disk — otherwise the comment inside it would be destroyed.");
        Assert.Contains("// keep this note", after);
    }

    /// <summary>
    /// A file with a comment used to load as <i>empty</i> and then get overwritten. This is
    /// the data-loss path, tested from the outside.
    /// </summary>
    [Fact]
    public async Task CommentedFile_LoadsItsRealContent_NotAnEmptyDocument()
    {
        const string original = "{\n  // a comment\n  \"model\": \"sonnet\",\n  \"verbose\": true\n}";

        (_, SettingsDocument doc) = await LoadFixtureAsync(original);

        MessageAssert.Equal(2, doc.Root.Count,
                        "A commented file must load its real keys. Loading it as empty is what "
                        + "let the next save destroy the user's config.");
        Assert.Equal("sonnet", doc.Root["model"]!.GetValue<string>());
        Assert.True(doc.Root["verbose"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TrailingCommaFile_AlsoLoadsItsRealContent()
    {
        const string original = "{\n  \"model\": \"sonnet\",\n}";

        (_, SettingsDocument doc) = await LoadFixtureAsync(original);

        Assert.Single(doc.Root);
        Assert.Equal("sonnet", doc.Root["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task RemovingAKey_TakesItsSeparatorWithIt_AndLeavesValidJson()
    {
        const string original = "{\n  \"model\": \"sonnet\",\n  \"verbose\": true\n}";

        (string path, SettingsDocument doc) = await LoadFixtureAsync(original);

        doc.Root.Remove("verbose");
        await ConfigFileLoader.SaveAsync(doc);

        string after = await File.ReadAllTextAsync(path);

        Assert.Equal("{\n  \"model\": \"sonnet\"\n}", after);
    }

    /// <summary>
    /// The escape hatch does what it says: the same edit through the legacy writer
    /// re-serializes and loses the comment.
    /// </summary>
    /// <remarks>
    /// Asserting the fallback is <i>lossy</i> rather than skipping it: this is what
    /// <c>--writer legacy</c> costs, and a user who reaches for it should have that
    /// documented and verified rather than discovering it. It also means the preservation
    /// tests above cannot pass by accident — if both writers behaved the same, this test
    /// would fail.
    /// </remarks>
    [Fact]
    public async Task LegacyWriter_StillReSerializes_SoTheContrastIsExplicit()
    {
        const string original = "{\n\t// this will not survive the legacy writer\n\t\"model\": \"sonnet\"\n}";

        (string path, SettingsDocument doc) = await LoadFixtureAsync(original);

        doc.Root["model"] = "opus";
        await ConfigFileLoader.SaveAsync(doc, writer: new LegacySerializingWriter());

        string after = await File.ReadAllTextAsync(path);

        Assert.False(after.Contains("this will not survive", StringComparison.Ordinal),
                       "The legacy writer is lossy by construction — that is why it is the "
                       + "fallback and not the default.");
        Assert.Contains("\"opus\"", after);
    }
}
