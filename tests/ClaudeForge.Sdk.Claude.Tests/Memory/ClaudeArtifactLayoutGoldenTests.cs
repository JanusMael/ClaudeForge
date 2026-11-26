using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk.Memory;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Memory;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Memory;

/// <summary>
/// <c>plans/00008</c> step 9's check: through <see cref="ClaudeArtifactLayout"/>, the inventory, the
/// editor and the footprint report for a fixture home exactly what <c>main</c> reported before the
/// layout existed.
/// </summary>
/// <remarks>
/// ⛔ <b>The golden is evidence, not a snapshot to refresh.</b> It was minted on <c>main</c> at
/// <c>87ce3d38</c>, through the hard-coded walk this layout replaced. A difference here is a
/// behaviour change in the layout; regenerating the file to make it pass would delete the only
/// record of what the walk did.
/// </remarks>
public sealed class ClaudeArtifactLayoutGoldenTests : IDisposable
{
    private const string GoldenFile = "claude-artifact-layout.golden.txt";

    private readonly string _profile;

    public ClaudeArtifactLayoutGoldenTests()
    {
        _profile = Path.Combine(Path.GetTempPath(), "claude-layout-golden-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_profile);
        PlatformPaths.TestUserProfileOverride = _profile;
    }

    public void Dispose()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            Directory.Delete(_profile, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp directory is left for the OS to clear.
        }
    }

    [Fact]
    public async Task TheLayout_ResolvesWhatMainsWalkResolved()
    {
        ClaudeArtifactLayoutGolden.Plant(_profile);
        string project = Path.Combine(_profile, ClaudeArtifactLayoutGolden.ProjectRoot);
        ClaudeArtifactLayout layout = ClaudeArtifactLayout.For(ClaudeEnvironment.Empty);
        FootprintService footprint = new(layout);

        IReadOnlyList<string> actual = ClaudeArtifactLayoutGolden.Render(
            _profile,
            UserMemoryService.SnapshotFiles(layout, project),
            EditableMemoryService.Snapshot(layout, project),
            await footprint.GetStatsAsync(CancellationToken.None),
            await footprint.GetProjectTranscriptStatsAsync(CancellationToken.None));

        string[] expected = ReadGolden();
        Assert.True(expected.Length > 30, $"The golden holds only {expected.Length} rows; it was truncated.");
        MessageAssert.SequenceEqual(expected, actual, "The layout must resolve what main's walk resolved.");
    }

    private static string[] ReadGolden()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "tests", "ClaudeForge.Sdk.Claude.Tests", "Fixtures", GoldenFile);
            if (File.Exists(candidate))
            {
                // ⚠ Line-ending-normalised: .gitattributes makes a Windows checkout CRLF.
                return File.ReadAllText(candidate)
                    .Split('\n')
                    .Select(line => line.TrimEnd('\r'))
                    .Where(line => line.Length > 0)
                    .ToArray();
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException($"Could not find Fixtures/{GoldenFile} above {AppContext.BaseDirectory}.");
    }
}
