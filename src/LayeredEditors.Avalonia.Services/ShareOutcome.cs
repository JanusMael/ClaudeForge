namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// What a share attempt actually did, so a caller can say so in the user's own terms.
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>This type exists because the void-returning share hid a total no-op for the whole life of
/// the product.</b> <c>ShareTextAsync</c> returned <see cref="Task"/>, so "handed the payload to
/// the desktop" and "matched no branch and fell through" were indistinguishable to the caller, to
/// a test, and to the user. Windows text-sharing was the second of those for every shipped build
/// and nothing anywhere could tell. Reporting the outcome is what closes that blind spot; a
/// message without an outcome would only have papered over it.
/// </para>
/// <para>
/// ⚠ <b>There is no native share sheet on any platform</b>, and none of these members claims one.
/// Each names the fallback that actually ran — see <see cref="DefaultShareService"/>'s remarks for
/// why the MAUI Essentials path never compiled. A generic "Shared" would be a lie on Windows,
/// where nothing is shared and something is copied.
/// </para>
/// <para>
/// ⚠ <see cref="Failed"/> is deliberately <b>zero</b>, so a fake or a partially-written
/// implementation that returns <c>default</c> reports a failure the status pill keeps on screen,
/// rather than a success it clears after six seconds. The honest direction for an unset value is
/// loud.
/// </para>
/// </remarks>
public enum ShareOutcome
{
    /// <summary>
    /// A path was chosen and attempted, and it did not work — the process would not start, or
    /// threw. Distinct from <see cref="Unavailable"/> because the user can act on this one:
    /// something is wrong, rather than absent.
    /// </summary>
    Failed = 0,

    /// <summary>
    /// Nothing could be attempted: an unsupported OS, an empty payload, or a file that is not
    /// on disk. ⚠ Kept distinct from <see cref="Failed"/> because the status pill treats them
    /// differently — a failure sticks until dismissed, and this is not a failure.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The text is on the system clipboard — <c>clip.exe</c> on Windows, <c>pbcopy</c> on macOS.
    /// The action every share sheet contains anyway, and the only one available without a
    /// platform TFM.
    /// </summary>
    CopiedToClipboard,

    /// <summary>The URI was handed to the default browser.</summary>
    OpenedInBrowser,

    /// <summary>A <c>mailto:</c> URL was handed to the desktop's mail handler.</summary>
    OpenedMailClient,

    /// <summary>
    /// The file was revealed in the platform's file manager — Explorer with the file selected,
    /// Finder via <c>open -R</c>, or the containing directory via <c>xdg-open</c>.
    /// </summary>
    RevealedInFileManager,
}
