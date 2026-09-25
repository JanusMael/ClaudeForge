using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.FileIO;

/// <summary>
/// Covers <see cref="SettingsDocument.LoadFailure"/> — the flag that lets a caller tell "this
/// file is empty" from "this file could not be parsed".
/// <para>
/// <b>Why it exists.</b> <see cref="ConfigFileLoader.LoadAsync"/> deliberately does not throw
/// on a corrupt file; it degrades to an empty root so the editor survives, a contract pinned
/// by <c>ConfigFileLoaderTests</c>. But an empty root is indistinguishable from a genuinely
/// empty file, so the GUI's reload used to swap the placeholder into memory and the next save
/// wrote that emptiness over the user's real settings — the loader's own comment said as much.
/// This flag resolves the two contracts instead of choosing between them: the loader still
/// does not throw, and the caller that must be transactional can now see the failure.
/// </para>
/// <para>
/// These assertions are in Core deliberately. The GUI-side behaviour is pinned by
/// <c>TransactionalReloadTests</c>, but that suite is headless and was itself inert for the
/// whole life of the contract — a Core-level guard does not depend on the harness being
/// wired correctly.
/// </para>
/// </summary>
public sealed class ConfigFileLoadFailureTests : IDisposable
{
    private string _dir = string.Empty;

    public ConfigFileLoadFailureTests() => Setup();

    private void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cfl_fail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    private void Cleanup()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort — an indexer may hold a transient lock
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private async Task<SettingsDocument> LoadText(string contents)
    {
        string path = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(path, contents);
        return await ConfigFileLoader.LoadAsync(
            new DiscoveredFile(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                               Exists: true, IsReadOnly: false));
    }

    [Fact]
    public async Task MalformedJson_IsFlagged_ButStillLoadsAsEmpty()
    {
        SettingsDocument doc = await LoadText("""{"model": invalid""");

        MessageAssert.NotNull(doc.LoadFailure,
            "A file that could not be parsed must say so. Without this the caller cannot "
            + "distinguish it from an empty file, which is how an unparseable file used to be "
            + "swapped into memory and then saved over.");
        MessageAssert.Equal(0, doc.Root.Count,
            "The resilience contract still holds: a corrupt file degrades to an empty root "
            + "rather than throwing. Both contracts hold at once — that is the point.");
        MessageAssert.Null(doc.OriginalText,
            "No original text, so a save cannot attempt a surgical edit against content that "
            + "was never parsed.");
    }

    /// <summary>
    /// A writer holding the file open — the agent saving its own settings, an editor mid-save —
    /// must not turn a load into a failure, and the load must not lock the writer out either.
    /// </summary>
    /// <remarks>
    /// ⛔ The loader read through <c>File.ReadAllTextAsync</c>, which shares READ only, so on Windows
    /// the two collide in both directions: a load while a writer holds the file fails (this test),
    /// and a write while a load holds it fails "being used by another process" (measured: 28,987
    /// writer failures in 5 s against that read, 0 against this one). The sharing check is
    /// symmetric, so this deterministic direction guards both. ⓘ Only Windows enforces FileShare
    /// between processes; elsewhere this passes either way.
    /// </remarks>
    [Fact]
    public async Task AFileHeldOpenByAWriter_StillLoads()
    {
        string path = Path.Combine(_dir, "settings.json");
        await File.WriteAllTextAsync(path, """{"model":"sonnet"}""", TestContext.Current.CancellationToken);

        await using FileStream writer = new(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        SettingsDocument doc = await ConfigFileLoader.LoadAsync(
            new DiscoveredFile(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                               Exists: true, IsReadOnly: false),
            TestContext.Current.CancellationToken);

        MessageAssert.Null(doc.LoadFailure,
            "A writer holding the file is not a corrupt file: the load must read it, not report a failure.");
        Assert.Single(doc.Root);
    }

    [Fact]
    public async Task ValidJson_IsNotFlagged()
    {
        SettingsDocument doc = await LoadText("""{"model":"sonnet"}""");

        Assert.Null(doc.LoadFailure);
        Assert.Single(doc.Root);
    }

    /// <summary>
    /// The counter-direction that matters most: a genuinely empty file must NOT be flagged.
    /// If it were, every reload against a fresh install would bail and the app would never
    /// load — which is exactly the failure mode a naive "empty means broken" check produces.
    /// </summary>
    [Fact]
    public async Task GenuinelyEmptyObject_IsNotFlagged()
    {
        SettingsDocument doc = await LoadText("{}");

        Assert.Null(doc.LoadFailure);
        Assert.Empty(doc.Root);
    }

    [Fact]
    public async Task MissingFile_IsNotFlagged()
    {
        // A file that does not exist is not a failure — it is the normal state before the user
        // has ever saved. Flagging it would block the first-run load.
        SettingsDocument doc = await ConfigFileLoader.LoadAsync(
            new DiscoveredFile(ConfigScope.User, ConfigFileType.ClaudeCodeSettings,
                               Path.Combine(_dir, "absent.json"),
                               Exists: false, IsReadOnly: false), TestContext.Current.CancellationToken);

        Assert.Null(doc.LoadFailure);
        Assert.Empty(doc.Root);
    }

    /// <summary>
    /// A non-object root (e.g. a bare array) parses fine as JSON but is not usable as a
    /// settings document. It degrades to an empty root and is deliberately NOT flagged as a
    /// load failure — nothing was corrupt, the shape is simply wrong, and blocking reloads on
    /// it would strand the user with no way to fix the file from the app.
    /// </summary>
    [Fact]
    public async Task NonObjectRoot_IsNotFlagged()
    {
        SettingsDocument doc = await LoadText("[1, 2, 3]");

        Assert.Null(doc.LoadFailure);
        Assert.Empty(doc.Root);
    }

    // ── Workspace-level view, which is what the reload actually consults ──

    [Fact]
    public async Task FailedDocuments_NamesOnlyTheUnparseableFile()
    {
        string good = Path.Combine(_dir, "good.json");
        string bad = Path.Combine(_dir, "bad.json");
        await File.WriteAllTextAsync(good, """{"model":"sonnet"}""", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(bad, """{"permissions": """, TestContext.Current.CancellationToken);

        SettingsWorkspace ws = await ConfigFileLoader.LoadWorkspaceAsync(
            [
                new DiscoveredFile(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, good,
                                   Exists: true, IsReadOnly: false),
                new DiscoveredFile(ConfigScope.Project, ConfigFileType.ClaudeCodeSettings, bad,
                                   Exists: true, IsReadOnly: false),
            ],
            TestMergePolicy.Inferring, TestContext.Current.CancellationToken);

        MessageAssert.Equal(2, ws.Documents.Count, "Both documents still load — nothing throws.");
        MessageAssert.SequenceEqual(
            new[] { bad },
            ws.FailedDocuments.Select(d => d.FilePath).ToArray(),
            "Only the unparseable file is reported. A workspace over a corrupt file is "
            + "structurally valid and merely looks empty, which is precisely why the caller "
            + "has to ask rather than inspect the merged result.");
    }

    [Fact]
    public async Task FailedDocuments_IsEmpty_ForACleanLoad()
    {
        string good = Path.Combine(_dir, "good.json");
        await File.WriteAllTextAsync(good, """{"model":"sonnet"}""", TestContext.Current.CancellationToken);

        SettingsWorkspace ws = await ConfigFileLoader.LoadWorkspaceAsync(
            [
                new DiscoveredFile(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, good,
                                   Exists: true, IsReadOnly: false),
            ],
            TestMergePolicy.Inferring, TestContext.Current.CancellationToken);

        Assert.False(ws.FailedDocuments.Any());
    }
}
