namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// The four environment variables that change where OpenCode reads its configuration from,
/// captured as a value so discovery can be tested without mutating the process.
/// </summary>
/// <remarks>
/// <para>
/// A record rather than direct <see cref="Environment.GetEnvironmentVariable(string)"/> calls
/// inside discovery, because process environment is global mutable state and this suite runs
/// many tests in one process. A test that set <c>OPENCODE_CONFIG</c> would leak into whatever
/// ran alongside it, and the resulting failure would look like a flake rather than a missing
/// reset. <c>TestUserProfileOverride</c> solves the same problem for the home directory by
/// being <c>AsyncLocal</c>; passing a value is simpler still and needs no ambient state at all.
/// </para>
/// <para>
/// Reading the real environment happens in exactly one place, <see cref="FromProcess"/>, so
/// that is the only thing a test cannot exercise directly.
/// </para>
/// </remarks>
/// <param name="ConfigDir">
/// <c>OPENCODE_CONFIG_DIR</c> — relocates the global config directory away from
/// <c>~/.config/opencode</c>.
/// </param>
/// <param name="ConfigPath">
/// <c>OPENCODE_CONFIG</c> — an explicit path to a config file, forming the Custom scope.
/// </param>
/// <param name="InlineContent">
/// <c>OPENCODE_CONFIG_CONTENT</c> — a whole config supplied as text. Highest-priority
/// non-managed layer, and the only one with no file behind it.
/// </param>
/// <param name="ProjectConfigDisabled">
/// <c>OPENCODE_DISABLE_PROJECT_CONFIG=1</c> — removes the project layer entirely. The
/// effective view must honour it or it will disagree with the running agent about which
/// settings apply.
/// </param>
public sealed record OpenCodeEnvironment(
    string? ConfigDir = null,
    string? ConfigPath = null,
    string? InlineContent = null,
    bool ProjectConfigDisabled = false)
{
    /// <summary>Nothing set — the plain default installation.</summary>
    public static OpenCodeEnvironment Empty { get; } = new();

    /// <summary>Read the four variables from the current process environment.</summary>
    /// <remarks>
    /// Only <c>"1"</c> disables the project layer. Treating any non-empty value as true would
    /// make <c>OPENCODE_DISABLE_PROJECT_CONFIG=0</c> disable it, which is the opposite of what
    /// someone writing that means.
    /// </remarks>
    public static OpenCodeEnvironment FromProcess()
    {
        return new OpenCodeEnvironment(
            NullIfBlank(Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR")),
            NullIfBlank(Environment.GetEnvironmentVariable("OPENCODE_CONFIG")),
            NullIfBlank(Environment.GetEnvironmentVariable("OPENCODE_CONFIG_CONTENT")),
            Environment.GetEnvironmentVariable("OPENCODE_DISABLE_PROJECT_CONFIG") == "1");
    }

    /// <summary>
    /// An unset variable and one set to the empty string mean the same thing here: a config
    /// path of <c>""</c> is not a path, and treating it as one produces a discovered file
    /// rooted at the current directory.
    /// </summary>
    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
