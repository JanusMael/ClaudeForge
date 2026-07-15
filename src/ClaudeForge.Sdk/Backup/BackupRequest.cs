using Bennewitz.Ninja.ClaudeForge.Core.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

/// <summary>
/// Inputs for <see cref="IBackupClient.CreateAsync"/>.
/// </summary>
/// <param name="Mode">How much of the Claude footprint to include.</param>
/// <param name="OutputDirectory">
/// Directory the new <c>backup-*.zip</c> file is written into. Created on
/// demand if it does not exist.
/// </param>
/// <param name="IncludeCredentials">
/// When <see langword="true"/>, includes <c>~/.claude/.credentials.json</c>
/// in the archive. Default <see langword="false"/>; consumers ask explicitly
/// because the file contains long-lived auth tokens.
/// </param>
/// <param name="ExplicitProjectDirs">
/// Optional list of project roots to include in addition to whatever the
/// engine discovers from settings files. <see langword="null"/> means
/// "discover-only mode".
/// </param>
/// <param name="KeepLast">
/// Retention: after a successful create, the engine deletes older backups in
/// <see cref="OutputDirectory"/> beyond this count. Defaults to <c>10</c>.
/// </param>
public sealed record BackupRequest(
    BackupMode Mode,
    string OutputDirectory,
    bool IncludeCredentials,
    IReadOnlyList<string>? ExplicitProjectDirs = null,
    int KeepLast = 10);