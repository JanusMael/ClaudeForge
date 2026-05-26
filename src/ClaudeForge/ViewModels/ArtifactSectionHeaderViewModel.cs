namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// A group-header row interleaved into the Agents &amp; Skills segment lists.
/// Each tab's flat item collection holds a "Yours" header followed by the
/// writable (User + Project) rows, then a "Plugin" header (with
/// <see cref="IsReadOnly"/> = <see langword="true"/>) followed by the
/// read-only plugin rows.
///
/// <para>
/// Keeping headers and rows in one flat collection lets a single
/// virtualizing list scroll the whole tab — the alternative (nested
/// per-group lists) breaks Avalonia's virtualization.  The View switches
/// templates on type (this vs <see cref="ArtifactRowViewModel"/>).
/// </para>
/// </summary>
/// <param name="Header">The section label, e.g. "Yours" or "Plugin".</param>
/// <param name="IsReadOnly">
/// <see langword="true"/> for the Plugin section — drives the per-group
/// read-only badge (replacing the old per-row badge).
/// </param>
public sealed record ArtifactSectionHeaderViewModel(string Header, bool IsReadOnly);
