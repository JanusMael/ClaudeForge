using System.Text.Json;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCode.Sdk.Themes;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// Theme discovery: the suggestion list for the TUI's <c>theme</c> setting.
/// </summary>
/// <remarks>
/// The behaviour worth guarding is that discovery is <b>additive and unfailing</b> — it contributes
/// names when it can and costs nothing when it cannot, because it feeds a free-form picker whose
/// list is explicitly not a limit.
/// </remarks>
[TestClass]
public sealed class OpenCodeThemeDiscoveryTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "octheme_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SeedTheme(string configDir, string name)
    {
        string themes = Path.Combine(configDir, OpenCodeThemeDiscovery.ThemeDirectoryName);
        Directory.CreateDirectory(themes);
        File.WriteAllText(Path.Combine(themes, name + ".json"), "{}");
    }

    [TestMethod]
    public void TheBuiltInThemeIsAlwaysOffered_EvenWithNoThemesDirectory()
    {
        string dir = NewDir();
        try
        {
            IReadOnlyList<string> names =
                OpenCodeThemeDiscovery.Discover(new OpenCodeEnvironment(ConfigDir: dir));

            CollectionAssert.AreEqual(new[] { OpenCodeThemeDiscovery.BuiltInTheme }, names.ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// ⚠ A missing directory is the normal case, not an error — this machine has no themes
    /// directory, and neither will most.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>This guards the OUTCOME, and the two mechanisms behind it mask each other.</b>
    /// Measured, not assumed: disabling the <c>Directory.Exists</c> fast path reddens nothing, and
    /// narrowing the <c>catch</c> so it no longer covers <c>IOException</c> reddens nothing either —
    /// because <c>DirectoryNotFoundException</c> derives from <c>IOException</c>, so whichever one
    /// survives still returns the same empty list. It took a <b>compound</b> canary disabling both
    /// at once to redden this test. That is the honest description: neither line is individually
    /// load-bearing, so no comment should claim one of them is "the guard".
    /// </remarks>
    [TestMethod]
    public void AMissingDirectoryYieldsNoNamesAndNoThrow()
    {
        Assert.AreEqual(
            0,
            OpenCodeThemeDiscovery
                .DiscoverFileThemes(Path.Combine(Path.GetTempPath(), "octheme_absent_" + Guid.NewGuid().ToString("N")))
                .Count);
    }

    [TestMethod]
    public void ThemeFilesBecomeNames_BuiltInFirstThenSorted()
    {
        string dir = NewDir();
        try
        {
            SeedTheme(dir, "zenburn");
            SeedTheme(dir, "dracula");
            SeedTheme(dir, "nord");

            IReadOnlyList<string> names =
                OpenCodeThemeDiscovery.Discover(new OpenCodeEnvironment(ConfigDir: dir));

            CollectionAssert.AreEqual(
                new[] { OpenCodeThemeDiscovery.BuiltInTheme, "dracula", "nord", "zenburn" },
                names.ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A theme's name is its file name without the extension — that is how OpenCode keys them, so
    /// case is preserved rather than normalised.
    /// </summary>
    [TestMethod]
    public void TheNameIsTheFileNameWithoutItsExtension_CasePreserved()
    {
        string dir = NewDir();
        try
        {
            SeedTheme(dir, "My-Custom_Theme");

            Assert.AreEqual(
                "My-Custom_Theme",
                OpenCodeThemeDiscovery.DiscoverFileThemes(dir).Single());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// ⚠ OpenCode lets a <c>themes/opencode.json</c> shadow the built-in, so the name exists twice
    /// in principle. A picker must still list it once.
    /// </summary>
    [TestMethod]
    public void AFileNamedLikeTheBuiltInDoesNotProduceADuplicate()
    {
        string dir = NewDir();
        try
        {
            SeedTheme(dir, OpenCodeThemeDiscovery.BuiltInTheme);
            SeedTheme(dir, "nord");

            IReadOnlyList<string> names =
                OpenCodeThemeDiscovery.Discover(new OpenCodeEnvironment(ConfigDir: dir));

            CollectionAssert.AreEqual(
                new[] { OpenCodeThemeDiscovery.BuiltInTheme, "nord" },
                names.ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Only <c>*.json</c> counts, and only the top level — OpenCode's own glob is
    /// <c>themes/*.json</c>, which matches neither a nested file nor another extension.
    /// </summary>
    [TestMethod]
    public void NonJsonAndNestedFilesAreIgnored()
    {
        string dir = NewDir();
        try
        {
            string themes = Path.Combine(dir, OpenCodeThemeDiscovery.ThemeDirectoryName);
            Directory.CreateDirectory(Path.Combine(themes, "nested"));
            File.WriteAllText(Path.Combine(themes, "readme.md"), "not a theme");
            File.WriteAllText(Path.Combine(themes, "nested", "deep.json"), "{}");
            SeedTheme(dir, "real");

            CollectionAssert.AreEqual(
                new[] { "real" },
                OpenCodeThemeDiscovery.DiscoverFileThemes(dir).ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// ⚠ The file's CONTENT is never parsed, deliberately. OpenCode reads it to render colours; this
    /// app only needs the name, and refusing to offer a theme because its JSON is malformed would
    /// hide a theme the user can still select by typing it.
    /// </summary>
    [TestMethod]
    public void AMalformedThemeFileStillContributesItsName()
    {
        string dir = NewDir();
        try
        {
            string themes = Path.Combine(dir, OpenCodeThemeDiscovery.ThemeDirectoryName);
            Directory.CreateDirectory(themes);
            File.WriteAllText(Path.Combine(themes, "broken.json"), "{ this is not json");

            CollectionAssert.AreEqual(
                new[] { "broken" },
                OpenCodeThemeDiscovery.DiscoverFileThemes(dir).ToArray());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// ⭐ The built-in name is stated twice — here and as the sole <c>examples</c> entry in
    /// <c>opencode-tui.overlay.json</c> — so this pins them together. Two copies of one fact is
    /// exactly the pair that drifts, and the overlay's copy is what makes the field a picker at all.
    /// </summary>
    [TestMethod]
    public void TheBuiltInNameMatchesTheSchemaOverlay()
    {
        string overlay = OverlayPath();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(overlay));

        JsonElement examples = doc.RootElement
            .GetProperty("properties")
            .GetProperty("theme")
            .GetProperty("examples");

        string[] values = [.. examples.EnumerateArray().Select(e => e.GetString() ?? string.Empty)];

        CollectionAssert.AreEqual(
            new[] { OpenCodeThemeDiscovery.BuiltInTheme },
            values,
            $"the overlay at {overlay} must offer exactly the built-in theme; discovery adds the rest");
    }

    /// <summary>
    /// ⚠ The overlay must stay ADDITIVE. RFC 7396 makes its keys win unconditionally, so a
    /// constraint keyword added here would silently replace the upstream schema's — and the TUI
    /// schema's own <c>enum: [false]</c> arms are what stop this app writing values OpenCode
    /// rejects.
    /// </summary>
    [TestMethod]
    public void TheOverlayCarriesNoConstraintKeywords()
    {
        string text = File.ReadAllText(OverlayPath());
        using JsonDocument doc = JsonDocument.Parse(text);

        List<string> found = [];
        Walk(doc.RootElement, found);

        Assert.AreEqual(
            0,
            found.Count,
            "constraint keywords in an RFC 7396 overlay replace the upstream schema's: "
                + string.Join(", ", found));

        static void Walk(JsonElement node, List<string> found)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in node.EnumerateObject())
                {
                    if (p.Name is "pattern" or "enum" or "required" or "const"
                        or "minLength" or "maxLength")
                    {
                        found.Add(p.Name);
                    }

                    Walk(p.Value, found);
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in node.EnumerateArray())
                {
                    Walk(item, found);
                }
            }
        }
    }

    private static string OverlayPath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClaudeForge.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.IsNotNull(dir, "could not locate the repository root from the test binary");
        string path = Path.Combine(
            dir!.FullName, "src", "AgentForge.Core", "Assets", "Schemas",
            "opencode-tui.overlay.json");

        Assert.IsTrue(File.Exists(path), $"the overlay is missing from {path}");
        return path;
    }
}
