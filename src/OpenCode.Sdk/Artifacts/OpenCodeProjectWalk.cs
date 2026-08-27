namespace Bennewitz.Ninja.OpenCode.Sdk.Artifacts;

/// <summary>
/// The directories OpenCode reads project-scope artifacts from: every ancestor of the working
/// directory, nearest first, up to and including the git worktree root.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Every ancestor contributes, not just the nearest.</b> Measured against v1.17.9: with
/// <c>.opencode/agent/</c> directories at both the worktree root and an intermediate directory,
/// running from a deeper subdirectory resolved the agents from <b>both</b>. A walk that stopped at
/// the first <c>.opencode/</c> it met would silently drop every artifact declared further up, and
/// the page would show fewer agents than the agent itself is running.
/// </para>
/// <para>
/// ⚠ <b>The worktree root is the boundary, and without one there is no boundary.</b> An
/// <c>.opencode/agent/</c> placed one level above the worktree root was <b>not</b> read; removing
/// the repository's <c>.git</c> and re-running read it. So in a directory that is not inside a git
/// repository the walk genuinely continues upward — which is worth showing a user rather than
/// hiding, because it means a stray <c>.opencode/</c> in a home directory applies to every
/// non-repository project beneath it.
/// </para>
/// <para>
/// ⚠ <b><c>.git</c> is tested as a file as well as a directory.</b> A linked worktree and a
/// submodule both record it as a file containing a <c>gitdir:</c> pointer, so a directory-only test
/// would walk straight past the root of exactly the checkouts this repository's own tooling
/// already handles specially.
/// </para>
/// </remarks>
public static class OpenCodeProjectWalk
{
    /// <summary>
    /// The ancestor chain for <paramref name="startDirectory"/>, nearest first.
    /// </summary>
    /// <param name="startDirectory">
    /// The working directory to walk up from. A blank or unusable path yields nothing rather than
    /// throwing — this runs while building a page, and one bad path should cost its own rows, not
    /// the whole list.
    /// </param>
    /// <returns>
    /// The directories to look for <c>.opencode/</c> and <c>.claude/</c> in, nearest first. Empty
    /// when <paramref name="startDirectory"/> cannot be resolved.
    /// </returns>
    public static IReadOnlyList<string> Ancestors(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return [];
        }

        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException
                                      or NotSupportedException or IOException
                                      or UnauthorizedAccessException)
        {
            return [];
        }

        List<string> ancestors = [];
        while (current is not null)
        {
            ancestors.Add(current.FullName);

            if (IsWorktreeRoot(current.FullName))
            {
                break;
            }

            current = current.Parent;
        }

        return ancestors;
    }

    /// <summary>
    /// Whether <paramref name="directory"/> holds a <c>.git</c> entry of either shape.
    /// </summary>
    private static bool IsWorktreeRoot(string directory)
    {
        string git = Path.Combine(directory, ".git");
        try
        {
            return Directory.Exists(git) || File.Exists(git);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable ancestor is not a stopping point: treating it as the root would
            // truncate the walk on exactly the machines where permissions are unusual.
            return false;
        }
    }
}
