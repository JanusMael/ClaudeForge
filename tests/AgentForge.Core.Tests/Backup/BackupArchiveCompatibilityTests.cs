using System.IO.Compression;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Restores a <b>frozen archive produced by a shipped build</b> and asserts every file lands where
/// it belongs.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This suite exists because the archive layout is about to change, and it was written
/// first.</b> The plan's Phase 14 calls for a per-product archive prefix supplied by the product
/// descriptor and a <c>RestoreEngine</c> that dispatches on it — which means the
/// <c>ClaudeCode/</c> and <c>ClaudeDesktop/</c> prefixes in every archive already on a user's disk
/// become one case among several. <b>A format change that only round-trips with itself is how
/// backup tools lose people's data</b>, and every round-trip test in this project today creates
/// its archive with the same build that reads it, so not one of them would notice.
/// </para>
/// <para>
/// ⛔⛔ <b><c>Fixtures/backup-v1-claudeforge.zip</c> MUST NOT BE REGENERATED.</b> It was minted by
/// the engine as it shipped — <c>manifest.json</c> at <c>schemaVersion 1</c>, entries prefixed
/// <c>ClaudeCode/</c> and <c>ClaudeDesktop/</c>, schemas under <c>Schemas/</c>. Re-minting it
/// against a changed layout deletes the only evidence of what users actually hold and turns this
/// suite green for the worst possible reason. If a change genuinely cannot restore it, that is the
/// finding — add a migration, or add a SECOND fixture beside it and keep this one.
/// </para>
/// <para>
/// The same discipline as <c>OpenCodeDatabaseSchemaTests</c>: a guard built before the thing it
/// protects, so it can never be written to match what the change happened to produce.
/// </para>
/// </remarks>
[TestClass]
public sealed class BackupArchiveCompatibilityTests
{
    /// <summary>The frozen archive, copied beside the test assembly by the csproj.</summary>
    private const string FixtureName = "backup-v1-claudeforge.zip";

    private string _fakeHome = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "bac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        BackupEngine.InvalidateListCache();
        PlatformPaths.TestUserProfileOverride = _fakeHome;
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

    // ── What the frozen archive contains ───────────────────────────────────

    [TestMethod]
    public void TheFixture_StillCarriesTheShippedProductPrefixes()
    {
        // ⚠ Asserted on the FIXTURE, not on a freshly-created archive. This is the statement
        // "archives on users' disks look like this", and it must not start tracking whatever the
        // writer produces today — that is the drift this whole suite exists to catch.
        List<string> entries = Entries();

        CollectionAssert.Contains(entries, "ClaudeCode/claude.json");
        CollectionAssert.Contains(entries, "ClaudeCode/claude-dir/settings.json");
        CollectionAssert.Contains(entries, "ClaudeCode/claude-dir/agents/reviewer.md");
        CollectionAssert.Contains(entries, "ClaudeDesktop/claude_desktop_config.json");
        CollectionAssert.Contains(entries, "manifest.json");

        Assert.IsTrue(
            entries.Any(e => e.StartsWith("Schemas/", StringComparison.Ordinal)),
            "The archive carries the schemas that were current when it was made, so restore "
            + "validates against those rather than today's.");
    }

    [TestMethod]
    public void TheFixture_IsAtManifestSchemaVersionOne()
    {
        // When the layout change bumps BackupManifest.CurrentSchemaVersion, this test does NOT
        // move with it. It pins what the frozen archive says, which is the whole point: the reader
        // must keep understanding version 1 after the writer has moved on.
        using ZipArchive zip = ZipFile.OpenRead(FixturePath());
        ZipArchiveEntry manifest = zip.GetEntry("manifest.json")
            ?? throw new AssertFailedException("The fixture has no manifest.json.");

        using Stream stream = manifest.Open();
        using StreamReader reader = new(stream);
        string json = reader.ReadToEnd();

        StringAssert.Contains(json, "\"schemaVersion\": 1",
            "The frozen archive is a v1 manifest. If this fails, the fixture was regenerated — "
            + "restore it from git rather than updating this assertion.");
        StringAssert.Contains(json, "\"ClaudeCode\"");
        StringAssert.Contains(json, "\"ClaudeDesktop\"");
    }

    // ── That it still restores ─────────────────────────────────────────────

    [TestMethod]
    public async Task AShippedArchive_StillRestoresEveryFileToItsRealPath()
    {
        BackupEntry entry = StageFixture();

        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entry);

        Assert.IsTrue(restore.Succeeded,
            "A backup written by a shipped build must keep restoring. " + restore.Message);

        // ~/.claude.json — the product root file.
        string claudeJson = Path.Combine(_fakeHome, ".claude.json");
        Assert.IsTrue(File.Exists(claudeJson), "ClaudeCode/claude.json must land at ~/.claude.json.");
        StringAssert.Contains(await File.ReadAllTextAsync(claudeJson), "\"fixture\"");

        // ~/.claude/settings.json — the claude-dir subtree.
        string settings = Path.Combine(_fakeHome, ".claude", "settings.json");
        Assert.IsTrue(File.Exists(settings),
            "ClaudeCode/claude-dir/settings.json must land at ~/.claude/settings.json.");
        StringAssert.Contains(await File.ReadAllTextAsync(settings), "\"opus\"");

        // A nested file under claude-dir, to prove the subtree is walked rather than one level.
        Assert.IsTrue(
            File.Exists(Path.Combine(_fakeHome, ".claude", "agents", "reviewer.md")),
            "Nested claude-dir entries must restore, not just top-level ones.");
    }

    [TestMethod]
    public async Task AShippedArchive_RestoresTheDesktopPrefixToo()
    {
        // ⚠ The second prefix is the one a per-product dispatch is most likely to drop: the Claude
        // Code path is what every other test exercises, so a dispatch that silently handles only
        // the first product would look green everywhere else.
        BackupEntry entry = StageFixture();

        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entry);
        Assert.IsTrue(restore.Succeeded, restore.Message);

        Assert.IsTrue(File.Exists(PlatformPaths.DesktopConfigPath),
            "ClaudeDesktop/claude_desktop_config.json must restore to the Desktop config path.");
    }

    [TestMethod]
    public async Task AShippedArchive_IsListedAsRestorable()
    {
        // The list path parses manifest.json and filters on `kind`. A v1 manifest must still be
        // recognised as a restorable backup rather than being quietly filtered out — an archive
        // that does not appear in the list can never be restored, and nothing errors.
        StageFixture();

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);

        Assert.AreEqual(1, entries.Count, "The frozen archive must appear in the restorable list.");
        await Task.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string FixturePath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);
        Assert.IsTrue(File.Exists(path),
            $"Fixture '{FixtureName}' was not copied to the output directory. Check the "
            + "Content item in AgentForge.Core.Tests.csproj.");
        return path;
    }

    private static List<string> Entries()
    {
        using ZipArchive zip = ZipFile.OpenRead(FixturePath());
        return [.. zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Copy the frozen archive into the sandbox under a name <see cref="BackupEngine.List"/>
    /// recognises, and return its entry.
    /// </summary>
    private BackupEntry StageFixture()
    {
        // The list path globs "backup-*.zip", so the staged name must keep that prefix.
        string staged = Path.Combine(_fakeHome, "backup-20260911-000000.zip");
        File.Copy(FixturePath(), staged, overwrite: true);

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);
        Assert.AreEqual(1, entries.Count, "Expected exactly the staged fixture in the sandbox.");
        return entries[0];
    }
}
