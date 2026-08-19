namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Navigation;

/// <summary>How faithfully a deep path should be re-applied.</summary>
public enum DeepRestoreMode
{
    /// <summary>
    /// Select the page, tab, and item — and stop there. Used for a cold launch
    /// and for an explicit <c>--deep-link</c> target: re-entering an editing
    /// experience the user can't see the origin of is surprising, and there is no
    /// unsaved buffer to put back.
    /// </summary>
    Locate,

    /// <summary>
    /// Restore the full in-page experience, including any editing state. Used
    /// across an in-process "Reload Window", where the user was mid-task a
    /// moment ago and expects to land exactly where they left off.
    /// </summary>
    Full,
}

/// <summary>
/// Implemented by an editor view-model that owns navigable state <em>below</em>
/// its navigation node — a tab, a selected item, an open editor — so that state
/// can be captured and put back.
///
/// <para>
/// Two consumers, with deliberately different fidelity:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Reload Window</b> is an in-process rebuild
///     (the host's reload tears down and recreates every editor VM), so it
///     restores with <see cref="DeepRestoreMode.Full"/> and
///     carries <see cref="CaptureTransientState"/> across in memory.
///   </item>
///   <item>
///     <b>Cold launch</b> and <b>--deep-link</b> have only the persisted or
///     typed path string, so they restore with
///     <see cref="DeepRestoreMode.Locate"/>.
///   </item>
/// </list>
///
/// <para>
/// The split between <see cref="CaptureDeepPath"/> and
/// <see cref="CaptureTransientState"/> is what makes a reload lossless without
/// writing anything sensitive to disk. The path is a short, culture-invariant,
/// safely-persistable address. The transient state is an opaque in-memory
/// snapshot that may contain an unsaved edit buffer — which is precisely why it
/// must never reach the host's persisted GUI-state file. Editors like Agents &amp;
/// Skills write files directly, so their edit buffer is not part of
/// <c>HasUnsavedChanges</c> and a reload would otherwise discard typed text with
/// no warning; carrying it in memory means the editing experience returns with
/// the user's <em>actual</em> text rather than a disk-seeded imitation.
/// </para>
/// <para>
/// App-local on purpose: only app editors implement it. Promoting it into
/// <c>LayeredEditors.Abstractions</c> would be speculative layering until an
/// out-of-tree consumer exists.
/// </para>
/// </summary>
public interface IDeepNavigable
{
    /// <summary>
    /// The segments below this node describing where the user currently is —
    /// e.g. <c>["skills", "pdf@user"]</c>. An empty list means "nothing worth
    /// restoring".
    /// <para>
    /// Must be culture-invariant and safe to persist: these segments go into
    /// <c>WindowState.LastDeepPath</c> and may be typed by a human on a command
    /// line. Never return a filesystem path — the segment separator is
    /// <c>/</c>, so a path would not survive a round trip.
    /// </para>
    /// </summary>
    IReadOnlyList<string> CaptureDeepPath();

    /// <summary>
    /// An opaque snapshot of state that is worth preserving across an in-process
    /// reload but must <b>never</b> be persisted — most importantly an unsaved
    /// edit buffer.
    /// <para>
    /// Default implementation returns <see langword="null"/>, so a page with no
    /// transient state adopts this interface with two members instead of three.
    /// </para>
    /// </summary>
    object? CaptureTransientState()
    {
        return null;
    }

    /// <summary>
    /// Re-apply a previously captured position.
    /// </summary>
    /// <param name="segments">
    /// Segments below this node, as produced by <see cref="CaptureDeepPath"/> or
    /// parsed from a <c>--deep-link</c> argument.
    /// </param>
    /// <param name="mode">How faithfully to restore — see <see cref="DeepRestoreMode"/>.</param>
    /// <param name="transientState">
    /// The value from <see cref="CaptureTransientState"/> when restoring across
    /// an in-process reload; <see langword="null"/> for a cold launch or an
    /// explicit deep link. Implementations must treat an unrecognised value as
    /// <see langword="null"/> rather than throwing.
    /// </param>
    /// <param name="ct">Cancels a restore superseded by newer navigation.</param>
    /// <returns>
    /// <see langword="true"/> when the target was found and applied;
    /// <see langword="false"/> when it no longer exists (a deleted artifact, a
    /// renamed tab). A <see langword="false"/> result is not an error — the
    /// caller has already landed the user on the right page, and a stale
    /// shortcut must degrade rather than fail.
    /// </returns>
    Task<bool> TryRestoreDeepPathAsync(
        IReadOnlyList<string> segments,
        DeepRestoreMode mode,
        object? transientState,
        CancellationToken ct);
}
