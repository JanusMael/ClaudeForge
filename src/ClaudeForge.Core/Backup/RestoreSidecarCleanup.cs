using System.Text.RegularExpressions;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// One-shot cleanup of <c>*.bak</c> sidecar files left behind by
/// <see cref="RestoreEngine"/> across the lifetime of the
/// <c>~/.claude/</c> directory.
/// <para>
/// Each <see cref="RestoreEngine.RestoreAsync"/> invocation copies any
/// existing destination file aside as
/// <c>&lt;destFile&gt;.pre-restore-{stamp}.bak</c>.  After the restore
/// is verified, that sidecar carries no further user-authored content —
/// the file it was a copy of came from a prior save, which the restored
/// backup archive itself preserves.  These sidecars accumulate
/// geometrically across restores (each new restore creates a
/// <c>.bak</c> of files that may already be <c>.bak</c> sidecars from
/// earlier restores), bloating the directory and slowing future
/// backups.
/// </para>
/// <para>
/// Invoked via the <c>--cleanup-restore-sidecars</c> command-line flag —
/// not exposed in the GUI because the operation is unconditionally
/// destructive and best performed as an explicit maintenance step the
/// user has consciously chosen.  See <c>Program.Main</c> for the entry
/// point that dispatches to <see cref="Run"/>.
/// </para>
/// </summary>
public static partial class RestoreSidecarCleanup
{
    /// <summary>Summary of a single cleanup run.</summary>
    public sealed record Result(
        int FilesScanned,
        int FilesDeleted,
        long BytesReclaimed,
        int Failures,
        IReadOnlyList<string> FailureMessages);

    /// <summary>
    /// Walk <paramref name="claudeHome"/> recursively and delete every
    /// file whose name ends with <c>.bak</c> (case-insensitive).  Returns
    /// a <see cref="Result"/> capturing the count + reclaimed bytes +
    /// any per-file failures (locked files, permission denied, etc.).
    /// Best-effort: a single file's failure does not abort the run.
    /// </summary>
    /// <param name="claudeHome">
    /// Root directory to walk.  Defaults to
    /// <see cref="PlatformPaths.ClaudeHome"/> when null — that's the
    /// production target.  Tests pass a sandboxed home.
    /// </param>
    /// <param name="onProgress">
    /// Optional progress callback fired every 1000 deleted files so the
    /// CLI can stream a heartbeat for long runs (the user's reported
    /// baseline was 99 307 sidecars).
    /// </param>
    public static Result Run(string? claudeHome = null, Action<int>? onProgress = null)
    {
        string home = claudeHome ?? PlatformPaths.ClaudeHome;
        if (!Directory.Exists(home))
        {
            return new Result(0, 0, 0, 0, Array.Empty<string>());
        }

        int scanned = 0;
        int deleted = 0;
        long bytes = 0L;
        int failures = 0;
        List<string> failMsgs = new();

        IEnumerable<string> sidecars;
        try
        {
            // EnumerateFiles is lazy; "*.bak" matches case-insensitively on
            // Windows and macOS, case-sensitively on Linux.  We post-filter
            // for case-insensitive correctness across all platforms.
            sidecars = Directory.EnumerateFiles(home, "*.bak", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failMsgs.Add($"Could not enumerate {home}: {ex.Message}");
            return new Result(0, 0, 0, 1, failMsgs);
        }

        foreach (string path in sidecars)
        {
            scanned++;

            // Defensive case-insensitive re-check (the *.bak filter is
            // case-sensitive on Linux).
            if (!path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // only target sidecars whose filename matches the
            // exact RestoreEngine pattern `*.pre-restore-{stamp}.bak`.  Earlier
            // code-review audit flagged the "delete every *.bak" sweep as
            // over-broad: a user (or third-party tool) might legitimately
            // place `notes.md.bak` inside `~/.claude/` as a hand-rolled
            // backup of their own work and would not expect this tool to
            // touch it.  The narrower pattern matches RestoreEngine's exact
            // output (RestoreEngine.cs writes
            //   "{destFile}.pre-restore-{stamp}.bak"
            // where stamp is "yyyyMMdd-HHmmss"), plus the compounded sidecar-
            // of-sidecar chain that appears when restores run repeatedly
            // (suffix sequence "…pre-restore-X.bak.pre-restore-Y.bak").  Any
            // `.bak` file that doesn't match (e.g. editor-style `notes.md.bak`
            // with no `pre-restore-` segment) is left alone.
            if (!LooksLikeRestoreSidecar(Path.GetFileName(path)))
            {
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Couldn't stat — file may have been deleted by another
                // process between EnumerateFiles and FileInfo, or is
                // locked.  Count it as a failure and move on.
                failures++;
                if (failMsgs.Count < 20) // cap stored messages to avoid runaway memory
                {
                    failMsgs.Add($"stat failed: {path} — {ex.Message}");
                }

                continue;
            }

            if (TryDeleteWithReadOnlyRetry(path, out string firstError))
            {
                deleted++;
                bytes += size;
                if (onProgress is not null && deleted % 1000 == 0)
                {
                    onProgress(deleted);
                }
            }
            else
            {
                failures++;
                if (failMsgs.Count < 20)
                {
                    failMsgs.Add($"delete failed: {path} — {firstError}");
                }
            }
        }

        return new Result(scanned, deleted, bytes, failures, failMsgs);
    }

    /// <summary>
    /// Attempt to delete <paramref name="path"/>; on
    /// <see cref="UnauthorizedAccessException"/>, clear the read-only
    /// attribute and retry once.  Read-only sidecars arise when the
    /// original file's attributes (e.g. Git pack objects under
    /// <c>.git/objects/pack/</c>, which Git marks 0444) were inherited
    /// by the <c>.bak</c> copy at restore time.  These are still safe
    /// to delete — the read-only marker reflects the original file's
    /// archive bit, not a "do not erase" intent.
    /// </summary>
    private static bool TryDeleteWithReadOnlyRetry(string path, out string firstError)
    {
        firstError = string.Empty;
        try
        {
            File.Delete(path);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            firstError = ex.Message;
            // Likely read-only — try to clear and retry once.
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return true;
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                firstError = $"{ex.Message} (retry after clearing read-only also failed: {inner.Message})";
                return false;
            }
        }
        catch (IOException ex)
        {
            firstError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Does <paramref name="fileName"/> match the exact pattern
    /// <see cref="RestoreEngine"/> uses for pre-restore sidecars?  Looks
    /// for at least one <c>.pre-restore-YYYYMMDD-HHmmss.bak</c> segment
    /// (anywhere in the name — the chain may be compounded across
    /// multiple restores, e.g.
    /// <c>foo.json.pre-restore-X.bak.pre-restore-Y.bak</c>).
    /// </summary>
    /// <remarks>
    /// Avoids deleting third-party or hand-rolled <c>.bak</c> files
    /// the user may have placed under <c>~/.claude/</c> deliberately
    /// (e.g. <c>notes.md.bak</c> from vim).  Conservative whitelist
    /// rather than blanket extension match.
    /// </remarks>
    internal static bool LooksLikeRestoreSidecar(string fileName)
    {
        return RestoreSidecarPattern.IsMatch(fileName);
    }

    /// <summary>
    /// Pattern: <c>.pre-restore-</c> followed by an 8-digit date, a dash,
    /// a 6-digit time, then <c>.bak</c>.  Compiled once for the hot
    /// enumeration path.
    /// </summary>
    private static readonly Regex RestoreSidecarPattern =
        MyRegex();

    [GeneratedRegex(@"\.pre-restore-\d{8}-\d{6}\.bak$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();
}