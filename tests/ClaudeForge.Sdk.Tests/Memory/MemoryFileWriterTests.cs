using System.IO;
using System.Text;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Memory;

/// <summary>
/// Locks the atomic-write contract for artifact files (group #3): UTF-8
/// without BOM, replace-in-place for existing files, create for absent
/// ones, and content round-trips exactly.
/// </summary>
[TestClass]
public sealed class MemoryFileWriterTests
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "claudetest_writer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    public async Task WriteAsync_NewFile_CreatesWithExactContent()
    {
        string path = Path.Combine(_dir, "new.md");
        const string content = "---\nname: foo\n---\n\nBody line.\n";

        await MemoryFileWriter.WriteAsync(path, content, CancellationToken.None);

        Assert.IsTrue(File.Exists(path));
        Assert.AreEqual(content, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task WriteAsync_ExistingFile_ReplacesContent()
    {
        string path = Path.Combine(_dir, "existing.md");
        await File.WriteAllTextAsync(path, "OLD CONTENT");

        const string updated = "---\nname: bar\n---\n\nNew body.\n";
        await MemoryFileWriter.WriteAsync(path, updated, CancellationToken.None);

        Assert.AreEqual(updated, await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task WriteAsync_NoBom_Utf8()
    {
        string path = Path.Combine(_dir, "nobom.md");
        await MemoryFileWriter.WriteAsync(path, "name: foo\n", CancellationToken.None);

        byte[] bytes = await File.ReadAllBytesAsync(path);
        // UTF-8 BOM is EF BB BF — must NOT be present.
        bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.IsFalse(hasBom, "Artifact files must be written UTF-8 without a BOM.");
    }

    [TestMethod]
    public async Task WriteAsync_LeavesNoTempFilesBehind()
    {
        string path = Path.Combine(_dir, "clean.md");
        await MemoryFileWriter.WriteAsync(path, "x\n", CancellationToken.None);

        string[] leftovers = Directory.GetFiles(_dir, "*.tmp-*");
        Assert.AreEqual(0, leftovers.Length, "The temp swap file must not survive a successful write.");
    }
}
