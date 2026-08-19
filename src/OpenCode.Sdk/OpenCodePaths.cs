using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// Where OpenCode keeps its configuration.
/// </summary>
/// <remarks>
/// Built on <see cref="PlatformPaths.UserProfile"/> rather than
/// <see cref="Environment.SpecialFolder.UserProfile"/> directly, so every path here honours
/// the test sandbox. ⚠ That override is <c>AsyncLocal</c> and in-process only — it does not
/// sandbox a child process or the real GUI.
/// </remarks>
public static class OpenCodePaths
{
    /// <summary>The project config file names, in the order OpenCode looks for them.</summary>
    /// <remarks>
    /// ⚠ <b>Three names, not one.</b> A walk that checks only <c>opencode.json</c> silently
    /// misses a project that uses <c>opencode.jsonc</c> or the <c>.opencode/</c> directory
    /// form, and the app then shows the user a different authoritative file than the one the
    /// agent is reading. Order matters: the first match at a given directory wins.
    /// </remarks>
    public static IReadOnlyList<string> ProjectFileNames { get; } =
    [
        "opencode.json",
        "opencode.jsonc",
        Path.Combine(".opencode", "opencode.json"),
    ];

    /// <summary>
    /// The global config directory — <c>$OPENCODE_CONFIG_DIR</c> when set, otherwise
    /// <c>~/.config/opencode</c>.
    /// </summary>
    public static string GlobalDirectory(OpenCodeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return env.ConfigDir is { } dir
            ? dir
            : Path.Combine(PlatformPaths.UserProfile, ".config", "opencode");
    }

    /// <summary>
    /// The global config file. <c>opencode.json</c> is preferred, but
    /// <c>opencode.jsonc</c> is returned when it is the one that exists — OpenCode writes the
    /// <c>.jsonc</c> form there by default, so checking only the <c>.json</c> name reports
    /// "no global config" for a perfectly ordinary installation.
    /// </summary>
    public static string GlobalConfigPath(OpenCodeEnvironment env)
    {
        string dir = GlobalDirectory(env);
        string json = Path.Combine(dir, "opencode.json");
        string jsonc = Path.Combine(dir, "opencode.jsonc");

        // Prefer whichever exists; fall back to the .json name so a not-yet-created file
        // still has a canonical path to report and to write to.
        if (!File.Exists(json) && File.Exists(jsonc))
        {
            return jsonc;
        }

        return json;
    }

    /// <summary>The global TUI config — <c>tui.json</c> beside the main config.</summary>
    public static string TuiConfigPath(OpenCodeEnvironment env)
        => Path.Combine(GlobalDirectory(env), "tui.json");

    /// <summary>
    /// The nearest project config at or above <paramref name="startDirectory"/>, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Walks upward one directory at a time, trying every name in
    /// <see cref="ProjectFileNames"/> before moving up. That order is the point: a
    /// <c>opencode.jsonc</c> in the current directory must beat an <c>opencode.json</c> in the
    /// parent, because it is the nearer file that OpenCode uses.
    /// </para>
    /// <para>
    /// The walk is bounded by the filesystem root. It does not stop at a worktree boundary,
    /// because a config above the repository is still a config OpenCode would read.
    /// </para>
    /// </remarks>
    public static string? FindProjectConfig(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }

        while (current is not null)
        {
            foreach (string name in ProjectFileNames)
            {
                string candidate = Path.Combine(current.FullName, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            current = current.Parent;
        }

        return null;
    }
}
