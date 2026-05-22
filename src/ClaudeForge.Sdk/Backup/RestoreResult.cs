namespace Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

/// <summary>
/// Outcome of <see cref="IBackupClient.RestoreAsync"/>.
/// </summary>
/// <param name="Success">
/// <see langword="true"/> when the restore completed without an aborting error.
/// Per-file failures recorded in <see cref="Failures"/> do not flip this flag —
/// the engine restores everything it can, then reports the rest.
/// </param>
/// <param name="Message">Human-readable summary of the outcome.</param>
/// <param name="FilesRestored">Number of files written into the live tree.</param>
/// <param name="Skipped">
/// Items the engine deliberately skipped (e.g. project paths that no longer
/// exist on the host).
/// </param>
/// <param name="Failures">
/// Per-file failures — typically locked files or permission-denied paths.
/// Each entry is <c>"{relative-path}: {error message}"</c>.
/// </param>
public sealed record RestoreResult(
    bool Success,
    string Message,
    int FilesRestored,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Failures);