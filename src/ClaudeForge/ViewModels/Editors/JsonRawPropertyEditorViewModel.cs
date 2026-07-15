using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Schema;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>
/// Fallback editor for schema nodes whose <see cref="SchemaValueType"/> is
/// <see cref="SchemaValueType.Complex"/> or <see cref="SchemaValueType.Unknown"/>
/// AND for which no specialised editor (Permissions, Hooks, MCP servers,
/// Marketplaces, EnabledPlugins) was registered with the
/// <see cref="CompositeEditorFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the prior fallback that routed every Complex / Unknown property
/// to a <see cref="LayeredEditors.Avalonia.ViewModels.StringPropertyEditorViewModel"/>.
/// That fallback let the user type any string into a slot the schema required
/// to be an object — the corrupting value (<c>"modelOverrides": "test"</c>,
/// for instance) round-tripped to disk and only surfaced as a schema-banner
/// validation error on the next reload.
/// </para>
/// <para>
/// This editor presents the property's value as JSON in a multi-line text
/// box. The text is parsed on every change; parse failures populate
/// <see cref="ParseError"/> with the parser's message and prevent the live-
/// write path from writing the unparseable text. Successful parses update
/// the underlying <see cref="JsonNode"/> so the workspace receives a well-
/// formed value (or <see langword="null"/>, when the box is empty).
/// </para>
/// <para>
/// This is deliberately a low-fidelity affordance: it shows the user's data
/// in the format the underlying file expects, and refuses to corrupt that
/// file. A richer editor (key-value table for free-form dicts, structured
/// editor for known shapes) is a future enhancement; until then this
/// editor is the safe default for every Complex / Unknown leaf.
/// </para>
/// </remarks>
public partial class JsonRawPropertyEditorViewModel : PropertyEditorViewModel
{
    public JsonRawPropertyEditorViewModel(SchemaNode schema, ConfigScope editingScope)
        : base(schema, editingScope)
    {
    }

    /// <summary>
    /// The user-visible JSON text. Two-way bound to a multi-line TextBox.
    /// Empty / whitespace-only text is treated as "no value at this scope"
    /// and stores <see langword="null"/> on the workspace, matching the
    /// reset-to-inherited semantics of the simple leaves.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValueSet))]
    private string _text = string.Empty;

    /// <summary>
    /// Inline parse-error message shown beneath the JSON text box when the
    /// current <see cref="Text"/> is not valid JSON. <see langword="null"/>
    /// when the parse succeeded (or the text is empty).
    /// </summary>
    [ObservableProperty] private string? _parseError;

    /// <summary>
    /// Inline, advisory schema-structure message shown beneath the box when the JSON
    /// parses but doesn't fit this property's shape — a wrong root kind (array vs
    /// object) or a missing required child property. Distinct from <see cref="ParseError"/>
    /// (a hard parse failure): a schema warning does NOT block the live write, since the
    /// save-time validator is the authoritative gate and reports deep value constraints
    /// (enum / pattern) the structural check can't see. <see langword="null"/> when the
    /// structure looks right (or the text is empty / unparseable).
    /// </summary>
    [ObservableProperty] private string? _schemaError;

    /// <summary>True when the JSON text is non-empty.</summary>
    public bool IsValueSet => !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Cached parsed <see cref="JsonNode"/> from the last successful parse of
    /// <see cref="Text"/>. <see langword="null"/> when the text is empty
    /// (intentional clear) OR when the latest parse failed (in which case the
    /// previous value is also discarded so we never hand a stale JsonNode to
    /// the workspace under a misleading <see cref="IsModified"/> flag).
    /// </summary>
    private JsonNode? _parsedValue;

    /// <summary>
    /// True while <see cref="LoadFromLayered"/> is hydrating the editor from
    /// disk. Suppresses the live-write path so the bulk-load assignment of
    /// <see cref="Text"/> does not flag the editor as user-modified.
    /// </summary>
    private bool _isLoading;

    // Reset semantic consistency.  See McpServerListEditorViewModel
    // for rationale.  JsonRaw is the fallback editor for arbitrary
    // unmodelled complex values; on Reset the user expects to undo unsaved
    // edits, not wipe the entire JSON blob.
    private LayeredValue? _lastLayered;
    private ConfigScope _lastScope;

    /// <inheritdoc/>
    protected override bool IsLoading => _isLoading;

    partial void OnTextChanged(string value)
    {
        // Empty / whitespace-only -> revert to inherited (null value).
        // This mirrors how the typed leaves handle their cleared state.
        if (string.IsNullOrWhiteSpace(value))
        {
            _parsedValue = null;
            ParseError = null;
            SchemaError = null;
            // Match the simple-leaf "cleared" semantics: not modified.
            // The live-write path translates IsModified=false into a
            // RemoveValue() call.
            if (!IsLoading)
            {
                IsModified = false;
            }

            return;
        }

        try
        {
            _parsedValue = JsonNode.Parse(value);
            ParseError = null;
            // Advisory structural check (never blocks the write; save-time
            // validation is the gate). Surfaces a wrong root kind / missing
            // required property live, before the user has to hit Save.
            SchemaError = ValidateStructure(_parsedValue);
            // MarkModified force-fires PropertyChanged even when IsModified is
            // already true, so consecutive successful edits keep flowing
            // through the live-write loop.
            MarkModified();
        }
        catch (JsonException ex)
        {
            // Parse failed: surface the message inline. Leave _parsedValue
            // alone (it still points at the last successful parse) but DO NOT
            // re-fire IsModified — the live-write path must not write the
            // unparseable text or the stale prior value as if the user had
            // confirmed it. The user fixes the JSON; on the next successful
            // parse we resume writing. Clear any stale schema warning — the
            // parse error takes precedence.
            ParseError = ex.Message;
            SchemaError = null;
        }
    }

    /// <summary>
    /// Re-indent the current JSON in place (parse → pretty-print). No-op when the text
    /// is empty or not valid JSON — in the latter case <see cref="ParseError"/> already
    /// explains why nothing happened. Setting <see cref="Text"/> re-runs the parse +
    /// structural check through <see cref="OnTextChanged"/>.
    /// </summary>
    [RelayCommand]
    private void Format()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            return;
        }

        try
        {
            JsonNode? parsed = JsonNode.Parse(Text);
            if (parsed is not null)
            {
                Text = PrettyPrint(parsed);
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — leave the text and ParseError untouched.
        }
    }

    /// <summary>
    /// Best-effort structural check of <paramref name="value"/> against this property's
    /// <see cref="SchemaNode"/> — the part verifiable without the full JSON-Schema engine:
    /// the root kind (array vs object) and any missing required child properties. Returns
    /// a specific, human message, or <see langword="null"/> when the structure looks right.
    /// Deep value constraints (enum / pattern / nested types) are left to the save-time
    /// validator, which reports them in the banner. Deliberately English — matching the
    /// other non-localized raw-JSON diagnostics (parse errors, the unsupported-shape notice).
    /// </summary>
    private string? ValidateStructure(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        if (Schema.ValueType == SchemaValueType.Array && value is not JsonArray)
        {
            return $"This setting expects a JSON array, but the value is {DescribeKind(value)}.";
        }

        if (Schema.ValueType == SchemaValueType.Object && value is not JsonObject)
        {
            return $"This setting expects a JSON object, but the value is {DescribeKind(value)}.";
        }

        if (value is JsonObject obj && Schema.Properties.Count > 0)
        {
            List<string> missing = Schema.Properties
                .Where(p => p.IsRequired && !obj.ContainsKey(p.Name))
                .Select(p => p.Name)
                .ToList();
            if (missing.Count > 0)
            {
                string label = missing.Count == 1 ? "property" : "properties";
                return $"Missing required {label}: {string.Join(", ", missing)}.";
            }
        }

        return null;
    }

    /// <summary>Human name for a JSON node's kind, for the structural message.</summary>
    private static string DescribeKind(JsonNode node)
    {
        return node switch
        {
            JsonArray => "an array",
            JsonObject => "an object",
            JsonValue v when v.TryGetValue(out string? _) => "a string",
            JsonValue v when v.TryGetValue(out bool _) => "a boolean",
            _ => "a number",
        };
    }

    /// <inheritdoc/>
    public override JsonNode? ToJsonValue()
    {
        // Refuse to write while a parse error is pending. The live-write loop
        // checks IsModified before calling ToJsonValue, but ApplyToWorkspace
        // (Save flow) iterates every modified editor, so guard here too.
        if (ParseError is not null)
        {
            return null;
        }

        return _parsedValue?.DeepClone();
    }

    /// <inheritdoc/>
    public override void LoadFromLayered(LayeredValue layered, ConfigScope editingScope)
    {
        _lastLayered = layered;
        _lastScope = editingScope;
        SetScopeState(layered, editingScope);

        _isLoading = true;
        try
        {
            JsonNode? scopeValue = layered.GetValueAt(editingScope);
            if (scopeValue is null)
            {
                _parsedValue = null;
                ParseError = null;
                Text = string.Empty;
                IsModified = false;
                return;
            }

            _parsedValue = scopeValue.DeepClone();
            ParseError = null;
            Text = PrettyPrint(scopeValue);
            IsModified = true;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <inheritdoc/>
    protected override void OnResetToInherited()
    {
        // Reset semantic consistency: prefer reload from the
        // cached snapshot so unsaved edits revert to the at-load JSON
        // rather than wiping to empty.  See McpServerListEditorViewModel.
        if (_lastLayered is not null)
        {
            LoadFromLayered(_lastLayered, _lastScope);
            return;
        }

        // Fallback path — no snapshot.
        _parsedValue = null;
        ParseError = null;
        Text = string.Empty;
    }

    /// <summary>
    /// Pretty-print a <see cref="JsonNode"/> using
    /// <see cref="Utf8JsonWriter"/> with <c>Indented = true</c>.
    /// Avoids <see cref="JsonSerializer"/> so the call site stays trim-safe.
    /// </summary>
    private static string PrettyPrint(JsonNode node)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
        {
            node.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}