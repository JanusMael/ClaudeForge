using System.Text;
using System.Text.Json;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Tests.Backup;

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
public sealed class ExportManifestTests
{
    private const string ClaudeCodeFolder = "ClaudeCode";
    private const string ClaudeDesktopFolder = "ClaudeDesktop";

    // ── v2, the shape written today ──────────────────────────────────────

    [Fact]
    public void Clients_UseTheSameVocabularyAsBackupManifest()
    {
        // Deliberately looks tautological, like ArchiveFolderNames_AreTheValuesAlreadyOn-
        // UsersDisks does for backups. Both manifests live in this folder, both are named
        // manifest.json inside their archive, and BackupRestoreViewModel.AbbreviateClient
        // renders BackupManifest.Clients directly. Two vocabularies for the same products is
        // precisely the mistake Phase 4d-2 removed.
        Assert.Equal(ClaudeCodeFolder, ClaudeProducts.ClaudeCodeArchiveFolder);
        Assert.Equal(ClaudeDesktopFolder, ClaudeProducts.ClaudeDesktopProduct.ArchiveFolder);
    }

    // ── v1, the shape already on disk ────────────────────────────────────

    // ── what TryRead must refuse ─────────────────────────────────────────

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
