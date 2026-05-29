using System.Text.Json;
using System.Text.Json.Serialization;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>Persists and restores window geometry and last-selected navigation node.</summary>
public static class WindowStateService
{
    /// <summary>
    /// Process-wide counter of <see cref="Load"/> invocations. the cache fix should hold the count to exactly 1 per
    /// session (a single hydrate at MWVM construction). Tests can probe
    /// this; production code logs the count at <c>Log.Debug</c> level
    /// to support startup-read regression detection without enabling a
    /// dedicated profiler.
    /// </summary>
    /// <remarks>
    /// Marked <c>internal</c> + uses <c>InternalsVisibleTo("Bennewitz.Ninja.ClaudeForge.Tests")</c>
    /// so tests can read it without exposing implementation detail to
    /// general consumers. Reset for tests via <see cref="ResetLoadCountForTesting"/>.
    /// </remarks>
    internal static int LoadCount => _loadCount;

    private static int _loadCount;

    /// <summary>Test seam: zero the counter so each test starts fresh.</summary>
    internal static void ResetLoadCountForTesting()
    {
        Interlocked.Exchange(ref _loadCount, 0);
    }

    // Computed on every access (rather than cached in a static readonly) so
    // tests that mutate PlatformPaths.TestUserProfileOverride between runs
    // see the right sandboxed path. The lookup is cheap — three Path.Combine
    // calls — and production code never changes the user-profile override
    // at runtime, so this has no effective cost.
    //
    // Filename includes the app name so the file is visibly attributable when
    // a user inspects ~/.claude/cache/ — generic "gui-state.json" was easy to
    // mistake for shared state belonging to Claude itself rather than to this
    // editor. Also future-proofs against a sibling tool dropping a file with
    // the same name into the same cache dir.
    private static string StatePath =>
        Path.Combine(PlatformPaths.ClaudeHome, "cache", "ClaudeForge-gui-state.json");

    public static WindowState Load()
    {
        // count every Load call so the cache fix can be
        // verified at runtime. The first call (initial cache hydrate) is
        // expected; anything after that means a regression introduced a
        // new disk read on a hot path. Logged at Debug so default-level
        // consumers don't see noise; the rolling log file (which captures
        // all levels) preserves the trace for bug reports.
        int n = Interlocked.Increment(ref _loadCount);
        // Phase 1.2 — log call wrapped in try/catch because Load() can run
        // during cold-start before Serilog is configured. The static `Log`
        // façade is a no-op when uninitialised, but a future logger swap
        // (or eager-fail sink) would otherwise break startup. The
        // `LoadCount` counter is the contract; the log line is best-effort.
        try
        {
            Log.Debug("[WindowState] Load() called (count={Count})", n);
        }
        catch (Exception)
        {
            // Logging is best-effort during early startup. Swallow to keep
            // the cache-hydrate path bullet-proof.
        }

        try
        {
            if (File.Exists(StatePath))
            {
                string json = File.ReadAllText(StatePath);
                // Use source-generated context for trimming compatibility (Release builds use PublishTrimmed=true).
                return JsonSerializer.Deserialize(json, AppJsonContext.Default.WindowState) ?? new WindowState();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new WindowState();
    }

    public static void Save(WindowState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            // Use source-generated context for trimming compatibility.
            string json = JsonSerializer.Serialize(state, AppJsonContext.Default.WindowState);
            // Atomic write: temp-file + rename. Plain File.WriteAllText opens with
            // FileMode.Create, which truncates the destination immediately and writes
            // in chunks — a crash mid-write leaves a 0-byte file. WindowState holds
            // BackupDirectory, theme, profile choice, last-backup time, and the
            // credentials-include preference; corruption silently resets all of them.
            // Same pattern is already used by ConfigFileLoader.SaveAsync for config
            // files (see CLAUDE.md "Common gotchas").
            string tmp = StatePath + $".tmp-{Guid.NewGuid():N}";
            File.WriteAllText(tmp, json);
            File.Move(tmp, StatePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes the persisted UI-state file from disk. Used by the
    /// <c>Clear App Data</c> button in the status bar — the caller is
    /// expected to exit the process immediately afterward so the next
    /// launch starts from clean defaults.
    /// <para>
    /// Only this single file is removed. Claude config files
    /// (<c>~/.claude/settings.json</c> and friends) are intentionally
    /// untouched: app-data clearing is a UI-state reset, not a config wipe.
    /// </para>
    /// <para>
    /// IO failures are swallowed — clearing should never block the user
    /// from exiting. If the file cannot be deleted (e.g. locked by another
    /// process or a permission error), the next launch will simply
    /// re-read the stale file and overwrite it on next save anyway.
    /// </para>
    /// </summary>
    /// <returns>The path that was targeted, for logging purposes.</returns>
    public static string Delete()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                File.Delete(StatePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return StatePath;
    }
}

public sealed class WindowState
{
    [JsonPropertyName("width")] public double Width { get; set; } = 1200;

    // The persisted-state path (after
    // first save) uses the actual window size so this only affects the
    // cold-start default.
    [JsonPropertyName("height")] public double Height { get; set; } = 900;
    [JsonPropertyName("x")] public double? X { get; set; }
    [JsonPropertyName("y")] public double? Y { get; set; }
    [JsonPropertyName("lastNode")] public string? LastSelectedNodeTitle { get; set; }
    [JsonPropertyName("projectRoot")] public string? ProjectRoot { get; set; }

    /// <summary>"System" = follow OS (default), "Dark" or "Light" = explicit override.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "System";

    [JsonPropertyName("selectedProfile")] public string? SelectedProfile { get; set; }

    /// <summary>Per-header IsExpanded state keyed by node Title. Missing keys default to expanded.</summary>
    [JsonPropertyName("navExpanded")]
    public Dictionary<string, bool> NavHeaderExpanded { get; set; } = new();

    /// <summary>
    /// Tri-state credential-inclusion preference for Backup:
    /// <c>null</c> = never asked (show the prompt), <c>true</c>/<c>false</c> = remembered choice.
    /// </summary>
    [JsonPropertyName("includeCredentialsInBackup")]
    public bool? IncludeCredentialsInBackup { get; set; }

    /// <summary>UTC timestamp of the most recent successful backup, or <c>null</c> if none yet.</summary>
    [JsonPropertyName("lastBackupUtc")]
    public DateTime? LastBackupUtc { get; set; }

    /// <summary>
    /// Folder where new backup archives are written. <c>null</c> / absent means the user
    /// has not yet chosen a folder — the Backup button is disabled until this is set.
    /// </summary>
    [JsonPropertyName("backupDirectory")]
    public string? BackupDirectory { get; set; }

    /// <summary>
    /// Folder the Restore tab scans for existing archives. Defaults to the same value
    /// as <see cref="BackupDirectory"/> the first time either is set, but can be
    /// changed independently afterward.
    /// </summary>
    [JsonPropertyName("restoreDirectory")]
    public string? RestoreDirectory { get; set; }

    /// <summary>
    /// user preference for whether the synthetic "Welcome"
    /// nav node appears in the navigation tree.  Default <c>true</c> so
    /// first-launch users see the orientation page; unchecking the
    /// "Show on launch" checkbox on the Welcome page itself flips this
    /// to <c>false</c>, hiding the node on subsequent launches.  The
    /// Welcome page itself remains reachable by clicking the
    /// "Claude Code" / "Claude Desktop" header nodes (which have no
    /// Editor, so <c>ActiveEditor</c> stays null and the existing
    /// WelcomeView renders).  Clearing app data resets to <c>true</c>.
    /// </summary>
    [JsonPropertyName("showWelcomeNode")]
    public bool ShowWelcomeNode { get; set; } = true;

    /// <summary>
    /// User preference for the once-per-launch check that queries
    /// GitHub for a newer ClaudeForge release.  Default <c>true</c> —
    /// users want to know when a new release is out, and the check is
    /// silent on failure so there's no downside to leaving it on.
    /// Toggled via the Essentials page card "Check for updates on
    /// launch".  Clearing app data resets to <c>true</c>.
    /// </summary>
    [JsonPropertyName("checkForUpdatesOnLaunch")]
    public bool CheckForUpdatesOnLaunch { get; set; } = true;

    /// <summary>
    /// Set of release tags the user has explicitly dismissed via the
    /// "Update available" banner's Dismiss button (or X-close).
    /// Stored as a flat list keyed by the raw GitHub tag string
    /// (e.g. <c>"v2026.5.524"</c>) so the comparison is byte-exact.
    /// On the next launch, the auto-check still runs and still fetches
    /// the latest release — but if the latest tag is in this list, the
    /// banner stays suppressed.  When a NEWER release supersedes the
    /// dismissed tag, the banner fires again (per-version
    /// persistence — not "dismiss forever").  Clearing app data
    /// resets to an empty list, so the next applicable release will
    /// surface the banner again.
    /// </summary>
    [JsonPropertyName("dismissedUpdateVersions")]
    public List<string> DismissedUpdateVersions { get; set; } = new();
}