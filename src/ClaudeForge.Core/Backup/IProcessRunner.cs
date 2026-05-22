namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Abstraction over <see cref="System.Diagnostics.Process"/> so we can unit-test
/// components that shell out to external tools (currently only <c>git worktree list</c>)
/// without actually invoking them.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="args"/> in
    /// <paramref name="workingDirectory"/>. Each element of <paramref name="args"/>
    /// is added to <c>ProcessStartInfo.ArgumentList</c> so no shell-level quoting or
    /// splitting occurs — safe against argument-injection even if a caller passes a
    /// user-derived string. Returns the combined stdout lines, or <c>null</c> if the
    /// process could not be started, exited non-zero, or exceeded
    /// <paramref name="timeout"/>. Never throws for expected failure modes.
    /// </summary>
    Task<IReadOnlyList<string>?> RunAsync(
        string fileName,
        string[] args,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken ct);
}