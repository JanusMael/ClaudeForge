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
    public static ProductDescriptor For(string root) =>
        new(Id, DisplayName, "bundled://neutral-test", SchemaFileName,
            ArchiveFolder: ArchiveFolder,
            BackupLayout: new ProductBackupLayout(
                Sections:
                [
                    ProductArchiveSection.File(ConfigSection, () => ConfigPath(root),
                        "Restoring the neutral config…"),
                    ProductArchiveSection.Home(HomeSection, () => HomePath(root),
                        "Restoring the neutral home…"),
                ],
                SkippedSubdirs:
                [
                    new(SkippedSubdir, "Working files; never archived."),
                    new(FullOnlySubdir, "Large history; only a full backup keeps it.",
                        IncludedInFullBackup: true),
                ],
                CredentialFileName: CredentialFileName));

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
