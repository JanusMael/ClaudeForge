namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Hands a text or file payload to the desktop, and reports which action that turned out to be.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>There is no native share sheet behind this interface on any platform.</b> This summary
/// said "invokes the native OS share sheet" and listed a MAUI Essentials flyout for Windows 10+;
/// that path lived behind a TFM that was never compiled, so no shipped build has ever opened one.
/// See <see cref="DefaultShareService"/>'s remarks for the measurement. Every implementation hands
/// the payload to the desktop by some other route, and <see cref="ShareOutcome"/> is how it says
/// which.
/// </para>
/// <para>
/// Platform behaviour of <see cref="DefaultShareService"/>:
/// <list type="bullet">
///   <item><b>Windows</b> — a URI opens in the default browser; text goes to the clipboard via
///     <c>clip.exe</c>; a file is revealed selected in Explorer.</item>
///   <item><b>macOS</b> — a URI opens via <c>open</c>; text goes to the clipboard via
///     <c>pbcopy</c>; a file is revealed in Finder via <c>open -R</c>.</item>
///   <item><b>Linux</b> — a URI or a constructed <c>mailto:</c> goes to the desktop handler;
///     a file's parent directory opens via <c>xdg-open</c>.</item>
/// </list>
/// </para>
/// </remarks>
public interface IShareService
{
    /// <summary>
    /// Hands a text (and optional URL) payload to the desktop, and reports what that turned out
    /// to be.
    /// </summary>
    /// <returns>
    /// The action actually taken — see <see cref="ShareOutcome"/>. ⚠ <b>Never assume success.</b>
    /// The three platforms do genuinely different things, and an implementation that can reach
    /// none of them returns <see cref="ShareOutcome.Unavailable"/> rather than completing quietly.
    /// </returns>
    Task<ShareOutcome> ShareTextAsync(string title, string text, string? uri = null);

    /// <summary>
    /// Hands a file payload to the desktop, and reports what that turned out to be.
    /// The file should already exist on disk.
    /// </summary>
    /// <returns>
    /// The action actually taken. A path that is not on disk is
    /// <see cref="ShareOutcome.Unavailable"/>, not <see cref="ShareOutcome.Failed"/>.
    /// </returns>
    Task<ShareOutcome> ShareFileAsync(string title, string filePath);
}