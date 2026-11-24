using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;
using Bennewitz.Ninja.AgentForge.Testing;
using JsonSchema = Json.Schema.JsonSchema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// plans/00008 step 6: a product's schema, overlay and enum descriptions reach the schema registry
/// and the backup and restore engines through <see cref="ProductDescriptor.BundledFiles"/>, never
/// from this library's own manifest.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Every test here uses the neutral test product, whose files exist only in memory.</b> A
/// product whose data sits in Core's manifest cannot tell "read through the descriptor" from "read
/// from Core": both find the file. These files are in no assembly, so any path that still looks in
/// a manifest finds nothing and the test goes red.
/// </para>
/// </remarks>
public sealed class BundledFilesThroughDescriptorTests : IDisposable
{
    private readonly string _root;

    public BundledFilesThroughDescriptorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bfd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        BackupEngine.InvalidateListCache();
        PlatformPaths.TestUserProfileOverride = _root;
        NeutralTestProduct.Seed(_root);
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

    // ── The registry ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheRegistry_LoadsAProductsSchemaFromItsBundledFiles_WithTheOverlayApplied()
    {
        using SchemaRegistry registry = new();

        JsonSchema schema = await registry.GetSchemaAsync(
            NeutralTestProduct.WithBundle(_root), TestContext.Current.CancellationToken);

        Assert.NotNull(schema);
        MessageAssert.Equal(SchemaSource.Bundled, registry.ProvenanceFor(NeutralTestProduct.SchemaFileName)?.Source,
            "With no network and no disk, the only source is the product's bundle.");
        JsonNode merged = JsonNode.Parse(
            SchemaRegistry.TryReadBundledBytesMerged(NeutralTestProduct.WithBundle(_root))!)!;
        MessageAssert.Equal("dark", merged["properties"]?["theme"]?["default"]?.GetValue<string>(),
            "The overlay's default must be merged onto the base schema. Absent, the overlay was "
            + "looked for somewhere other than the product's bundle.");
    }

    [Fact]
    public async Task AProductWithNoBundledFiles_HasNoOfflineSchema()
    {
        using SchemaRegistry registry = new();

        await Assert.ThrowsAsync<SchemaUnavailableException>(() => registry.GetSchemaAsync(
            NeutralTestProduct.For(_root), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EnumDescriptions_AreReadFromTheProductsBundle()
    {
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> descriptions =
            SchemaRegistry.GetEnumDescriptions(NeutralTestProduct.WithBundle(_root));

        Assert.True(descriptions.TryGetValue("theme", out IReadOnlyDictionary<string, string>? theme),
            "The product's enum descriptions were not read.");
        Assert.Equal("Light background.", theme!["light"]);
        Assert.Empty(SchemaRegistry.GetEnumDescriptions(NeutralTestProduct.For(_root)));
    }

    // ── The backup engine ─────────────────────────────────────────────────

    [Fact]
    public async Task ABackup_CarriesTheProductsBundledSchemaAndOverlay_AndNothingElse()
    {
        ProductDescriptor product = NeutralTestProduct.WithBundle(_root);
        string zip = await CreateBackupAsync(product);

        using ZipArchive archive = ZipFile.OpenRead(zip);
        string[] schemas = archive.Entries
            .Select(e => e.FullName)
            .Where(name => name.StartsWith("Schemas/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Nothing of Claude's: the engine no longer scans Core's manifest, so the bundled Claude and
        // OpenCode schemas that are still embedded there cannot leak into another product's archive.
        Assert.Equal(
            [$"Schemas/{NeutralTestProduct.SchemaFileName}", $"Schemas/{NeutralTestProduct.OverlayFileName}"],
            schemas);
        Assert.Equal(NeutralTestProduct.SchemaJson, ReadEntry(archive, $"Schemas/{NeutralTestProduct.SchemaFileName}"));
        Assert.Equal(NeutralTestProduct.OverlayJson, ReadEntry(archive, $"Schemas/{NeutralTestProduct.OverlayFileName}"));
    }

    [Fact]
    public async Task ABackupOfAProductWithNoBundledFiles_CarriesNoSchemas()
    {
        string zip = await CreateBackupAsync(NeutralTestProduct.For(_root));

        using ZipArchive archive = ZipFile.OpenRead(zip);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("Schemas/", StringComparison.Ordinal));
    }

    // ── The restore engine ────────────────────────────────────────────────

    [Fact]
    public async Task ARestore_ValidatesTheFilesTheProductNames_AgainstItsBundledSchema()
    {
        // Invalid against the bundled schema: theme is one of two strings.
        await File.WriteAllTextAsync(
            Path.Combine(NeutralTestProduct.HomePath(_root), "settings.json"), """{"theme":"purple"}""",
            TestContext.Current.CancellationToken);
        ProductDescriptor product = NeutralTestProduct.WithBundle(_root);

        RestoreResult restore = await RestoreFreshBackupAsync(product);

        Assert.True(restore.Succeeded, restore.Message);
        string expectedFile = $"{NeutralTestProduct.ArchiveFolder}/{NeutralTestProduct.HomeSection}/settings.json";
        Assert.True((restore.ValidationWarnings ?? []).Any(w => w.StartsWith(expectedFile + ":", StringComparison.Ordinal)),
            "The home's settings.json breaks the product's schema, and the product names it as "
            + "validatable, so the restore must warn about it. No warning means the restore did not "
            + "find the file through the product's layout. Warnings: "
            + string.Join(" | ", restore.ValidationWarnings ?? []));
    }

    [Fact]
    public async Task ARestore_DoesNotValidateFilesTheProductDoesNotName()
    {
        await File.WriteAllTextAsync(
            Path.Combine(NeutralTestProduct.HomePath(_root), "settings.json"), """{"theme":"purple"}""",
            TestContext.Current.CancellationToken);
        ProductDescriptor bundledButSilent = NeutralTestProduct.WithBundle(_root) is var p
            ? p with { BackupLayout = p.Backup with { ValidatableFiles = null } }
            : throw new InvalidOperationException();

        RestoreResult restore = await RestoreFreshBackupAsync(bundledButSilent);

        Assert.True(restore.Succeeded, restore.Message);
        Assert.Empty(restore.ValidationWarnings ?? []);
    }

    // ── EmbeddedResourceFiles ─────────────────────────────────────────────

    [Fact]
    public void EmbeddedResourceFiles_RefusesAPrefixThatMatchesNothing()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            new EmbeddedResourceFiles(typeof(SchemaRegistry).Assembly, "No.Such.Prefix."));
        Assert.Contains("No.Such.Prefix.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedResourceFiles_ReadsAFile_AndAnswersNullForOneThatIsNotThere()
    {
        IBundledFiles files = BundledProduct.OpenCodeFixtures;

        Assert.NotNull(files.TryRead("opencode-config.json"));
        Assert.Null(files.TryRead("no-such-file.json"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<string> CreateBackupAsync(ProductDescriptor product)
    {
        string zip = Path.Combine(_root, "backups", "backup-20300101-000000.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        BackupResult result = await new BackupEngine([product]).CreateAsync(
            new BackupRequest { DestinationZipPath = zip, Products = [product] },
            progress: null, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Message);
        return zip;
    }

    private async Task<RestoreResult> RestoreFreshBackupAsync(ProductDescriptor product)
    {
        string zip = await CreateBackupAsync(product);
        BackupEngine engine = new([product]);
        BackupEntry entry = engine.TryReadEntry(zip)!;
        return await engine.RestoreAsync(entry, progress: null, TestContext.Current.CancellationToken);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using StreamReader reader = new(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
