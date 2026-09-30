using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;
using Bennewitz.Ninja.AgentForge.Core.Backup;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Profile;
using Bennewitz.Ninja.AppServices.Abstractions;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.ClaudeForge.Tests.TestSupport;
using Bennewitz.Ninja.ClaudeForge.ViewModels;
using Bennewitz.Ninja.ScopedEditors.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Golden;

/// <summary>
/// plans/00008 step 3: what a backup, a profile export and the settings pages' scopes look like on
/// <c>main</c> before AgentForge's Claude knowledge moves out, recorded as fixtures so each later
/// step can prove it changed none of them.
/// </summary>
/// <remarks>
/// <para>
/// Each comparison is normalized, and the fields it ignores are written down in the fixture itself
/// (<c>ignored</c>), because they vary run to run with no seam to pin them: the backup manifest's
/// <c>createdUtc</c>, <c>appVersion</c>, <c>platform</c> and <c>sizeBytes</c>, and a profile's
/// <c>exported_at</c>. Archive entries are compared by name and SHA-256 of their contents, never by
/// zip bytes, since every entry carries the time it was written.
/// </para>
/// <para>
/// ⚠ <b>The bundled schemas are in the backup fixture by SHA-256 on purpose.</b>
/// <c>BackupEngine</c> bundles schemas by resource prefix, and a prefix that matches nothing writes
/// an archive with ZERO schemas, which <c>RestoreEngine</c> then restores WITHOUT validating. So a
/// schema refresh that changes a bundled file changes this fixture too, and has to re-mint it with
/// <see cref="WriteGoldens"/>, reading the diff.
/// </para>
/// <para>
/// ⚠ <see cref="WriteGoldens"/> is explicit and never runs with the suite. The fixtures were minted
/// from <c>main</c> before any code moved; re-minting one is a decision to accept a behaviour change,
/// and belongs in the diff a reviewer reads.
/// </para>
/// </remarks>
public sealed class GoldenFixtureTests : IDisposable
{
    private const string FixtureDirRelative = "tests/ClaudeForge.Tests/Golden/Fixtures";

    private static readonly string[] ManifestIgnored = ["createdUtc", "appVersion", "platform", "sizeBytes"];
    private static readonly string[] ProfileIgnored = ["exported_at"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _sandbox;

    public GoldenFixtureTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "claudetest_golden_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
        PlatformPaths.TestUserProfileOverride = _sandbox;
        BackupEngine.InvalidateListCache();
        SeedHome();
    }

    public void Dispose()
    {
        DebugFlags.ResetForTesting();
        PlatformPaths.TestUserProfileOverride = null;
        try
        {
            if (Directory.Exists(_sandbox))
            {
                TestCleanupHelpers.DeleteDirectoryWithRetry(_sandbox);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task BackupOfTheFixtureHome_MatchesItsGolden()
        => AssertMatches("backup.json", await MeasureBackupAsync());

    [Fact]
    public async Task ProfileExport_MatchesItsGolden()
        => AssertMatches("profile.json", await MeasureProfileAsync());

    [Fact]
    public async Task ScopesEachSettingsPageOffers_MatchTheirGolden()
        => AssertMatches("scopes.json", await MeasureScopesAsync());

    /// <summary>Re-mints all three fixtures from this build. Explicit: never runs with the suite.</summary>
    [Fact(Explicit = true)]
    public async Task WriteGoldens()
    {
        Write("backup.json", await MeasureBackupAsync());
        Write("profile.json", await MeasureProfileAsync());
        Write("scopes.json", await MeasureScopesAsync());
    }

    // ── The fixture home ─────────────────────────────────────────────────────────────────────

    private void SeedHome()
    {
        string claudeDir = Path.Combine(_sandbox, ".claude");
        Directory.CreateDirectory(claudeDir);
        File.WriteAllText(Path.Combine(claudeDir, "settings.json"),
            """{ "model": "sonnet", "env": { "GOLDEN_FIXTURE": "1" }, "permissions": { "allow": ["Read"] } }""");
        File.WriteAllText(Path.Combine(claudeDir, "CLAUDE.md"), "# Golden fixture memory\n");

        // No "projects" map: settings-only, so no project or git discovery varies the result.
        File.WriteAllText(Path.Combine(_sandbox, ".claude.json"), """{ "numStartups": 3 }""");

        Directory.CreateDirectory(Path.GetDirectoryName(PlatformPaths.DesktopConfigPath)!);
        File.WriteAllText(PlatformPaths.DesktopConfigPath,
            """{ "mcpServers": { "golden": { "command": "echo", "args": ["fixture"] } } }""");

        string profileDir = Path.Combine(claudeDir, "profiles", "golden");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "settings.json"), """{ "model": "opus" }""");
        File.WriteAllText(Path.Combine(profileDir, "CLAUDE.md"), "# Golden profile\n");
        File.WriteAllText(Path.Combine(profileDir, "mcp.json"),
            """{ "mcpServers": { "golden": { "command": "echo" } } }""");
    }

    // ── Measurements ────────────────────────────────────────────────────────────────────────

    private async Task<JsonObject> MeasureBackupAsync()
    {
        string archive = Path.Combine(_sandbox, "golden-backup.zip");
        BackupResult result = await new BackupEngine(ClaudeEnvironment.Empty).CreateAsync(
            new BackupRequest
            {
                DestinationZipPath = archive,
                Mode = BackupMode.SettingsOnly,
                Products = [SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), SchemaRegistry.ClaudeDesktopProduct],
            },
            progress: null,
            TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, "the fixture backup failed: " + result.Message);

        JsonArray entries = [];
        JsonNode? manifest = null;
        using (ZipArchive zip = ZipFile.OpenRead(archive))
        {
            foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                using Stream stream = entry.Open();
                using MemoryStream bytes = new();
                await stream.CopyToAsync(bytes, TestContext.Current.CancellationToken);
                if (entry.FullName == "manifest.json")
                {
                    manifest = JsonNode.Parse(bytes.ToArray());
                    continue;
                }

                // ⚠ Line endings are normalized before hashing: `.gitattributes` sets text=auto, so a
                // bundled schema is CRLF in a Windows checkout and LF on Linux and macOS, and the raw
                // bytes (and every hash) differ by platform while the content is the same.
                string text = Encoding.UTF8.GetString(bytes.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
                entries.Add(new JsonObject
                {
                    ["name"] = entry.FullName,
                    ["sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                });
            }
        }

        MessageAssert.NotNull(manifest, "the archive carries no manifest.json");
        JsonObject manifestObject = manifest!.AsObject();
        foreach (string key in ManifestIgnored)
        {
            manifestObject.Remove(key);
        }

        // Premise: the archive must carry schemas at all, or the SHA comparison below is vacuous.
        Assert.Contains(entries, e => e!["name"]!.GetValue<string>().StartsWith("Schemas/", StringComparison.Ordinal));

        return new JsonObject
        {
            ["ignored"] = new JsonArray([.. ManifestIgnored.Select(k => JsonValue.Create(k))]),
            ["manifest"] = manifestObject.DeepClone(),
            ["entries"] = entries,
        };
    }

    private async Task<JsonObject> MeasureProfileAsync()
    {
        string destination = Path.Combine(_sandbox, "golden-profile.json");
        await ProfileEngine.ExportProfileAsync(ClaudeEnvironment.Empty, "golden", destination, TestContext.Current.CancellationToken);
        JsonObject exported = JsonNode.Parse(await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken))!.AsObject();
        foreach (string key in ProfileIgnored)
        {
            Assert.True(exported.Remove(key), $"the export no longer writes '{key}'; update ProfileIgnored");
        }

        return new JsonObject
        {
            ["ignored"] = new JsonArray([.. ProfileIgnored.Select(k => JsonValue.Create(k))]),
            ["profile"] = exported.DeepClone(),
        };
    }

    private async Task<JsonObject> MeasureScopesAsync()
    {
        JsonObject pages = [];
        using (MainWindowViewModel vm = new(ClaudeEnvironment.Empty, new SchemaRegistry(), new NullDialogService()))
        {
            await vm.LoadAllWorkspacesAsync();
            foreach (string section in new[] { "Claude Code", "Claude Desktop" })
            {
                NavigationNodeViewModel? node = vm.NavigationTree.FirstOrDefault(n => n.Title == section);
                MessageAssert.NotNull(node, $"no navigation section titled '{section}'");
                List<SettingsGroupEditorViewModel> groups = [];
                Collect(node!, groups);
                Assert.True(groups.Count > 0, $"'{section}' built no settings pages; the fixture would record nothing");
                JsonObject perPage = [];
                foreach (SettingsGroupEditorViewModel group in groups)
                {
                    perPage[group.GroupName] = new JsonArray([.. group.AvailableScopes.Select(s => JsonValue.Create(s.DisplayName))]);
                }

                pages[section] = perPage;
            }
        }

        // The app opens no project, so its pages offer User only. With a project the ladder's other
        // editable rungs appear; that is recorded at the SDK, where a project can be opened.
        string projectRoot = Path.Combine(_sandbox, "golden-project");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".claude"));
        File.WriteAllText(Path.Combine(projectRoot, ".claude", "settings.json"), "{}");
        File.WriteAllText(Path.Combine(projectRoot, ".claude", "settings.local.json"), "{}");
        using ClaudeCodeClient client = new(ClaudeEnvironment.Empty);
        await client.OpenAsync(projectRoot, TestContext.Current.CancellationToken);

        return new JsonObject
        {
            ["appPages"] = pages,
            ["claudeCodeEditableScopesWithAProject"] = new JsonArray([.. client.EditableScopes.Select(s => JsonValue.Create(s.DisplayName))]),
        };

        static void Collect(NavigationNodeViewModel node, List<SettingsGroupEditorViewModel> sink)
        {
            if (node.Editor is SettingsGroupEditorViewModel group)
            {
                sink.Add(group);
            }

            foreach (NavigationNodeViewModel child in node.Children)
            {
                Collect(child, sink);
            }
        }
    }

    // ── Fixture files ───────────────────────────────────────────────────────────────────────

    private static void AssertMatches(string fixture, JsonObject measured)
    {
        string path = FixturePath(fixture);
        Assert.True(File.Exists(path), $"{FixtureDirRelative}/{fixture} is missing; mint it with the explicit WriteGoldens test");
        JsonNode expected = JsonNode.Parse(File.ReadAllText(path))!;
        if (!JsonNode.DeepEquals(expected, measured))
        {
            Assert.Fail($"{fixture} no longer matches what main produced before plans/00008 moved code.{Environment.NewLine}"
                + $"expected:{Environment.NewLine}{expected.ToJsonString(Indented)}{Environment.NewLine}"
                + $"measured:{Environment.NewLine}{measured.ToJsonString(Indented)}");
        }
    }

    private static void Write(string fixture, JsonObject measured)
    {
        string path = FixturePath(fixture);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, measured.ToJsonString(Indented).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static string FixturePath(string fixture)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return Path.Combine(dir, FixtureDirRelative, fixture);
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException($"Could not locate the repo root from '{AppContext.BaseDirectory}'.");
    }

    /// <summary>Inert dialog service; these tests never open anything.</summary>
    private sealed class NullDialogService : IDialogService
    {
        public Task<string?> PickFolderAsync(string? title = null) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? title = null, IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string? title, string defaultFileName,
                                               IReadOnlyList<FilePickerFilter>? filters = null) =>
            Task.FromResult<string?>(null);

        public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> ShowInputAsync(string title, string prompt, string? placeholder = null) =>
            Task.FromResult<string?>(null);

        public Task<bool?> ShowConfirmAsync(string title, string message, string confirmLabel = "Confirm",
                                            string cancelLabel = "Cancel") => Task.FromResult<bool?>(false);

        public Task<bool> ShowSaveChangesDialogAsync(ISaveChangesPrompt prompt) => Task.FromResult(false);
    }
}
