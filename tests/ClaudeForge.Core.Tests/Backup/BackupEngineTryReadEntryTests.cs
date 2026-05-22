using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Covers <see cref="BackupEngine.TryReadEntry"/> — the single-archive
/// version of <see cref="BackupEngine.List"/> used by the drag-drop restore
/// flow (W8 2026-05-15) to parse a backup .zip that may live outside the
/// user's configured restore directory.
/// </summary>
/// <remarks>
/// Four shapes to lock:
/// <list type="number">
///   <item>Null / empty / non-existent path → returns <c>null</c>.</item>
///   <item>Valid backup zip → returns a non-corrupt <see cref="BackupEntry"/>
///   with the parsed manifest.</item>
///   <item>Non-zip random bytes → returns an entry with
///   <see cref="BackupEntry.IsCorrupt"/> <c>true</c>.</item>
///   <item>Empty file → returns an entry with
///   <see cref="BackupEntry.IsCorrupt"/> <c>true</c>.</item>
/// </list>
/// </remarks>
[TestClass]
public sealed class BackupEngineTryReadEntryTests
{
    private string _tmp = string.Empty;
    private string _fakeHome = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "tre-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);

        _fakeHome = Path.Combine(_tmp, "home");
        Directory.CreateDirectory(Path.Combine(_fakeHome, ".claude"));
        File.WriteAllText(Path.Combine(_fakeHome, ".claude", "settings.json"), """{"theme":"dark"}""");
        PlatformPaths.TestUserProfileOverride = _fakeHome;

        BackupEngine.InvalidateListCache();
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        BackupEngine.InvalidateListCache();
        try
        {
            if (Directory.Exists(_tmp))
            {
                Directory.Delete(_tmp, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    public void TryReadEntry_NullPath_ReturnsNull()
    {
        Assert.IsNull(BackupEngine.Default.TryReadEntry(null!),
            "Null path is the documented 'no file' signal; must short-circuit to null.");
    }

    [TestMethod]
    public void TryReadEntry_EmptyPath_ReturnsNull()
    {
        Assert.IsNull(BackupEngine.Default.TryReadEntry(string.Empty));
        Assert.IsNull(BackupEngine.Default.TryReadEntry("   "));
    }

    [TestMethod]
    public void TryReadEntry_NonExistentFile_ReturnsNull()
    {
        string missing = Path.Combine(_tmp, "does-not-exist.zip");
        Assert.IsNull(BackupEngine.Default.TryReadEntry(missing),
            "A path that exists on no filesystem must return null, not a corrupt entry.");
    }

    [TestMethod]
    public async Task TryReadEntry_ValidBackupZip_ReturnsParsedEntry()
    {
        string zipPath = Path.Combine(_tmp, "backup-20300101-000000.zip");
        BackupResult result = await BackupEngine.Default.CreateAsync(new BackupRequest
        {
            DestinationZipPath = zipPath,
            IncludeClaudeCode = true,
            IncludeClaudeDesktop = false,
        });
        Assert.IsTrue(result.Succeeded, "Test prerequisite: backup must be creatable.");

        BackupEntry? entry = BackupEngine.Default.TryReadEntry(zipPath);

        Assert.IsNotNull(entry, "A valid backup zip must produce a non-null entry.");
        Assert.IsFalse(entry!.IsCorrupt,
            "A freshly-created backup zip must parse cleanly — IsCorrupt should be false.");
        Assert.IsNotNull(entry.Manifest, "Manifest must be populated for a valid backup.");
        Assert.AreEqual(zipPath, entry.ArchivePath,
            "ArchivePath is set from FileInfo.FullName so it survives relative-path callers.");
        Assert.AreEqual("backup-20300101-000000.zip", entry.FileName);
    }

    [TestMethod]
    public void TryReadEntry_RandomBytes_ReturnsCorruptEntry()
    {
        string bogusZip = Path.Combine(_tmp, "not-a-real-zip.zip");
        File.WriteAllBytes(bogusZip, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x42]);

        BackupEntry? entry = BackupEngine.Default.TryReadEntry(bogusZip);

        Assert.IsNotNull(entry,
            "An unreadable zip should still return an entry (with IsCorrupt=true), " +
            "so the caller can surface the right error message rather than confuse " +
            "'no file' with 'bad file'.");
        Assert.IsTrue(entry!.IsCorrupt,
            "Random bytes should not parse as a backup manifest; IsCorrupt must be true.");
        Assert.IsNull(entry.Manifest);
    }

    [TestMethod]
    public void TryReadEntry_EmptyFile_ReturnsCorruptEntry()
    {
        string emptyZip = Path.Combine(_tmp, "empty.zip");
        File.WriteAllBytes(emptyZip, Array.Empty<byte>());

        BackupEntry? entry = BackupEngine.Default.TryReadEntry(emptyZip);

        Assert.IsNotNull(entry);
        Assert.IsTrue(entry!.IsCorrupt,
            "Zero-byte 'zip' file: IsCorrupt must be true so the drop-restore caller " +
            "shows the invalid-archive alert rather than running RestoreCommand on garbage.");
    }
}