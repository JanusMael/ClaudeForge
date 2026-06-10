namespace Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

/// <summary>
/// Filesystem anchors needed to resolve path-based permission rules
/// (<c>Read</c> / <c>Edit</c> / <c>Write</c>) against a candidate path.
/// </summary>
/// <remarks>
/// Per the Claude Code permissions spec
/// (<see href="https://code.claude.com/docs/en/permissions">code.claude.com/docs/en/permissions</see>
/// §"Read and Edit"), a path rule's anchor type determines its base directory:
/// <list type="bullet">
///   <item><c>//path</c> → filesystem root (absolute)</item>
///   <item><c>~/path</c> → <see cref="HomeDirectory"/></item>
///   <item><c>/path</c> → <see cref="ProjectRoot"/> (NOT absolute)</item>
///   <item><c>path</c> or <c>./path</c> → <see cref="CurrentDirectory"/></item>
/// </list>
/// Bare filenames follow gitignore semantics and match at any depth.
/// </remarks>
public sealed record PermissionMatchContext(
    string CurrentDirectory,
    string ProjectRoot,
    string HomeDirectory)
{
    /// <summary>
    /// A context seeded from the running process — current directory and the
    /// user profile as home, with the project root defaulting to the current
    /// directory. Suitable when no richer project context is available; the
    /// GUI host should supply a context with the real project root instead.
    /// </summary>
    public static PermissionMatchContext FromEnvironment()
    {
        string cwd = Directory.GetCurrentDirectory();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new PermissionMatchContext(cwd, cwd, home);
    }
}
