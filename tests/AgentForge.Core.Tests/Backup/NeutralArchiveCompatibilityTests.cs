using System.IO.Compression;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Testing;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Restores a <b>frozen archive of <see cref="NeutralTestProduct"/></b> and asserts every file
/// lands where the descriptor says.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Core's own compatibility test</b> (<c>plans/00008</c> decision 6).
/// <c>BackupArchiveCompatibilityTests</c> restores a shipped Claude archive into Claude's paths, so
/// it leaves for <c>ClaudeForge.Sdk.Claude.Tests</c> with Claude's descriptors. This one stays with
/// the engine: an archive written by a product the engine has never heard of, read back through
/// nothing but that product's descriptor.
/// </para>
/// <para>
/// ⛔⛔ <b><c>Fixtures/backup-v2-neutral.zip</c> MUST NOT BE REGENERATED.</b> It was minted on
/// 2026-11-16 by the engine as it was then — <c>manifest.json</c> at <c>schemaVersion 2</c>, a
/// full backup with the credential opt-in, entries under <c>NeutralTest/</c>, and no
/// <c>Schemas/</c> because the product bundles none. Re-minting it against a changed writer turns
/// this suite into a round-trip of the writer with itself. If a change cannot restore it, that is
/// the finding: add a migration, or a second fixture beside it.
/// </para>
/// <para>
/// ⚠ The descriptor's names (<see cref="NeutralTestProduct.ArchiveFolder"/>, the section names)
/// are now PERSISTED in this fixture. Changing one in <c>NeutralTestProduct</c> breaks this suite,
/// which is the same contract a real product's <see cref="ProductDescriptor.ArchiveFolder"/> has.
/// </para>
/// </remarks>
public sealed class NeutralArchiveCompatibilityTests : IDisposable
{
    private const string FixtureName = "backup-v2-neutral.zip";

    private readonly string _root;
    private readonly BackupEngine _engine;

    public NeutralArchiveCompatibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        BackupEngine.InvalidateListCache();
        // The engine still reads Claude's settings to discover projects until plans/00008 step 8;
        // this keeps that read inside the sandbox.
        PlatformPaths.TestUserProfileOverride = _root;
        _engine = new BackupEngine(ClaudeEnvironment.Empty, restorableProducts: [NeutralTestProduct.For(_root)]);
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

    // ── What the frozen archive contains ───────────────────────────────────

    [Fact]
    public void TheFixture_StillCarriesExactlyItsMintedEntries()
    {
        // ⚠ Asserted on the FIXTURE, and as literals: this is the statement "this archive looks
        // like this", and it must not start tracking whatever the writer produces today.
        string[] expected =
        [
            "NeutralTest/neutral-config.json",
            "NeutralTest/neutral-home/agents/reviewer.md",
            "NeutralTest/neutral-home/history/session.jsonl",
            "NeutralTest/neutral-home/settings.json",
            "NeutralTest/neutral-home/token.json",
            "manifest.json",
        ];

        Assert.Equal(expected, Entries());
    }

    [Fact]
    public void TheFixture_IsAtManifestSchemaVersionTwo_ForTheNeutralClient()
    {
        string json = ReadFixtureEntry("manifest.json");

        MessageAssert.Contains("\"schemaVersion\": 2", json,
            "The frozen archive is a v2 manifest. If this fails, the fixture was regenerated — "
            + "restore it from git rather than updating this assertion.");
        OrdinalAssert.Contains("\"NeutralTest\"", json);
        OrdinalAssert.Contains("\"includedCredentials\": true", json);
    }

    // ── That it still restores ─────────────────────────────────────────────

    [Fact]
    public async Task TheArchive_RestoresEveryFileWhereTheDescriptorSays()
    {
        BackupEntry entry = StageFixture();

        RestoreResult restore = await _engine.RestoreAsync(entry, progress: null, TestContext.Current.CancellationToken);

        Assert.True(restore.Succeeded, "A frozen archive must keep restoring. " + restore.Message);
        string home = NeutralTestProduct.HomePath(_root);
        OrdinalAssert.Contains("\"neutral-v1\"",
            await File.ReadAllTextAsync(NeutralTestProduct.ConfigPath(_root), TestContext.Current.CancellationToken));
        OrdinalAssert.Contains("\"fixture-model\"",
            await File.ReadAllTextAsync(Path.Combine(home, "settings.json"), TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(home, "agents", "reviewer.md")),
            "Nested home entries must restore, not just top-level ones.");
        Assert.True(File.Exists(Path.Combine(home, NeutralTestProduct.FullOnlySubdir, "session.jsonl")),
            "A full archive's full-only subdirectory must restore.");
        Assert.True(File.Exists(Path.Combine(home, NeutralTestProduct.CredentialFileName)),
            "An archive taken with the credential opt-in must restore the credential file.");
    }

    [Fact]
    public async Task TheArchive_IsListedAsRestorable()
    {
        StageFixture();

        IReadOnlyList<BackupEntry> entries = _engine.List(_root);

        MessageAssert.Equal(1, entries.Count, "The frozen archive must appear in the restorable list.");
        Assert.False(entries[0].IsCorrupt);
        await Task.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string FixturePath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureName);
        Assert.True(File.Exists(path),
            $"Fixture '{FixtureName}' was not copied to the output directory. Check the "
            + "Content item in AgentForge.Core.Tests.csproj.");
        return path;
    }

    private static List<string> Entries()
    {
        using ZipArchive zip = ZipFile.OpenRead(FixturePath());
        return [.. zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal)];
    }

    private static string ReadFixtureEntry(string name)
    {
        using ZipArchive zip = ZipFile.OpenRead(FixturePath());
        ZipArchiveEntry entry = zip.GetEntry(name)
                                ?? throw new Xunit.Sdk.XunitException($"The fixture has no {name}.");
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>Copy the frozen archive into the sandbox under a name the list recognises.</summary>
    private BackupEntry StageFixture()
    {
        string staged = Path.Combine(_root, "backup-20261116-000000.zip");
        File.Copy(FixturePath(), staged, overwrite: true);
        return _engine.TryReadEntry(staged)
               ?? throw new Xunit.Sdk.XunitException("The staged fixture could not be read.");
    }
}
