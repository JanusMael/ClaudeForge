namespace Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

/// <summary>
/// One progress update emitted by <see cref="IBackupClient.CreateAsync"/> or
/// <see cref="IBackupClient.RestoreAsync"/>. Delivered to the consumer's
/// <see cref="BackupProgressHandler"/>.
/// </summary>
/// <param name="Step">Zero-based step index.</param>
/// <param name="Total">Total number of steps the engine plans to take.</param>
/// <param name="Message">Human-readable description of the current step.</param>
/// <param name="BytesWritten">
/// Cumulative bytes written so far during a Create operation; cumulative
/// bytes restored during a Restore operation. <c>0</c> for purely
/// informational progress events.
/// </param>
public sealed record BackupProgress(
    int Step,
    int Total,
    string Message,
    long BytesWritten);