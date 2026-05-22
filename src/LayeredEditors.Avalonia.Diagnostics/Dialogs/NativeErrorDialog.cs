using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Dialogs;

/// <summary>
/// Last-resort error dialog for fatal situations where the Avalonia runtime may
/// be dead (startup failure, AppDomain-level unhandled exception).
/// <para>
/// The <em>primary</em> crash UI is the Avalonia-based
/// <see cref="FatalErrorDialog"/>, which only works while the UI thread is
/// alive. This helper is a fallback: MessageBox on Windows, <c>osascript</c> on
/// macOS, and a <c>zenity</c> / <c>kdialog</c> / <c>xmessage</c> chain on
/// Linux. If every platform helper is missing, the final <c>catch</c> writes
/// to <see cref="JSType.Error"/> so at least the message is visible when the
/// user runs the app from a terminal.
/// </para>
/// <para>
/// Every branch is wrapped in a <c>try</c> that swallows its own failure: this
/// method runs from crash handlers and must never throw.
/// </para>
/// </summary>
public static class NativeErrorDialog
{
    /// <summary>
    /// Set to <c>true</c> in tests to prevent any platform dialog from appearing.
    /// When suppressed, <see cref="ShowFatalError"/> records the last call but
    /// never invokes MessageBox, osascript, zenity, or writes to Console.Error.
    /// Must only be set from test code; never set in production.
    /// </summary>
    internal static bool SuppressForTests { get; set; }

    /// <summary>
    /// The title and message from the most recent <see cref="ShowFatalError"/>
    /// call when <see cref="SuppressForTests"/> is <c>true</c>. Allows tests to
    /// assert that the method was invoked with specific content without showing
    /// a real OS dialog. Reset between tests via <see cref="Reset"/>.
    /// </summary>
    internal static (string? Title, string? Message) LastSuppressedCall { get; private set; }

    /// <summary>Resets test state. Call from <c>[TestCleanup]</c>.</summary>
    internal static void Reset()
    {
        SuppressForTests = false;
        LastSuppressedCall = default;
    }

    /// <summary>
    /// Shows a platform-native error dialog. Never throws.
    /// </summary>
    /// <param name="title">Window title / dialog heading.</param>
    /// <param name="message">Full error text. Rendered verbatim — callers may pass
    /// an <see cref="Exception.ToString"/> result.</param>
    public static void ShowFatalError(string title, string message)
    {
        if (SuppressForTests)
        {
            LastSuppressedCall = (title, message);
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ShowWindows(title, message);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ShowMacOS(title, message);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ShowLinux(title, message);
                return;
            }
        }
        catch
        {
            // Fall through to the last-resort console write.
        }

        try
        {
            Console.Error.WriteLine($"[FATAL] {title}");
            Console.Error.WriteLine(message);
        }
        catch
        {
            // Even Console.Error failed (e.g. detached stream). Nothing else to do.
        }
    }

    // -----------------------------------------------------------------------
    // Platform branches
    // -----------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static void ShowWindows(string title, string message)
    {
        // MB_OK | MB_ICONERROR | MB_TASKMODAL (stay-on-top without a parent HWND).
        const uint MB_OK = 0x00000000;
        const uint MB_ICONERROR = 0x00000010;
        const uint MB_TASKMODAL = 0x00002000;

        MessageBoxW(nint.Zero, message, title, MB_OK | MB_ICONERROR | MB_TASKMODAL);
    }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private static void ShowMacOS(string title, string message)
    {
        // osascript is always present on a stock macOS install. Escape embedded
        // double-quotes and backslashes so the user's exception text does not
        // break the AppleScript string literal.
        string safeTitle = EscapeForAppleScript(title);
        string safeMessage = EscapeForAppleScript(message);
        string script =
            $"display dialog \"{safeMessage}\" with title \"{safeTitle}\" " +
            "with icon stop buttons {\"OK\"} default button 1";

        RunAndWait("osascript", ["-e", script], timeoutMs: 5000);
    }

    private static void ShowLinux(string title, string message)
    {
        // Try three common dialog helpers in order. Each one is quick to
        // invoke-and-fail, so the fallback chain is fast even on headless boxes.
        if (RunAndWait("zenity",
                ["--error", $"--title={title}", $"--text={message}", "--no-wrap"],
                timeoutMs: 5000))
        {
            return;
        }

        if (RunAndWait("kdialog",
                ["--title", title, "--error", message],
                timeoutMs: 5000))
        {
            return;
        }

        // xmessage is the oldest and most universal fallback on X11 boxes.
        RunAndWait("xmessage", ["-center", $"{title}\n\n{message}"], timeoutMs: 5000);
    }

    // -----------------------------------------------------------------------
    // Process helper
    // -----------------------------------------------------------------------

    /// <summary>
    /// Launches <paramref name="exe"/> with the supplied argument list and waits
    /// for it to exit (bounded by <paramref name="timeoutMs"/>). Returns
    /// <c>true</c> when the process started and exited normally with a zero exit
    /// code — used by Linux branches to chain fallbacks. Returns <c>false</c>
    /// on any failure (missing binary, non-zero exit, timeout).
    /// </summary>
    private static bool RunAndWait(string exe, string[] args, int timeoutMs)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using Process? proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            if (!proc.WaitForExit(timeoutMs))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception killEx) when (killEx is InvalidOperationException
                                                   or Win32Exception
                                                   or NotSupportedException)
                {
                    // Already exited / access denied / unsupported — nothing more to do.
                }

                return false;
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception
                                       or InvalidOperationException
                                       or FileNotFoundException
                                       or PlatformNotSupportedException)
        {
            // Native helper is not installed (e.g. zenity on a minimal Linux desktop)
            // or PSI is invalid for the host OS. Caller falls through to its next
            // last-resort dialog implementation.
            return false;
        }
    }

    private static string EscapeForAppleScript(string s)
    {
        return s.Replace("\\", @"\\").Replace("\"", "\\\"");
    }
}