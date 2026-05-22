namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// Represents a single item in the unified profile dropdown.
/// <para>
/// The dropdown merges CLI and Desktop profiles by name: if a profile with the
/// same name exists in both <c>~/.claude/profiles/</c> and the Desktop profiles
/// directory, a single entry with <see cref="HasCli"/> and <see cref="HasDesktop"/>
/// both true is shown.  CLI-only profiles appear first; Desktop-only profiles
/// appear last.  The sentinel <see cref="Global"/> entry always comes first.
/// </para>
/// </summary>
/// <param name="Name">Profile name, or <see cref="GlobalName"/> for the live-config sentinel.</param>
/// <param name="HasCli">True when a CLI profile with this name exists.</param>
/// <param name="HasDesktop">True when a Desktop profile with this name exists.</param>
public sealed record UnifiedProfileEntry(string Name, bool HasCli, bool HasDesktop)
{
    /// <summary>Display name used for the "(global)" sentinel entry.</summary>
    public const string GlobalName = "(global)";

    /// <summary>
    /// Sentinel entry meaning "use the live global settings for both products".
    /// Both <see cref="HasCli"/> and <see cref="HasDesktop"/> are true so the
    /// workspace loader knows no profile override applies.
    /// </summary>
    public static readonly UnifiedProfileEntry Global = new(GlobalName, HasCli: true, HasDesktop: true);

    /// <summary>True when this entry represents the global (non-profiled) state.</summary>
    public bool IsGlobal => Name == GlobalName;

    /// <summary>
    /// Show the CC chiclet in the dropdown item.
    /// Only shown for non-global entries that have a CLI profile.
    /// </summary>
    public bool ShowCliChiclet => HasCli && !IsGlobal;

    /// <summary>
    /// Show the Desktop chiclet in the dropdown item.
    /// Only shown for non-global entries that have a Desktop profile.
    /// </summary>
    public bool ShowDesktopChiclet => HasDesktop && !IsGlobal;

    /// <summary>
    /// Tooltip shown when the user hovers over this item in the profile dropdown.
    /// Explains what the entry represents (global vs. named profile, CLI/Desktop coverage).
    /// </summary>
    public string Tooltip => IsGlobal
        ? "Global profile (~/.claude/) — applies to all projects unless a project-specific profile overrides it"
        : $"Profile '{Name}' stored in " + (HasCli && HasDesktop
            ? "Claude Code and Claude Desktop"
            : HasCli
                ? "Claude Code"
                : "Claude Desktop");

    /// <inheritdoc/>
    public override string ToString()
    {
        return Name;
    }
}