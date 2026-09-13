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
        else if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(uri))
        {
            // Windows: open the URI in the default browser.
            TryStart(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }

        // Other OS or plain-text with no URI — no-op.
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

    private void TryStart(ProcessStartInfo psi)
    {
        try
        {
            // Dispose the returned Process so the OS handle is released promptly.
            using Process? proc = _processLauncher(psi);
            if (proc is null)
            {
                Debug.WriteLine($"[DefaultShareService] Process.Start returned null for '{psi.FileName}'.");
            }
        }
        catch (Exception ex)
        {
            // Share is best-effort; surface to Debug output so developers can diagnose
            // without showing an error dialog to the user.
            Debug.WriteLine($"[DefaultShareService] Process launch failed ({psi.FileName}): {ex.Message}");
        }
    }
}
