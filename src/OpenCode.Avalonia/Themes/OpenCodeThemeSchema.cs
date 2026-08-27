using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Themes;

/// <summary>
/// One schema, with runtime-discovered theme names added to its suggestion list.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This is the whole <c>theme</c> editor, and that is the point.</b> The library's
/// <c>EnumPropertyEditorViewModel</c> already <i>is</i> the control the plan asks for — a picker
/// that also accepts free text — and it derives every part of itself from the schema it is handed:
/// the options from <see cref="EnumValues"/>, "may I type anything?" from a non-empty
/// <see cref="Examples"/>, and a per-option tooltip from <see cref="EnumValueDescriptions"/>. So the
/// slice needs no new view-model, no view, and no <c>DataTemplate</c> — only a schema that knows
/// what this machine has installed. Writing a tenth specialised editor here would have duplicated a
/// tested control in order to change one list.
/// </para>
/// <para>
/// ⚠ <b>Both <c>EnumValues</c> and <c>Examples</c> are supplied, and both are load-bearing.</b>
/// <c>EnumValues</c> is what the picker lists; a non-empty <c>Examples</c> is the flag that makes it
/// free-form rather than a closed ComboBox. Supplying only the first would silently turn a
/// free-form string into a control that refuses every theme this build has not heard of — the exact
/// over-constraint the <c>agent.color</c> field had to avoid.
/// </para>
/// <para>
/// Local to this project rather than added to the editor library: "augment a schema's suggestions
/// from disk" is a generic idea, but the only caller is this one field, and a library change would
/// need the library to care about a product's directory layout. Same call
/// <c>NestedEditorSchema</c> made next door.
/// </para>
/// </remarks>
public sealed class OpenCodeThemeSchema : IEditorSchema
{
    private readonly IEditorSchema _inner;

    /// <summary>Wraps <paramref name="inner"/>, offering <paramref name="themes"/> as options.</summary>
    /// <param name="inner">The real schema node for the property.</param>
    /// <param name="themes">
    /// The names to offer, in the order they should appear. The first is treated as the built-in.
    /// </param>
    public OpenCodeThemeSchema(IEditorSchema inner, IReadOnlyList<string> themes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(themes);

        _inner = inner;
        EnumValues = themes;

        // ⚠ Not `themes` itself. Examples only has to be NON-EMPTY to unlock free-form typing, and
        // reusing the full list would mean the same 40 strings are held twice for no reader. The
        // upstream overlay states the built-in here for the same reason.
        Examples = themes.Count > 0 ? [themes[0]] : [];

        Dictionary<string, string> descriptions = new(StringComparer.Ordinal);
        for (int i = 0; i < themes.Count; i++)
        {
            descriptions[themes[i]] = i == 0
                ? Strings.ThemeBuiltInTooltip
                : Strings.ThemeDiscoveredTooltip;
        }

        EnumValueDescriptions = descriptions;
    }

    /// <inheritdoc />
    /// <remarks>The discovered names, built-in first — see the constructor.</remarks>
    public IReadOnlyList<string>? EnumValues { get; }

    /// <inheritdoc />
    /// <remarks>Non-empty so the picker accepts a name this machine has never seen.</remarks>
    public IReadOnlyList<string> Examples { get; }

    /// <inheritdoc />
    /// <remarks>Says which suggestions are built in and which came off this disk.</remarks>
    public IReadOnlyDictionary<string, string> EnumValueDescriptions { get; }

    /// <inheritdoc />
    public string Path => _inner.Path;

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public string? Title => _inner.Title;

    /// <inheritdoc />
    /// <remarks>
    /// Passed through untouched: the text that explains where themes come from lives in
    /// <c>opencode-tui.overlay.json</c>, so it survives even when this wrapper is not used.
    /// </remarks>
    public string? Description => _inner.Description;

    /// <inheritdoc />
    /// <remarks>
    /// ⚠ Forced to <see cref="EditorValueType.Enum"/> rather than passed through. The builder
    /// already promotes a string-with-examples to <c>Enum</c>, so the inner node normally arrives as
    /// one — but this wrapper's whole purpose is to be handed to the enum editor, and inheriting
    /// <c>String</c> from a schema whose overlay had gone missing would leave that editor with a
    /// value type contradicting its own control.
    /// </remarks>
    public EditorValueType ValueType => EditorValueType.Enum;

    /// <inheritdoc />
    public double? Minimum => _inner.Minimum;

    /// <inheritdoc />
    public double? Maximum => _inner.Maximum;

    /// <inheritdoc />
    public IReadOnlyList<IEditorSchema> Properties => _inner.Properties;

    /// <inheritdoc />
    public IEditorSchema? ItemsSchema => _inner.ItemsSchema;

    /// <inheritdoc />
    public object? DefaultValue => _inner.DefaultValue;

    /// <inheritdoc />
    public bool IsReadOnly => _inner.IsReadOnly;

    /// <inheritdoc />
    public bool IsNew => _inner.IsNew;

    /// <inheritdoc />
    public bool IsDeprecated => _inner.IsDeprecated;

    /// <inheritdoc />
    public bool IsUndocumented => _inner.IsUndocumented;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Metadata => _inner.Metadata;
}
