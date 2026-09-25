using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Verifies the first-run / steady-state / diff semantics of
/// <see cref="SchemaSnapshotService"/> — the engine behind the "✨ NEW" badges
/// in the editor UI. Uses a temp directory for isolation from the real
/// <c>~/.claude/cache/</c>.
/// </summary>
public sealed class SchemaSnapshotServiceTests : IDisposable
{
    private string _tempDir = null!;

    public SchemaSnapshotServiceTests() => SetUp();

    private void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ccg-snapshot-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    private void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    public void Dispose()
    {
        TearDown();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LoadSnapshot_FirstRun_ReturnsEmptySet()
    {
        SchemaSnapshotService svc = new(_tempDir);

        HashSet<string> snap = svc.LoadSnapshot("claude-code-settings");

        Assert.NotNull(snap);
        MessageAssert.Equal(0, snap.Count,
            "First run must return an empty set so every property badges as NEW.");
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPaths()
    {
        SchemaSnapshotService svc = new(_tempDir);
        string[] paths = ["model", "permissions.defaultMode", "env.DEBUG"];

        svc.SaveSnapshot("claude-code-settings", paths);
        HashSet<string> loaded = svc.LoadSnapshot("claude-code-settings");

        MessageAssert.SameElements(paths, loaded.ToArray());
    }

    [Fact]
    public void SecondRun_AfterSave_YieldsNoNewProperties()
    {
        // Seed: pretend we saw a set last time
        SchemaSnapshotService svc = new(_tempDir);
        svc.SaveSnapshot("claude-code-settings", ["model", "permissions.defaultMode"]);

        HashSet<string> snap = svc.LoadSnapshot("claude-code-settings");

        // Every currently-known path is already in the snapshot, so no path is "new"
        string[] currentPaths = ["model", "permissions.defaultMode"];
        string[] newPaths = currentPaths.Where(p => !snap.Contains(p)).ToArray();

        MessageAssert.Equal(0, newPaths.Length,
            "After saving and reloading, nothing should be flagged new on the next launch.");
    }

    [Fact]
    public void AddPropertyBetweenRuns_MarksOnlyNewOne()
    {
        SchemaSnapshotService svc = new(_tempDir);
        svc.SaveSnapshot("claude-code-settings", ["model", "permissions.defaultMode"]);

        HashSet<string> snap = svc.LoadSnapshot("claude-code-settings");

        // Simulate a schema update that added "outputStyle"
        string[] currentPaths = ["model", "permissions.defaultMode", "outputStyle"];
        string[] newPaths = currentPaths.Where(p => !snap.Contains(p)).ToArray();

        MessageAssert.SequenceEqual(new[] { "outputStyle" }, newPaths,
            "Only the freshly-added property should be flagged new.");
    }

    [Fact]
    public void LoadSnapshot_CorruptFile_ReturnsEmptySet()
    {
        SchemaSnapshotService svc = new(_tempDir);
        File.WriteAllText(svc.GetSnapshotPath("claude-code-settings"), "not-json{{");

        HashSet<string> snap = svc.LoadSnapshot("claude-code-settings");

        MessageAssert.Equal(0, snap.Count,
            "Corrupt snapshot must be treated as first-run rather than crashing the app.");
    }

    [Fact]
    public void SaveSnapshot_Deduplicates()
    {
        SchemaSnapshotService svc = new(_tempDir);
        string[] paths = ["model", "model", "env.DEBUG", "env.DEBUG"];

        svc.SaveSnapshot("claude-code-settings", paths);
        HashSet<string> loaded = svc.LoadSnapshot("claude-code-settings");

        MessageAssert.Equal(2, loaded.Count,
            "Duplicates in the input enumerable must be collapsed on disk.");
    }
}