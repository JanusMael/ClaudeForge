namespace Bennewitz.Ninja.AgentForge.Core.Platform;

/// <summary>
/// The environment variables that change where Claude Code reads its configuration from,
/// captured as a value so path resolution can be tested without mutating the process.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Deliberately the same shape as <c>OpenCodeEnvironment</c>, which solved this first.</b>
/// A record rather than <see cref="Environment.GetEnvironmentVariable(string)"/> calls inside the
/// accessors, because the process environment is global mutable state and this suite runs many
/// tests in one process: a test that set <c>CLAUDE_CONFIG_DIR</c> would leak into whatever ran
/// alongside it, and the resulting failure would look like a flake rather than a missing reset.
/// </para>
/// <para>
/// ⚠ <b><c>TestUserProfileOverride</c> stays <see cref="AsyncLocal{T}"/> and keeps priority.</b>
/// That is measured rather than stylistic — its own remark records <i>"a plain static races there
/// — ~48 failures/run"</i>. This record is not a replacement for it; it is the production-facing
/// half, and passing a value needs no ambient state at all.
/// </para>
/// <para>
/// Reading the real environment happens in exactly one place, <see cref="FromProcess"/>, so that
/// is the only member no test exercises directly.
/// </para>
/// <para>
/// ⛔ <b>This does NOT relocate managed settings, and that is settled rather than overlooked.</b>
/// Managed policy lives in a per-OS <i>system</i> directory, outside the config directory
/// entirely, so a user-controlled variable cannot move it. If it could, a user could escape
/// enterprise policy by exporting one variable. See <see cref="PlatformPaths.ManagedSettingsPath"/>.
/// </para>
/// </remarks>
/// <param name="ConfigDir">
/// <c>CLAUDE_CONFIG_DIR</c> — relocates the user config directory away from <c>~/.claude</c>.
/// Claude Code's own wording is <i>"every <c>~/.claude</c> path lives under that directory
/// instead"</i>.
/// </param>
public sealed record ClaudeEnvironment(string? ConfigDir = null)
{
    /// <summary>Nothing set — the plain default installation.</summary>
    public static ClaudeEnvironment Empty { get; } = new();

    /// <summary>Read the variables from the current process environment.</summary>
    public static ClaudeEnvironment FromProcess() =>
        new(NullIfBlank(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")));

    /// <summary>
    /// The config directory as an absolute path, or <c>null</c> when the variable is unset.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>The plan left this open, so the choice is made here and stated here: RESOLVE, never
    /// refuse and never create.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Resolve</b>, because a relative value would otherwise be re-resolved against whatever
    /// the current directory happened to be at each call, and the whole point of reading the
    /// environment once is that the answer cannot change mid-session.
    /// </description></item>
    /// <item><description>
    /// <b>Never create</b>, because a path accessor that makes directories is a side effect in the
    /// one place nobody expects one — and a typo in the variable would silently produce a new empty
    /// config tree rather than an error anyone could see.
    /// </description></item>
    /// <item><description>
    /// <b>Never refuse</b>, because a directory that does not exist yet is ordinary: Claude Code
    /// creates it on first write, so refusing would break the user who set the variable before
    /// running the agent. A missing directory reads as "no settings here", which is true.
    /// </description></item>
    /// </list>
    /// <para>
    /// ⛔ A value that cannot be resolved at all — malformed, or containing characters the platform
    /// rejects — falls back to <c>null</c> rather than throwing. Throwing here would take down
    /// startup from a path accessor, and the fallback is the documented default the user already
    /// had.
    /// </para>
    /// </remarks>
    public string? ResolvedConfigDir
    {
        get
        {
            if (ConfigDir is null) { return null; }

            try
            {
                return Path.GetFullPath(ConfigDir);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// An unset variable and one set to the empty string mean the same thing here: a config
    /// directory of <c>""</c> is not a directory, and treating it as one produces a config tree
    /// rooted at the current working directory.
    /// </summary>
    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
