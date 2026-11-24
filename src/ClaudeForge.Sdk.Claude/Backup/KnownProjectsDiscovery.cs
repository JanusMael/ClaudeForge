using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Backup;

/// <summary>
/// Discovers the set of project root paths Claude Code has known across
/// sessions, for use by the multi-project expansion path of Full-mode
/// backups.
///
/// <para>
/// Source of truth is the top-level <c>projects</c> object inside
/// <c>~/.claude.json</c>: keys are the canonical absolute project paths
/// the user has opened.  Preferred over walking
/// <c>~/.claude/projects/</c> because Claude's per-session-history
/// sub-directories use a lossy path-encoding scheme (path separators
/// replaced with <c>-</c>) that cannot be unambiguously decoded back to
/// the original absolute path on a different machine.
/// </para>
///
/// <para>
/// Project roots that no longer exist on disk at discovery time are
/// silently dropped, and the count of dropped entries surfaces via
/// <see cref="ProductKnownProjects.SkippedNonExistentCount"/> so the backup
/// engine, which asks through Claude Code's <see cref="ProductProjectLayout"/>, can fold a
/// single-line warning into the backup manifest.  No exception is thrown for a
/// missing or malformed <c>~/.claude.json</c> — the discovery degrades
/// gracefully to "no known projects" so a corrupt config never blocks
/// a backup.
/// </para>
/// <para>
/// ⓘ In <c>AgentForge.Core</c> until plans/00008 step 8, which put it behind
/// <see cref="ProductProjectLayout.KnownProjects"/>.
/// </para>
/// </summary>
internal static class KnownProjectsDiscovery
{
    /// <summary>
    /// Reads <paramref name="claudeJsonPath"/> (typically
    /// <see cref="Bennewitz.Ninja.AgentForge.Core.Platform.PlatformPaths.ClaudeJsonPath"/>),
    /// extracts the keys of its top-level <c>projects</c> object, and
    /// partitions them into "exists on disk" vs "skipped".  Returns an
    /// empty result with zero skipped if the file is missing, unreadable,
    /// malformed, or has no <c>projects</c> object.
    /// </summary>
    public static ProductKnownProjects ResolveExisting(string claudeJsonPath)
    {
        if (string.IsNullOrWhiteSpace(claudeJsonPath) || !File.Exists(claudeJsonPath))
        {
            return ProductKnownProjects.None;
        }

        JsonObject? root;
        try
        {
            string raw = File.ReadAllText(claudeJsonPath);
            root = JsonNode.Parse(raw) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Malformed / unreadable → gracefully degrade.  A corrupted
            // ~/.claude.json should not block the rest of the backup
            // from completing.
            return ProductKnownProjects.None;
        }

        if (root?["projects"] is not JsonObject projectsMap)
        {
            return ProductKnownProjects.None;
        }

        List<string> existing = new();
        int skipped = 0;
        foreach (KeyValuePair<string, JsonNode?> kv in projectsMap)
        {
            string path = kv.Key;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            // Directory.Exists is the canonical "is this still a usable
            // project root?" check.  A path with permission issues will
            // also report false here — same graceful-skip behaviour the
            // backup's own per-project capture uses.
            try
            {
                if (Directory.Exists(path))
                {
                    existing.Add(path);
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Treat permission/IO errors during the existence check
                // as "not usable" rather than failing the discovery.
                skipped++;
            }
        }

        return new ProductKnownProjects(existing, skipped);
    }
}
