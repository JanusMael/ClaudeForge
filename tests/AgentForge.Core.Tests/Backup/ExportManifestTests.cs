using System.Text;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Backup;

/// <summary>
/// Round-trip and migration tests for <see cref="ExportManifest"/>, the sibling of
/// <see cref="BackupManifestTests"/>.
/// <para>
/// <b>Written because this format had no tests at all.</b> Before this file nothing in the
/// suite referenced <see cref="ExportManifest"/>, <c>ZipArchiveWriter.SerialiseExportManifest</c>
/// or <c>MainWindowViewModel.ExportAsync</c> — so which products an export claimed to cover
/// was unguarded end to end, in a format written to users' disks.
/// </para>
/// </summary>
[TestClass]
public sealed class ExportManifestTests
{
    private const string ClaudeCodeFolder = "ClaudeCode";
    private const string ClaudeDesktopFolder = "ClaudeDesktop";

    // ── v2, the shape written today ──────────────────────────────────────

    [TestMethod]
    public void RoundTrip_V2_PreservesEveryField()
    {
        // TWO clients on purpose. A single-product list would pass just as well with a
        // writer that only ever emits the first entry, and that exact hole — every test
        // exercising one product at a time — is what let a one-product save loop pass all
        // 2,814 tests in Phase 4d.
        ExportManifest original = new()
        {
            CreatedUtc = new DateTime(2030, 3, 24, 2, 0, 0, DateTimeKind.Utc),
            Platform = "linux",
            AppVersion = "1.2.3.4",
            Clients = [ClaudeCodeFolder, ClaudeDesktopFolder],
            HeaderComment = "Exported by ClaudeForge",
        };

        // Source-gen path — identical to production behaviour under trimming.
        string json = JsonSerializer.Serialize(original, BackupJsonContext.Default.ExportManifest);
        ExportManifest? round = ExportManifest.TryRead(Utf8(json));

        Assert.IsNotNull(round);
        Assert.AreEqual("export", round!.Kind);
        Assert.AreEqual(ExportManifest.CurrentSchemaVersion, round.SchemaVersion);
        Assert.AreEqual(original.CreatedUtc, round.CreatedUtc);
        Assert.AreEqual(original.Platform, round.Platform);
        Assert.AreEqual(original.AppVersion, round.AppVersion);
        Assert.AreEqual(original.HeaderComment, round.HeaderComment);
        CollectionAssert.AreEqual(original.Clients, round.Clients,
            "Both products must survive the round trip, in order.");
    }

    [TestMethod]
    public void V2Write_CarriesNoTraceOfTheV1Booleans()
    {
        ExportManifest m = new() { Clients = [ClaudeCodeFolder] };

        string json = JsonSerializer.Serialize(m, BackupJsonContext.Default.ExportManifest);

        Assert.IsFalse(json.Contains("includesClaude", StringComparison.Ordinal),
            "A v2 export must not emit the legacy booleans at all — not even as null. They "
            + "are read-only compatibility fields; writing them would leave a second, "
            + "silently stale statement of which products the archive covers. Actual JSON:\n"
            + json);
        StringAssert.Contains(json, "\"clients\"",
            "The product list is the whole point of v2.");
    }

    [TestMethod]
    public void SchemaVersion_DefaultIsCurrent()
    {
        ExportManifest m = new();
        Assert.AreEqual(ExportManifest.CurrentSchemaVersion, m.SchemaVersion);
    }

    [TestMethod]
    public void NewExport_Declares_SchemaVersion2_OnDisk()
    {
        // Pins the bump, through the bytes rather than the constant: comparing the constant
        // to a literal is folded at compile time and rejected as a tautology (MSTEST0032),
        // and asserting on the serialised text covers the JSON property name too.
        // If this fails because someone bumped to 3, TryRead must have learned to map v2
        // forward in the same commit — otherwise every archive written by a v2 build
        // silently reports no products at all.
        string json = JsonSerializer.Serialize(new ExportManifest(), BackupJsonContext.Default.ExportManifest);

        StringAssert.Contains(json, "\"schemaVersion\": 2",
            "v1 was two booleans; v2 is the Clients list. A further bump needs a matching "
            + $"branch in ExportManifest.TryRead. Actual JSON:\n{json}");
    }

    [TestMethod]
    public void Clients_UseTheSameVocabularyAsBackupManifest()
    {
        // Deliberately looks tautological, like ArchiveFolderNames_AreTheValuesAlreadyOn-
        // UsersDisks does for backups. Both manifests live in this folder, both are named
        // manifest.json inside their archive, and BackupRestoreViewModel.AbbreviateClient
        // renders BackupManifest.Clients directly. Two vocabularies for the same products is
        // precisely the mistake Phase 4d-2 removed.
        Assert.AreEqual(ClaudeCodeFolder, SchemaRegistry.ClaudeCodeProduct.ArchiveFolder);
        Assert.AreEqual(ClaudeDesktopFolder, SchemaRegistry.ClaudeDesktopProduct.ArchiveFolder);
    }

    // ── v1, the shape already on disk ────────────────────────────────────

    [TestMethod]
    [DataRow(true, true, ClaudeCodeFolder + "," + ClaudeDesktopFolder)]
    [DataRow(true, false, ClaudeCodeFolder)]
    [DataRow(false, true, ClaudeDesktopFolder)]
    [DataRow(false, false, "")]
    public void TryRead_V1_MapsTheBooleansOntoClients(
        bool includesCode, bool includesDesktop, string expectedCsv)
    {
        string v1 = V1Json(includesCode, includesDesktop);

        ExportManifest? read = ExportManifest.TryRead(Utf8(v1));

        Assert.IsNotNull(read, "A v1 manifest is still readable — v1 <= CurrentSchemaVersion.");
        string[] expected = expectedCsv.Length == 0 ? [] : expectedCsv.Split(',');
        CollectionAssert.AreEqual(expected, read!.Clients,
            "Without this mapping a v1 archive deserialises to an EMPTY product list, which "
            + "is indistinguishable from an export that genuinely covered nothing — a silent "
            + "wrong answer rather than a loud one.");
    }

    [TestMethod]
    public void TryRead_ManifestWithNoSchemaVersion_StillHonoursItsBooleans()
    {
        // This test failed when first written, and the reason is the finding: a missing
        // schemaVersion does NOT deserialise to 0. `SchemaVersion` has a property
        // initialiser, and System.Text.Json leaves an initialised value untouched when the
        // field is absent — so this manifest arrives claiming to be v2, and a purely
        // version-gated migration ignores its booleans and reports an export covering
        // nothing. TryRead's empty-list fallback is what makes it readable.
        string json =
            $$"""
            {
              "kind": "export",
              "includesClaudeCode": true,
              "includesClaudeDesktop": true
            }
            """;

        ExportManifest? read = ExportManifest.TryRead(Utf8(json));

        Assert.IsNotNull(read);
        CollectionAssert.AreEqual(new[] { ClaudeCodeFolder, ClaudeDesktopFolder }, read!.Clients);
    }

    [TestMethod]
    public void TryRead_V2WithStrayLegacyBooleans_KeepsClients()
    {
        // The version gate drives the mapping, not field presence. If it were the other way
        // round, a hand-edited or future manifest carrying both shapes would have its real
        // product list silently replaced by whatever the stale booleans said.
        string hybrid =
            $$"""
            {
              "kind": "export",
              "schemaVersion": 2,
              "clients": ["{{ClaudeDesktopFolder}}"],
              "includesClaudeCode": true,
              "includesClaudeDesktop": false
            }
            """;

        ExportManifest? read = ExportManifest.TryRead(Utf8(hybrid));

        Assert.IsNotNull(read);
        CollectionAssert.AreEqual(new[] { ClaudeDesktopFolder }, read!.Clients,
            "clients is authoritative at schemaVersion 2; the booleans must be ignored.");
    }

    // ── what TryRead must refuse ─────────────────────────────────────────

    [TestMethod]
    public void TryRead_RejectsABackupManifest()
    {
        // Both formats are named manifest.json inside their archive, so either reader can be
        // handed the other's file. This is the mirror of BackupEngine's own
        // `Kind != "backup"` gate; without it an export reader would happily report a
        // backup's clients list as an export's.
        BackupManifest backup = new() { Clients = [ClaudeCodeFolder, ClaudeDesktopFolder] };
        string json = JsonSerializer.Serialize(backup, BackupJsonContext.Default.BackupManifest);

        Assert.IsNull(ExportManifest.TryRead(Utf8(json)),
            "kind=\"backup\" is not an export and must not parse as one.");
    }

    [TestMethod]
    public void TryRead_RejectsAFutureSchemaVersion()
    {
        string future =
            """
            {
              "kind": "export",
              "schemaVersion": 3,
              "clients": ["OpenCode"]
            }
            """;

        Assert.IsNull(ExportManifest.TryRead(Utf8(future)),
            "An unknown future version must be rejected outright rather than partly "
            + "understood — the same contract BackupEngine applies to backups.");
    }

    [TestMethod]
    public void TryRead_RejectsMalformedJson()
    {
        Assert.IsNull(ExportManifest.TryRead(Utf8("{ not json")),
            "A truncated or corrupt manifest must return null, not throw at the call site.");
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static MemoryStream Utf8(string json) => new(Encoding.UTF8.GetBytes(json));

    private static string V1Json(bool includesCode, bool includesDesktop) =>
        $$"""
        {
          "kind": "export",
          "schemaVersion": 1,
          "createdUtc": "2026-01-02T03:04:05Z",
          "platform": "windows",
          "appVersion": "2026.1.2.3",
          "includesClaudeCode": {{(includesCode ? "true" : "false")}},
          "includesClaudeDesktop": {{(includesDesktop ? "true" : "false")}},
          "headerComment": "Exported by ClaudeForge"
        }
        """;
}
