using System.Text.Json;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Round-trip tests for <see cref="BackupManifest"/> through the source-generated
/// JSON context. Guards against accidentally breaking backup-archive compatibility
/// when evolving the POCO.
/// </summary>
[TestClass]
public sealed class BackupManifestTests
{
    [DataTestMethod]
    [DataRow(BackupMode.SettingsOnly, "windows")]
    [DataRow(BackupMode.SettingsOnly, "macos")]
    [DataRow(BackupMode.SettingsOnly, "linux")]
    [DataRow(BackupMode.Full, "windows")]
    [DataRow(BackupMode.Full, "macos")]
    [DataRow(BackupMode.Full, "linux")]
    public void RoundTrip_EveryModeAndPlatform(BackupMode mode, string platform)
    {
        BackupManifest original = new()
        {
            CreatedUtc = new DateTime(2030, 3, 24, 2, 0, 0, DateTimeKind.Utc),
            Platform = platform,
            AppVersion = "1.2.3.4",
            Mode = mode,
            Clients = ["ClaudeCode", "ClaudeDesktop"],
            Projects = ["/home/me/app1", "/home/me/app2"],
            Worktrees =
            [
                new BackupWorktreeEntry { ProjectRoot = "/home/me/app1", WorktreePath = "/tmp/wt1" },
            ],
            IncludedCredentials = true,
            SizeBytes = 1_234_567,
            ItemCount = 42,
            Warnings = ["git not found"],
        };

        // Use the source-gen path — identical to production behaviour under trimming.
        string json = JsonSerializer.Serialize(original, BackupJsonContext.Default.BackupManifest);
        BackupManifest? round = JsonSerializer.Deserialize(json, BackupJsonContext.Default.BackupManifest);

        Assert.IsNotNull(round);
        Assert.AreEqual(original.Kind, round!.Kind);
        Assert.AreEqual(original.SchemaVersion, round.SchemaVersion);
        Assert.AreEqual(original.CreatedUtc, round.CreatedUtc);
        Assert.AreEqual(original.Platform, round.Platform);
        Assert.AreEqual(original.AppVersion, round.AppVersion);
        Assert.AreEqual(original.Mode, round.Mode);
        Assert.AreEqual(original.IncludedCredentials, round.IncludedCredentials);
        Assert.AreEqual(original.SizeBytes, round.SizeBytes);
        Assert.AreEqual(original.ItemCount, round.ItemCount);
        CollectionAssert.AreEqual(original.Clients, round.Clients);
        CollectionAssert.AreEqual(original.Projects, round.Projects);
        CollectionAssert.AreEqual(original.Warnings, round.Warnings);
        Assert.AreEqual(1, round.Worktrees.Count);
        Assert.AreEqual(original.Worktrees[0].ProjectRoot, round.Worktrees[0].ProjectRoot);
        Assert.AreEqual(original.Worktrees[0].WorktreePath, round.Worktrees[0].WorktreePath);
    }

    [TestMethod]
    public void ModeSerialisedAsReadableString()
    {
        BackupManifest m = new() { Mode = BackupMode.Full };
        string json = JsonSerializer.Serialize(m, BackupJsonContext.Default.BackupManifest);
        StringAssert.Contains(json, "\"mode\": \"Full\"",
            "BackupMode must be serialised as a string for on-disk readability.");
    }

    [TestMethod]
    public void SchemaVersion_DefaultIsCurrent()
    {
        BackupManifest m = new();
        Assert.AreEqual(BackupManifest.CurrentSchemaVersion, m.SchemaVersion);
    }
}