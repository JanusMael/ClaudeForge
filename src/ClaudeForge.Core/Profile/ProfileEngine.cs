using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Profile;

/// <summary>
/// Core engine for claudectx-compatible profile operations.
/// <para>
/// Profiles are stored as subdirectories under <c>~/.claude/profiles/&lt;name&gt;/</c>,
/// each containing up to three files:
/// <list type="bullet">
///   <item><c>settings.json</c> — Claude Code settings (required for a profile to be valid)</item>
///   <item><c>CLAUDE.md</c> — global instruction file (optional)</item>
///   <item><c>mcp.json</c> — MCP server definitions, a plain JSON object (optional)</item>
/// </list>
/// </para>
/// <para>
/// The "live" Claude Code files (<c>~/.claude/settings.json</c>, <c>~/.claude/CLAUDE.md</c>,
/// and the <c>mcpServers</c> key inside <c>~/.claude.json</c>) are what the Claude Code CLI
/// actually reads. <see cref="ApplyProfileToLiveAsync"/> copies a profile into these live
/// locations (making it active for the CLI) and <see cref="SyncFromLiveAsync"/> copies
/// the live files back into a profile directory (preserving external edits).
/// </para>
/// <para>
/// The active profile name is tracked in <c>~/.claude/.claudectx-current</c> for
/// interoperability with the claudectx CLI tool.
/// </para>
/// </summary>
public static class ProfileEngine
{
    // ═══════════════════════════════════════════════════════════════════════
    //  Profile discovery
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns metadata for every valid profile (i.e. every subdirectory under
    /// <c>~/.claude/profiles/</c> that contains a <c>settings.json</c>).
    /// Sorted alphabetically by name.
    /// </summary>
    public static IReadOnlyList<ProfileInfo> DiscoverProfiles()
    {
        string dir = PlatformPaths.ProfilesDirectory;
        if (!Directory.Exists(dir))
        {
            return [];
        }

        string? cliActive = ReadCurrentProfileName();

        try
        {
            return Directory
                   .GetDirectories(dir)
                   .Select(d => BuildInfo(d, cliActive))
                   .Where(p => p.HasSettings) // claudectx requirement: settings.json must exist
                   .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                   .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static ProfileInfo BuildInfo(string profileDir, string? cliActive)
    {
        string name = Path.GetFileName(profileDir);
        return new ProfileInfo(
            Name: name,
            HasSettings: File.Exists(Path.Combine(profileDir, "settings.json")),
            HasClaudeMd: File.Exists(Path.Combine(profileDir, "CLAUDE.md")),
            HasMcp: File.Exists(Path.Combine(profileDir, "mcp.json")),
            IsCliActive: string.Equals(name, cliActive, StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  .claudectx-current pointer
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the name of the CLI-active profile from <c>~/.claude/.claudectx-current</c>.
    /// Returns <c>null</c> when the file is absent or empty (meaning the CLI is using
    /// the live <c>~/.claude/settings.json</c> directly, i.e. the "(global)" state).
    /// </summary>
    public static string? ReadCurrentProfileName()
    {
        string path = PlatformPaths.CurrentProfileFilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string text = File.ReadAllText(path).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="name"/> to <c>~/.claude/.claudectx-current</c>.
    /// Pass <c>null</c> to remove the file (represents "no active profile" / global mode).
    /// Uses an atomic write (temp → rename) so a crash mid-write never leaves a partial file.
    /// </summary>
    public static void WriteCurrentProfileName(string? name)
    {
        string path = PlatformPaths.CurrentProfileFilePath;
        if (name == null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(PlatformPaths.ClaudeHome);
        WriteAtomicText(path, name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Create profile from live files
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a new profile directory under <c>~/.claude/profiles/&lt;name&gt;/</c> and
    /// populates it by snapshotting the current live Claude Code files.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the profile was created; <c>false</c> when a directory with that
    /// name already exists (caller should validate beforehand via
    /// <see cref="PlatformPaths.ProfilesDirectory"/>).
    /// </returns>
    public static async Task<bool> CreateFromLiveAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);
        if (Directory.Exists(profileDir))
        {
            return false;
        }

        Directory.CreateDirectory(profileDir);

        // If live settings exist, seed from them; otherwise write a minimal {}.
        string liveSettings = PlatformPaths.UserSettingsPath;
        if (File.Exists(liveSettings))
        {
            await CopyFileAsync(liveSettings, Path.Combine(profileDir, "settings.json"), ct);
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(profileDir, "settings.json"), "{}", ct);
        }

        // CLAUDE.md is optional — only copy if present.
        if (File.Exists(PlatformPaths.ClaudeMdPath))
        {
            await CopyFileAsync(PlatformPaths.ClaudeMdPath, Path.Combine(profileDir, "CLAUDE.md"), ct);
        }

        // Extract mcpServers from ~/.claude.json into a standalone mcp.json.
        await ExtractMcpToProfileAsync(profileDir, ct);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Apply profile → live files
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies a profile to the live Claude Code files so the Claude Code CLI picks it up.
    /// <para>Steps:</para>
    /// <list type="number">
    ///   <item>If a different profile is currently CLI-active, sync the live files back
    ///   into that profile first (auto-sync preserves any external edits).</item>
    ///   <item>Copy <c>profile/settings.json</c> → <c>~/.claude/settings.json</c>.</item>
    ///   <item>Copy <c>profile/CLAUDE.md</c> → <c>~/.claude/CLAUDE.md</c>
    ///   (or delete the live one if the profile has no CLAUDE.md).</item>
    ///   <item>Merge <c>profile/mcp.json</c> into the <c>mcpServers</c> key of
    ///   <c>~/.claude.json</c> (or remove that key if the profile has no mcp.json).</item>
    ///   <item>Write <paramref name="name"/> to <c>~/.claude/.claudectx-current</c>.</item>
    /// </list>
    /// </summary>
    /// <param name="autoSync">
    /// When <c>true</c> (default), the currently CLI-active profile's directory is updated
    /// from the live files before the switch — preserving any edits made directly to
    /// <c>~/.claude/settings.json</c> outside the GUI.
    /// </param>
    public static async Task ApplyProfileToLiveAsync(
        string name,
        bool autoSync = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);
        string settingsSrc = Path.Combine(profileDir, "settings.json");
        if (!File.Exists(settingsSrc))
        {
            throw new FileNotFoundException(
                $"Profile '{name}' is not valid — settings.json not found.", settingsSrc);
        }

        // Auto-sync the current CLI-active profile so no external edits are lost.
        if (autoSync)
        {
            string? current = ReadCurrentProfileName();
            if (!string.IsNullOrEmpty(current) &&
                !string.Equals(current, name, StringComparison.OrdinalIgnoreCase))
            {
                await SyncFromLiveAsync(current, ct);
            }
        }

        // 1. settings.json
        Directory.CreateDirectory(PlatformPaths.ClaudeHome);
        await CopyFileAsync(settingsSrc, PlatformPaths.UserSettingsPath, ct);

        // 2. CLAUDE.md
        string profileMd = Path.Combine(profileDir, "CLAUDE.md");
        if (File.Exists(profileMd))
        {
            await CopyFileAsync(profileMd, PlatformPaths.ClaudeMdPath, ct);
        }
        else if (File.Exists(PlatformPaths.ClaudeMdPath))
        {
            File.Delete(PlatformPaths.ClaudeMdPath);
        }

        // 3. mcpServers → ~/.claude.json
        string profileMcp = Path.Combine(profileDir, "mcp.json");
        if (File.Exists(profileMcp))
        {
            await MergeMcpIntoClaudeJsonAsync(profileMcp, ct);
        }
        else
        {
            await RemoveMcpFromClaudeJsonAsync(ct);
        }

        // 4. Update the .claudectx-current pointer.
        WriteCurrentProfileName(name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Sync live → profile directory
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Copies the current live Claude Code files into the profile directory, overwriting
    /// whatever the profile previously stored. Useful for capturing external edits made
    /// directly to <c>~/.claude/settings.json</c> (e.g. via the Claude Code CLI itself).
    /// </summary>
    public static async Task SyncFromLiveAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);
        Directory.CreateDirectory(profileDir);

        // settings.json is always written (even if live is absent — write {}).
        if (File.Exists(PlatformPaths.UserSettingsPath))
        {
            await CopyFileAsync(PlatformPaths.UserSettingsPath, Path.Combine(profileDir, "settings.json"), ct);
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(profileDir, "settings.json"), "{}", ct);
        }

        // CLAUDE.md: copy if present; remove from profile if absent.
        string profileMd = Path.Combine(profileDir, "CLAUDE.md");
        if (File.Exists(PlatformPaths.ClaudeMdPath))
        {
            await CopyFileAsync(PlatformPaths.ClaudeMdPath, profileMd, ct);
        }
        else if (File.Exists(profileMd))
        {
            File.Delete(profileMd);
        }

        // mcp.json: extract from ~/.claude.json; remove from profile if nothing there.
        await ExtractMcpToProfileAsync(profileDir, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  MCP helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts the <c>mcpServers</c> key from <c>~/.claude.json</c> and writes it
    /// as a plain JSON object to <c>&lt;profileDir&gt;/mcp.json</c>.
    /// Removes the existing <c>mcp.json</c> from the profile if <c>mcpServers</c> is absent.
    /// </summary>
    private static async Task ExtractMcpToProfileAsync(string profileDir, CancellationToken ct)
    {
        string claudeJson = PlatformPaths.ClaudeJsonPath;
        string profileMcp = Path.Combine(profileDir, "mcp.json");

        if (!File.Exists(claudeJson))
        {
            if (File.Exists(profileMcp))
            {
                File.Delete(profileMcp);
            }

            return;
        }

        try
        {
            string raw = await File.ReadAllTextAsync(claudeJson, ct);
            JsonObject? node = JsonNode.Parse(raw) as JsonObject;

            if (node?["mcpServers"] is not JsonObject mcp || mcp.Count == 0)
            {
                if (File.Exists(profileMcp))
                {
                    File.Delete(profileMcp);
                }

                return;
            }

            string json = mcp.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(profileMcp, json, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Non-fatal: skip MCP sync if the file is unreadable or malformed.
        }
    }

    /// <summary>
    /// Reads <c>&lt;profileDir&gt;/mcp.json</c> and sets it as the <c>mcpServers</c>
    /// key inside <c>~/.claude.json</c>, preserving all other keys in that file.
    /// </summary>
    private static async Task MergeMcpIntoClaudeJsonAsync(
        string profileMcp, CancellationToken ct)
    {
        string claudeJson = PlatformPaths.ClaudeJsonPath;
        try
        {
            // Read the profile's mcp.json.
            string mcpRaw = await File.ReadAllTextAsync(profileMcp, ct);
            JsonObject mcpNode = JsonNode.Parse(mcpRaw) as JsonObject ?? new JsonObject();

            // Read (or start fresh) ~/.claude.json as a raw opaque object.
            JsonObject root;
            if (File.Exists(claudeJson))
            {
                string raw = await File.ReadAllTextAsync(claudeJson, ct);
                root = JsonNode.Parse(raw) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            // Replace / set the mcpServers key, leave everything else untouched.
            // DeepClone detaches the node from its current parent without a roundtrip
            // through JSON serialisation.
            root["mcpServers"] = mcpNode.DeepClone();

            Directory.CreateDirectory(Path.GetDirectoryName(claudeJson)!);
            string output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await WriteAtomicAsync(claudeJson, output, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Non-fatal: log / surface through the caller's error handling.
            throw new InvalidOperationException(
                $"Failed to merge MCP servers into ~/.claude.json: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Removes the <c>mcpServers</c> key from <c>~/.claude.json</c>, preserving all
    /// other keys. No-op if the file does not exist or contains no <c>mcpServers</c>.
    /// </summary>
    private static async Task RemoveMcpFromClaudeJsonAsync(CancellationToken ct)
    {
        string claudeJson = PlatformPaths.ClaudeJsonPath;
        if (!File.Exists(claudeJson))
        {
            return;
        }

        try
        {
            string raw = await File.ReadAllTextAsync(claudeJson, ct);
            JsonObject? root = JsonNode.Parse(raw) as JsonObject;
            if (root == null || !root.ContainsKey("mcpServers"))
            {
                return;
            }

            root.Remove("mcpServers");
            string output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await WriteAtomicAsync(claudeJson, output, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Non-fatal.
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Desktop profile discovery
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns metadata for every valid Desktop profile (i.e. every subdirectory under
    /// <c>&lt;DesktopConfigDir&gt;/profiles/</c> that contains a
    /// <c>claude_desktop_config.json</c>). Sorted alphabetically by name.
    /// </summary>
    public static IReadOnlyList<DesktopProfileInfo> DiscoverDesktopProfiles()
    {
        string dir = PlatformPaths.DesktopProfilesDirectory;
        if (!Directory.Exists(dir))
        {
            return [];
        }

        string? active = ReadCurrentDesktopProfileName();

        try
        {
            return Directory
                   .GetDirectories(dir)
                   .Select(d => BuildDesktopInfo(d, active))
                   .Where(p => p.HasConfig) // only profiles that have a config file are actionable
                   .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                   .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static DesktopProfileInfo BuildDesktopInfo(string profileDir, string? active)
    {
        string name = Path.GetFileName(profileDir);
        return new DesktopProfileInfo(
            Name: name,
            HasConfig: File.Exists(Path.Combine(profileDir, "claude_desktop_config.json")),
            IsActive: string.Equals(name, active, StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Desktop .desktop-current pointer
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the name of the active Desktop profile from <c>&lt;DesktopConfigDir&gt;/.desktop-current</c>.
    /// Returns <c>null</c> when absent or empty (no profile active).
    /// </summary>
    public static string? ReadCurrentDesktopProfileName()
    {
        string path = PlatformPaths.DesktopCurrentProfileFilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string text = File.ReadAllText(path).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="name"/> to <c>.desktop-current</c>.
    /// Pass <c>null</c> to remove the file (no active Desktop profile).
    /// Uses an atomic write (temp → rename) so a crash mid-write never leaves a partial file.
    /// </summary>
    public static void WriteCurrentDesktopProfileName(string? name)
    {
        string path = PlatformPaths.DesktopCurrentProfileFilePath;
        if (name == null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        WriteAtomicText(path, name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Desktop profile CRUD
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a new Desktop profile by snapshotting the current live
    /// <c>claude_desktop_config.json</c>. Returns <c>false</c> if the name already exists.
    /// </summary>
    public static async Task<bool> CreateDesktopProfileFromLiveAsync(
        string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string profileDir = Path.Combine(PlatformPaths.DesktopProfilesDirectory, name);
        string profileConfig = Path.Combine(profileDir, "claude_desktop_config.json");
        if (Directory.Exists(profileDir))
        {
            return false;
        }

        Directory.CreateDirectory(profileDir);

        string live = PlatformPaths.DesktopConfigPath;
        if (File.Exists(live))
        {
            await CopyFileAsync(live, profileConfig, ct);
        }
        else
        {
            await File.WriteAllTextAsync(profileConfig, "{}", ct);
        }

        return true;
    }

    /// <summary>
    /// Applies a Desktop profile to the live Desktop config file.
    /// <para>Steps:</para>
    /// <list type="number">
    ///   <item>If <paramref name="autoSync"/> and a different profile is currently active,
    ///   sync the live config back into that profile first.</item>
    ///   <item>Copy <c>profile/claude_desktop_config.json</c> → live Desktop config.</item>
    ///   <item>Write <paramref name="name"/> to <c>.desktop-current</c>.</item>
    /// </list>
    /// </summary>
    public static async Task ApplyDesktopProfileToLiveAsync(
        string name,
        bool autoSync = true,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string profileConfig = Path.Combine(PlatformPaths.DesktopProfilesDirectory, name, "claude_desktop_config.json");
        if (!File.Exists(profileConfig))
        {
            throw new FileNotFoundException(
                $"Desktop profile '{name}' is not valid — claude_desktop_config.json not found.", profileConfig);
        }

        if (autoSync)
        {
            string? current = ReadCurrentDesktopProfileName();
            if (!string.IsNullOrEmpty(current) &&
                !string.Equals(current, name, StringComparison.OrdinalIgnoreCase))
            {
                await SyncDesktopFromLiveAsync(current, ct);
            }
        }

        string? destDir = Path.GetDirectoryName(PlatformPaths.DesktopConfigPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        await CopyFileAsync(profileConfig, PlatformPaths.DesktopConfigPath, ct);
        WriteCurrentDesktopProfileName(name);
    }

    /// <summary>
    /// Copies the current live Desktop config into the profile directory,
    /// overwriting whatever was previously stored.
    /// </summary>
    public static async Task SyncDesktopFromLiveAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string profileDir = Path.Combine(PlatformPaths.DesktopProfilesDirectory, name);
        string profileConfig = Path.Combine(profileDir, "claude_desktop_config.json");
        Directory.CreateDirectory(profileDir);

        string live = PlatformPaths.DesktopConfigPath;
        if (File.Exists(live))
        {
            await CopyFileAsync(live, profileConfig, ct);
        }
        else
        {
            await File.WriteAllTextAsync(profileConfig, "{}", ct);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Per-profile JSON export / import (claudectx-compatible)
    //
    //  these two methods produce / consume the same
    //  single-file JSON artefact format as claudectx's `export` /
    //  `import` subcommands, so a profile can round-trip between the
    //  two tools without translation.  See ExportedProfile.cs for the
    //  schema contract and the claudectx repo
    //  (https://github.com/foxj77/claudectx, internal/exporter/exporter.go)
    //  for the source-of-truth shape.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export a CLI profile to a single-file JSON artefact at
    /// <paramref name="destPath"/>.  Format: claudectx-compatible
    /// <see cref="ExportedProfile"/> (snake_case keys, version
    /// <c>"1.0.0"</c>).  Settings are required; CLAUDE.md and mcp.json
    /// are optional and omitted from the JSON when absent.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    /// Thrown when <c>~/.claude/profiles/&lt;name&gt;/settings.json</c>
    /// does not exist.
    /// </exception>
    public static async Task ExportProfileAsync(
        string name,
        string destPath,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        string profileDir = Path.Combine(PlatformPaths.ProfilesDirectory, name);
        string settingsPath = Path.Combine(profileDir, "settings.json");
        string claudeMdPath = Path.Combine(profileDir, "CLAUDE.md");
        string mcpPath = Path.Combine(profileDir, "mcp.json");

        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException(
                $"Profile '{name}' is not valid — settings.json not found.", settingsPath);
        }

        // settings.json — pass through as a parsed JsonNode so the
        // export carries it byte-equivalent (no schema reshaping).
        string settingsRaw = await File.ReadAllTextAsync(settingsPath, ct);
        JsonNode? settingsNode = JsonNode.Parse(settingsRaw);

        // CLAUDE.md — optional; omit when absent OR empty (matches
        // claudectx ',omitempty' on the empty-string Go value).
        string? claudeMd = null;
        if (File.Exists(claudeMdPath))
        {
            string raw = await File.ReadAllTextAsync(claudeMdPath, ct);
            if (!string.IsNullOrEmpty(raw))
            {
                claudeMd = raw;
            }
        }

        // mcp.json — optional; omit when absent or contains an empty object.
        JsonNode? mcpNode = null;
        if (File.Exists(mcpPath))
        {
            string raw = await File.ReadAllTextAsync(mcpPath, ct);
            JsonNode? parsed = JsonNode.Parse(raw);
            if (parsed is JsonObject jo && jo.Count > 0)
            {
                mcpNode = jo;
            }
        }

        ExportedProfile exported = new()
        {
            Version = ExportedProfileFormat.CurrentVersion,
            Name = name,
            Settings = settingsNode,
            ClaudeMD = claudeMd,
            MCPServers = mcpNode,
            ExportedAt = DateTime.UtcNow.ToString("o"),
        };

        string json = JsonSerializer.Serialize(exported, ProfileJsonContext.Default.ExportedProfile);
        await WriteAtomicAsync(destPath, json, ct);
    }

    /// <summary>
    /// Import a single-file JSON artefact (claudectx-compatible) at
    /// <paramref name="sourcePath"/> into a new CLI profile directory
    /// under <c>~/.claude/profiles/</c>.
    /// </summary>
    /// <param name="overrideName">
    /// When non-null, lands the profile at this name instead of the
    /// embedded <see cref="ExportedProfile.Name"/>.  Mirrors
    /// <c>claudectx import &lt;file&gt; &lt;new-name&gt;</c>.
    /// </param>
    /// <returns>The profile name actually used (after override resolution).</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the JSON is malformed, the version is incompatible,
    /// or settings is missing.
    /// </exception>
    /// <exception cref="IOException">
    /// Thrown when the target profile directory already exists — never
    /// overwrite (mirrors claudectx's "profile %q already exists" guard).
    /// </exception>
    public static async Task<string> ImportProfileAsync(
        string sourcePath,
        string? overrideName = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string json = await File.ReadAllTextAsync(sourcePath, ct);

        ExportedProfile? exported;
        try
        {
            exported = JsonSerializer.Deserialize(json, ProfileJsonContext.Default.ExportedProfile);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Imported file is not valid JSON: {ex.Message}", ex);
        }

        if (exported is null)
        {
            throw new InvalidDataException("Imported file decoded to null.");
        }

        // Strict version equality matches claudectx's behaviour.
        if (!string.Equals(exported.Version, ExportedProfileFormat.CurrentVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Incompatible export version '{exported.Version}' (expected '{ExportedProfileFormat.CurrentVersion}').");
        }

        if (exported.Settings is null)
        {
            throw new InvalidDataException("Imported profile is missing required 'settings' field.");
        }

        string targetName = !string.IsNullOrWhiteSpace(overrideName)
            ? overrideName!.Trim()
            : exported.Name;
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new InvalidDataException(
                "Imported profile has no 'name' and no override name was supplied.");
        }

        // security: sanity-check the target name BEFORE
        // Path.Combine so a malicious "name": "../../something" cannot
        // escape the profiles directory.  Both the directory-traversal
        // string check and the resolved-path containment check are
        // applied; either alone is insufficient (the latter handles
        // platform-specific edge cases like Windows drive letters and
        // backslash separators in attacker-supplied names).  See
        // docs/CLAUDECTX-COMPATIBILITY.md.
        string profileDir = ResolveProfileDirSecurely(targetName);
        if (Directory.Exists(profileDir))
        {
            throw new IOException($"Profile '{targetName}' already exists.");
        }

        Directory.CreateDirectory(profileDir);

        // partial-write cleanup: if any of the writes
        // below fails (cancellation, disk full, permission error),
        // remove the half-populated profile directory so the user can
        // retry under the same name.  Without this, a failed import
        // strands the name slot until manual cleanup.
        bool committed = false;
        try
        {
            // settings.json (required) — write the JsonNode pretty-printed.
            string settingsJson = exported.Settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await WriteAtomicAsync(Path.Combine(profileDir, "settings.json"), settingsJson, ct);

            // CLAUDE.md (optional)
            if (!string.IsNullOrEmpty(exported.ClaudeMD))
            {
                await WriteAtomicAsync(Path.Combine(profileDir, "CLAUDE.md"), exported.ClaudeMD, ct);
            }

            // mcp.json (optional, only when non-empty object)
            if (exported.MCPServers is JsonObject mcpObj && mcpObj.Count > 0)
            {
                string mcpJson = mcpObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                await WriteAtomicAsync(Path.Combine(profileDir, "mcp.json"), mcpJson, ct);
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    Directory.Delete(profileDir, recursive: true);
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup; the original exception will propagate.
                }
            }
        }

        return targetName;
    }

    /// <summary>
    /// security: validate <paramref name="profileName"/>
    /// is a single path-segment without traversal smuggling, then
    /// resolve it under <see cref="PlatformPaths.ProfilesDirectory"/>
    /// and confirm the result stays inside that root.
    /// <para>
    /// Both checks are needed: the string-shape check rejects obvious
    /// traversals (<c>../</c>, absolute paths, embedded separators)
    /// before any filesystem call; the resolved-path containment check
    /// is the final backstop against platform-specific edge cases
    /// (drive letters on Windows, junction points, NTFS short names).
    /// </para>
    /// <para>
    /// Throws <see cref="InvalidDataException"/> on rejection so callers
    /// surface the failure as user-facing text alongside other import
    /// validation errors (version mismatch, missing settings, etc.).
    /// </para>
    /// </summary>
    private static string ResolveProfileDirSecurely(string profileName)
    {
        if (profileName is "." or ".."
            || profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || profileName.Contains(Path.DirectorySeparatorChar)
            || profileName.Contains(Path.AltDirectorySeparatorChar)
            || Path.IsPathRooted(profileName))
        {
            throw new InvalidDataException(
                $"Profile name '{profileName}' is not a valid single-segment directory name " +
                "(must not contain path separators, '..', or absolute-path components).");
        }

        string profilesRoot = Path.GetFullPath(PlatformPaths.ProfilesDirectory);
        string resolved = Path.GetFullPath(Path.Combine(profilesRoot, profileName));

        // Belt-and-suspenders: reject if the resolved path doesn't sit
        // directly under profilesRoot.  Comparison uses ordinal-ignore-case
        // so it works on case-insensitive Windows volumes; on case-
        // sensitive Linux this still rejects anything outside the root
        // (a different-case attempt to match would land outside).
        string rootWithSep = profilesRoot.EndsWith(Path.DirectorySeparatorChar)
            ? profilesRoot
            : profilesRoot + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Profile name '{profileName}' resolves outside the profiles root " +
                $"('{resolved}' is not under '{profilesRoot}').");
        }

        return resolved;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  I/O helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task CopyFileAsync(string src, string dst, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

        // Atomic copy: write to a sibling .tmp-{guid}, then rename. The previous
        // FileMode.Create on the destination truncated immediately and copied in
        // chunks — a crash mid-copy left the destination config zero-length.
        // Profile files include claude.json / claude_desktop_config.json, both
        // of which are large enough that a partial write is realistic on slower
        // disks or under cancellation. Mirrors the WriteAtomicAsync helper below.
        string tmp = $"{dst}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (FileStream fsIn = new(src, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                             useAsync: true))
            await using (FileStream fsOut = new(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                             useAsync: true))
            {
                await fsIn.CopyToAsync(fsOut, ct).ConfigureAwait(false);
            }

            File.Move(tmp, dst, overwrite: true);
        }
        catch (Exception)
        {
            // Catch *everything* (including OperationCanceledException) so the
            // sibling temp file is removed before propagating the original failure.
            // Re-throw immediately afterwards, satisfying the project rule
            // "log or re-throw" for unfiltered catches.
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                // Best effort: failing to delete the temp file is non-fatal compared
                // to the original exception we are about to re-throw.
            }

            throw;
        }
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> atomically by
    /// writing to a sibling <c>.tmp-{guid}</c> file first, then renaming on success.
    /// The GUID suffix avoids collisions when multiple callers write to different
    /// paths simultaneously (e.g. parallel profile apply + MCP merge).
    /// </summary>
    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        string tmp = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(tmp, content, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception)
        {
            // Catch *everything* (including OperationCanceledException) so the
            // sibling temp file is removed before propagating the original failure.
            // Re-throw immediately afterwards, satisfying the project rule
            // "log or re-throw" for unfiltered catches.
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                // Best effort: failing to delete the temp file is non-fatal compared
                // to the original exception we are about to re-throw.
            }

            throw;
        }
    }

    /// <summary>
    /// Synchronous atomic text write for pointer files. Uses the same
    /// temp-then-rename pattern as <see cref="WriteAtomicAsync"/>.
    /// </summary>
    private static void WriteAtomicText(string path, string content)
    {
        string tmp = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception)
        {
            // Catch *everything* (including OperationCanceledException) so the
            // sibling temp file is removed before propagating the original failure.
            // Re-throw immediately afterwards, satisfying the project rule
            // "log or re-throw" for unfiltered catches.
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                // Best effort: failing to delete the temp file is non-fatal compared
                // to the original exception we are about to re-throw.
            }

            throw;
        }
    }
}