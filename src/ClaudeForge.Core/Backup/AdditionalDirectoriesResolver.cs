using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Parses the <c>additionalDirectories</c> setting from Claude Code settings files and
/// returns the resulting set of existing absolute directory paths. Mirrors the behaviour
/// of the PowerShell reference implementation but in a test-friendly, trimming-safe form.
/// </summary>
/// <remarks>
/// <para>
/// The setting can appear in two shapes in a settings file:
/// </para>
/// <list type="bullet">
///   <item><c>{ "additionalDirectories": [ "path", ... ] }</c> (root-level)</item>
///   <item><c>{ "permissions": { "additionalDirectories": [ ... ] } }</c> (permissions-nested)</item>
/// </list>
/// <para>
/// Each entry is either a plain string or an object with a <c>path</c> field. Relative
/// paths are resolved against the directory containing the settings file. A leading
/// <c>~</c> is expanded to the user's home directory.
/// </para>
/// </remarks>
public static class AdditionalDirectoriesResolver
{
    /// <summary>
    /// Parses <paramref name="settingsFilePaths"/> and returns the de-duplicated list of
    /// resolved absolute paths that exist on disk as directories.
    /// </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> settingsFilePaths)
    {
        ArgumentNullException.ThrowIfNull(settingsFilePaths);

        // Windows paths are case-insensitive; Linux/macOS paths are case-sensitive.
        // Using OrdinalIgnoreCase on Linux would silently merge "/home/u/Project" and
        // "/home/u/project" into one entry, dropping one of the two directories.
        StringComparer pathComparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        HashSet<string> found = new(pathComparer);

        foreach (string settingsPath in settingsFilePaths)
        {
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
            {
                continue;
            }

            JsonNode? root;
            try
            {
                string text = File.ReadAllText(settingsPath);
                root = JsonNode.Parse(text);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Malformed or unreadable → skip silently; the caller may have other sources.
                continue;
            }

            if (root is not JsonObject obj)
            {
                continue;
            }

            JsonArray? entries = ExtractEntries(obj);
            if (entries is null)
            {
                continue;
            }

            string? baseDir = Path.GetDirectoryName(settingsPath);
            foreach (JsonNode? rawEntry in entries)
            {
                string? rawPath = ExtractRawPath(rawEntry);
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string? resolved = Resolve(rawPath!, baseDir);
                if (resolved != null && Directory.Exists(resolved))
                {
                    found.Add(resolved);
                }
            }
        }

        // Stable ordering for deterministic output.
        List<string> list = found.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static JsonArray? ExtractEntries(JsonObject root)
    {
        // Root-level form first
        if (root["additionalDirectories"] is JsonArray rootArr)
        {
            return rootArr;
        }

        // Permissions-nested form
        if (root["permissions"] is JsonObject permissions &&
            permissions["additionalDirectories"] is JsonArray permArr)
        {
            return permArr;
        }

        return null;
    }

    private static string? ExtractRawPath(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonValue v when v.TryGetValue(out string? s) => s,
            JsonObject o when o["path"].AsStringOrNull() is { } p => p,
            var _ => null,
        };
    }

    /// <summary>
    /// Resolve a path that may start with <c>~</c> and may be relative. Relative paths
    /// are resolved against <paramref name="baseDir"/> (the directory of the settings
    /// file that declared them).
    /// </summary>
    internal static string? Resolve(string rawPath, string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string expanded = ExpandTilde(rawPath);

        if (!Path.IsPathRooted(expanded) && !string.IsNullOrEmpty(baseDir))
        {
            expanded = Path.Combine(baseDir, expanded);
        }

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ExpandTilde(string path)
    {
        if (!path.StartsWith('~'))
        {
            return path;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.Length == 1)
        {
            return home;
        }

        // Accept "~/" or "~\"
        if (path[1] is '/' or '\\')
        {
            return Path.Combine(home, path[2..]);
        }

        return path; // "~user" form — not supported, return as-is
    }
}