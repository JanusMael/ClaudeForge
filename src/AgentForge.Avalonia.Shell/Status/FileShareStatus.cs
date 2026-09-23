using Bennewitz.Ninja.AppServices.Abstractions;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Status;

/// <summary>
/// Turns a <see cref="ShareOutcome"/> from <see cref="IShareService.ShareFileAsync"/> into the
/// sentence and severity a host's status pill needs.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>One mapper, because there are two callers and they must not drift.</b> *Share log* in
/// ClaudeForge's About page and *Share* on a backup row do the same thing to different payloads,
/// and both sat on the silent-success defect F3 fixed on Effective settings. A switch written
/// twice is two chances to answer the same outcome differently.
/// </para>
/// <para>
/// ⚠ <b>The strings are passed in, never held here.</b> This assembly is product-neutral and has
/// no resx; user-visible text comes from the host, the same seam as
/// <c>BackupPageText</c> and <c>SaveDialogText</c>.
/// </para>
/// </remarks>
public static class FileShareStatus
{
    /// <summary>
    /// Selects the sentence for <paramref name="outcome"/> and says whether it is a failure —
    /// which is what decides whether the pill auto-clears or sticks until dismissed. A
    /// <see langword="null"/> sentence means say nothing: the user cancelled, and knows it.
    /// </summary>
    /// <param name="outcome">What <see cref="IShareService.ShareFileAsync"/> reported.</param>
    /// <param name="revealed">Said when the file was revealed in the platform's file manager.</param>
    /// <param name="unavailable">
    /// Said when nothing could be attempted — an unsupported OS, or a path not on disk. ⚠ Not a
    /// failure: nothing went wrong, so this clears itself rather than waiting to be dismissed.
    /// </param>
    /// <param name="failed">Said when a file manager was attempted and would not start.</param>
    public static (string? Text, bool IsFailure) Describe(
        ShareOutcome outcome,
        string revealed,
        string unavailable,
        string failed)
    {
        return outcome switch
        {
            ShareOutcome.RevealedInFileManager => (revealed, false),
            ShareOutcome.Unavailable => (unavailable, false),

            // ⛔ Answered EXPLICITLY, above the catch-all. Cancelled arrived with the required
            // CancellationToken, and it is not a contract violation: the caller asked to stop.
            // Left to the arm below it would report a cancel as a sticky FAILURE — compiling
            // cleanly, because that arm catches everything, which is precisely why it is spelled out.
            ShareOutcome.Cancelled => (null, false),

            // ⛔ Everything else is a CONTRACT VIOLATION, reported as a failure on purpose.
            // ShareFileAsync reveals a file on all three platforms; it cannot write a clipboard,
            // open a browser or reach a mail client. An implementation that returns one of those
            // is not doing what its caller asked, and the honest response is the failure sentence
            // — telling the user "revealed in your file manager" when it was not is exactly the
            // class of lie this whole change exists to remove. Failed itself lands here too, which
            // is why there is no separate arm for it.
            _ => (failed, true),
        };
    }
}
