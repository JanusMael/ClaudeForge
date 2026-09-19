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
    public Task<ShareOutcome> ShareTextAsync(string title, string text, string? uri = null)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (!string.IsNullOrEmpty(uri))
            {
                // Open URI in the default browser.
                return Task.FromResult(
                    TryStart(new ProcessStartInfo
                        { FileName = "open", ArgumentList = { uri }, UseShellExecute = false })
                        ? ShareOutcome.OpenedInBrowser
                        : ShareOutcome.Failed);
            }

            if (!string.IsNullOrEmpty(text))
            {
                // macOS has no Share sheet API without NSSharingService (requires a net10.0-macos
                // TFM). Fall back to pbcopy so the user at least has the text on the clipboard —
                // analogous to the "Copy" action that every Share sheet contains.
                return CopyViaPbcopyAsync(text);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // ⚠ An empty payload no longer reaches the desktop handler. The mailto: below was
            // built unconditionally, so a caller with neither URI nor text launched a blank
            // compose window — an action the caller would now have to describe to the user as
            // though something had been shared.
            if (string.IsNullOrEmpty(uri) && string.IsNullOrEmpty(text))
            {
                return Task.FromResult(ShareOutcome.Unavailable);
            }

            // Linux: construct a mailto: URI and hand it to the desktop handler.
            string target = uri
                            ?? $"mailto:?subject={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(text)}";
            bool launched = TryStart(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            if (!launched)
            {
                return Task.FromResult(ShareOutcome.Failed);
            }

            // Which handler ran is decided by what was handed over: a caller-supplied URI goes
            // wherever the desktop sends that scheme, and the fallback is a mailto: by
            // construction.
            return Task.FromResult(uri is null
                ? ShareOutcome.OpenedMailClient
                : ShareOutcome.OpenedInBrowser);
        }
        else if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrEmpty(uri))
            {
                // Windows: open the URI in the default browser.
                return Task.FromResult(
                    TryStart(new ProcessStartInfo { FileName = uri, UseShellExecute = true })
                        ? ShareOutcome.OpenedInBrowser
                        : ShareOutcome.Failed);
            }

            if (!string.IsNullOrEmpty(text))
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
        return Task.FromResult(ShareOutcome.Unavailable);
    }

    /// <inheritdoc />
    public Task<ShareOutcome> ShareFileAsync(string title, string filePath)
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
                return Task.FromResult(
                    TryStart(new ProcessStartInfo
                        { FileName = "open", ArgumentList = { "-R", filePath }, UseShellExecute = false })
                        ? ShareOutcome.RevealedInFileManager
                        : ShareOutcome.Failed);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux: open the directory containing the file.
            //
            // ⛔ THE FILE'S EXISTENCE IS THE PRECONDITION, NOT THE DIRECTORY'S. This branch used
            // to ask only whether the containing directory existed, so sharing a file that is not
            // on disk opened its parent anyway — and when no `xdg-open` is present (a CI runner,
            // a headless box) that surfaced as Failed, which the status pill keeps on screen,
            // rather than Unavailable, which it clears. macOS and Windows both require the file;
            // only this branch disagreed, so the defect was invisible on two platforms out of
            // three.
            string? dir = Path.GetDirectoryName(filePath);
            if (File.Exists(filePath) && !string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                return Task.FromResult(
                    TryStart(new ProcessStartInfo
                        { FileName = "xdg-open", ArgumentList = { dir }, UseShellExecute = false })
                        ? ShareOutcome.RevealedInFileManager
                        : ShareOutcome.Failed);
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
                return Task.FromResult(
                    TryStart(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{filePath}\"",
                        UseShellExecute = false,
                    })
                        ? ShareOutcome.RevealedInFileManager
                        : ShareOutcome.Failed);
            }
        }

        // Unsupported OS, or the path is not on disk. ⚠ Not a failure — nothing was attempted,
        // and the two are distinguished because the status pill keeps a failure on screen.
        return Task.FromResult(ShareOutcome.Unavailable);
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
    private static async Task<ShareOutcome> CopyViaClipExeAsync(string text)
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

            // ⚠ The exit code is the evidence, not the fact that Start() returned. clip.exe
            // writes nothing on success, so a non-zero code is the only signal there is.
            return proc.ExitCode == 0 ? ShareOutcome.CopiedToClipboard : ShareOutcome.Failed;
        }
        catch (Exception ex)
        {
            Report($"clip.exe failed; text was not copied to the clipboard: {ex.Message}");
            return ShareOutcome.Failed;
        }
    }

    private static async Task<ShareOutcome> CopyViaPbcopyAsync(string text)
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

            return proc.ExitCode == 0 ? ShareOutcome.CopiedToClipboard : ShareOutcome.Failed;
        }
        catch (Exception ex)
        {
            // ⛔ THIS WAS Debug.WriteLine, WHICH IS COMPILED OUT OF RELEASE — the same defect
            // already corrected in Report() and in TryStart(), left behind here because this
            // sibling was not on the same screen. Route it through Report() like the others.
            Report($"pbcopy failed; text was not copied to the clipboard: {ex.Message}");
            return ShareOutcome.Failed;
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

    /// <summary>Launches <paramref name="psi"/>; <see langword="false"/> when it did not start.</summary>
    /// <remarks>
    /// ⛔ <b>This returned <c>void</c>, and that is why a share failure could never reach the
    /// user.</b> It caught, it logged, and it told its caller nothing — so <c>ShareTextAsync</c>
    /// completed identically whether the browser opened or <c>Process.Start</c> threw. The
    /// <see cref="ShareOutcome.Failed"/> arm of every caller is wired to this return value; it is
    /// not assumed anywhere.
    /// <para>
    /// ⚠ A <see langword="null"/> process still counts as not started. That is the shape a test
    /// launcher takes (<c>_ =&gt; null</c>), so <see cref="ShareOutcome.Failed"/> is what a
    /// suppressed launch reports — deliberately, since a test that saw success from a launch that
    /// never happened would vouch for the defect this type exists to catch.
    /// </para>
    /// </remarks>
    private bool TryStart(ProcessStartInfo psi)
    {
        try
        {
            // Dispose the returned Process so the OS handle is released promptly.
            using Process? proc = _processLauncher(psi);
            if (proc is null)
            {
                Report($"Process.Start returned null for '{psi.FileName}'.");
                return false;
            }

            return true;
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
            return false;
        }
    }
}
