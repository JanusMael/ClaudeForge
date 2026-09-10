using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Bennewitz.Ninja.OpenCodeForge.Services;

/// <summary>
/// Hands a URL to the platform's default handler.
/// </summary>
/// <remarks>
/// <para>
/// Three platforms, three mechanisms, and none of them is portable: Windows shell-executes the
/// URL itself, macOS has <c>open</c>, everything else gets <c>xdg-open</c>.
/// </para>
/// <para>
/// ⚠ <b>Failure is swallowed on purpose.</b> Opening a browser is cosmetic — a machine with no
/// default handler, or a sandbox that forbids launching one, must not take the window down over
/// it. Every caller shows the URL on screen as well, so it can still be copied.
/// </para>
/// <para>
/// The <c>catch</c> is narrow rather than blanket: these four are what a missing or unusable
/// handler actually throws. Anything else is a bug worth surfacing.
/// </para>
/// </remarks>
internal static class UrlOpener
{
    /// <summary>Open <paramref name="url"/>, or do nothing if it cannot be opened.</summary>
    internal static void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            // Process.Start hands back a Process? the caller owns even under UseShellExecute,
            // so the discard `using` disposes the handle now rather than at GC.
            using Process? _ =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                        ? Process.Start(new ProcessStartInfo
                            { FileName = "open", ArgumentList = { url }, UseShellExecute = false })
                        : Process.Start(new ProcessStartInfo
                            { FileName = "xdg-open", ArgumentList = { url }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is Win32Exception
                                       or InvalidOperationException
                                       or FileNotFoundException
                                       or PlatformNotSupportedException)
        {
            _ = ex;
        }
    }
}
