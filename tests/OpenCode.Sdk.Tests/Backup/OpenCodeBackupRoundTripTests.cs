using System.IO.Compression;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.OpenCode.Sdk;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests.Backup;

/// <summary>
/// End-to-end: an OpenCode backup is written, and restores.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>The round trip is the test, not the write.</b> A backup takes its products from the
/// request, so writing an OpenCode archive works with ANY engine — including one that cannot
/// restore a single file of it. Asserting only on archive entries would have passed while the
/// clients still used <c>BackupEngine.Default</c>, whose restorable products are Claude's two: the
/// archive exists, the restore reports success, and nothing comes back. A one-way backup is worse
/// than no backup, because the user believes they have one.
/// </para>
/// </remarks>
[TestClass]
public sealed class OpenCodeBackupRoundTripTests
{
    private string _fakeHome = string.Empty;
    private string _configDir = string.Empty;
    private string _dataDir = string.Empty;

    /// <summary>
    /// ⚠ <b>The engine the CLIENTS use, not a fresh one configured the way this test would like.</b>
    /// An earlier draft built its own <c>new BackupEngine(restorableProducts: OpenCodeProducts.All)</c>
    /// — which passes whatever <c>OpenCodeClient.CreateBackupClient</c> actually wires, and so would
    /// have gone green while the clients still used <c>BackupEngine.Default</c> and restored
    /// nothing.
    /// </summary>
    private static BackupEngine Engine => OpenCodeBackup.Engine;

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "ocb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        BackupEngine.InvalidateListCache();
        PlatformPaths.TestUserProfileOverride = _fakeHome;

        _configDir = OpenCodePaths.DefaultGlobalDirectory();
        _dataDir = OpenCodePaths.DataDirectory();
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_dataDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_fakeHome))
            {
                Directory.Delete(_fakeHome, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    [TestMethod]
    public async Task ConfigRoundTrips_AndTheGitignoredWeightIsLeftBehind()
    {
        Write(Path.Combine(_configDir, "opencode.json"), """{"model":"anthropic/claude-opus-5"}""");
        Write(Path.Combine(_configDir, "plugins", "gk-hooks.js"), "export const hooks = {};");

        // The five entries OpenCode really writes there — measured, not guessed.
        Write(Path.Combine(_configDir, ".gitignore"),
            "node_modules\npackage.json\npackage-lock.json\nbun.lock\n.gitignore");
        Write(Path.Combine(_configDir, "package.json"), """{"dependencies":{}}""");
        Write(Path.Combine(_configDir, "node_modules", "@opencode-ai", "plugin", "index.js"), "// 52 MiB of this");

        string dest = Path.Combine(_fakeHome, "backup-oc.zip");
        BackupResult create = await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [OpenCodeProducts.Config],
        });
        Assert.IsTrue(create.Succeeded, create.Message);

        List<string> entries = Entries(dest);
        CollectionAssert.Contains(entries, "OpenCode/config/opencode.json");
        CollectionAssert.Contains(entries, "OpenCode/config/plugins/gk-hooks.js");

        // ⭐ The point of archiving the root whole instead of hardcoding a skip list: the
        // .gitignore OpenCode maintains there does the excluding, and it stays correct when
        // upstream changes it.
        Assert.IsFalse(entries.Any(e => e.Contains("node_modules", StringComparison.Ordinal)),
            "node_modules is 99.97% of the config root and regenerates — the .gitignore excludes it.");
        Assert.IsFalse(entries.Any(e => e.EndsWith("config/package.json", StringComparison.Ordinal)),
            "package.json is named in that .gitignore too — the entry two plan drafts left out.");

        // Clobber both, then restore.
        Write(Path.Combine(_configDir, "opencode.json"), """{"model":"CLOBBERED"}""");
        Write(Path.Combine(_configDir, "plugins", "gk-hooks.js"), "CLOBBERED");

        RestoreResult restore = await Engine.RestoreAsync(Single(dest));
        Assert.IsTrue(restore.Succeeded, restore.Message);

        StringAssert.Contains(
            await File.ReadAllTextAsync(Path.Combine(_configDir, "opencode.json")), "claude-opus-5");
        StringAssert.Contains(
            await File.ReadAllTextAsync(Path.Combine(_configDir, "plugins", "gk-hooks.js")), "export const hooks",
            "plugins/ is user-authored and irreplaceable, and is named by no exclusion list.");
    }

    [TestMethod]
    public async Task TheDatabase_IsAbsentWithoutTheOptIn_AndPresentWithIt()
    {
        Write(Path.Combine(_configDir, "opencode.json"), "{}");
        WriteDatabaseTriple();

        string without = Path.Combine(_fakeHome, "backup-nodb.zip");
        await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = without,
            Products = [OpenCodeProducts.Config],
        });

        Assert.IsFalse(Entries(without).Any(e => e.Contains("opencode.db", StringComparison.Ordinal)),
            "Without the opt-in the database — and the session history in it — stays out.");

        string with = Path.Combine(_fakeHome, "backup-db.zip");
        await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = with,
            Products = [OpenCodeProducts.Config],
            IncludeCredentials = true,
        });

        List<string> entries = Entries(with);

        // ⛔ All three, or the restored database is a stale snapshot by construction: opencode.db
        // runs journal_mode=wal, so its most recent transactions live in the -wal.
        CollectionAssert.Contains(entries, "OpenCode/data/opencode.db");
        CollectionAssert.Contains(entries, "OpenCode/data/opencode.db-wal");
        CollectionAssert.Contains(entries, "OpenCode/data/opencode.db-shm");
    }

    [TestMethod]
    public async Task Sanitized_ExcludesTheDatabase_EvenWhenTheOptInIsGiven()
    {
        // ⛔⛔ The opt-in does NOT override this. Sanitized exists to be shareable, and the
        // database's secrets are SQLite rows — account/control_account hold access_token and
        // refresh_token, credential holds `value`, session_share holds `secret`. A JSON redactor
        // cannot reach any of them, so including the file would put plaintext tokens into an
        // archive whose whole purpose is being handed to someone else.
        Write(Path.Combine(_configDir, "opencode.json"), "{}");
        WriteDatabaseTriple();

        string dest = Path.Combine(_fakeHome, "backup-sanitized.zip");
        await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = dest,
            Products = [OpenCodeProducts.Config],
            Mode = BackupMode.Sanitized,
            IncludeCredentials = true,
        });

        Assert.IsFalse(Entries(dest).Any(e => e.Contains("opencode.db", StringComparison.Ordinal)),
            "Sanitized must drop the database regardless of the opt-in.");
    }

    [TestMethod]
    public async Task TheBackupIsARead_AndLeavesTheDatabaseFilesUntouched()
    {
        // ⛔ Merely OPENING a -wal database checkpoints it, which rewrites the user's file. The
        // archive step copies bytes and never opens SQLite; this pins that, because the cost of
        // getting it wrong is a backup that mutates what it is backing up.
        Write(Path.Combine(_configDir, "opencode.json"), "{}");
        WriteDatabaseTriple();

        string db = Path.Combine(_dataDir, "opencode.db");
        string wal = Path.Combine(_dataDir, "opencode.db-wal");
        (long dbLen, DateTime dbWrite) = (new FileInfo(db).Length, File.GetLastWriteTimeUtc(db));
        (long walLen, DateTime walWrite) = (new FileInfo(wal).Length, File.GetLastWriteTimeUtc(wal));

        await Engine.CreateAsync(new BackupRequest
        {
            DestinationZipPath = Path.Combine(_fakeHome, "backup-read.zip"),
            Products = [OpenCodeProducts.Config],
            IncludeCredentials = true,
        });

        Assert.AreEqual(dbLen, new FileInfo(db).Length);
        Assert.AreEqual(dbWrite, File.GetLastWriteTimeUtc(db));
        Assert.AreEqual(walLen, new FileInfo(wal).Length, "A checkpoint would have drained the -wal.");
        Assert.AreEqual(walWrite, File.GetLastWriteTimeUtc(wal));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void WriteDatabaseTriple()
    {
        // Byte content is irrelevant — nothing here parses SQLite, which is the point.
        Write(Path.Combine(_dataDir, "opencode.db"), "SQLite format 3 fake");
        Write(Path.Combine(_dataDir, "opencode.db-wal"), "wal bytes");
        Write(Path.Combine(_dataDir, "opencode.db-shm"), "shm bytes");
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static List<string> Entries(string zipPath)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        return [.. zip.Entries.Select(e => e.FullName)];
    }

    private BackupEntry Single(string archivePath)
    {
        BackupEntry? entry = Engine.TryReadEntry(archivePath);
        Assert.IsNotNull(entry, "The archive must be readable as a backup entry.");
        return entry!;
    }
}
