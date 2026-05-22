namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Discovers git worktrees whose paths fall **outside** their project root.
/// In-project worktrees (e.g. <c>project/.claude/worktrees/feature-x</c>) are covered
/// by the regular project backup and do not need separate handling.
/// </summary>
public sealed class WorktreeProbe
{
    private readonly IProcessRunner _runner;
    private readonly TimeSpan _timeout;
    private static readonly string[] args = ["worktree", "list", "--porcelain"];

    /// <summary>
    /// Construct a probe. Defaults to <see cref="DefaultProcessRunner"/> and a 5-second
    /// timeout per project. Tests inject a stub runner.
    /// </summary>
    public WorktreeProbe(IProcessRunner? runner = null, TimeSpan? timeout = null)
    {
        _runner = runner ?? DefaultProcessRunner.Instance;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// For each of <paramref name="projectRoots"/> that is a git repository, runs
    /// <c>git worktree list --porcelain</c> and returns every worktree whose path is
    /// outside the project root. Non-git projects and projects where <c>git</c> fails
    /// are silently skipped — the caller can expose a warning via the manifest.
    /// </summary>
    /// <summary>
    /// Returns the discovered external worktrees together with a flag indicating
    /// whether at least one git-repo project was skipped because <c>git</c> was not
    /// found on PATH (or timed out). The caller should record a manifest warning when
    /// <c>GitMissing</c> is <c>true</c>.
    /// </summary>
    public async Task<WorktreeDiscoveryResult> DiscoverExternalAsync(
        IEnumerable<string> projectRoots,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectRoots);

        List<BackupWorktreeEntry> entries = new();
        bool gitMissing = false;

        foreach (string root in projectRoots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            // .git is a directory for the main worktree and a file (containing "gitdir: …")
            // for linked worktrees created with `git worktree add`.  Accept either form so
            // project roots that are themselves worktrees are not silently skipped.
            string dotGit = Path.Combine(root, ".git");
            if (!Directory.Exists(dotGit) && !File.Exists(dotGit))
            {
                continue;
            }

            IReadOnlyList<string>? lines = await _runner.RunAsync(
                fileName: "git",
                args: args,
                workingDirectory: root,
                timeout: _timeout,
                ct: ct).ConfigureAwait(false);

            if (lines is null)
            {
                // null means git was not found on PATH, timed out, or returned non-zero.
                // Flag as git-missing so the caller can add a manifest warning.
                gitMissing = true;
                continue;
            }

            foreach (string wt in ParseWorktreeList(lines))
            {
                if (IsPathUnder(wt, root))
                {
                    continue;
                }

                if (!Directory.Exists(wt))
                {
                    continue;
                }

                entries.Add(new BackupWorktreeEntry
                {
                    ProjectRoot = Path.GetFullPath(root),
                    WorktreePath = Path.GetFullPath(wt),
                });
            }
        }

        return new WorktreeDiscoveryResult(entries, gitMissing);
    }

    /// <summary>
    /// Parses <c>git worktree list --porcelain</c> output. Each worktree is a block
    /// of lines starting with <c>worktree &lt;path&gt;</c>; blocks are separated by blanks.
    /// </summary>
    internal static IReadOnlyList<string> ParseWorktreeList(IReadOnlyList<string> lines)
    {
        List<string> paths = new();
        foreach (string line in lines)
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                paths.Add(line["worktree ".Length..].Trim());
            }
        }

        return paths;
    }

    /// <summary>
    /// Case-insensitive prefix test (Windows paths); matches the reference PS script's
    /// <c>StartsWith(StringComparison.OrdinalIgnoreCase)</c> behaviour.
    /// </summary>
    private static bool IsPathUnder(string candidate, string root)
    {
        try
        {
            string full = Path.GetFullPath(candidate);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                   || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || full.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Result of <see cref="WorktreeProbe.DiscoverExternalAsync"/>. Carries both the
/// discovered worktree entries and a diagnostic flag for the manifest.
/// </summary>
/// <param name="Worktrees">External worktree entries found across all project roots.</param>
/// <param name="GitMissing">
/// <c>true</c> when at least one git-repository project was skipped because
/// <c>git</c> was not reachable on PATH or timed out. The caller should add a warning
/// to the backup manifest so the user knows external worktrees may be incomplete.
/// </param>
public sealed record WorktreeDiscoveryResult(
    IReadOnlyList<BackupWorktreeEntry> Worktrees,
    bool GitMissing);