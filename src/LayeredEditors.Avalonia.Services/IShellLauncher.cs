namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Platform-agnostic shell / file-manager launcher.
/// Encapsulates all OS-specific logic for opening terminals and revealing
/// files in the platform file manager.
/// </summary>
public interface IShellLauncher
{
    /// <summary>
    /// Opens the platform terminal with <paramref name="command"/> pre-filled in the
    /// command line.  The user still has to press Enter — the command is never
    /// executed automatically.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a terminal was successfully launched;
    /// <see langword="false"/> when no suitable terminal emulator could be found
    /// (e.g. a headless or WSL environment with no installed terminal emulator).
    /// Callers should surface a "Copy" fallback or a "no terminal found" message
    /// when this returns <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Platform behaviour:
    /// <list type="bullet">
    ///   <item><b>Windows</b> — Prefers PowerShell 7+ (pwsh.exe). If the user has
    ///     configured Windows Terminal as their default terminal host it opens a new
    ///     Windows Terminal tab; otherwise opens a standalone PowerShell window.</item>
    ///   <item><b>macOS</b> — Opens Terminal.app via osascript.</item>
    ///   <item><b>Linux / WSL</b> — In WSL, tries wt.exe (Windows Terminal) first,
    ///     which opens a new WSL tab. Otherwise tries gnome-terminal → cosmic-term →
    ///     xfce4-terminal → mate-terminal → tilix → konsole → lxterminal → xterm.</item>
    /// </list>
    /// </remarks>
    bool LaunchTerminalWithCommand(string command);

    /// <summary>
    /// Opens the platform file manager showing the folder that contains
    /// <paramref name="filePath"/>, with the file pre-selected where the platform
    /// supports it.
    /// </summary>
    /// <remarks>
    /// Platform behaviour:
    /// <list type="bullet">
    ///   <item><b>Windows</b> — <c>explorer.exe /select,"&lt;path&gt;"</c></item>
    ///   <item><b>macOS</b> — <c>open -R "&lt;path&gt;"</c> (Finder, selects the file)</item>
    ///   <item><b>Linux</b> — Tries nautilus → dolphin → nemo → thunar → xdg-open,
    ///     falling back to opening the parent directory when the file manager does not
    ///     support single-file selection.</item>
    /// </list>
    /// Failures are silently swallowed.
    /// </remarks>
    void RevealInFileManager(string filePath);

    /// <summary>
    /// Opens <paramref name="filePath"/> in the platform default text editor.
    /// </summary>
    /// <remarks>
    /// Platform behaviour:
    /// <list type="bullet">
    ///   <item><b>Windows</b> — <c>Process.Start</c> with <c>UseShellExecute=true</c>
    ///     lets the OS dispatch the file via its registered handler for <c>.json</c>.</item>
    ///   <item><b>macOS</b> — <c>open -t "&lt;path&gt;"</c> opens in the user's default
    ///     text editor.</item>
    ///   <item><b>Linux</b> — <c>xdg-open "&lt;path&gt;"</c> defers to the desktop
    ///     environment's default handler for the file type.</item>
    /// </list>
    /// The path must be absolute. Failures are silently swallowed.
    /// </remarks>
    void OpenInDefaultEditor(string filePath);

    /// <summary>
    /// Opens <paramref name="url"/> in the platform default browser.
    /// </summary>
    /// <remarks>
    /// Platform behaviour:
    /// <list type="bullet">
    ///   <item><b>Windows</b> — <c>Process.Start</c> with <c>UseShellExecute=true</c>
    ///     dispatches the URL via the OS's registered <c>http://</c> /
    ///     <c>https://</c> handler (typically the default browser).</item>
    ///   <item><b>macOS</b> — <c>open "&lt;url&gt;"</c> (Launch Services routes to
    ///     the default browser).</item>
    ///   <item><b>Linux</b> — <c>xdg-open "&lt;url&gt;"</c> defers to the desktop
    ///     environment's default URL handler.</item>
    /// </list>
    /// Failures are silently swallowed — URL opening is cosmetic and the
    /// caller is expected to surface a copy-link affordance as fallback.
    /// </remarks>
    void LaunchUrl(string url);
}