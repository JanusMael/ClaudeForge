using System.Diagnostics;
#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER
using Microsoft.Maui.ApplicationModel.DataTransfer;
#endif

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Default cross-platform implementation of <see cref="IShareService"/>.
/// </summary>
/// <remarks>
/// On <b>Windows 10 v1809+</b> (build 17763+), uses MAUI Essentials and the
/// caller-supplied HWND factory to anchor the share flyout to the correct window.
/// On <b>macOS</b>, reveals files in Finder or opens URIs via <c>open</c>.
/// On <b>Linux</b>, opens the parent directory via <c>xdg-open</c> or
/// constructs a <c>mailto:</c> URL for text payloads.
/// </remarks>
public sealed class DefaultShareService : IShareService
{
    private readonly Func<nint> _hwndProvider;
    private readonly Func<ProcessStartInfo, Process?> _processLauncher;
#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER
    private bool _mauiInitialized;
#endif

    /// <param name="hwndProvider">
    /// Factory invoked once (lazily on first share operation) to retrieve the native
    /// window handle for MAUI Essentials initialisation.  Pass
    /// <c>() => avaloniaWindow.TryGetPlatformHandle()?.Handle ?? default</c>
    /// from the app's startup code.  Omit or pass <see langword="null"/> on
    /// non-Windows platforms — the value is never accessed then.
    /// </param>
    /// <param name="processLauncher">
    /// Optional override for <see cref="Process.Start(ProcessStartInfo)"/>.
    /// Pass <c>_ =&gt; null</c> in unit tests to suppress real process launches.
    /// When <see langword="null"/> the default <see cref="Process.Start(ProcessStartInfo)"/>
    /// is used.
    /// </param>
    public DefaultShareService(
        Func<nint>? hwndProvider = null,
        Func<ProcessStartInfo, Process?>? processLauncher = null)
    {
        _hwndProvider = hwndProvider ?? (() => 0);
        _processLauncher = processLauncher ?? Process.Start;
    }

#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER
    /// <summary>
    /// Initialises MAUI Essentials with the current window HWND the first time a
    /// share operation is requested.  Subsequent calls are a no-op so the WinRT
    /// interop layer is not disturbed mid-operation.
    /// </summary>
    private void EnsureMauiInit()
    {
        if (_mauiInitialized) return;
        Microsoft.Maui.ApplicationModel.Platform.Init(_hwndProvider());
        _mauiInitialized = true;
    }
#endif

    /// <inheritdoc />
    public Task ShareTextAsync(string title, string text, string? uri = null)
    {
#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return ShareTextWindowsAsync(title, text, uri);
#endif
        if (OperatingSystem.IsMacOS())
        {
            if (!string.IsNullOrEmpty(uri))
            {
                // Open URI in the default browser.
                TryStart(new ProcessStartInfo { FileName = "open", ArgumentList = { uri }, UseShellExecute = false });
            }
            else if (!string.IsNullOrEmpty(text))
            {
                // macOS has no Share sheet API without NSSharingService (requires net10.0-macos
                // third TFM). Fall back to pbcopy so the user at least has the text on the
                // clipboard — analogous to the "Copy" action that every Share sheet contains.
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
            // Windows fallback when MAUI is not active (net10.0 TFM / debug builds):
            // open the URI in the default browser.
            TryStart(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
        }

        // Other OS or plain-text with no URI — no-op.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShareFileAsync(string title, string filePath)
    {
#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return ShareFileWindowsAsync(title, filePath);
#endif
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
            // Windows fallback when MAUI is not active (net10.0 TFM / debug builds):
            // reveal the file selected in Explorer so the user can share from there.
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

#if NET10_0_WINDOWS10_0_19041_0_OR_GREATER
    private async Task ShareTextWindowsAsync(string title, string text, string? uri)
    {
        EnsureMauiInit();
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = title,
            Text = text,
            Uri = uri,
        });
    }

    private async Task ShareFileWindowsAsync(string title, string filePath)
    {
        EnsureMauiInit();
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(filePath),
        });
    }
#endif

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