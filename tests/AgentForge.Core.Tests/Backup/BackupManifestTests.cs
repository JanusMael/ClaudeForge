using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Round-trip tests for <see cref="BackupManifest"/> through the source-generated
/// JSON context. Guards against accidentally breaking backup-archive compatibility
/// when evolving the POCO.
/// </summary>
public sealed class BackupManifestTests
{
    [Theory]
    [InlineData(BackupMode.SettingsOnly, "windows")]
    [InlineData(BackupMode.SettingsOnly, "macos")]
    [InlineData(BackupMode.SettingsOnly, "linux")]
    [InlineData(BackupMode.Full, "windows")]
    [InlineData(BackupMode.Full, "macos")]
    [InlineData(BackupMode.Full, "linux")]
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

        Assert.NotNull(round);
        Assert.Equal(original.Kind, round!.Kind);
        Assert.Equal(original.SchemaVersion, round.SchemaVersion);
        Assert.Equal(original.CreatedUtc, round.CreatedUtc);
        Assert.Equal(original.Platform, round.Platform);
        Assert.Equal(original.AppVersion, round.AppVersion);
        Assert.Equal(original.Mode, round.Mode);
        Assert.Equal(original.IncludedCredentials, round.IncludedCredentials);
        Assert.Equal(original.SizeBytes, round.SizeBytes);
        Assert.Equal(original.ItemCount, round.ItemCount);
        Assert.Equal(original.Clients, round.Clients);
        Assert.Equal(original.Projects, round.Projects);
        Assert.Equal(original.Warnings, round.Warnings);
        Assert.Single(round.Worktrees);
        Assert.Equal(original.Worktrees[0].ProjectRoot, round.Worktrees[0].ProjectRoot);
        Assert.Equal(original.Worktrees[0].WorktreePath, round.Worktrees[0].WorktreePath);
    }

    [Fact]
    public void ModeSerialisedAsReadableString()
    {
        BackupManifest m = new() { Mode = BackupMode.Full };
        string json = JsonSerializer.Serialize(m, BackupJsonContext.Default.BackupManifest);
        MessageAssert.Contains("\"mode\": \"Full\"", json,
            "BackupMode must be serialised as a string for on-disk readability.");
    }

    [Fact]
    public void SchemaVersion_DefaultIsCurrent()
    {
        BackupManifest m = new();
        Assert.Equal(BackupManifest.CurrentSchemaVersion, m.SchemaVersion);
    }
}