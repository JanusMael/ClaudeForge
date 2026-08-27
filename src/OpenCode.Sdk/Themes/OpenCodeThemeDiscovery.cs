namespace Bennewitz.Ninja.OpenCode.Sdk.Themes;

/// <summary>
/// The theme names this machine can offer for the TUI's <c>theme</c> setting.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Why discovery exists at all: OpenCode has no built-in theme table to read.</b> Measured
/// against the installed <c>opencode</c> v1.17.9 binary — its TUI theme store is initialised
/// <c>active:"opencode"</c> and its <c>source</c> is a disk-scan discover function, so apart from
/// that one default every theme is a <c>themes/*.json</c> file or a plugin contribution. A static
/// suggestion list therefore cannot be right for anyone; the names have to come off the disk.
/// </para>
/// <para>
/// ⚠⚠ <b>Only the config directory is scanned, and the omission is deliberate.</b> OpenCode also
/// scans <c>.opencode/themes/*.json</c> in the <b>current working directory and every ancestor of
/// it</b>, walking to the filesystem root. That set depends on where <c>opencode</c> was launched
/// from, which a GUI editing a global file does not know and must not guess: a name offered from
/// this app's own cwd would resolve for OpenCode only by coincidence. The config directory is
/// cwd-independent, so what it contains is true wherever OpenCode runs. The remaining locations are
/// named in the schema overlay's <c>theme.description</c> instead, which informs without claiming.
/// </para>
/// <para>
/// ⚠ <b>Every failure is swallowed and yields fewer suggestions, never an error.</b> This feeds a
/// free-form picker whose list is explicitly not a limit, so a missing directory, a denied ACL or a
/// racing delete must cost the user a suggestion and nothing more. An exception here would take out
/// the whole Appearance page for a cosmetic feature.
/// </para>
/// </remarks>
public static class OpenCodeThemeDiscovery
{
    /// <summary>The one theme name that does not come from a file.</summary>
    /// <remarks>
    /// Also stated as the sole <c>examples</c> entry in <c>opencode-tui.overlay.json</c>, so the
    /// field still offers a picker naming the default even if this discovery is never called.
    /// <c>OpenCodeThemeDiscoveryTests.TheBuiltInNameMatchesTheSchemaOverlay</c> keeps the two in
    /// step — two copies of one fact is exactly the pair that drifts.
    /// </remarks>
    public const string BuiltInTheme = "opencode";

    /// <summary>The directory name OpenCode scans for theme files.</summary>
    public const string ThemeDirectoryName = "themes";

    /// <summary>
    /// The theme names to offer: the built-in one, then whatever the config directory holds.
    /// </summary>
    /// <param name="env">Which OpenCode environment to resolve the config directory from.</param>
    /// <returns>
    /// Distinct names, the built-in first and the discovered ones in ordinal order. Never empty —
    /// it always contains at least <see cref="BuiltInTheme"/>.
    /// </returns>
    /// <remarks>
    /// ⚠ The built-in comes <b>first</b> rather than sorting in among the rest: it is the value the
    /// user gets by doing nothing, so it is the one worth seeing without scrolling. Everything after
    /// it is sorted, because a directory listing's order is not a meaningful ranking and an unstable
    /// picker order is its own small bug.
    /// </remarks>
    public static IReadOnlyList<string> Discover(OpenCodeEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        List<string> names = [BuiltInTheme];
        names.AddRange(DiscoverFileThemes(OpenCodePaths.GlobalDirectory(env)));
        return names;
    }

    /// <summary>The theme names contributed by <c>*.json</c> files under one config directory.</summary>
    /// <remarks>
    /// A theme's name is its file name without the extension — that is how OpenCode keys them
    /// (<c>basename(path, ".json")</c>), so a file named <c>Dracula.json</c> is the theme
    /// <c>Dracula</c> and case is preserved rather than normalised.
    /// </remarks>
    internal static IReadOnlyList<string> DiscoverFileThemes(string configDirectory)
    {
        string dir = Path.Combine(configDirectory, ThemeDirectoryName);

        string[] files;
        try
        {
            // ⚠ THIS CHECK AND THE CATCH BELOW ARE REDUNDANT WITH EACH OTHER, and canaries
            // measured it precisely: disabling either one alone reddens NOTHING, because
            // `DirectoryNotFoundException` derives from `IOException`, so whichever survives still
            // returns the same empty list. Only disabling BOTH lets the exception escape. So
            // neither line is individually load-bearing and the pair is what holds — stated here
            // because a comment claiming either one is "the guard" would be false.
            //
            // The check stays for cost, not correctness: "no themes directory" is the common case
            // on every machine that has never added a theme, and throwing plus catching on the
            // common path is paid every time the Appearance page builds its editors.
            if (!Directory.Exists(dir))
            {
                return [];
            }

            files = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // See the type remarks: fewer suggestions, never a failure. This covers a denied ACL
            // and a delete racing the enumeration — neither of which the check above can see —
            // as well as the missing directory it shares with that check.
            return [];
        }

        return [.. files
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            // The built-in is prepended by the caller, so a themes/opencode.json — which OpenCode
            // would let shadow the built-in — must not produce the name twice in the picker.
            .Where(n => !string.Equals(n, BuiltInTheme, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)];
    }
}
