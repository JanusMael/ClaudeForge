using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.FileIO;

public class ConfigFileLoaderTests
{
    [Fact]
    public async Task LoadAsync_NonExistentFile_ReturnsEmptyRoot()
    {
        DiscoveredFile file = new(
            ConfigScope.User,
            ConfigFileType.ClaudeCodeSettings,
            Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json"),
            Exists: false,
            IsReadOnly: false);

        SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);

        Assert.Empty(doc.Root);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task LoadAsync_ValidJson_ParsesCorrectly()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"model":"sonnet","cleanupPeriodDays":30}""", TestContext.Current.CancellationToken);

            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);

            Assert.Equal("sonnet", doc.Root["model"]!.GetValue<string>());
            Assert.Equal(30, doc.Root["cleanupPeriodDays"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsync_WritesIndentedJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.json");
        try
        {
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: false, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);
            doc.Root["model"] = "opus";

            await ConfigFileLoader.SaveAsync(doc, ct: TestContext.Current.CancellationToken);

            string written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.True(written.Contains('\n'), "Expected indented JSON with newlines.");
            OrdinalAssert.Contains("opus", written);
            Assert.False(doc.IsDirty);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SaveAsync_ReadOnlyDoc_Throws()
    {
        DiscoveredFile file = new(
            ConfigScope.Managed, ConfigFileType.ClaudeCodeSettings, "/some/path.json",
            Exists: false, IsReadOnly: true);

        SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConfigFileLoader.SaveAsync(doc, ct: TestContext.Current.CancellationToken));
    }

    // ── LoadAsync error / edge paths + workspace helpers ──

    [Fact]
    public async Task LoadAsync_CorruptJson_ReturnsEmptyRoot_NotCrash()
    {
        // Resilience contract: a hand-corrupted settings file must not crash
        // the editor on startup. The catch-block in LoadAsync turns parse
        // failures into an empty-root document; the editor surfaces as
        // "settings appear empty" rather than dying.
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "this { is { not / valid JSON", TestContext.Current.CancellationToken);
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);

            Assert.Empty(doc.Root);
            Assert.False(doc.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_NonObjectRootJson_ReturnsEmptyRoot()
    {
        // `42` is valid JSON but not a JsonObject; the loader must coerce
        // to an empty root rather than handing the editor a bare number.
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "42", TestContext.Current.CancellationToken);
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);

            Assert.Empty(doc.Root);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_ArrayRootJson_ReturnsEmptyRoot()
    {
        // Same coercion contract for a JsonArray root.
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "[1, 2, 3]", TestContext.Current.CancellationToken);
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);

            Assert.Empty(doc.Root);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_StripsMetadataStamp_FromRoot()
    {
        // ConfigFileLoader.SaveAsync writes a "//" tool-stamp comment to
        // the top of every saved file. LoadAsync must strip it on
        // re-read so the editor doesn't surface it as a real setting
        // (and so the next save replaces it with a fresh timestamp
        // rather than treating it as an inherited value).
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path,
                """{"//":"ClaudeForge wrote this on 2026-05-05","model":"sonnet"}""", TestContext.Current.CancellationToken);
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file, TestContext.Current.CancellationToken);

            Assert.False(doc.Root.ContainsKey("//"),
                "Tool-written metadata stamp must be stripped on load.");
            MessageAssert.Equal("sonnet", doc.Root["model"]!.GetValue<string>(),
                "Real settings keys must survive the strip.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadWorkspaceAsync_LoadsAllFiles_InOrder()
    {
        string pathA = Path.Combine(Path.GetTempPath(), $"wA_{Guid.NewGuid()}.json");
        string pathB = Path.Combine(Path.GetTempPath(), $"wB_{Guid.NewGuid()}.json");
        try
        {
            await File.WriteAllTextAsync(pathA, """{"a":1}""", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(pathB, """{"b":2}""", TestContext.Current.CancellationToken);

            List<DiscoveredFile> files =
            [
                new(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, pathA, true, false),
                new(ConfigScope.Project, ConfigFileType.ClaudeCodeSettings, pathB, true, false),
            ];

            SettingsWorkspace workspace = await ConfigFileLoader.LoadWorkspaceAsync(files, TestMergePolicy.Inferring, TestContext.Current.CancellationToken);

            Assert.Equal(2, workspace.Documents.Count);
            // Documents iterate in priority order. Project (= 2) outranks
            // User (= 3) per the merge-engine convention, so Project comes
            // first when sorted highest-priority-first.
            Assert.Equal(ConfigScope.Project, workspace.Documents[0].Scope);
            Assert.Equal(ConfigScope.User, workspace.Documents[1].Scope);
        }
        finally
        {
            if (File.Exists(pathA))
            {
                File.Delete(pathA);
            }

            if (File.Exists(pathB))
            {
                File.Delete(pathB);
            }
        }
    }

    [Fact]
    public async Task SaveDirtyAsync_OnlyWritesDirtyDocuments()
    {
        string pathA = Path.Combine(Path.GetTempPath(), $"sA_{Guid.NewGuid()}.json");
        string pathB = Path.Combine(Path.GetTempPath(), $"sB_{Guid.NewGuid()}.json");
        try
        {
            // A starts existing on disk, B does not.
            await File.WriteAllTextAsync(pathA, """{"existing":"value"}""", TestContext.Current.CancellationToken);

            List<DiscoveredFile> files =
            [
                new(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, pathA, true, false),
                new(ConfigScope.Project, ConfigFileType.ClaudeCodeSettings, pathB, false, false),
            ];
            SettingsWorkspace workspace = await ConfigFileLoader.LoadWorkspaceAsync(files, TestMergePolicy.Inferring, TestContext.Current.CancellationToken);

            // Mutate ONLY document A — B remains clean.
            SettingsDocument docA = workspace.Documents.Single(d => d.Scope == ConfigScope.User);
            docA.Root["new"] = "set";
            docA.MarkDirty();

            await ConfigFileLoader.SaveDirtyAsync(workspace, ct: TestContext.Current.CancellationToken);

            // A should be written with the new value.
            string aText = await File.ReadAllTextAsync(pathA, TestContext.Current.CancellationToken);
            OrdinalAssert.Contains("\"new\"", aText);

            // B should NOT have been written — the file should still not exist.
            Assert.False(File.Exists(pathB),
                "Clean documents must NOT be persisted by SaveDirtyAsync.");
        }
        finally
        {
            if (File.Exists(pathA))
            {
                File.Delete(pathA);
            }

            if (File.Exists(pathB))
            {
                File.Delete(pathB);
            }
        }
    }
}