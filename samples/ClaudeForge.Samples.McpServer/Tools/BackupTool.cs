using Bennewitz.Ninja.ClaudeForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Backup;

namespace Bennewitz.Ninja.ClaudeForge.Samples.McpServer.Tools;

/// <summary>
/// Backup / restore demos:
/// <list type="bullet">
///   <item><c>list-backups &lt;directory&gt;</c></item>
///   <item><c>create-backup &lt;directory&gt;</c></item>
///   <item><c>restore-backup &lt;archive-path&gt;</c></item>
/// </list>
/// Demonstrates <see cref="IBackupClient"/> + the async
/// <see cref="BackupProgressHandler"/> contract.
/// </summary>
internal static class BackupTool
{
    public static async Task<int> ListAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            await Console.Error.WriteLineAsync("error: list-backups requires a directory argument.");
            return 1;
        }

        IReadOnlyList<BackupArchive> archives = await client.Backup.ListAsync(args[1], ct).ConfigureAwait(false);
        if (archives.Count == 0)
        {
            Console.WriteLine("(no backups found)");
            return 0;
        }

        foreach (BackupArchive a in archives)
        {
            Console.WriteLine(
                $"{Path.GetFileName(a.FilePath),-44}  {a.CreatedUtc:yyyy-MM-dd HH:mm:ss}  " +
                $"mode={a.Manifest.Mode}  items={a.Manifest.ItemCount}");
        }

        return 0;
    }

    public static async Task<int> CreateAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            await Console.Error.WriteLineAsync("error: create-backup requires a directory argument.");
            return 1;
        }

        // Async progress callback — demonstrates the BackupProgressHandler
        // contract. The producer awaits each invocation, so any awaitable
        // work (logging, streaming to a remote MCP client, dispatcher
        // marshaling) integrates naturally.
        BackupProgressHandler onProgress = async p =>
        {
            await Console.Out.WriteLineAsync(
                $"  [{p.Step,3}/{p.Total,3}]  {p.Message}").ConfigureAwait(false);
        };

        BackupRequest request = new(
            Mode: BackupMode.SettingsOnly,
            OutputDirectory: args[1],
            IncludeCredentials: false,
            KeepLast: 10);

        BackupArchive archive = await client.Backup.CreateAsync(request, onProgress, ct).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"created: {archive.FilePath}");
        Console.WriteLine($"  items:    {archive.Manifest.ItemCount}");
        Console.WriteLine($"  bytes:    {archive.Manifest.SizeBytes:N0}");
        Console.WriteLine($"  warnings: {archive.Manifest.Warnings.Count}");
        return 0;
    }

    public static async Task<int> RestoreAsync(IClaudeConfigClient client, string[] args, CancellationToken ct)
    {
        if (args.Length < 2)
        {
            await Console.Error.WriteLineAsync("error: restore-backup requires an archive path argument.");
            return 1;
        }

        // Locate the archive in its containing directory via ListAsync so we
        // get a properly-populated BackupArchive (with Manifest projected from
        // the on-disk manifest) rather than synthesising a stub.
        string archivePath = Path.GetFullPath(args[1]);
        string directory = Path.GetDirectoryName(archivePath)
                           ?? throw new InvalidOperationException(
                               $"Cannot derive backup directory from '{archivePath}'.");

        IReadOnlyList<BackupArchive> archives = await client.Backup.ListAsync(directory, ct).ConfigureAwait(false);
        BackupArchive? archive = archives.FirstOrDefault(a =>
            string.Equals(a.FilePath, archivePath, StringComparison.OrdinalIgnoreCase));
        if (archive is null)
        {
            await Console.Error.WriteLineAsync($"error: archive '{archivePath}' not found in {directory}.");
            return 4;
        }

        RestoreResult result = await client.Backup.RestoreAsync(archive, onProgress: null, ct: ct).ConfigureAwait(false);
        Console.WriteLine(result.Success ? "ok" : "failed");
        Console.WriteLine($"  files restored: {result.FilesRestored}");
        Console.WriteLine($"  message:        {result.Message}");
        if (result.Failures.Count > 0)
        {
            Console.WriteLine("  failures:");
            foreach (string f in result.Failures)
            {
                Console.WriteLine($"    {f}");
            }
        }

        return result.Success ? 0 : 5;
    }
}