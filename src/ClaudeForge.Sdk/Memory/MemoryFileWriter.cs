using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Memory;

/// <summary>
/// Atomic UTF-8 (no BOM) write for agent / skill / slash-command files.
/// A crash mid-write leaves either the old file intact or the new file in
/// place — never a torn half-write.
///
/// <para>
/// Mechanism: write to a sibling temp file, flush, then swap.  When the
/// target already exists (the only case in group #3 — we edit existing
/// artifacts, never create new ones), <see cref="File.Replace(string,string,string)"/>
/// performs the atomic swap on NTFS / ext4 / APFS.  When the target is
/// absent, a plain <see cref="File.Move(string,string)"/> of the temp file
/// into place is used.  On any failure the temp file is best-effort deleted
/// and the original exception rethrown.
/// </para>
/// </summary>
public static class MemoryFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Atomically write <paramref name="content"/> to <paramref name="targetPath"/>.</summary>
    public static async Task WriteAsync(string targetPath, string content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Utf8NoBom, ct).ConfigureAwait(false);

            if (File.Exists(targetPath))
            {
                // Atomic swap — leaves the original untouched on failure.
                File.Replace(tempPath, targetPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup; surface the original failure below.
            }
            throw;
        }
    }
}
