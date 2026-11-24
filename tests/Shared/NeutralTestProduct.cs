using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Testing;

/// <summary>
/// A product that is not Claude, for testing the neutral engines in <c>AgentForge.Core</c> and
/// <c>AgentForge.Sdk</c> without Claude's data (<c>plans/00008</c> decision 6, step 5).
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Every name here differs from Claude's on purpose.</b> The archive folder, section
/// names, home directory, skipped subdirectories and credential file are all its own, so a test
/// built on this product fails if an engine still reaches for a Claude name instead of reading the
/// descriptor. A double that copied Claude's layout would pass whether or not the engine was
/// neutral.
/// </para>
/// <para>
/// ⚠ <b>Its ladder is <see cref="ScopeLadder.Default"/> until step 13c.</b> Until 13a, the shell
/// still falls back to the <see cref="ConfigScope"/> statics, so a ladder of its own would test
/// that fallback, not the product. 13c gives it one in the same change that moves the defaults.
/// </para>
/// <para>
/// ⚠ <b>It has no bundled schema.</b> <see cref="SchemaFileName"/> names no resource, so a backup
/// of this product carries no <c>Schemas/</c> entry for it and a restore does not validate it.
/// Tests of schema bundling and restore validation stay on Claude's descriptors until step 6
/// moves that data.
/// </para>
/// <para>
/// ⛔ Linked into each test project that needs it, never copied, so there is one definition.
/// </para>
/// </remarks>
internal static class NeutralTestProduct
{
    /// <summary>The product id.</summary>
    public const string Id = "neutral-test";

    /// <summary>The product's display name.</summary>
    public const string DisplayName = "Neutral Test Product";

    /// <summary>The folder this product's sections live under inside an archive.</summary>
    public const string ArchiveFolder = "NeutralTest";

    /// <summary>The schema file name; no resource carries it.</summary>
    public const string SchemaFileName = "neutral-test-settings.json";

    /// <summary>The archive name of the single-file section.</summary>
    public const string ConfigSection = "neutral-config.json";

    /// <summary>The archive name of the product-home section.</summary>
    public const string HomeSection = "neutral-home";

    /// <summary>The live file the single-file section restores to, under the root.</summary>
    public const string ConfigFileName = "neutral.json";

    /// <summary>The live home directory, under the root.</summary>
    public const string HomeDirectoryName = ".neutral";

    /// <summary>A subdirectory of the home that no backup archives.</summary>
    public const string SkippedSubdir = "scratch";

    /// <summary>A subdirectory of the home that only a full backup archives.</summary>
    public const string FullOnlySubdir = "history";

    /// <summary>The credential file in the home, archived only on an explicit opt-in.</summary>
    public const string CredentialFileName = "token.json";

    /// <summary>The ladder this product's scopes come from; see the class remarks.</summary>
    public static ScopeLadder Ladder => ScopeLadder.Default;

    /// <summary>The live config file for a product rooted at <paramref name="root"/>.</summary>
    public static string ConfigPath(string root) => Path.Combine(root, ConfigFileName);

    /// <summary>The live home directory for a product rooted at <paramref name="root"/>.</summary>
    public static string HomePath(string root) => Path.Combine(root, HomeDirectoryName);

    /// <summary>
    /// The descriptor for a product whose files live under <paramref name="root"/>: one config
    /// file and one home directory, with one skipped and one full-only subdirectory.
    /// </summary>
    public static ProductDescriptor For(string root) => For(() => root);

    /// <summary>
    /// <see cref="For(string)"/>, with the root read each time a path is needed, as a real
    /// product's paths follow the redirected profile.
    /// </summary>
    public static ProductDescriptor For(Func<string> root) =>
        new(Id, DisplayName, "bundled://neutral-test", SchemaFileName,
            ArchiveFolder: ArchiveFolder,
            BackupLayout: new ProductBackupLayout(
                Sections:
                [
                    ProductArchiveSection.File(ConfigSection, () => ConfigPath(root()),
                        "Restoring the neutral config…"),
                    ProductArchiveSection.Home(HomeSection, () => HomePath(root()),
                        "Restoring the neutral home…"),
                ],
                SkippedSubdirs:
                [
                    new(SkippedSubdir, "Working files; never archived."),
                    new(FullOnlySubdir, "Large history; only a full backup keeps it.",
                        IncludedInFullBackup: true),
                ],
                CredentialFileName: CredentialFileName));

    /// <summary>What the neutral product keeps in each project: one directory and one file.</summary>
    public static readonly IReadOnlyList<string> ProjectItems = [".neutral", "NEUTRAL.md"];

    /// <summary>
    /// <see cref="For(string)"/> with a <see cref="ProductProjectLayout"/> over
    /// <see cref="ProjectItems"/>, and the two discovery sources a test supplies.
    /// </summary>
    public static ProductDescriptor WithProjects(
        string root,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> relatedProjects,
        Func<ProductKnownProjects> knownProjects)
    {
        ProductDescriptor plain = For(root);
        return plain with
        {
            BackupLayout = plain.Backup with
            {
                Projects = new ProductProjectLayout(ProjectItems, relatedProjects, knownProjects),
            },
        };
    }

    /// <summary>The bundled schema's overlay, named by the engines' convention.</summary>
    public const string OverlayFileName = "neutral-test-settings.overlay.json";

    /// <summary>The bundled enum descriptions, named by the engines' convention.</summary>
    public const string EnumDescriptionsFileName = "neutral-test-settings.enumdescriptions.json";

    /// <summary>
    /// The bundled schema: an object whose <c>theme</c> is one of two strings. The overlay adds a
    /// <c>default</c> the base does not carry, so a test can tell whether it was applied.
    /// </summary>
    public const string SchemaJson = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "properties": {
            "neutral": { "type": "boolean" },
            "theme": { "type": "string", "enum": ["dark", "light"] }
          }
        }
        """;

    /// <summary>Adds <c>theme.default</c>; see <see cref="SchemaJson"/>.</summary>
    public const string OverlayJson = """{ "properties": { "theme": { "default": "dark" } } }""";

    /// <summary>One description per <c>theme</c> value.</summary>
    public const string EnumDescriptionsJson =
        """{ "theme": { "dark": "Dark background.", "light": "Light background." } }""";

    /// <summary>The three files above, as the product would ship them.</summary>
    public static InMemoryBundledFiles Bundle { get; } = new(new Dictionary<string, string>
    {
        [SchemaFileName] = SchemaJson,
        [OverlayFileName] = OverlayJson,
        [EnumDescriptionsFileName] = EnumDescriptionsJson,
    });

    /// <summary>
    /// <see cref="For"/>, plus a bundled schema and the archive files a restore validates against
    /// it: the config file, and every <c>settings.json</c> in the home.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="For"/> so the engine tests written before the descriptor carried
    /// data keep the archives they were written against: a bundled schema adds entries under
    /// <c>Schemas/</c>.
    /// </remarks>
    public static ProductDescriptor WithBundle(string root)
    {
        ProductDescriptor plain = For(root);
        return plain with
        {
            BackupLayout = plain.Backup with
            {
                ValidatableFiles =
                [
                    new([], [ConfigSection], Recursive: false),
                    new([HomeSection], ["settings.json"], Recursive: true),
                ],
            },
            BundledFiles = Bundle,
        };
    }

    /// <summary>
    /// A second product, for tests that need two: one file, its own id, archive folder and
    /// section, so selecting or restoring one can be told apart from the other.
    /// </summary>
    public static ProductDescriptor Second(string root) => Second(() => root);

    /// <inheritdoc cref="Second(string)"/>
    public static ProductDescriptor Second(Func<string> root) =>
        new(Id + "-b", DisplayName + " B", "bundled://neutral-test-b", "neutral-test-b-settings.json",
            ArchiveFolder: ArchiveFolder + "B",
            BackupLayout: new ProductBackupLayout(
                Sections:
                [
                    ProductArchiveSection.File("neutral-b.json", () => Path.Combine(root(), "neutral-b.json"),
                        "Restoring the second neutral config…"),
                ],
                SkippedSubdirs: []));

    /// <summary>
    /// The product a test client edits: this product's names and bundled schema, with one home
    /// section at <paramref name="home"/>, where the client's discoverer finds its settings.
    /// </summary>
    /// <remarks>
    /// ⚠ The test clients still discover through <c>ConfigFileDiscoverer</c>, which knows only
    /// Claude's layout until <c>plans/00008</c> step 11. So the caller passes the directory that
    /// discoverer reads, from the same <c>PlatformPaths</c> member, rather than this type copying
    /// Claude's layout. Nothing else here is Claude's: the archive folder, the section name and the
    /// schema are this product's own.
    /// </remarks>
    public static ProductDescriptor WithHome(Func<string> home) =>
        new(Id, DisplayName, "bundled://neutral-test", SchemaFileName,
            ArchiveFolder: ArchiveFolder,
            BackupLayout: new ProductBackupLayout(
                Sections: [ProductArchiveSection.Home(HomeSection, home, "Restoring the neutral home…")],
                SkippedSubdirs: [],
                ValidatableFiles: [new([HomeSection], ["settings.json"], Recursive: true)]))
        {
            BundledFiles = Bundle,
        };

    /// <summary>
    /// Writes a small live footprint under <paramref name="root"/>: the config file, a settings
    /// file in the home, and one file in each of the skipped and full-only subdirectories.
    /// </summary>
    public static void Seed(string root)
    {
        string home = HomePath(root);
        Directory.CreateDirectory(Path.Combine(home, SkippedSubdir));
        Directory.CreateDirectory(Path.Combine(home, FullOnlySubdir));
        File.WriteAllText(ConfigPath(root), """{"neutral":true}""");
        File.WriteAllText(Path.Combine(home, "settings.json"), """{"theme":"dark"}""");
        File.WriteAllText(Path.Combine(home, SkippedSubdir, "work.tmp"), "scratch");
        File.WriteAllText(Path.Combine(home, FullOnlySubdir, "session.jsonl"), """{"turn":1}""");
    }
}

/// <summary>
/// <see cref="IBundledFiles"/> over strings held in memory, for a test product that ships its data
/// without an assembly to embed it in. Counts its reads, so a test can show a file was asked for.
/// </summary>
internal sealed class InMemoryBundledFiles(IReadOnlyDictionary<string, string> files) : IBundledFiles
{
    private int _reads;

    /// <summary>How many times <see cref="TryRead"/> has been called.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <inheritdoc/>
    public byte[]? TryRead(string fileName)
    {
        Interlocked.Increment(ref _reads);
        return files.TryGetValue(fileName, out string? text) ? System.Text.Encoding.UTF8.GetBytes(text) : null;
    }
}
