namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Input record for <c>BackupEngine.CreateAsync</c>.
/// Immutable by convention — callers build an instance and hand it off.
/// </summary>
public sealed record BackupRequest
{
    /// <summary>Absolute path to the <c>.zip</c> the engine should write.</summary>
    public required string DestinationZipPath { get; init; }

    /// <summary><see cref="BackupMode.SettingsOnly"/> (default) or <see cref="BackupMode.Full"/>.</summary>
    public BackupMode Mode { get; init; } = BackupMode.SettingsOnly;

    /// <summary>Which product areas to include in the archive.</summary>
    public bool IncludeClaudeCode { get; init; } = true;

    public bool IncludeClaudeDesktop { get; init; } = true;

    /// <summary>
    /// True ⇒ also bundle <c>~/.claude/.credentials.json</c> (Windows/Linux only).
    /// macOS stores credentials in Keychain — the file is absent there and this flag
    /// has no effect.
    /// </summary>
    public bool IncludeCredentials { get; init; }

    /// <summary>
    /// Project root paths explicitly supplied by the caller. Merged at runtime with
    /// paths auto-discovered via <c>AdditionalDirectoriesResolver</c>.
    /// </summary>
    public IReadOnlyList<string> ExplicitProjectDirs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// After a successful backup, prune older <c>backup-*.zip</c> files in the same
    /// directory keeping only the N most recent. <c>0</c> (default) disables retention.
    /// </summary>
    public int KeepLast { get; init; }
}