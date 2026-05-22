using Bennewitz.Ninja.ClaudeForge.Core.FileIO;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.FileIO;

[TestClass]
public class ConfigFileLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_NonExistentFile_ReturnsEmptyRoot()
    {
        DiscoveredFile file = new(
            ConfigScope.User,
            ConfigFileType.ClaudeCodeSettings,
            Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json"),
            Exists: false,
            IsReadOnly: false);

        SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);

        Assert.AreEqual(0, doc.Root.Count);
        Assert.IsFalse(doc.IsDirty);
    }

    [TestMethod]
    public async Task LoadAsync_ValidJson_ParsesCorrectly()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """{"model":"sonnet","cleanupPeriodDays":30}""");

            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);

            Assert.AreEqual("sonnet", doc.Root["model"]!.GetValue<string>());
            Assert.AreEqual(30, doc.Root["cleanupPeriodDays"]!.GetValue<int>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SaveAsync_WritesIndentedJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.json");
        try
        {
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: false, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);
            doc.Root["model"] = "opus";

            await ConfigFileLoader.SaveAsync(doc);

            string written = await File.ReadAllTextAsync(path);
            Assert.IsTrue(written.Contains('\n'), "Expected indented JSON with newlines.");
            Assert.IsTrue(written.Contains("opus"));
            Assert.IsFalse(doc.IsDirty);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public async Task SaveAsync_ReadOnlyDoc_Throws()
    {
        DiscoveredFile file = new(
            ConfigScope.Managed, ConfigFileType.ClaudeCodeSettings, "/some/path.json",
            Exists: false, IsReadOnly: true);

        SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            ConfigFileLoader.SaveAsync(doc));
    }

    // ── LoadAsync error / edge paths + workspace helpers ──

    [TestMethod]
    public async Task LoadAsync_CorruptJson_ReturnsEmptyRoot_NotCrash()
    {
        // Resilience contract: a hand-corrupted settings file must not crash
        // the editor on startup. The catch-block in LoadAsync turns parse
        // failures into an empty-root document; the editor surfaces as
        // "settings appear empty" rather than dying.
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "this { is { not / valid JSON");
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);

            Assert.AreEqual(0, doc.Root.Count);
            Assert.IsFalse(doc.IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task LoadAsync_NonObjectRootJson_ReturnsEmptyRoot()
    {
        // `42` is valid JSON but not a JsonObject; the loader must coerce
        // to an empty root rather than handing the editor a bare number.
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "42");
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);

            Assert.AreEqual(0, doc.Root.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task LoadAsync_ArrayRootJson_ReturnsEmptyRoot()
    {
        // Same coercion contract for a JsonArray root.
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "[1, 2, 3]");
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);

            Assert.AreEqual(0, doc.Root.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
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
                """{"//":"ClaudeForge wrote this on 2026-05-05","model":"sonnet"}""");
            DiscoveredFile file = new(
                ConfigScope.User, ConfigFileType.ClaudeCodeSettings, path,
                Exists: true, IsReadOnly: false);

            SettingsDocument doc = await ConfigFileLoader.LoadAsync(file);

            Assert.IsFalse(doc.Root.ContainsKey("//"),
                "Tool-written metadata stamp must be stripped on load.");
            Assert.AreEqual("sonnet", doc.Root["model"]!.GetValue<string>(),
                "Real settings keys must survive the strip.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task LoadWorkspaceAsync_LoadsAllFiles_InOrder()
    {
        string pathA = Path.Combine(Path.GetTempPath(), $"wA_{Guid.NewGuid()}.json");
        string pathB = Path.Combine(Path.GetTempPath(), $"wB_{Guid.NewGuid()}.json");
        try
        {
            await File.WriteAllTextAsync(pathA, """{"a":1}""");
            await File.WriteAllTextAsync(pathB, """{"b":2}""");

            List<DiscoveredFile> files =
            [
                new(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, pathA, true, false),
                new(ConfigScope.Project, ConfigFileType.ClaudeCodeSettings, pathB, true, false),
            ];

            SettingsWorkspace workspace = await ConfigFileLoader.LoadWorkspaceAsync(files);

            Assert.AreEqual(2, workspace.Documents.Count);
            // Documents iterate in priority order. Project (= 2) outranks
            // User (= 3) per the merge-engine convention, so Project comes
            // first when sorted highest-priority-first.
            Assert.AreEqual(ConfigScope.Project, workspace.Documents[0].Scope);
            Assert.AreEqual(ConfigScope.User, workspace.Documents[1].Scope);
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

    [TestMethod]
    public async Task SaveDirtyAsync_OnlyWritesDirtyDocuments()
    {
        string pathA = Path.Combine(Path.GetTempPath(), $"sA_{Guid.NewGuid()}.json");
        string pathB = Path.Combine(Path.GetTempPath(), $"sB_{Guid.NewGuid()}.json");
        try
        {
            // A starts existing on disk, B does not.
            await File.WriteAllTextAsync(pathA, """{"existing":"value"}""");

            List<DiscoveredFile> files =
            [
                new(ConfigScope.User, ConfigFileType.ClaudeCodeSettings, pathA, true, false),
                new(ConfigScope.Project, ConfigFileType.ClaudeCodeSettings, pathB, false, false),
            ];
            SettingsWorkspace workspace = await ConfigFileLoader.LoadWorkspaceAsync(files);

            // Mutate ONLY document A — B remains clean.
            SettingsDocument docA = workspace.Documents.Single(d => d.Scope == ConfigScope.User);
            docA.Root["new"] = "set";
            docA.MarkDirty();

            await ConfigFileLoader.SaveDirtyAsync(workspace);

            // A should be written with the new value.
            string aText = await File.ReadAllTextAsync(pathA);
            StringAssert.Contains(aText, "\"new\"");

            // B should NOT have been written — the file should still not exist.
            Assert.IsFalse(File.Exists(pathB),
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