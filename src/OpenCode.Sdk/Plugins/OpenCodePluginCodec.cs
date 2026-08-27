using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk.Plugins;

/// <summary>
/// One element of a <c>plugin</c> array: either a bare specifier, or a specifier paired with an
/// options object.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b><c>"foo"</c> and <c>["foo", {}]</c> are different values, and this type keeps them
/// apart.</b> The schema's second arm is a strict two-element tuple (<c>prefixItems</c> with
/// <c>minItems: 2, maxItems: 2</c>), so an empty options object is a deliberate statement rather
/// than noise. Collapsing it to the bare form would rewrite a user's file on save for no reason —
/// which is exactly the class of silent edit this phase keeps finding.
/// <see cref="HasOptions"/> is the discriminator: <see langword="null"/> options means the bare
/// form.
/// </para>
/// <para>
/// Anything else — a one-element array, a two-element array whose second item is not an object, a
/// number — is held verbatim in <see cref="Raw"/>. The tuple is strict enough that "nearly right"
/// is common, and a plugin silently dropped is a plugin the user believes is loaded.
/// </para>
/// </remarks>
public sealed record OpenCodePluginEntry
{
    /// <summary>The plugin specifier, e.g. an npm package name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The options object, or <see langword="null"/> for the bare-string form.
    /// </summary>
    /// <remarks>
    /// Opaque: the schema types it as <c>object</c> with no declared properties, so there is nothing
    /// to model and nothing this SDK could usefully validate.
    /// </remarks>
    public object? Options { get; init; }

    /// <summary>True when this entry writes the two-element tuple form.</summary>
    public bool HasOptions => !IsOpaque && Options is not null;

    /// <summary>The element exactly as read, when it matched neither arm.</summary>
    public object? Raw { get; init; }

    /// <summary>True when the element matched neither arm and is held verbatim.</summary>
    /// <remarks>
    /// An explicit flag rather than <c>Raw is not null</c>, which cannot tell a JSON <c>null</c>
    /// element from a readable one — the same derivation that made an agent entry round-trip as an
    /// invented empty object.
    /// </remarks>
    public bool IsOpaque { get; init; }
}

/// <summary>
/// One entry of the TUI's <c>plugin_enabled</c> map: a plugin name and whether it is on.
/// </summary>
/// <remarks>
/// Carries an opaque arm for the same reason every other shape in this phase does — the schema says
/// these values are booleans, and a config that says otherwise is exactly the one whose evidence
/// must survive being opened.
/// </remarks>
public sealed record OpenCodePluginToggle
{
    /// <summary>The plugin name, verbatim.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether the plugin is enabled, or <see langword="null"/> when unreadable.</summary>
    public bool? Enabled { get; init; }

    /// <summary>The value exactly as read, when it was not a boolean.</summary>
    public object? Raw { get; init; }

    /// <summary>True when the value was not a boolean and is held verbatim.</summary>
    public bool IsOpaque => Enabled is null;
}

/// <summary>
/// Reads and writes <c>plugin</c> arrays and the TUI's <c>plugin_enabled</c> map in the editor
/// library's value currency.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>The <c>plugin</c> shape is byte-identical in both products</b> — <c>Config.plugin</c> and
/// the TUI's <c>plugin</c> declare the same <c>anyOf</c>, so one codec and one editor serve both.
/// <c>plugin_enabled</c> is TUI-only and is a plain name→bool map.
/// </para>
/// <para>
/// ⛔ <b>The plan points at <c>MarketplaceListEditorViewModel</c> for this shape on the stated
/// grounds that it echoes an unknown variant back unchanged. It does not</b> — measured in Phase
/// 9a-3: it returns <see langword="null"/> from <c>TryHydrateEntry</c> for an unknown variant and
/// the caller skips the row, and its save path carries the comment
/// <c>// unknown source — drop on save</c>. The preservation here follows 9a-3's per-entry pattern
/// instead: one unreadable element does not make the rest of the list read-only.
/// </para>
/// </remarks>
public static class OpenCodePluginCodec
{
    /// <summary>Parse a <c>plugin</c> array, preserving element order.</summary>
    public static IReadOnlyList<OpenCodePluginEntry> ReadList(object? value)
    {
        if (value is not IReadOnlyList<object?> list)
        {
            return [];
        }

        List<OpenCodePluginEntry> entries = [];
        foreach (object? element in list)
        {
            entries.Add(ReadEntry(element));
        }

        return entries;
    }

    /// <summary>Classify and read one array element.</summary>
    public static OpenCodePluginEntry ReadEntry(object? value)
    {
        if (value is string bare)
        {
            return new OpenCodePluginEntry { Name = bare };
        }

        // The tuple arm is strict: exactly two items, a string then an object. Anything looser is
        // not a plugin this build can edit, and is kept rather than guessed at.
        if (value is IReadOnlyList<object?> { Count: 2 } tuple
            && tuple[0] is string name
            && tuple[1] is IReadOnlyDictionary<string, object?> options)
        {
            return new OpenCodePluginEntry { Name = name, Options = options };
        }

        return new OpenCodePluginEntry { IsOpaque = true, Raw = value };
    }

    /// <summary>
    /// Serialise a <c>plugin</c> array, or <see langword="null"/> when there is nothing to write.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> rather than an empty array, so the workspace removes the key instead
    /// of persisting a <c>"plugin": []</c> that loads nothing. An entry with a blank name is
    /// skipped — a half-added row is not yet a plugin.
    /// </remarks>
    public static object? WriteList(IReadOnlyList<OpenCodePluginEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<object?> written = [];
        foreach (OpenCodePluginEntry entry in entries)
        {
            if (entry.IsOpaque)
            {
                written.Add(entry.Raw);
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            written.Add(entry.HasOptions
                ? new List<object?> { entry.Name, entry.Options }
                : entry.Name);
        }

        return written.Count == 0 ? null : written;
    }

    /// <summary>Parse the TUI's <c>plugin_enabled</c> map, preserving key order.</summary>
    /// <remarks>
    /// ⚠ A value that is not a boolean is preserved rather than skipped. Filtering the map down to
    /// its readable entries looks harmless on a read and deletes the rest on the next write — the
    /// same silent loss every other codec in this phase routes through an opaque arm.
    /// </remarks>
    public static IReadOnlyList<OpenCodePluginToggle> ReadEnabledMap(object? value)
    {
        if (value is not IReadOnlyDictionary<string, object?> map)
        {
            return [];
        }

        List<OpenCodePluginToggle> toggles = [];
        foreach ((string name, object? enabled) in map)
        {
            toggles.Add(enabled is bool flag
                ? new OpenCodePluginToggle { Name = name, Enabled = flag }
                : new OpenCodePluginToggle { Name = name, Raw = enabled });
        }

        return toggles;
    }

    /// <summary>
    /// Serialise the TUI's <c>plugin_enabled</c> map, or <see langword="null"/> when empty.
    /// </summary>
    public static object? WriteEnabledMap(IReadOnlyList<OpenCodePluginToggle> toggles)
    {
        ArgumentNullException.ThrowIfNull(toggles);

        OrderedPropertyMap map = new();
        foreach (OpenCodePluginToggle toggle in toggles)
        {
            if (string.IsNullOrWhiteSpace(toggle.Name))
            {
                continue;
            }

            map.Set(toggle.Name, toggle.IsOpaque ? toggle.Raw : toggle.Enabled);
        }

        return map.Count == 0 ? null : map;
    }
}
