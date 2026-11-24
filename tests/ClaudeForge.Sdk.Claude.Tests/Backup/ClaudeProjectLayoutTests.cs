using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Backup;

/// <summary>
/// Claude Code's <see cref="ProductProjectLayout"/>: the four items, the settings files that name
/// related projects, and the projects <c>~/.claude.json</c> knows.
/// </summary>
/// <remarks>
/// ⓘ <see cref="KnownProjectsDiscovery"/> had no direct test while it lived in
/// <c>AgentForge.Core</c>; it was reached only through a full backup of the real home.
/// </remarks>
public sealed class ClaudeProjectLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cpl-" + Guid.NewGuid().ToString("N"));

    public ClaudeProjectLayoutTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void ClaudeCodesDescriptor_CarriesTheLayout_WithItsFourItems()
    {
        ProductProjectLayout? layout = ClaudeProducts.ClaudeCodeProductFor(ClaudeEnvironment.Empty).Backup.Projects;

        Assert.NotNull(layout);
        MessageAssert.Equal(
            [".claude", ".mcp.json", "CLAUDE.md", "CLAUDE.local.md"], layout.Items,
            "These are what every Claude archive since v1 carries per project; changing them changes the archive.");
    }

    [Fact]
    public void ClaudeDesktop_KeepsNothingInProjects()
    {
        Assert.Null(ClaudeProducts.ClaudeDesktopProduct.Backup.Projects);
    }

    [Fact]
    public void SettingsFiles_AreTheUsersTwo_ThenEachOpenProjectsTwo()
    {
        string a = Path.Combine(_root, "a");
        string b = Path.Combine(_root, "b");

        IReadOnlyList<string> files = ClaudeProjectLayout.SettingsFiles(ClaudeEnvironment.Empty, [a, b]);

        MessageAssert.Equal(
            [
                PlatformPaths.UserSettingsPath(ClaudeEnvironment.Empty),
                Path.Combine(PlatformPaths.ClaudeHome(ClaudeEnvironment.Empty), "settings.local.json"),
                PlatformPaths.ProjectSettingsPath(a),
                PlatformPaths.LocalSettingsPath(a),
                PlatformPaths.ProjectSettingsPath(b),
                PlatformPaths.LocalSettingsPath(b),
            ],
            files,
            "The same files the backup engine read before plans/00008 step 8, in the same order.");
    }

    [Fact]
    public void KnownProjects_ReturnsTheRootsThatExist_AndCountsTheOnesThatDoNot()
    {
        string present = Directory.CreateDirectory(Path.Combine(_root, "present")).FullName;
        string gone = Path.Combine(_root, "gone");
        string json = Write("claude.json", $$"""
            { "projects": { {{Quote(present)}}: {}, {{Quote(gone)}}: {}, "": {} } }
            """);

        ProductKnownProjects known = KnownProjectsDiscovery.ResolveExisting(json);

        MessageAssert.Equal(present, Assert.Single(known.ExistingProjectRoots), "Only the root on disk is usable.");
        MessageAssert.Equal(1, known.SkippedNonExistentCount,
            "The missing root is counted, and the blank key is neither kept nor counted.");
    }

    [Theory]
    [InlineData("""{ "projects": [ "not", "an", "object" ] }""")]
    [InlineData("""{ "other": {} }""")]
    [InlineData("""{ not json""")]
    public void KnownProjects_DegradesToNone_ForAFileWithNoUsableProjectsMap(string text)
    {
        ProductKnownProjects known = KnownProjectsDiscovery.ResolveExisting(Write("claude.json", text));

        Assert.Empty(known.ExistingProjectRoots);
        MessageAssert.Equal(0, known.SkippedNonExistentCount, "A broken file must never block a backup.");
    }

    [Fact]
    public void KnownProjects_DegradesToNone_ForAMissingFile()
    {
        ProductKnownProjects known = KnownProjectsDiscovery.ResolveExisting(Path.Combine(_root, "absent.json"));

        Assert.Same(ProductKnownProjects.None, known);
    }

    private static string Quote(string path) => System.Text.Json.JsonSerializer.Serialize(path);
}
