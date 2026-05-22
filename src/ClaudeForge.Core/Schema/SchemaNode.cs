namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// A resolved, navigable representation of one JSON Schema property node.
/// All $ref references are already resolved; anyOf [type, null] is collapsed to the concrete type.
/// </summary>
public sealed class SchemaNode
{
    public SchemaNode(string jsonPath, string name)
    {
        JsonPath = jsonPath;
        Name = name;
    }

    /// <summary>Dot-separated JSON path from the root, e.g. "permissions.defaultMode".</summary>
    public string JsonPath { get; }

    /// <summary>The property name at this level, e.g. "defaultMode".</summary>
    public string Name { get; }

    /// <summary>Human-readable title from the schema, or null.</summary>
    public string? Title { get; init; }

    /// <summary>Description from the schema, or null.</summary>
    public string? Description { get; init; }

    /// <summary>The resolved value type for editor selection.</summary>
    public SchemaValueType ValueType { get; init; } = SchemaValueType.Unknown;

    /// <summary>For Enum type: the allowed string values.</summary>
    public IReadOnlyList<string> EnumValues { get; init; } = [];

    /// <summary>For Number/Integer type: minimum constraint, or null.</summary>
    public double? Minimum { get; init; }

    /// <summary>For Number/Integer type: maximum constraint, or null.</summary>
    public double? Maximum { get; init; }

    /// <summary>For Array type: the schema of each item element.</summary>
    public SchemaNode? ItemsSchema { get; init; }

    /// <summary>For Object type: child property nodes.</summary>
    public IReadOnlyList<SchemaNode> Properties { get; init; } = [];

    /// <summary>The default value as a JSON string, or null.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Example values from the schema's "examples" keyword — used as suggestions for free-form fields.</summary>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>JSON Schema format hint (e.g. "uri", "date-time"), or null.</summary>
    public string? Format { get; init; }

    /// <summary>JSON Schema pattern constraint, or null.</summary>
    public string? Pattern { get; init; }

    /// <summary>True when this property is listed as required in the parent schema.</summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// True when the description or title mentions "managed" settings only.
    /// Such properties should be displayed as read-only in the UI.
    /// </summary>
    public bool IsManagedOnly { get; init; }

    /// <summary>
    /// True when this property can also be null (from anyOf [type, null] patterns).
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// True when this property was not present in the last persisted schema snapshot.
    /// The UI renders a "✨ NEW" badge for these so users can discover new settings
    /// that shipped since they last opened the app. Flag is cleared on next launch
    /// after <see cref="SchemaSnapshotService.SaveSnapshot"/> is called at exit.
    /// </summary>
    public bool IsNew { get; init; }

    /// <summary>
    /// True when the schema marks this property as deprecated — either via the
    /// JSON-Schema Draft-2019-09 <c>deprecated</c> keyword, or (as a fallback)
    /// when the description begins with <c>"DEPRECATED"</c>. The UI hides
    /// deprecated properties that have no value set at any scope so users
    /// aren't prompted to adopt obsolete settings, while still surfacing them
    /// when they are already set (so the user can unset them).
    /// </summary>
    public bool IsDeprecated { get; init; }

    /// <summary>
    /// True when the description contains <c>"UNDOCUMENTED"</c>, indicating
    /// this property is not in the official Claude documentation. The UI shows
    /// a detective 🕵 badge instead of the raw "UNDOCUMENTED:" prefix text.
    /// The prefix is stripped from <see cref="Description"/> so it does not
    /// appear twice.
    /// </summary>
    public bool IsUndocumented { get; init; }

    /// <summary>
    /// Environment-variable names found in this property's description when the
    /// description mentions "environment variable" and the token begins with or
    /// contains <c>ANTHROPIC_</c> or <c>CLAUDE</c>.  These vars are surfaced as
    /// settable suggestions in the Environment editor even when they are not
    /// currently set anywhere.  Empty for most nodes.
    /// </summary>
    public IReadOnlyList<string> SuggestedEnvVars { get; init; } = [];
}