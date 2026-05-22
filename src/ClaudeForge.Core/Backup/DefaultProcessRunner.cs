using System.ComponentModel;
using System.Diagnostics;

namespace Bennewitz.Ninja.ClaudeForge.Core.Backup;

/// <summary>
/// Production implementation of <see cref="IProcessRunner"/> that actually spawns a
/// child process. Tests inject a stub instead.
/// </summary>
public sealed class DefaultProcessRunner : IProcessRunner
{
    /// <summary>Shared instance — this class is stateless.</summary>
    public static readonly DefaultProcessRunner Instance = new();

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> RunAsync(
        string fileName,
        string[] args,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken ct)
    {
        Process? process = null;
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // Use ArgumentList (not Arguments string) to avoid any shell-level tokenisation
            // and make each argument opaque to the OS argument parser.
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);

            // Both pipes MUST be drained concurrently. If either is left unread, the child
            // blocks on WriteFile once that pipe's ~4 KB buffer fills, never exits, and
            // WaitForExitAsync hangs until the timeout fires. `git worktree list --porcelain`
            // is the canonical victim here: many real-world repos emit warnings to stderr
            // (detached HEAD, broken refs, unreachable objects) that exceed 4 KB.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx) when (killEx is InvalidOperationException
                                                   or Win32Exception
                                                   or NotSupportedException)
                {
                    // Already exited / access denied / unsupported on this platform —
                    // we are about to return null anyway.
                    _ = killEx;
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            string stdout = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false); // drain to completion
            // Split on either newline form so the caller does not need to care.
            return stdout.Split('\n')
                         .Select(l => l.TrimEnd('\r'))
                         .ToArray();
        }
        catch (Exception ex) when (ex is Win32Exception
                                       or InvalidOperationException
                                       or IOException)
        {
            // Binary not found, access denied, etc. — treat as "not runnable".
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }
}