using Bennewitz.Ninja.ClaudeForge.Core.Settings;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Plugins;

/// <summary>
/// Strongly-typed accessor for the <c>enabledPlugins</c> block of Claude Code
/// settings.
/// </summary>
public interface IEnabledPluginsAccessor
{
    /// <summary>Snapshot of every plugin registration (lazy materialization).</summary>
    /// <remarks>
    /// Reads from the merged effective view across all loaded scopes. For the
    /// raw value at one specific scope (used by editor UIs that show "what's
    /// stored at THIS scope"), see <see cref="GetAt"/>.
    /// </remarks>
    IReadOnlyList<EnabledPlugin> All { get; }

    /// <summary>
    /// Snapshot of plugin registrations stored at the given <paramref name="scope"/>
    /// only (no effective merging). Used by GUI editors that bind to the
    /// per-scope view rather than the merged effective view.
    /// </summary>
    /// <remarks>Lazy materialization — same semantics as <see cref="All"/>.</remarks>
    IReadOnlyList<EnabledPlugin> GetAt(ConfigScope scope);

    /// <summary>Returns the plugin entry registered as <paramref name="pluginRef"/>,
    /// or <see langword="null"/> when no entry exists.</summary>
    EnabledPlugin? Get(string pluginRef);

    /// <summary>Insert or replace a plugin entry by its <see cref="EnabledPlugin.PluginRef"/>.</summary>
    void Set(EnabledPlugin plugin);

    /// <summary>Remove the entry registered as <paramref name="pluginRef"/>. Returns
    /// <see langword="true"/> when an entry was removed.</summary>
    bool Remove(string pluginRef);

    /// <summary>Remove every plugin entry at the default scope.</summary>
    void Clear();
}