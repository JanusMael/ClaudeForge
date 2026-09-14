using System.Diagnostics;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Default cross-platform implementation of <see cref="IShareService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every platform hands the payload to the desktop rather than opening a share sheet:
/// <b>Windows</b> reveals the file selected in Explorer, or opens a URI in the default browser;
/// <b>macOS</b> reveals in Finder, opens a URI via <c>open</c>, or falls back to <c>pbcopy</c> for
/// plain text; <b>Linux</b> opens the containing directory via <c>xdg-open</c>, or builds a
/// <c>mailto:</c> URL.
/// </para>
/// <para>
/// ⛔ <b>There is no native Share-sheet path, and there never has been one at runtime.</b> This
/// class used to carry a MAUI Essentials implementation behind
/// <c>#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER</c>, together with an <c>hwndProvider</c>
/// constructor parameter that existed only to initialise it. That TFM was declared but never
/// built — the repo-root <c>Directory.Build.props</c> sets the singular
/// <c>&lt;TargetFramework&gt;</c>, and MSBuild cross-targets only when that property is empty, so
/// the plural <c>&lt;TargetFrameworks&gt;</c> in this project was ignored. Every guarded block was
/// therefore dead in every build that has ever shipped, and the fallbacks below are what users
/// have always got. Removing them changed no behaviour; it made the source agree with the binary.
/// </para>
/// <para>
/// ⚠ <b>Reintroducing a real share sheet is a new feature, not a revert.</b> It needs its own
/// TFM that actually builds, its own trim pass — MAUI Essentials brought
/// <c>ILLink.Suppressions.Windows.xml</c> with it — and its own place in a manual retest, because
/// nothing in this repository has ever exercised that path.
/// </para>
/// </remarks>
public sealed class DefaultShareService : IShareService
{
    private readonly Func<ProcessStartInfo, Process?> _processLauncher;

    /// <param name="processLauncher">
    /// Optional override for <see cref="Process.Start(ProcessStartInfo)"/>.
    /// Pass <c>_ =&gt; null</c> in unit tests to suppress real process launches.
    /// When <see langword="null"/> the default <see cref="Process.Start(ProcessStartInfo)"/>
    /// is used.
    /// </param>
    public DefaultShareService(Func<ProcessStartInfo, Process?>? processLauncher = null)
    {
        _processLauncher = processLauncher ?? Process.Start;
    }

    /// <inheritdoc />
    public Task ShareTextAsync(string title, string text, string? uri = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (!string.IsNullOrEmpty(uri))
            {
                // Open URI in the default browser.
                TryStart(new ProcessStartInfo { FileName = "open", ArgumentList = { uri }, UseShellExecute = false });
            }
            else if (!string.IsNullOrEmpty(text))
            {
                // macOS has no Share sheet API without NSSharingService (requires a net10.0-macos
                // TFM). Fall back to pbcopy so the user at least has the text on the clipboard —
                // analogous to the "Copy" action that every Share sheet contains.
                return CopyViaPbcopyAsync(text);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux: construct a mailto: URI and hand it to the desktop handler.
            string target = uri
                            ?? $"mailto:?subject={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(text)}";
            TryStart(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        else if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrEmpty(uri))
            {
                // Windows: open the URI in the default browser.
                TryStart(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
            }
            else if (!string.IsNullOrEmpty(text))
            {
                // ⛔ THIS BRANCH DID NOT EXIST, AND SHARING TEXT ON WINDOWS WAS A SILENT NO-OP.
                // The condition above used to be `IsWindows() && !string.IsNullOrEmpty(uri)`, so
                // a caller passing text with no URI - which is what the Effective-settings
                // "Share config" button does - matched nothing and fell through to
                // Task.CompletedTask. Windows was the only platform with no fallback at all:
                // macOS copies via pbcopy, Linux builds a mailto:. Reported 2026-09-14 as "the
                // share config button appears to do nothing", and it never worked in any shipped
                // build - the MAUI share path it was waiting for lived behind a TFM that was
                // never compiled (see DefaultShareService's class remarks).
                //
                // clip.exe is the OS's own clipboard tool, present on every supported Windows,
                // and takes its payload on stdin - the same shape as pbcopy above.
                return CopyViaClipExeAsync(text);
            }
        }

        // Unsupported OS, or nothing to share.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShareFileAsync(string title, string filePath)
    {
        // ⚠ `title` is accepted and unused on every platform. It names the share sheet, and none
        // of the three fallbacks opens one — a file manager titles its own window. Kept because
        // it is part of IShareService and a future share-sheet implementation needs it.
        _ = title;

        if (OperatingSystem.IsMacOS())
        {
            // Reveal the file in Finder — the user can right-click → Share.
            if (File.Exists(filePath))
            {
                TryStart(new ProcessStartInfo
                    { FileName = "open", ArgumentList = { "-R", filePath }, UseShellExecute = false });
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux: open the directory containing the file.
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                TryStart(new ProcessStartInfo
                    { FileName = "xdg-open", ArgumentList = { dir }, UseShellExecute = false });
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            // Windows: reveal the file selected in Explorer so the user can share from there.
            // Use Arguments (not ArgumentList) so `/select,` and the quoted path stay
            // as a single token — ArgumentList splits on commas and double-escapes
            // inner quotes, which causes explorer.exe to silently ignore the argument.
            if (File.Exists(filePath))
            {
                TryStart(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = false,
                });
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Pipes <paramref name="text"/> into <c>pbcopy</c> to place it on the macOS clipboard.
    /// Used as a Share-sheet substitute when no URI is available and NSSharingService is
    /// not accessible without the <c>net10.0-macos</c> TFM.
    /// </summary>
    /// <summary>
    /// Copies <paramref name="text"/> to the Windows clipboard via <c>clip.exe</c>.
    /// </summary>
    /// <remarks>
    /// The sibling of <see cref="CopyViaPbcopyAsync"/>, for the same reason: there is no share
    /// sheet available without a platform TFM, so the user at least gets the text on the
    /// clipboard - which is the action every share sheet contains anyway.
    /// ⚠ <c>clip.exe</c> reads stdin and writes nothing; it is not launched through
    /// <see cref="TryStart"/> because that path does not redirect stdin.
    /// </remarks>
    private static async Task CopyViaClipExeAsync(string text)
    {
        try
        {
            using Process proc = new();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "clip.exe",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            proc.Start();
            await proc.StandardInput.WriteAsync(text).ConfigureAwait(false);
            proc.StandardInput.Close();
            await proc.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Report($"clip.exe failed; text was not copied to the clipboard: {ex.Message}");
        }
    }

    private static async Task CopyViaPbcopyAsync(string text)
    {
        try
        {
            using Process proc = new();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "pbcopy",
                UseShellExecute = false,
                RedirectStandardInput = true,
            };
            proc.Start();
            await proc.StandardInput.WriteAsync(text);
            proc.StandardInput.Close();
            await proc.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DefaultShareService] pbcopy failed: {ex.Message}");
        }
    }

    /// <summary>Reports a share failure. Best-effort, and never shown to the user.</summary>
    /// <remarks>
    /// ⛔ <b>This was <c>Debug.WriteLine</c>, which is compiled OUT of Release.</b> Its comment
    /// claimed it existed "so developers can diagnose" — but <c>Debug.WriteLine</c> carries
    /// <c>[Conditional("DEBUG")]</c>, so in the configuration users actually run a share failure
    /// produced no output anywhere. <c>Trace</c> carries <c>[Conditional("TRACE")]</c>, which the
    /// SDK defines in Release as well, so this survives into a shipped build.
    /// <para>
    /// ⚠ It still does not reach Serilog, and deliberately so: this project's references are
    /// Avalonia plus AgentForge.Abstractions and nothing else, and it is now a published package —
    /// taking a Serilog dependency for two warning lines is a packaging decision, not a detail.
    /// If these failures need to land in the app log, the cheap route is a static hook here that
    /// each host wires to its own logger.
    /// </para>
    /// </remarks>
    private static void Report(string message)
    {
        Trace.WriteLine($"[DefaultShareService] {message}");
    }

    private void TryStart(ProcessStartInfo psi)
    {
        try
        {
            // Dispose the returned Process so the OS handle is released promptly.
            using Process? proc = _processLauncher(psi);
            if (proc is null)
            {
                Report($"Process.Start returned null for '{psi.FileName}'.");
            }
        }
        catch (Exception ex)
        {
            // ⛔ THIS USED TO BE Debug.WriteLine, WHICH IS COMPILED OUT OF RELEASE.
            // Its comment said it was there "so developers can diagnose" - but
            // Debug.WriteLine carries [Conditional("DEBUG")], so in the configuration users
            // actually run a share failure produced no output anywhere, not even in the live
            // log, because it never reached Serilog. Share stays best-effort and still shows
            // the user no dialog; it is simply no longer silent to whoever reads the log.
            Report($"Process launch failed ({psi.FileName}): {ex.Message}");
        }
    }
}
