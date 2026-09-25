namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Locks the JSON-raw fallback editor's contract:
/// - parses successfully into a JsonNode the workspace can write
/// - refuses to write malformed JSON (ParseError set, ToJsonValue null)
/// - empty / whitespace-only text reverts to inherited
/// - LoadFromLayered hydrates pretty-printed JSON of the scope value
/// - reset clears value + error
/// </summary>
public class JsonRawPropertyEditorViewModelTests
{
    private static SchemaNode ComplexSchema(string name = "modelOverrides")
    {
        return new SchemaNode(name, name) { ValueType = SchemaValueType.Complex };
    }

    private static LayeredValue Empty(string key = "modelOverrides")
    {
        return new LayeredValue(key, []);
    }

    private static LayeredValue WithObject(string key, ConfigScope scope, JsonObject obj)
    {
        ScopeEntry entry = new(scope, obj.DeepClone(), "/fake");
        return new LayeredValue(key, [entry])
        {
            EffectiveValue = obj.DeepClone(),
            EffectiveScope = scope,
        };
    }

    // -----------------------------------------------------------------------

    [Fact]
    public void Initial_NoLayeredEntry_TextEmpty_NotModified()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        Assert.Equal(string.Empty, vm.Text);
        Assert.False(vm.IsModified);
        Assert.Null(vm.ParseError);
        Assert.Null(vm.ToJsonValue());
    }

    [Fact]
    public void LoadFromLayered_HydratesPrettyJson_FromScopeValue()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        JsonObject obj = new() { ["sonnet"] = "anthropic.claude-3.5-sonnet" };

        vm.LoadFromLayered(WithObject("modelOverrides", ConfigScope.User, obj), ConfigScope.User);

        OrdinalAssert.Contains("sonnet", vm.Text);
        OrdinalAssert.Contains("anthropic.claude-3.5-sonnet", vm.Text);
        // Pretty-printed -> contains a newline.
        OrdinalAssert.Contains("\n", vm.Text);
        Assert.True(vm.IsModified);
        Assert.Null(vm.ParseError);
    }

    [Fact]
    public void Edit_ValidJson_ParsesAndMarksModified()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.Text = """{"a":"b"}""";

        Assert.Null(vm.ParseError);
        Assert.True(vm.IsModified);

        JsonNode? written = vm.ToJsonValue();
        Assert.NotNull(written);
        Assert.Equal("b", written!["a"]?.GetValue<string>());
    }

    [Fact]
    public void Edit_InvalidJson_SetsParseError_AndToJsonValueRefuses()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        // Garbage text: schema banner WAS getting "test" written; this
        // editor must refuse instead.
        vm.Text = "not-json{";

        MessageAssert.NotNull(vm.ParseError, "Parse error must be surfaced.");
        // ToJsonValue must NOT hand the workspace a stale value while a
        // parse error is pending.
        Assert.Null(vm.ToJsonValue());
    }

    [Fact]
    public void Edit_InvalidThenValid_RecoversAndWrites()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.Text = "not-json{";
        Assert.NotNull(vm.ParseError);

        vm.Text = """{"x":1}""";
        Assert.Null(vm.ParseError);
        JsonNode? written = vm.ToJsonValue();
        Assert.NotNull(written);
        Assert.Equal(1, written!["x"]?.GetValue<int>());
    }

    [Fact]
    public void Clear_RevertsToInherited()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        JsonObject obj = new() { ["a"] = "b" };
        vm.LoadFromLayered(WithObject("modelOverrides", ConfigScope.User, obj), ConfigScope.User);

        Assert.True(vm.IsModified);

        vm.Text = string.Empty;

        Assert.False(vm.IsModified);
        Assert.Null(vm.ToJsonValue());
        Assert.Null(vm.ParseError);
    }

    [Fact]
    public void WhitespaceOnly_TreatedAsCleared()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.Text = "   \n   \t  ";

        Assert.False(vm.IsModified);
        Assert.Null(vm.ToJsonValue());
        Assert.Null(vm.ParseError);
    }

    [Fact]
    public void ResetCommand_AfterLoad_RestoresOnDiskJson_NotClearsIt()
    {
        // Reset semantic consistency.  Prior shape cleared
        // Text + _parsedValue unconditionally, wiping the user's saved
        // JSON blob when they hit Reset after editing.  The fix restores
        // the at-load JSON via LoadFromLayered.  See
        // McpServerListEditorViewModelTests for the rationale.
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        JsonObject obj = new() { ["a"] = "b" };
        vm.LoadFromLayered(WithObject("modelOverrides", ConfigScope.User, obj), ConfigScope.User);
        Assert.True(vm.Text.Contains("\"a\""), "precondition: load populated Text");
        Assert.True(vm.IsModified);

        // User edits the JSON.
        vm.Text = """{"changed": true}""";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.True(vm.Text.Contains("\"a\""),
            "Reset must restore the at-load JSON, not clear Text.");
        Assert.Null(vm.ParseError);
    }

    [Fact]
    public void ResetCommand_WithoutPriorLoad_FallsBackToClear()
    {
        // Edge case: Reset before LoadFromLayered ran.
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.IsModified = true;
        vm.Text = """{"x":1}""";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.Equal(string.Empty, vm.Text);
        Assert.Null(vm.ParseError);
        Assert.False(vm.IsModified);
        Assert.Null(vm.ToJsonValue());
    }

    [Fact]
    public void Hydrate_ScalarValue_DoesNotThrow()
    {
        // Even when the schema is Complex but the existing on-disk value is a
        // scalar (like the user's "modelOverrides": "test" repro), the editor
        // should still hydrate without crashing — it just shows the scalar
        // JSON for the user to edit.
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        ScopeEntry entry = new(ConfigScope.User, JsonValue.Create("test"), "/fake");
        LayeredValue layered = new("modelOverrides", [entry])
        {
            EffectiveValue = JsonValue.Create("test"),
            EffectiveScope = ConfigScope.User,
        };

        vm.LoadFromLayered(layered, ConfigScope.User);

        OrdinalAssert.Contains("test", vm.Text);
        Assert.True(vm.IsModified);
        Assert.Null(vm.ParseError);
    }

    // ── Smart box: Format + structural validation ──────────────────────────

    private static SchemaNode ArraySchema(string name = "sandbox.enabledPlatforms")
    {
        return new SchemaNode(name, name) { ValueType = SchemaValueType.Array };
    }

    private static SchemaNode ObjectSchemaWithRequired(string name = "theming")
    {
        return new SchemaNode(name, name)
        {
            ValueType = SchemaValueType.Object,
            Properties = [new SchemaNode($"{name}.base", "base") { ValueType = SchemaValueType.String, IsRequired = true }],
        };
    }

    [Fact]
    public void Format_ReindentsCompactJson_KeepingItValid()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.Text = """{"a":1,"b":{"c":2}}""";

        vm.FormatCommand.Execute(null);

        MessageAssert.Contains("\n", vm.Text, "Formatting must indent onto multiple lines.");
        MessageAssert.Null(vm.ParseError, "Formatted JSON must still parse.");
        Assert.Equal(2, vm.ToJsonValue()?["b"]?["c"]?.GetValue<int>());
    }

    [Fact]
    public void Format_InvalidJson_IsNoOp_LeavesParseError()
    {
        JsonRawPropertyEditorViewModel vm = new(ComplexSchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.Text = "not-json{";

        vm.FormatCommand.Execute(null);

        MessageAssert.Equal("not-json{", vm.Text, "Format must not alter unparseable text.");
        Assert.NotNull(vm.ParseError);
    }

    [Fact]
    public void Structure_WrongRootKind_WarnsButDoesNotBlockWrite()
    {
        JsonRawPropertyEditorViewModel vm = new(ArraySchema(), ConfigScope.User);
        vm.LoadFromLayered(Empty("sandbox.enabledPlatforms"), ConfigScope.User);

        // Schema wants an array; the user typed an object.
        vm.Text = """{"a":1}""";

        MessageAssert.Null(vm.ParseError, "Valid JSON — no parse error.");
        MessageAssert.NotNull(vm.SchemaError, "A wrong root kind must raise the advisory schema warning.");
        OrdinalAssert.Contains("array", vm.SchemaError!);
        // Advisory only — the save-time validator is the gate, so the write still flows.
        MessageAssert.NotNull(vm.ToJsonValue(), "A schema warning must NOT block the live write.");
    }

    [Fact]
    public void Structure_MissingRequiredProperty_Warns()
    {
        JsonRawPropertyEditorViewModel vm = new(ObjectSchemaWithRequired(), ConfigScope.User);
        vm.LoadFromLayered(Empty("theming"), ConfigScope.User);

        vm.Text = "{}"; // missing the required "base"

        Assert.Null(vm.ParseError);
        Assert.NotNull(vm.SchemaError);
        OrdinalAssert.Contains("base", vm.SchemaError!);
    }

    [Fact]
    public void Structure_ValidShape_ClearsWarning()
    {
        JsonRawPropertyEditorViewModel vm = new(ObjectSchemaWithRequired(), ConfigScope.User);
        vm.LoadFromLayered(Empty("theming"), ConfigScope.User);

        vm.Text = "{}";
        MessageAssert.NotNull(vm.SchemaError, "precondition: empty object is missing required 'base'");

        vm.Text = """{"base":"dark"}""";

        MessageAssert.Null(vm.SchemaError, "A structurally valid value must clear the advisory warning.");
        Assert.Null(vm.ParseError);
    }
}