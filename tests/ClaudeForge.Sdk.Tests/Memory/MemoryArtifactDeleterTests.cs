using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Tests.Memory;

/// <summary>
/// Locks the shared artifact-deletion helper used by both the Memory page and
/// the Agents &amp; Skills page: a plain file is removed by itself; a skill is
/// removed as its WHOLE directory (SKILL.md + sibling assets); a mis-tagged
/// skill flag never recursively wipes an unexpected directory; and
/// <see cref="MemoryArtifactDeleter.StatTarget"/> reports the honest count + size
/// the confirm dialog cites.
/// </summary>
[TestClass]
public sealed class MemoryArtifactDeleterTests
{
    private string _sandbox = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_deleter_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    [TestCleanup]
    public void Cleanup()
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
            _ = ex;
        }
    }

    private string Write(string relPath, string content)
    {
        string full = Path.Combine(_sandbox, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    // ── DeleteAsync ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task DeleteAsync_PlainFile_RemovesOnlyThatFile()
    {
        string agent = Write(Path.Combine("agents", "reviewer.md"), "x");
        string sibling = Write(Path.Combine("agents", "other.md"), "y");

        string removed = await MemoryArtifactDeleter.DeleteAsync(agent, isSkill: false, CancellationToken.None);

        Assert.AreEqual(agent, removed);
        Assert.IsFalse(File.Exists(agent), "Target file must be deleted.");
        Assert.IsTrue(File.Exists(sibling), "Neighbouring files must be untouched.");
    }

    [TestMethod]
    public async Task DeleteAsync_Skill_RemovesWholeDirectoryIncludingSiblingAssets()
    {
        string skillMd = Write(Path.Combine("skills", "pdf", "SKILL.md"), "---\nname: pdf\n---\n");
        Write(Path.Combine("skills", "pdf", "scripts", "run.py"), "print('hi')");
        Write(Path.Combine("skills", "pdf", "reference.md"), "ref");
        string otherSkill = Write(Path.Combine("skills", "docx", "SKILL.md"), "---\nname: docx\n---\n");
        string skillDir = Path.Combine(_sandbox, "skills", "pdf");

        string removed = await MemoryArtifactDeleter.DeleteAsync(skillMd, isSkill: true, CancellationToken.None);

        Assert.AreEqual(skillDir, removed, "A skill delete removes (and returns) the whole skill directory.");
        Assert.IsFalse(Directory.Exists(skillDir), "Skill directory + all assets must be gone.");
        Assert.IsTrue(File.Exists(otherSkill), "Sibling skills must be untouched.");
    }

    [TestMethod]
    public async Task DeleteAsync_SkillFlagButFileNotSkillMd_DeletesOnlyTheFile()
    {
        // Defence in depth: isSkill=true but the path isn't a SKILL.md → only the
        // single file is removed, never the parent directory.
        string file = Write(Path.Combine("skills", "pdf", "notes.md"), "x");
        string skillMd = Write(Path.Combine("skills", "pdf", "SKILL.md"), "y");
        string skillDir = Path.Combine(_sandbox, "skills", "pdf");

        string removed = await MemoryArtifactDeleter.DeleteAsync(file, isSkill: true, CancellationToken.None);

        Assert.AreEqual(file, removed);
        Assert.IsFalse(File.Exists(file));
        Assert.IsTrue(Directory.Exists(skillDir), "Parent directory must NOT be wiped for a non-SKILL.md path.");
        Assert.IsTrue(File.Exists(skillMd));
    }

    [TestMethod]
    public async Task DeleteAsync_NotSkillButSkillMdPath_DeletesOnlyTheFile()
    {
        // isSkill=false on a SKILL.md → single-file delete, directory survives.
        string skillMd = Write(Path.Combine("skills", "pdf", "SKILL.md"), "y");
        Write(Path.Combine("skills", "pdf", "keep.txt"), "z");
        string skillDir = Path.Combine(_sandbox, "skills", "pdf");

        await MemoryArtifactDeleter.DeleteAsync(skillMd, isSkill: false, CancellationToken.None);

        Assert.IsFalse(File.Exists(skillMd));
        Assert.IsTrue(Directory.Exists(skillDir), "Without the skill flag only the file is removed.");
    }

    [TestMethod]
    public async Task DeleteAsync_MissingPath_NoThrow()
    {
        string ghost = Path.Combine(_sandbox, "agents", "ghost.md");
        string removed = await MemoryArtifactDeleter.DeleteAsync(ghost, isSkill: false, CancellationToken.None);
        Assert.AreEqual(ghost, removed, "Deleting a non-existent file is a no-op that returns the path.");
    }

    [TestMethod]
    public async Task DeleteAsync_BlankPath_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => MemoryArtifactDeleter.DeleteAsync("   ", isSkill: false, CancellationToken.None));
    }

    // ── StatTarget ───────────────────────────────────────────────────────

    [TestMethod]
    public void StatTarget_PlainFile_ReturnsSelfCountOneAndKnownSize()
    {
        string agent = Write(Path.Combine("agents", "reviewer.md"), "x");

        (string path, int count, long bytes) = MemoryArtifactDeleter.StatTarget(agent, isSkill: false, knownSize: 123);

        Assert.AreEqual(agent, path);
        Assert.AreEqual(1, count);
        Assert.AreEqual(123, bytes, "A plain file reports the caller's known size, no walk.");
    }

    [TestMethod]
    public void StatTarget_Skill_WalksDirectoryForRealCountAndBytes()
    {
        string skillMd = Write(Path.Combine("skills", "pdf", "SKILL.md"), "ab");      // 2 bytes
        Write(Path.Combine("skills", "pdf", "scripts", "run.py"), "abcde");           // 5 bytes
        string skillDir = Path.Combine(_sandbox, "skills", "pdf");

        (string path, int count, long bytes) = MemoryArtifactDeleter.StatTarget(skillMd, isSkill: true, knownSize: 2);

        Assert.AreEqual(skillDir, path, "Skill stats target the directory, not SKILL.md.");
        Assert.AreEqual(2, count, "Both files under the skill directory are counted.");
        Assert.AreEqual(7, bytes, "Size is the recursive sum, not just SKILL.md.");
    }
}
