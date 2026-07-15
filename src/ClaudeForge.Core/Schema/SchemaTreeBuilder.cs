using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Core.Schema;

/// <summary>
/// Builds a navigable tree of <see cref="SchemaNode"/> from a <see cref="JsonSchemaNode"/>.
/// Uses the JsonSchema.Net v8 keyword API (Handler.Name, RawValue, Subschemas).
/// </summary>
public static partial class SchemaTreeBuilder
{
    private static readonly HashSet<string> SpecializedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "mcpServers", "hooks", "permissions",
    };

    private static readonly string[] PathKeywords = ["path", "dir", "directory", "file", "folder"];

    // Matches any ALL_CAPS token (e.g. CLAUDE_CODE_TIMEOUT_MS, ANTHROPIC_API_KEY).
    private static readonly Regex UpperCaseTokenRegex = MyRegex();

    // Matches explicit env-var assignments in documentation prose, e.g. "Set DISABLE_AUTOUPDATER=1"
    // or "MAX_THINKING_TOKENS=8000". The token before = is extracted as a suggested variable.
    private static readonly Regex EnvAssignmentRegex = new(
        @"\b([A-Z][A-Z0-9_]{2,})=", RegexOptions.Compiled);

    // Strips inline "UNDOCUMENTED." / "UNDOCUMENTED: " markers that appear mid-description.
    private static readonly Regex InlineUndocumentedRegex = new(
        @"\bUNDOCUMENTED[.:]\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Build a flat list of SchemaNodes for all top-level properties in the root schema.
    /// </summary>
    public static IReadOnlyList<SchemaNode> BuildTopLevel(JsonSchemaNode rootNode)
    {
        return BuildTopLevel(rootNode, knownPaths: null, flagAllAsNew: false);
    }

    /// <summary>
    /// Build a flat list of SchemaNodes, flagging any path not in
    /// <paramref name="knownPaths"/> as <see cref="SchemaNode.IsNew"/>.
    /// Pass <c>null</c> to disable the feature entirely.
    /// An empty set means first run — nothing is flagged new.
    /// A non-empty set diffs against the last snapshot.
    /// </summary>
    public static IReadOnlyList<SchemaNode> BuildTopLevel(JsonSchemaNode rootNode, ISet<string>? knownPaths)
    {
        return BuildTopLevel(rootNode, knownPaths, flagAllAsNew: false);
    }

    /// <summary>
    /// Build a flat list of SchemaNodes with explicit control over the
    /// "force all as new" debug override.  When <paramref name="flagAllAsNew"/>
    /// is <see langword="true"/>, every node is stamped with <c>IsNew = true</c>
    /// regardless of <paramref name="knownPaths"/> — exposes the badge
    /// styling in the GUI without requiring a schema bump or hand-edited
    /// snapshot cache.  Wired via <c>DebugFlags.ShowAllNewBadges</c>
    /// (<c>--showAllNew</c> command-line flag).
    /// </summary>
    public static IReadOnlyList<SchemaNode> BuildTopLevel(
        JsonSchemaNode rootNode,
        ISet<string>? knownPaths,
        bool flagAllAsNew,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? enumDescriptionsByPath = null)
    {
        List<SchemaNode> result = new();
        foreach (PropertySubschema sub in GetPropertySubschemas(rootNode))
        {
            result.Add(BuildNode(sub.Name, sub.Name, sub.Node, knownPaths, flagAllAsNew, enumDescriptionsByPath));
        }

        return result;
    }

    /// <summary>
    /// Build a single SchemaNode for a named property.
    /// </summary>
    public static SchemaNode BuildNode(string jsonPath, string name, JsonSchemaNode schemaNode)
    {
        return BuildNode(jsonPath, name, schemaNode, knownPaths: null, flagAllAsNew: false);
    }

    /// <summary>
    /// Build a single SchemaNode, flagging <see cref="SchemaNode.IsNew"/> when
    /// <paramref name="knownPaths"/> is non-null and does not contain
    /// <paramref name="jsonPath"/>.
    /// </summary>
    public static SchemaNode BuildNode(string jsonPath, string name, JsonSchemaNode schemaNode,
                                       ISet<string>? knownPaths)
    {
        return BuildNode(jsonPath, name, schemaNode, knownPaths, flagAllAsNew: false);
    }

    /// <summary>
    /// Build a single SchemaNode with explicit control over the "force all as
    /// new" debug override.  See
    /// <see cref="BuildTopLevel(JsonSchemaNode, ISet{string}?, bool)"/> for the
    /// rationale.
    /// </summary>
    public static SchemaNode BuildNode(
        string jsonPath,
        string name,
        JsonSchemaNode schemaNode,
        ISet<string>? knownPaths,
        bool flagAllAsNew,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? enumDescriptionsByPath = null)
    {
        NullableCollapseResult collapse = CollapseNullable(schemaNode);
        if (collapse.Concrete != null)
        {
            schemaNode = collapse.Concrete;
        }

        bool isNullable = collapse.IsNullable;

        SchemaValueType valueType = DetermineValueType(name, schemaNode);

        IReadOnlyList<string> enumValues = GetEnumValues(schemaNode);
        IReadOnlyList<string> examples = GetExamples(schemaNode);

        // anyOf/oneOf whose branches are ALL strings, at least one an enum (e.g.
        // `theme`: a fixed enum OR a "custom:<slug>" pattern string). Classify as an
        // enum so it gets the value picker instead of the raw-JSON fallback; when a
        // non-enum string branch exists (a pattern / plain string), values beyond the
        // list are permitted, so also seed Examples — the enum editor reads a non-empty
        // Examples as "allow free-form typing" (AutoCompleteBox, not a strict ComboBox).
        if (valueType == SchemaValueType.Complex && enumValues.Count == 0)
        {
            StringUnionEnum? union = TryGetStringUnionEnum(schemaNode);
            if (union is { } u)
            {
                valueType = SchemaValueType.Enum;
                enumValues = u.Values;
                if (u.AllowsFreeForm && examples.Count == 0)
                {
                    examples = u.Values;
                }
            }
        }

        if (enumValues.Count > 0 && valueType is SchemaValueType.String or SchemaValueType.Unknown)
        {
            valueType = SchemaValueType.Enum;
        }
        else if (enumValues.Count == 0 && examples.Count > 0 &&
                 valueType is SchemaValueType.String or SchemaValueType.Unknown)
        {
            valueType = SchemaValueType.Enum;
        }

        IReadOnlyList<string> finalEnumValues = enumValues.Count > 0 ? enumValues
            : examples.Count > 0 && valueType == SchemaValueType.Enum ? examples : [];

        List<SchemaNode> children = new();
        // Recurse into Object nodes (always) and Complex nodes that expose
        // fixed named properties (e.g. "permissions" with allow/deny/ask,
        // "hooks" with PreToolUse/PostToolUse/…).  Complex nodes without a
        // "properties" keyword — i.e. dynamic bags like "mcpServers" whose
        // children are named by the user — return an empty list from
        // GetPropertySubschemas and the loop is a no-op for those.
        if (valueType is SchemaValueType.Object or SchemaValueType.Complex)
        {
            foreach (PropertySubschema sub in GetPropertySubschemas(schemaNode))
            {
                children.Add(BuildNode($"{jsonPath}.{sub.Name}", sub.Name, sub.Node, knownPaths, flagAllAsNew,
                    enumDescriptionsByPath));
            }
        }

        SchemaNode? itemsNode = null;
        if (valueType == SchemaValueType.Array)
        {
            KeywordData? itemsKw = FindKeyword(schemaNode, "items");
            if (itemsKw?.Subschemas.Length > 0)
            {
                itemsNode = BuildNode($"{jsonPath}[]", "item", itemsKw.Subschemas[0], knownPaths, flagAllAsNew,
                    enumDescriptionsByPath);
            }
        }

        string? title = GetStringRaw(schemaNode, "title");
        string? description = GetStringRaw(schemaNode, "description");
        double? min = GetNumberRaw(schemaNode, "minimum");
        double? max = GetNumberRaw(schemaNode, "maximum");
        KeywordData? defaultKw = FindKeyword(schemaNode, "default");
        string? defaultValue = defaultKw != null ? defaultKw.RawValue.ToString() : null;

        // flagAllAsNew == true     → debug override; every node is "new".
        // knownPaths == null        → feature disabled, no badges.
        // knownPaths.Count == 0    → first run, nothing is new yet.
        // knownPaths.Count > 0     → diff against last snapshot; flag unseen paths.
        bool isNew = flagAllAsNew
                     || (knownPaths is { Count: > 0 } && !knownPaths.Contains(jsonPath));

        // Honors both the JSON-Schema Draft-2019-09 "deprecated" keyword and our
        // description-prefix heuristic. The heuristic catches legacy schemas that
        // predate the keyword — e.g. `includeCoAuthoredBy` whose description begins
        // with "DEPRECATED.".
        bool isDeprecated = GetBoolRaw(schemaNode, "deprecated") ?? false;
        if (!isDeprecated && description != null
                          && description.StartsWith("DEPRECATED", StringComparison.OrdinalIgnoreCase))
        {
            isDeprecated = true;
        }

        // Detect "UNDOCUMENTED" anywhere in the description and strip the marker
        // so only the badge (not the raw text) appears in the UI.
        // Two cases:
        //   (a) Leading prefix — "UNDOCUMENTED. Hooks that run…" → strip prefix entirely.
        //   (b) Mid-description — "…\nUNDOCUMENTED: CLAUDE_VAR…" → strip just the marker word.
        bool isUndocumented = false;
        if (description != null
            && description.Contains("UNDOCUMENTED", StringComparison.OrdinalIgnoreCase))
        {
            isUndocumented = true;
            if (description.StartsWith("UNDOCUMENTED", StringComparison.OrdinalIgnoreCase))
            {
                // Leading — strip the whole prefix token + punctuation/space
                string rest = description["UNDOCUMENTED".Length..].TrimStart('.', ':', ' ');
                description = rest.Length > 0 ? rest : null;
            }
            else
            {
                // Mid-description — strip each inline "UNDOCUMENTED." / "UNDOCUMENTED: " marker
                description = InlineUndocumentedRegex.Replace(description, "");
            }
        }

        // Scan the raw description for environment-variable hints: if the text
        // mentions "environment variable" and contains an ALL_CAPS token that
        // starts with or contains ANTHROPIC_ or CLAUDE, surface those names as
        // suggestions in the Environment editor.
        IReadOnlyList<string> suggestedEnvVars = ExtractSuggestedEnvVarNames(description);

        return new SchemaNode(jsonPath, name)
        {
            Title = title,
            Description = description,
            ValueType = valueType,
            EnumValues = finalEnumValues,
            Minimum = min,
            Maximum = max,
            ItemsSchema = itemsNode,
            Properties = children,
            DefaultValue = defaultValue,
            Examples = examples,
            EnumValueDescriptions = enumDescriptionsByPath is not null
                                    && enumDescriptionsByPath.TryGetValue(jsonPath, out IReadOnlyDictionary<string, string>? descs)
                ? descs
                : SchemaNode.EmptyEnumValueDescriptions,
            IsNullable = isNullable,
            IsManagedOnly = IsManagedOnly(description),
            IsNew = isNew,
            IsDeprecated = isDeprecated,
            IsUndocumented = isUndocumented,
            SuggestedEnvVars = suggestedEnvVars,
        };
    }

    /// <summary>
    /// Collect every <see cref="SchemaNode.JsonPath"/> in the tree. Useful for
    /// building a fresh snapshot at application exit (pass the union of the
    /// known schema roots).
    /// </summary>
    public static IEnumerable<string> CollectPaths(IEnumerable<SchemaNode> nodes)
    {
        foreach (SchemaNode node in nodes)
        {
            yield return node.JsonPath;
            foreach (string p in CollectPaths(node.Properties))
            {
                yield return p;
            }

            if (node.ItemsSchema is not null)
            {
                foreach (string p in CollectPaths([node.ItemsSchema]))
                {
                    yield return p;
                }
            }
        }
    }

    /// <summary>
    /// Map every <see cref="SchemaNode.JsonPath"/> that carries help text to it
    /// (<see cref="SchemaNode.Description"/>, falling back to <see cref="SchemaNode.Title"/>).
    /// Lets a surface that only knows the effective dot-path — e.g. the Effective
    /// Settings grid — show the property's description on hover.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CollectDescriptions(IEnumerable<SchemaNode> nodes)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        Walk(nodes, map);
        return map;

        static void Walk(IEnumerable<SchemaNode> ns, Dictionary<string, string> sink)
        {
            foreach (SchemaNode node in ns)
            {
                string? text = !string.IsNullOrWhiteSpace(node.Description) ? node.Description
                    : !string.IsNullOrWhiteSpace(node.Title) ? node.Title
                    : null;
                if (text is not null)
                {
                    sink.TryAdd(node.JsonPath, text);
                }

                Walk(node.Properties, sink);
                if (node.ItemsSchema is not null)
                {
                    Walk([node.ItemsSchema], sink);
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static SchemaValueType DetermineValueType(string name, JsonSchemaNode node)
    {
        if (SpecializedProperties.Contains(name))
        {
            return SchemaValueType.Complex;
        }

        KeywordData? typeKw = FindKeyword(node, "type");
        if (typeKw != null)
        {
            // Value is a boxed SchemaValueType enum; RawValue is the JSON string
            string typeStr = typeKw.Value?.ToString() ?? typeKw.RawValue.GetString() ?? "";
            return typeStr switch
            {
                "Boolean" or "boolean" => SchemaValueType.Boolean,
                "Integer" or "integer" => SchemaValueType.Integer,
                "Number" or "number" => SchemaValueType.Number,
                "Array" or "array" => SchemaValueType.Array,
                "Object" or "object" => DetermineObjectType(node),
                "String" or "string" => DetermineStringType(name),
                var _ => SchemaValueType.Unknown,
            };
        }

        // anyOf/oneOf with multiple non-null variants → Complex
        KeywordData? anyOfKw = FindKeyword(node, "anyOf");
        if (anyOfKw != null)
        {
            JsonSchemaNode[] subs = anyOfKw.Subschemas ?? [];
            List<JsonSchemaNode> nonNull = subs.Where(s => GetTypeStringFromNode(s) is not ("null" or "Null")).ToList();
            if (nonNull.Count > 1)
            {
                return SchemaValueType.Complex;
            }
        }

        KeywordData? oneOfKw = FindKeyword(node, "oneOf");
        if (oneOfKw?.Subschemas.Length > 1)
        {
            return SchemaValueType.Complex;
        }

        return SchemaValueType.Unknown;
    }

    private static SchemaValueType DetermineObjectType(JsonSchemaNode node)
    {
        IReadOnlyList<PropertySubschema> props = GetPropertySubschemas(node);
        return props.Count == 0 ? SchemaValueType.Complex : SchemaValueType.Object;
    }

    private static SchemaValueType DetermineStringType(string name)
    {
        if (PathKeywords.Any(kw => name.Contains(kw, StringComparison.OrdinalIgnoreCase)))
        {
            return SchemaValueType.Path;
        }

        return SchemaValueType.String;
    }

    private static string? GetTypeStringFromNode(JsonSchemaNode node)
    {
        KeywordData? kw = FindKeyword(node, "type");
        if (kw == null)
        {
            return null;
        }

        return kw.Value?.ToString() ?? kw.RawValue.GetString();
    }

    private static IReadOnlyList<string> GetEnumValues(JsonSchemaNode node)
    {
        KeywordData? enumKw = FindKeyword(node, "enum");
        if (enumKw == null)
        {
            return [];
        }

        List<string> values = new();
        JsonElement raw = enumKw.RawValue;
        if (raw.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in raw.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString()!);
                }
            }
        }

        return values;
    }

    private static IReadOnlyList<string> GetExamples(JsonSchemaNode node)
    {
        KeywordData? examplesKw = FindKeyword(node, "examples");
        if (examplesKw == null)
        {
            return [];
        }

        List<string> values = new();
        JsonElement raw = examplesKw.RawValue;
        if (raw.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in raw.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    values.Add(item.GetString()!);
                }
            }
        }

        return values;
    }

    /// <summary>
    /// When <paramref name="node"/> is an <c>anyOf</c>/<c>oneOf</c> whose non-null
    /// branches are ALL strings and at least one carries an <c>enum</c> — e.g.
    /// <c>theme</c>, a fixed enum OR a <c>"custom:&lt;slug&gt;"</c> pattern string —
    /// returns the ordered, de-duplicated union of those enum values plus whether a
    /// free-form branch exists (a string branch with no enum: a pattern or plain string,
    /// meaning values beyond the enum are permitted). Returns <see langword="null"/> when
    /// the node is not that shape, so the caller leaves classification untouched.
    /// </summary>
    private static StringUnionEnum? TryGetStringUnionEnum(JsonSchemaNode node)
    {
        KeywordData? kw = FindKeyword(node, "anyOf") ?? FindKeyword(node, "oneOf");
        JsonSchemaNode[] subs = kw?.Subschemas ?? [];
        if (subs.Length < 2)
        {
            return null;
        }

        List<string> values = new();
        bool allowsFreeForm = false;
        foreach (JsonSchemaNode sub in subs)
        {
            string? type = GetTypeStringFromNode(sub);
            if (type is "null" or "Null")
            {
                continue;
            }

            if (type is not ("string" or "String"))
            {
                return null; // a non-string branch — not the theme-like shape
            }

            IReadOnlyList<string> subEnum = GetEnumValues(sub);
            if (subEnum.Count > 0)
            {
                values.AddRange(subEnum);
            }
            else
            {
                allowsFreeForm = true; // string branch w/o enum (pattern / plain)
            }
        }

        if (values.Count == 0)
        {
            return null; // need at least one enum branch to seed the picker
        }

        return new StringUnionEnum(values.Distinct().ToList(), allowsFreeForm);
    }

    private readonly record struct StringUnionEnum(IReadOnlyList<string> Values, bool AllowsFreeForm);

    private static IReadOnlyList<PropertySubschema> GetPropertySubschemas(JsonSchemaNode node)
    {
        List<PropertySubschema> result = new();
        KeywordData? propsKw = FindKeyword(node, "properties");
        if (propsKw?.Subschemas == null)
        {
            return result;
        }

        foreach (JsonSchemaNode sub in propsKw.Subschemas)
        {
            string path = sub.RelativePath.ToString();
            string propName = path.TrimStart('/');
            if (!string.IsNullOrEmpty(propName))
            {
                result.Add(new PropertySubschema(propName, sub));
            }
        }

        return result;
    }

    private static NullableCollapseResult CollapseNullable(JsonSchemaNode node)
    {
        KeywordData? anyOfKw = FindKeyword(node, "anyOf");
        if (anyOfKw?.Subschemas == null || anyOfKw.Subschemas.Length != 2)
        {
            return new NullableCollapseResult(null, false);
        }

        JsonSchemaNode? nonNull = null;
        bool hasNull = false;

        foreach (JsonSchemaNode sub in anyOfKw.Subschemas)
        {
            string? typeStr = GetTypeStringFromNode(sub);
            if (typeStr is "null" or "Null")
            {
                hasNull = true;
            }
            else
            {
                nonNull = sub;
            }
        }

        return (hasNull && nonNull != null)
            ? new NullableCollapseResult(nonNull, true)
            : new NullableCollapseResult(null, false);
    }

    private static KeywordData? FindKeyword(JsonSchemaNode node, string keywordName)
    {
        if (node.Keywords == null)
        {
            return null;
        }

        foreach (KeywordData kd in node.Keywords)
        {
            if (kd.Handler.Name == keywordName)
            {
                return kd;
            }
        }

        return null;
    }

    private static string? GetStringRaw(JsonSchemaNode node, string keywordName)
    {
        KeywordData? kw = FindKeyword(node, keywordName);
        if (kw == null)
        {
            return null;
        }

        return kw.RawValue.ValueKind == JsonValueKind.String ? kw.RawValue.GetString() : null;
    }

    private static double? GetNumberRaw(JsonSchemaNode node, string keywordName)
    {
        KeywordData? kw = FindKeyword(node, keywordName);
        if (kw == null)
        {
            return null;
        }

        JsonElement raw = kw.RawValue;
        return raw.ValueKind == JsonValueKind.Number ? raw.GetDouble() : null;
    }

    private static bool? GetBoolRaw(JsonSchemaNode node, string keywordName)
    {
        KeywordData? kw = FindKeyword(node, keywordName);
        if (kw == null)
        {
            return null;
        }

        JsonElement raw = kw.RawValue;
        return raw.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            var _ => null,
        };
    }

    private static bool IsManagedOnly(string? description)
    {
        return description != null
               && description.Contains("managed", StringComparison.OrdinalIgnoreCase)
               && description.Contains("only", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scan <paramref name="description"/> for ALL_CAPS tokens that look like
    /// environment-variable names when the text also mentions the phrase
    /// "environment variable".  Only tokens containing <c>ANTHROPIC_</c> or
    /// <c>CLAUDE</c> (case-sensitive) are returned — this avoids surfacing
    /// every generic constant like <c>PATH</c>.
    /// </summary>
    public static IReadOnlyList<string> ExtractSuggestedEnvVarNames(string? description)
    {
        if (description == null)
        {
            return [];
        }

        List<string> result = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        // Mode 1 — "environment variable" prose: extract Claude/Anthropic-prefixed vars.
        if (description.Contains("environment variable", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in UpperCaseTokenRegex.Matches(description))
            {
                string token = m.Groups[1].Value;
                if ((token.Contains("ANTHROPIC_") || token.Contains("CLAUDE")) && seen.Add(token))
                {
                    result.Add(token);
                }
            }
        }

        // Mode 2 — explicit assignment syntax ("Set DISABLE_AUTOUPDATER=1", "MAX_THINKING_TOKENS=8000"):
        // extract any upper-case token that appears immediately before '=' in the description.
        // This catches Claude-adjacent variables (DISABLE_*, MAX_*, API_*) that are documented
        // without the "environment variable" phrase.
        foreach (Match m in EnvAssignmentRegex.Matches(description))
        {
            string token = m.Groups[1].Value;
            if (token.Contains('_') && seen.Add(token))
            {
                result.Add(token);
            }
        }

        return result.Count > 0 ? result : [];
    }

    /// <summary>
    /// Recursively collect every environment-variable name suggested by any
    /// <see cref="SchemaNode.SuggestedEnvVars"/> list in the tree rooted at
    /// <paramref name="nodes"/>.  Returns a de-duplicated, sorted list.
    /// </summary>
    public static IReadOnlyList<string> CollectSuggestedEnvVars(IEnumerable<SchemaNode> nodes)
    {
        SortedSet<string> result = new(StringComparer.Ordinal);
        CollectSuggestedEnvVarsCore(nodes, result);
        return [..result];
    }

    private static void CollectSuggestedEnvVarsCore(IEnumerable<SchemaNode> nodes, SortedSet<string> result)
    {
        foreach (SchemaNode node in nodes)
        {
            foreach (string v in node.SuggestedEnvVars)
            {
                result.Add(v);
            }

            CollectSuggestedEnvVarsCore(node.Properties, result);
            if (node.ItemsSchema is not null)
            {
                CollectSuggestedEnvVarsCore([node.ItemsSchema], result);
            }
        }
    }

    // ── Private companion records ──────────────────────────────────────────────

    private sealed record PropertySubschema(string Name, JsonSchemaNode Node);

    private sealed record NullableCollapseResult(JsonSchemaNode? Concrete, bool IsNullable);

    [GeneratedRegex(@"\b([A-Z][A-Z0-9_]{2,})\b", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}