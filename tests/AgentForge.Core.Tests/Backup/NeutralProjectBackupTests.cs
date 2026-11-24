using System.IO.Compression;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Testing;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// The project half of a backup and a restore, through a <see cref="ProductProjectLayout"/> on
/// <see cref="NeutralTestProduct"/>: what is captured from each project, which projects, and which
/// a restore may write to.
/// </summary>
/// <remarks>
/// ⭐ <b>Every name here is one only the layout knows.</b> Until plans/00008 step 8 the engines
/// hardcoded Claude Code's four project items and read Claude's own project lists, so a test on
/// Claude's descriptor could not tell an engine that reads the layout from one that does not. The
/// items here are <c>.neutral</c> and <c>NEUTRAL.md</c>, and both discovery sources are delegates
/// the test controls.
/// </remarks>
public sealed class NeutralProjectBackupTests : IDisposable
{
    private readonly string _root;
    private readonly string _open;
    private readonly string _related;
    private readonly string _known;

    public NeutralProjectBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nproj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        BackupEngine.InvalidateListCache();
        PlatformPaths.TestUserProfileOverride = _root;
        NeutralTestProduct.Seed(_root);

        _open = SeedProject("OpenProject");
        _related = SeedProject("RelatedProject");
        _known = SeedProject("KnownProject");
    }

    public void Dispose()
    {
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A project with both items and one file that is not an item.</summary>
    private string SeedProject(string name)
    {
        string project = Path.Combine(_root, "work", name);
        Directory.CreateDirectory(Path.Combine(project, ".neutral"));
        File.WriteAllText(Path.Combine(project, ".neutral", "settings.json"), """{"theme":"dark"}""");
        File.WriteAllText(Path.Combine(project, "NEUTRAL.md"), "# notes");
        File.WriteAllText(Path.Combine(project, "source.txt"), "not an item");
        return project;
    }

    private ProductDescriptor Product(
        Func<IReadOnlyList<string>, IReadOnlyList<string>>? related = null,
        Func<ProductKnownProjects>? known = null) =>
        NeutralTestProduct.WithProjects(_root, related ?? (_ => []), known ?? (() => ProductKnownProjects.None));

    private static string ProjectEntry(string project, string relative) =>
        $"{NeutralTestProduct.ArchiveFolder}/projects/{Path.GetFileName(project)}/{relative}";

    private async Task<(BackupResult Result, List<string> Entries)> BackUpAsync(
        ProductDescriptor restorable, ProductDescriptor requested, BackupMode mode)
    {
        string zip = Path.Combine(_root, "backups", $"backup-{mode}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        BackupResult result = await new BackupEngine([restorable]).CreateAsync(
            new BackupRequest
            {
                DestinationZipPath = zip,
                Mode = mode,
                Products = [requested],
                ExplicitProjectDirs = [_open],
            },
            progress: null, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Message);

        using ZipArchive archive = ZipFile.OpenRead(zip);
        return (result, [.. archive.Entries.Select(e => e.FullName.Replace('\\', '/'))]);
    }

    // ── What a backup captures from a project ─────────────────────────────

    [Fact]
    public async Task TheOpenProject_IsCapturedByItsItems_UnderTheProductsOwnFolder()
    {
        ProductDescriptor product = Product();

        (_, List<string> entries) = await BackUpAsync(product, product, BackupMode.SettingsOnly);

        Assert.Contains(ProjectEntry(_open, ".neutral/settings.json"), entries);
        Assert.Contains(ProjectEntry(_open, "NEUTRAL.md"), entries);
        MessageAssert.DoesNotContain(ProjectEntry(_open, "source.txt"), entries,
            "Only the layout's items are captured from a project, never the rest of its tree.");
    }

    [Fact]
    public async Task RelatedProjects_AreAskedWithTheOpenRoots_AndCapturedInEveryMode()
    {
        List<IReadOnlyList<string>> asked = [];
        ProductDescriptor product = Product(related: open =>
        {
            asked.Add(open);
            return [_related];
        });

        (_, List<string> entries) = await BackUpAsync(product, product, BackupMode.SettingsOnly);

        Assert.Contains(ProjectEntry(_related, "NEUTRAL.md"), entries);
        IReadOnlyList<string> first = Assert.Single(asked);
        MessageAssert.Equal(_open, Assert.Single(first),
            "The provider is given the roots the host has open, which is what its configuration is read beside.");
    }

    [Fact]
    public async Task KnownProjects_AreCapturedByAFullBackupOnly()
    {
        ProductDescriptor product = Product(known: () => new ProductKnownProjects([_known], 0));

        (_, List<string> settingsOnly) = await BackUpAsync(product, product, BackupMode.SettingsOnly);
        (_, List<string> full) = await BackUpAsync(product, product, BackupMode.Full);

        MessageAssert.DoesNotContain(ProjectEntry(_known, "NEUTRAL.md"), settingsOnly,
            "A settings-only backup keeps to the open project; every known project is a full backup's job.");
        Assert.Contains(ProjectEntry(_known, "NEUTRAL.md"), full);
    }

    [Fact]
    public async Task AFullBackup_WarnsAboutKnownProjectsThatNoLongerExist()
    {
        ProductDescriptor product = Product(known: () => new ProductKnownProjects([_known], 2));

        (BackupResult result, _) = await BackUpAsync(product, product, BackupMode.Full);

        Assert.Contains(result.Manifest!.Warnings, w => w.Contains("skipped 2 known project", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARequestedProductWithNoProjectLayout_CapturesNoProjectData_ButTheManifestKeepsTheHostsProjects()
    {
        // Discovery comes from the host's products, as it did before step 8; capture follows the
        // request. A backup of a product that keeps nothing in projects lists them and copies none.
        ProductDescriptor host = Product(related: _ => [_related]);
        ProductDescriptor plain = NeutralTestProduct.For(_root);

        (BackupResult result, List<string> entries) = await BackUpAsync(host, plain, BackupMode.SettingsOnly);

        // A product with no project layout contributes no project files.
        Assert.DoesNotContain(entries, e => e.Contains("/projects/", StringComparison.Ordinal));
        Assert.Contains(_related, result.Manifest!.Projects);
    }

    // ── What a restore may write to ───────────────────────────────────────

    [Fact]
    public void BuildAuthorisedRoots_TakesTheOpenKnownAndRelatedRoots()
    {
        List<IReadOnlyList<string>> asked = [];
        ProductDescriptor product = Product(
            related: open =>
            {
                asked.Add(open);
                return [_related];
            },
            known: () => new ProductKnownProjects([_known], 0));

        IReadOnlyCollection<string> roots = RestoreEngine.BuildAuthorisedRoots([product], [_open]);

        Assert.Contains(_open, roots);
        Assert.Contains(_known, roots);
        Assert.Contains(_related, roots);
        MessageAssert.Equal(_open, Assert.Single(Assert.Single(asked)),
            "Restore asks the same question backup asked, of the same open roots.");
    }

    [Fact]
    public void BuildAuthorisedRoots_AFailingRelatedSource_NarrowsTheSetWithoutFailing()
    {
        ProductDescriptor product = Product(
            related: _ => throw new IOException("settings file locked"),
            known: () => new ProductKnownProjects([_known], 0));

        IReadOnlyCollection<string> roots = RestoreEngine.BuildAuthorisedRoots([product], [_open]);

        Assert.Contains(_open, roots);
        Assert.Contains(_known, roots);
        MessageAssert.DoesNotContain(_related, roots, "The source that failed contributes nothing.");
    }

    [Fact]
    public void BuildAuthorisedRoots_AProductWithNoProjectLayout_AddsNothing()
    {
        IReadOnlyCollection<string> roots =
            RestoreEngine.BuildAuthorisedRoots([NeutralTestProduct.For(_root)], [_open]);

        MessageAssert.Equal(_open, Assert.Single(roots), "Only the open project is authorised.");
    }

    // ── The round trip ────────────────────────────────────────────────────

    [Fact]
    public async Task AProjectsItems_AreRestoredToItsLivePath()
    {
        ProductDescriptor product = Product();
        (BackupResult result, _) = await BackUpAsync(product, product, BackupMode.SettingsOnly);
        string notes = Path.Combine(_open, "NEUTRAL.md");
        File.WriteAllText(notes, "# changed after the backup");

        BackupEngine engine = new([product]);
        BackupEntry entry = engine.TryReadEntry(result.ArchivePath!)!;
        RestoreResult restore = await engine.RestoreAsync(
            entry, progress: null, TestContext.Current.CancellationToken, openProjectRoots: [_open]);

        Assert.True(restore.Succeeded, restore.Message);
        MessageAssert.Equal("# notes", File.ReadAllText(notes),
            "The project's item is put back from the product's own projects folder.");
    }
}
