using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Marketplaces;

/// <summary>
/// Strongly-typed accessor for the <c>marketplaces</c> block of Claude Code
/// settings.
/// </summary>
public interface IMarketplacesAccessor
{
    /// <summary>Snapshot of every configured marketplace (lazy materialization).</summary>
    /// <remarks>
    /// Reads from the merged effective view across all loaded scopes. For the
    /// raw value at one specific scope (used by editor UIs that show "what's
    /// stored at THIS scope"), see <see cref="GetAt"/>.
    /// </remarks>
    IReadOnlyList<MarketplaceEntry> All { get; }

    /// <summary>
    /// Snapshot of marketplaces stored at the given <paramref name="scope"/>
    /// only (no effective merging). Used by GUI editors that bind to the
    /// per-scope view rather than the merged effective view.
    /// </summary>
    /// <remarks>Lazy materialization — same semantics as <see cref="All"/>.</remarks>
    IReadOnlyList<MarketplaceEntry> GetAt(ConfigScope scope);

    /// <summary>Returns the marketplace named <paramref name="name"/>, or
    /// <see langword="null"/> when no entry exists.</summary>
    MarketplaceEntry? Get(string name);

    /// <summary>Insert or replace a marketplace by its <see cref="MarketplaceEntry.Name"/>.</summary>
    void Set(MarketplaceEntry entry);

    /// <summary>Remove the marketplace named <paramref name="name"/>. Returns
    /// <see langword="true"/> when an entry was removed.</summary>
    bool Remove(string name);

    /// <summary>Remove every marketplace at the default scope.</summary>
    void Clear();
}