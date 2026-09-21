namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Settings;

/// <summary>
/// An editor that shows contextual hint banners which should disappear when the user clears the
/// page filter.
/// </summary>
/// <remarks>
/// <para>
/// Clearing a filter is expected to return the page to its default state. An editor that raised
/// a hint <i>because</i> of what the filter surfaced would otherwise leave a stale banner behind,
/// pointing at something no longer on screen.
/// </para>
/// <para>
/// The group editor previously did this by reaching for one product's concrete editor type and
/// setting one of its properties. The behaviour is not product-specific — any product's compound
/// editor can raise a transient hint — so the page asks for the capability instead of naming the
/// editor.
/// </para>
/// </remarks>
public interface ITransientHintHost
{
    /// <summary>
    /// Hide any hint banners that were shown in response to the current filter or selection.
    /// Called when the filter is cleared. Implementations should be idempotent and must not
    /// dismiss hints that are part of the editor's normal, unfiltered presentation.
    /// </summary>
    void DismissTransientHints();
}
