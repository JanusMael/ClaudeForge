using System.Security;
using Bennewitz.Ninja.ClaudeForge.Localization;

namespace Bennewitz.Ninja.ClaudeForge.Services;

/// <summary>
/// Builds the project-loaded indicator suffix for the window titlebar
/// (added 2026-05-15).  Cross-platform — reads <c>.git/HEAD</c> as plain
/// text rather than shelling out to <c>git</c>, so it works on Windows /
/// macOS / Linux uniformly and adds no external-process dependency.
/// </summary>
/// <remarks>
/// <para>
/// Three resolution tiers, tried in order:
/// </para>
/// <list type="number">
///   <item>Empty / null project root → returns <see cref="Strings.TitleNoProjectLoaded"/>.</item>
///   <item>Project root contains a readable <c>.git/HEAD</c> (or worktree
///   <c>gitdir:</c> pointer chase to the same) → returns the branch name
///   (or short SHA for a detached HEAD).</item>
///   <item>Otherwise → returns the leaf folder name of the project root.</item>
/// </list>
/// <para>
/// All IO is wrapped in narrow exception filters; any failure during a
/// resolution tier falls through to the next tier rather than throwing,
/// so the titlebar update is always best-effort and never blocks the UI.
/// </para>
/// <para>
/// Static so the helper is unit-testable without spinning up a
/// MainWindowViewModel; <see cref="BuildIndicator"/> is the one entry
/// point production code should use.
/// </para>
/// </remarks>
public static class ProjectIndicator
{
    /// <summary>
    /// Returns the string to append to <see cref="Strings.AppTitle"/> after
    /// the em-dash separator.  Examples (with prefix added by caller):
    /// <list type="bullet">
    ///   <item><c>"ClaudeForge — No Project Loaded"</c> when <paramref name="projectRoot"/> is null / empty.</item>
    ///   <item><c>"ClaudeForge — feature/auth"</c> when the project is a git repo on a named branch.</item>
    ///   <item><c>"ClaudeForge — a1b2c3d"</c> when the project is a git repo in detached HEAD.</item>
    ///   <item><c>"ClaudeForge — my-project"</c> when the project is not a git repo.</item>
    /// </list>
    /// </summary>
    public static string BuildIndicator(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return Strings.TitleNoProjectLoaded;
        }

        // Fall back to folder name.  TrimEnd handles both Windows and POSIX
        // separators so "C:\projects\foo\" and "/home/user/foo/" both yield
        // "foo".
        string projectRootTrimmed = Path.GetFileName(projectRoot.TrimEnd('/', '\\'));

        // Try git branch detection first — most informative when applicable.
        string? gitBranch = TryReadGitBranch(projectRoot);
        if (!string.IsNullOrEmpty(gitBranch))
        {
            return $"{projectRootTrimmed} - {gitBranch}";
        }

        return projectRootTrimmed;
    }

    /// <summary>
    /// Reads <c>.git/HEAD</c> at <paramref name="projectRoot"/> and returns
    /// the current branch name, or a short SHA for detached HEAD.  Handles
    /// the standard repo case (<c>.git</c> is a directory) and the
    /// worktree case (<c>.git</c> is a file with <c>gitdir: &lt;path&gt;</c>
    /// pointing at the actual git directory under the main repo's
    /// <c>worktrees/</c> subdirectory).
    /// </summary>
    /// <returns>
    /// The branch name (e.g. <c>"main"</c>, <c>"feature/auth"</c>), a
    /// 7-char short SHA for detached HEAD, or <see langword="null"/> when
    /// the path is not a git repo / HEAD is unreadable / format is unknown.
    /// </returns>
    internal static string? TryReadGitBranch(string projectRoot)
    {
        try
        {
            string gitPath = Path.Combine(projectRoot, ".git");
            string? gitDir;

            if (Directory.Exists(gitPath))
            {
                // Standard case: .git is a directory.
                gitDir = gitPath;
            }
            else if (File.Exists(gitPath))
            {
                // Worktree case: .git is a text file with "gitdir: <path>".
                // The pointed-at path is the worktree's per-worktree dir
                // under the MAIN repo's .git/worktrees/<name>/, and that
                // dir has its own HEAD.  Relative paths are resolved
                // against projectRoot.
                string pointer = File.ReadAllText(gitPath).Trim();
                const string gitDirPrefix = "gitdir: ";
                if (!pointer.StartsWith(gitDirPrefix, StringComparison.Ordinal))
                {
                    return null;
                }

                string pointed = pointer[gitDirPrefix.Length..].Trim();
                gitDir = Path.IsPathRooted(pointed)
                    ? pointed
                    : Path.GetFullPath(Path.Combine(projectRoot, pointed));
                if (!Directory.Exists(gitDir))
                {
                    return null;
                }
            }
            else
            {
                // Not a git repo.
                return null;
            }

            string headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath))
            {
                return null;
            }

            string head = File.ReadAllText(headPath).Trim();

            // Most common: "ref: refs/heads/<branch>".  Branch names can
            // contain slashes (e.g. "feature/auth") so we keep everything
            // after the prefix verbatim.
            const string refPrefix = "ref: refs/heads/";
            if (head.StartsWith(refPrefix, StringComparison.Ordinal))
            {
                return head[refPrefix.Length..];
            }

            // Detached HEAD: the file contains a 40-char hex SHA.  Truncate
            // to 7 chars to match git's default short-sha rendering.
            if (head.Length >= 7 && IsHex(head, 7))
            {
                return head[..7];
            }

            // Unrecognised format (e.g. someone hand-edited HEAD, or a
            // future git format we don't know about).  Fall back to
            // null so the caller uses the folder-name path.
            return null;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or NotSupportedException
                                       or SecurityException)
        {
            // Any IO / permission error → silent fallback.  The titlebar
            // update must NEVER block the UI; the next-tier helper
            // (folder name) handles the case.
            _ = ex;
            return null;
        }
    }

    /// <summary>
    /// True iff the first <paramref name="length"/> chars of <paramref name="s"/>
    /// are all hex digits.  Used to recognise a 40-char SHA in a detached-HEAD
    /// file.
    /// </summary>
    private static bool IsHex(string s, int length)
    {
        for (int i = 0; i < length; i++)
        {
            char c = s[i];
            bool isHex = (c >= '0' && c <= '9')
                         || (c >= 'a' && c <= 'f')
                         || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}