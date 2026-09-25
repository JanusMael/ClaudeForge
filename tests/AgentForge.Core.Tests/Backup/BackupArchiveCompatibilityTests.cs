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
public sealed class BackupArchiveCompatibilityTests : IDisposable
{
    /// <summary>The frozen archive, copied beside the test assembly by the csproj.</summary>
    private const string FixtureName = "backup-v1-claudeforge.zip";

    private string _fakeHome = string.Empty;

    public BackupArchiveCompatibilityTests() => Setup();

    private void Setup()
    {
        _fakeHome = Path.Combine(Path.GetTempPath(), "bac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHome);
        BackupEngine.InvalidateListCache();
        PlatformPaths.TestUserProfileOverride = _fakeHome;
    }

    private void Cleanup()
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

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }

    // ── What the frozen archive contains ───────────────────────────────────

    [Fact]
    public void TheFixture_StillCarriesTheShippedProductPrefixes()
    {
        // ⚠ Asserted on the FIXTURE, not on a freshly-created archive. This is the statement
        // "archives on users' disks look like this", and it must not start tracking whatever the
        // writer produces today — that is the drift this whole suite exists to catch.
        List<string> entries = Entries();

        Assert.Contains("ClaudeCode/claude.json", entries);
        Assert.Contains("ClaudeCode/claude-dir/settings.json", entries);
        Assert.Contains("ClaudeCode/claude-dir/agents/reviewer.md", entries);
        Assert.Contains("ClaudeDesktop/claude_desktop_config.json", entries);
        Assert.Contains("manifest.json", entries);

        Assert.True(
            entries.Any(e => e.StartsWith("Schemas/", StringComparison.Ordinal)),
            "The archive carries the schemas that were current when it was made, so restore "
            + "validates against those rather than today's.");
    }

    [Fact]
    public void TheFixture_IsAtManifestSchemaVersionOne()
    {
        // When the layout change bumps BackupManifest.CurrentSchemaVersion, this test does NOT
        // move with it. It pins what the frozen archive says, which is the whole point: the reader
        // must keep understanding version 1 after the writer has moved on.
        using ZipArchive zip = ZipFile.OpenRead(FixturePath());
        ZipArchiveEntry manifest = zip.GetEntry("manifest.json")
            ?? throw new Xunit.Sdk.XunitException("The fixture has no manifest.json.");

        using Stream stream = manifest.Open();
        using StreamReader reader = new(stream);
        string json = reader.ReadToEnd();

        MessageAssert.Contains("\"schemaVersion\": 1", json,
            "The frozen archive is a v1 manifest. If this fails, the fixture was regenerated — "
            + "restore it from git rather than updating this assertion.");
        OrdinalAssert.Contains("\"ClaudeCode\"", json);
        OrdinalAssert.Contains("\"ClaudeDesktop\"", json);
    }

    // ── That it still restores ─────────────────────────────────────────────

    [Fact]
    public async Task AShippedArchive_StillRestoresEveryFileToItsRealPath()
    {
        BackupEntry entry = StageFixture();

        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entry, ct: TestContext.Current.CancellationToken);

        Assert.True(restore.Succeeded,
            "A backup written by a shipped build must keep restoring. " + restore.Message);

        // ~/.claude.json — the product root file.
        string claudeJson = Path.Combine(_fakeHome, ".claude.json");
        Assert.True(File.Exists(claudeJson), "ClaudeCode/claude.json must land at ~/.claude.json.");
        OrdinalAssert.Contains("\"fixture\"", await File.ReadAllTextAsync(claudeJson, TestContext.Current.CancellationToken));

        // ~/.claude/settings.json — the claude-dir subtree.
        string settings = Path.Combine(_fakeHome, ".claude", "settings.json");
        Assert.True(File.Exists(settings),
            "ClaudeCode/claude-dir/settings.json must land at ~/.claude/settings.json.");
        OrdinalAssert.Contains("\"opus\"", await File.ReadAllTextAsync(settings, TestContext.Current.CancellationToken));

        // A nested file under claude-dir, to prove the subtree is walked rather than one level.
        Assert.True(
            File.Exists(Path.Combine(_fakeHome, ".claude", "agents", "reviewer.md")),
            "Nested claude-dir entries must restore, not just top-level ones.");
    }

    [Fact]
    public async Task AShippedArchive_RestoresTheDesktopPrefixToo()
    {
        // ⚠ The second prefix is the one a per-product dispatch is most likely to drop: the Claude
        // Code path is what every other test exercises, so a dispatch that silently handles only
        // the first product would look green everywhere else.
        BackupEntry entry = StageFixture();

        RestoreResult restore = await TestBackupEngine.Default.RestoreAsync(entry, ct: TestContext.Current.CancellationToken);
        Assert.True(restore.Succeeded, restore.Message);

        Assert.True(File.Exists(PlatformPaths.DesktopConfigPath),
            "ClaudeDesktop/claude_desktop_config.json must restore to the Desktop config path.");
    }

    [Fact]
    public async Task AShippedArchive_IsListedAsRestorable()
    {
        // The list path parses manifest.json and filters on `kind`. A v1 manifest must still be
        // recognised as a restorable backup rather than being quietly filtered out — an archive
        // that does not appear in the list can never be restored, and nothing errors.
        StageFixture();

        IReadOnlyList<BackupEntry> entries = TestBackupEngine.Default.List(_fakeHome);

        MessageAssert.Equal(1, entries.Count, "The frozen archive must appear in the restorable list.");
        await Task.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string FixturePath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);
        Assert.True(File.Exists(path),
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
        MessageAssert.Equal(1, entries.Count, "Expected exactly the staged fixture in the sandbox.");
        return entries[0];
    }
}
