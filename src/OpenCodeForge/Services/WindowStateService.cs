using System.Text.Json;
using Bennewitz.Ninja.OpenCode.Sdk;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge.Services;

/// <summary>Remembered window geometry and app-level preferences, persisted between runs.</summary>
/// <remarks>
/// ⚠ <b>This record is a PERSISTED FORMAT.</b> Every property is written to and read back from
/// <c>OpenCodeForge-gui-state.json</c>, so adding one has to stay readable by a copy of the app
/// that predates it and vice versa. The two update-related properties below were added after the
/// geometry ones; both carry defaults that make an older file — which has neither — behave the
/// way the app behaved before they existed.
/// </remarks>
/// <param name="Width">Window width in device-independent pixels.</param>
/// <param name="Height">Window height.</param>
/// <param name="IsMaximized">Whether the window was maximized when last closed.</param>
/// <param name="CheckForUpdatesOnLaunch">
/// Whether the app may check GitHub for a newer release at launch and periodically after.
/// <para>
/// ⭐ <b>Defaults to <see langword="true"/>, and the default is what an absent field
/// deserialises to</b> — so a state file written before this property existed opts IN. That is
/// the deliberate choice: the check is silent unless it finds something, and a user who never
/// saw the toggle is better served by being told an update exists. Clearing it stops the launch
/// and periodic checks; it never stops the explicit About-dialog button, because clicking that
/// is consent on its own.
/// </para>
/// </param>
/// <param name="DismissedUpdateVersions">
/// Release tags the user has already dismissed the banner for, so it does not return for the
/// same version on every launch.
/// <para>
/// Tags rather than versions, because the tag is what the release is addressed by and what the
/// banner links to. Empty by default; an older state file therefore has dismissed nothing, which
/// is correct — it could not have.
/// </para>
/// </param>
public sealed record WindowState(
    double Width,
    double Height,
    bool IsMaximized,
    bool CheckForUpdatesOnLaunch = true,
    IReadOnlyList<string>? DismissedUpdateVersions = null)
{
    /// <summary>The size a first run opens at.</summary>
    public static WindowState Default { get; } = new(1280, 860, IsMaximized: false);

    /// <summary>Dismissed tags, never <see langword="null"/>.</summary>
    /// <remarks>
    /// A separate accessor rather than a non-null parameter default: records cannot default a
    /// parameter to a new collection, and JSON deserialisation of an absent array yields null
    /// whatever the constructor says. Normalising on read means no caller has to remember.
    /// </remarks>
    public IReadOnlyList<string> Dismissed => DismissedUpdateVersions ?? [];
}

/// <summary>
/// Loads and saves this app's window geometry.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>The state file must not live under the other product's home directory.</b> The sibling app
/// keeps its GUI state in <c>~/.claude/cache/</c>, which is correct for it and wrong here twice
/// over: it would write into a directory belonging to a tool this app does not manage, and
/// uninstalling that tool would take this app's state with it.
/// </para>
/// <para>
/// State goes beside the config this app edits — <c>$OPENCODE_CONFIG_DIR</c> when set, otherwise
/// <c>~/.config/opencode/</c> — in a <c>cache/</c> subdirectory, under a name that identifies the
/// writer. A generic <c>gui-state.json</c> would be easy to mistake for state belonging to the
/// agent itself.
/// </para>
/// <para>
/// Honouring <c>OPENCODE_CONFIG_DIR</c> matters beyond tidiness: a user who relocates their config
/// expects everything about that install to move with it, and tests rely on the same redirection to
/// avoid touching a real home directory.
/// </para>
/// </remarks>
public static class WindowStateService
{
    private static string StatePath => Path.Combine(
        OpenCodePaths.GlobalDirectory(OpenCodeEnvironment.FromProcess()),
        "cache",
        "OpenCodeForge-gui-state.json");

    /// <summary>Read the remembered geometry, falling back to <see cref="WindowState.Default"/>.</summary>
    /// <remarks>
    /// Any failure returns the default rather than propagating: a corrupt or unreadable state file
    /// must never stop the app from opening. It is the least important file the app owns.
    /// </remarks>
    public static WindowState Load()
    {
        try
        {
            string path = StatePath;
            if (!File.Exists(path))
            {
                return WindowState.Default;
            }

            WindowState? state = JsonSerializer.Deserialize(
                File.ReadAllText(path), WindowStateJson.Default.WindowState);

            // A file containing `null`, or nonsensical dimensions, is as useless as no file.
            return state is null || state.Width <= 0 || state.Height <= 0
                ? WindowState.Default
                : state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Debug(ex, "[WindowState] could not read state; using defaults");
            return WindowState.Default;
        }
    }

    /// <summary>Persist <paramref name="state"/>, writing through a temporary file.</summary>
    /// <remarks>
    /// Written to a sibling temp file and moved into place, so an interrupted write cannot leave a
    /// truncated file that the next run has to recover from.
    /// </remarks>
    public static void Save(WindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string path = StatePath;
        string? tmp = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            tmp = path + $".tmp-{Guid.NewGuid():N}";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, WindowStateJson.Default.WindowState));
            File.Move(tmp, path, overwrite: true);
            tmp = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "[WindowState] could not save state");
        }
        finally
        {
            if (tmp is not null && File.Exists(tmp))
            {
                try
                {
                    File.Delete(tmp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Debug(ex, "[WindowState] could not clean up a temporary state file");
                }
            }
        }
    }
}
