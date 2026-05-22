using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Backup;

/// <summary>
/// Covers every shape of the <c>additionalDirectories</c> setting we must accept,
/// plus robustness for missing / malformed input.
/// </summary>
[TestClass]
public sealed class AdditionalDirectoriesResolverTests
{
    private string _scratch = string.Empty;
    private string _realDirA = string.Empty;
    private string _realDirB = string.Empty;
    private string _settingsFile = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "adr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
        _realDirA = Path.Combine(_scratch, "realA");
        _realDirB = Path.Combine(_scratch, "realB");
        Directory.CreateDirectory(_realDirA);
        Directory.CreateDirectory(_realDirB);
        _settingsFile = Path.Combine(_scratch, "settings.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_scratch))
            {
                Directory.Delete(_scratch, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    private static string JsonEscape(string path)
    {
        return path.Replace("\\", "\\\\");
    }

    [TestMethod]
    public void Resolve_RootLevelArrayOfStrings()
    {
        File.WriteAllText(_settingsFile,
            "{\"additionalDirectories\": [\"" + JsonEscape(_realDirA) + "\"]}");
        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([_settingsFile]);
        CollectionAssert.AreEqual(new[] { Path.GetFullPath(_realDirA) }, result.ToArray());
    }

    [TestMethod]
    public void Resolve_PermissionsNested()
    {
        File.WriteAllText(_settingsFile,
            "{\"permissions\": {\"additionalDirectories\": [\"" + JsonEscape(_realDirA) + "\"]}}");
        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([_settingsFile]);
        CollectionAssert.AreEqual(new[] { Path.GetFullPath(_realDirA) }, result.ToArray());
    }

    [TestMethod]
    public void Resolve_ObjectEntriesWithPath()
    {
        File.WriteAllText(_settingsFile,
            "{\"additionalDirectories\": [{\"path\": \"" + JsonEscape(_realDirA) + "\", \"readClaudeMd\": true}]}");
        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([_settingsFile]);
        CollectionAssert.AreEqual(new[] { Path.GetFullPath(_realDirA) }, result.ToArray());
    }

    [TestMethod]
    public void Resolve_NonExistentDirsAreFilteredOut()
    {
        string missing = Path.Combine(_scratch, "not-real");
        File.WriteAllText(_settingsFile,
            "{\"additionalDirectories\": [\"" + JsonEscape(missing) + "\", \"" + JsonEscape(_realDirA) + "\"]}");
        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([_settingsFile]);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(Path.GetFullPath(_realDirA), result[0]);
    }

    [TestMethod]
    public void Resolve_MalformedJsonReturnsEmpty()
    {
        File.WriteAllText(_settingsFile, "{ this is not json }");
        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([_settingsFile]);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Resolve_MissingSettingsFileReturnsEmpty()
    {
        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([Path.Combine(_scratch, "nope.json")]);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Resolve_DeduplicatesAcrossFiles()
    {
        string a = Path.Combine(_scratch, "a.json");
        string b = Path.Combine(_scratch, "b.json");
        File.WriteAllText(a,
            "{\"additionalDirectories\": [\"" + JsonEscape(_realDirA) + "\"]}");
        File.WriteAllText(b,
            "{\"additionalDirectories\": [\"" + JsonEscape(_realDirA) + "\", \"" + JsonEscape(_realDirB) + "\"]}");

        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([a, b]);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Resolve_RelativePathsAreResolvedAgainstSettingsFile()
    {
        string relDir = Path.Combine(_scratch, "relative");
        Directory.CreateDirectory(relDir);

        File.WriteAllText(_settingsFile, "{\"additionalDirectories\": [\"relative\"]}");
        IReadOnlyList<string> result = AdditionalDirectoriesResolver.Resolve([_settingsFile]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(Path.GetFullPath(relDir), result[0]);
    }

    [TestMethod]
    public void Resolve_TildeIsExpanded()
    {
        // Tilde expansion only matters when HOME exists and contains the target; rather
        // than mutate HOME, we assert the Resolve helper directly.
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? resolved = AdditionalDirectoriesResolver.Resolve("~/docs", null);
        Assert.AreEqual(Path.Combine(home, "docs"), resolved,
            "'~' should expand to the user's home directory.");
    }
}