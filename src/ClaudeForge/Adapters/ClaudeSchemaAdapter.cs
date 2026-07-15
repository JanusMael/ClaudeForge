using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Wraps a <see cref="SchemaNode"/> as an <see cref="IEditorSchema"/>, mapping
/// <see cref="SchemaValueType"/> to <see cref="EditorValueType"/> and parsing the
/// <see cref="SchemaNode.DefaultValue"/> JSON string once at construction time.
/// </summary>
public sealed class ClaudeSchemaAdapter : IEditorSchema
{
    private readonly SchemaNode _inner;

    // lazy fields for the three properties whose eager
    // computation surfaced as a UI-freeze hot path on profile switch.
    //
    // Profile switch fires ReloadAsync → BuildNavigationTree → constructs
    // many editors → each editor builds a ClaudeSchemaAdapter from its
    // SchemaNode. Pre-fix, the constructor:
    //   • recursed through ALL Properties to build child adapters
    //   • parsed every DefaultValue JSON string at construction time
    //   • walked into ItemsSchema if present
    // For the Claude Code settings tree (~100 schema nodes, many with
    // defaults), this turned a single editor construction into hundreds
    // of allocations and JSON parses on the UI thread.
    //
    // Lazy <T> defers the work until the first access. Most editors don't
    // walk into Properties / DefaultValue unless the user actively
    // navigates into them, so the bulk of the work is now amortised over
    // user interaction rather than concentrated on profile switch.
    //
    // Lazy<> uses LazyThreadSafetyMode.ExecutionAndPublication by default
    // — fine here, the editor stack is single-threaded on the UI dispatcher.
    private readonly Lazy<IReadOnlyList<IEditorSchema>> _properties;
    private readonly Lazy<IEditorSchema?> _itemsSchema;
    private readonly Lazy<object?> _defaultValue;

    public ClaudeSchemaAdapter(SchemaNode inner)
    {
        _inner = inner;

        ValueType = MapValueType(inner.ValueType);
        _properties = new Lazy<IReadOnlyList<IEditorSchema>>(() =>
            inner.Properties.Select(p => (IEditorSchema)new ClaudeSchemaAdapter(p)).ToList());
        _itemsSchema =
            new Lazy<IEditorSchema?>(() => inner.ItemsSchema is { } items ? new ClaudeSchemaAdapter(items) : null);
        _defaultValue = new Lazy<object?>(() => ParseDefault(inner.DefaultValue));
        Metadata = BuildMetadata(inner);
    }

    public string Path => _inner.JsonPath;
    public string Name => _inner.Name;
    public string? Title => _inner.Title;
    public string? Description => _inner.Description;
    public EditorValueType ValueType { get; }

    public IReadOnlyList<string>? EnumValues => _inner.EnumValues.Count > 0 ? _inner.EnumValues : null;
    public IReadOnlyList<string> Examples => _inner.Examples;
    public IReadOnlyDictionary<string, string> EnumValueDescriptions => _inner.EnumValueDescriptions;
    public double? Minimum => _inner.Minimum;
    public double? Maximum => _inner.Maximum;
    public IReadOnlyList<IEditorSchema> Properties => _properties.Value;
    public IEditorSchema? ItemsSchema => _itemsSchema.Value;
    public object? DefaultValue => _defaultValue.Value;
    public bool IsReadOnly => _inner.IsManagedOnly;
    public bool IsNew => _inner.IsNew;
    public bool IsDeprecated => _inner.IsDeprecated;
    public bool IsUndocumented => _inner.IsUndocumented;

    public IReadOnlyDictionary<string, object?> Metadata { get; }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static EditorValueType MapValueType(SchemaValueType t)
    {
        return t switch
        {
            SchemaValueType.Boolean => EditorValueType.Boolean,
            SchemaValueType.String => EditorValueType.String,
            SchemaValueType.Path => EditorValueType.Path,
            SchemaValueType.Enum => EditorValueType.Enum,
            SchemaValueType.Integer => EditorValueType.Integer,
            SchemaValueType.Number => EditorValueType.Number,
            SchemaValueType.Array => EditorValueType.StringArray,
            SchemaValueType.Object => EditorValueType.Object,
            SchemaValueType.Complex => EditorValueType.Complex,
            var _ => EditorValueType.Unknown,
        };
    }

    private static object? ParseDefault(string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            JsonNode? node = JsonNode.Parse(json);
            return ClaudeValueAdapter.Normalise(node);
        }
        catch (JsonException)
        {
            // If the default is not valid JSON, treat as a raw string
            return json;
        }
    }

    private static IReadOnlyDictionary<string, object?> BuildMetadata(SchemaNode node)
    {
        Dictionary<string, object?> meta = new(StringComparer.Ordinal);
        if (node.Format is not null)
        {
            meta["Format"] = node.Format;
        }

        if (node.Pattern is not null)
        {
            meta["Pattern"] = node.Pattern;
        }

        if (node.IsNullable)
        {
            meta["IsNullable"] = true;
        }

        if (node.IsRequired)
        {
            meta["IsRequired"] = true;
        }

        return meta;
    }
}