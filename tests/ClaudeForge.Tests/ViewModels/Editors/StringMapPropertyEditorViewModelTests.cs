namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Locks the typed string→string map editor's contract:
/// - hydrates rows from a JsonObject scope value
/// - rebuilds a JsonObject on ToJsonValue (skipping blank keys)
/// - tolerates pre-existing bad data (bare strings) without crashing
/// - Add / Remove flows propagate IsModified
/// - duplicate-key Add overwrites existing entry
/// - Reset clears rows and Add inputs
/// </summary>
[TestClass]
public class StringMapPropertyEditorViewModelTests
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

    private static StringMapPropertyEditorViewModel NewVm(IReadOnlyList<string>? suggestions = null)
    {
        return new StringMapPropertyEditorViewModel(ComplexSchema(), ConfigScope.User, suggestions);
    }

    // -----------------------------------------------------------------------

    [TestMethod]
    public void Initial_NoLayeredEntry_NoRows_NotModified()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void LoadFromLayered_HydratesRowsFromObject()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        JsonObject obj = new()
        {
            ["sonnet"] = "anthropic.claude-3.5-sonnet",
            ["opus"] = "anthropic.claude-3-opus",
        };
        vm.LoadFromLayered(WithObject("modelOverrides", ConfigScope.User, obj), ConfigScope.User);

        Assert.AreEqual(2, vm.Items.Count);
        Assert.IsTrue(vm.IsModified);

        StringMapEntryViewModel sonnet = vm.Items.First(i => i.Key == "sonnet");
        Assert.AreEqual("anthropic.claude-3.5-sonnet", sonnet.Value);
    }

    [TestMethod]
    public void ToJsonValue_RebuildsJsonObject_FromRows()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewKeyText = "sonnet";
        vm.NewValueText = "anthropic.claude-3.5-sonnet";
        vm.AddEntryCommand.Execute(null);

        Assert.IsTrue(vm.IsModified);

        JsonObject written = (JsonObject)vm.ToJsonValue()!;
        Assert.AreEqual(1, written.Count);
        Assert.AreEqual("anthropic.claude-3.5-sonnet", written["sonnet"]?.GetValue<string>());
    }

    [TestMethod]
    public void BareStringScope_HydratesEmpty_NoCrash()
    {
        // Repro of the user's pre-existing bad on-disk data:
        //   "modelOverrides": "test"
        // Editor must tolerate the shape mismatch and start with no rows.
        StringMapPropertyEditorViewModel vm = NewVm();
        ScopeEntry entry = new(ConfigScope.User, JsonValue.Create("test"), "/fake");
        LayeredValue bad = new("modelOverrides", [entry])
        {
            EffectiveValue = JsonValue.Create("test"),
            EffectiveScope = ConfigScope.User,
        };

        vm.LoadFromLayered(bad, ConfigScope.User);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void Add_FlagsModified_AndPropagatesEntryEdits()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewKeyText = "sonnet";
        vm.AddEntryCommand.Execute(null);
        Assert.IsTrue(vm.IsModified);

        int modifiedFireCount = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StringMapPropertyEditorViewModel.IsModified))
            {
                modifiedFireCount++;
            }
        };

        // Editing an existing entry's value must re-fire IsModified so the
        // live-write path picks up the change (CommunityToolkit elides equal
        // bool assignments without the force-fire in MarkModified).
        vm.Items[0].Value = "anthropic.claude-3.5-sonnet";
        Assert.IsTrue(modifiedFireCount > 0);
    }

    [TestMethod]
    public void Add_DuplicateKey_OverwritesExistingValue()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewKeyText = "sonnet";
        vm.NewValueText = "first";
        vm.AddEntryCommand.Execute(null);

        vm.NewKeyText = "sonnet";
        vm.NewValueText = "second";
        vm.AddEntryCommand.Execute(null);

        Assert.AreEqual(1, vm.Items.Count);
        Assert.AreEqual("second", vm.Items[0].Value);
    }

    [TestMethod]
    public void Add_DisabledWhenKeyBlank()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        Assert.IsFalse(vm.AddEntryCommand.CanExecute(null), "blank key");
        vm.NewKeyText = "  ";
        Assert.IsFalse(vm.AddEntryCommand.CanExecute(null), "whitespace key");
        vm.NewKeyText = "sonnet";
        Assert.IsTrue(vm.AddEntryCommand.CanExecute(null));
    }

    [TestMethod]
    public void Remove_ShrinksItemsAndFlagsModified()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        JsonObject obj = new()
        {
            ["sonnet"] = "a",
            ["opus"] = "b",
        };
        vm.LoadFromLayered(WithObject("modelOverrides", ConfigScope.User, obj), ConfigScope.User);

        int modifiedFireCount = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StringMapPropertyEditorViewModel.IsModified))
            {
                modifiedFireCount++;
            }
        };

        StringMapEntryViewModel first = vm.Items[0];
        vm.RemoveEntryCommand.Execute(first);

        Assert.AreEqual(1, vm.Items.Count);
        Assert.IsTrue(modifiedFireCount > 0);
    }

    [TestMethod]
    public void ToJsonValue_SkipsBlankKeys()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewKeyText = "sonnet";
        vm.AddEntryCommand.Execute(null);

        // Direct mutation to simulate a row whose key was blanked after add —
        // ToJsonValue must skip it rather than emit "" : "".
        vm.Items[0].Key = string.Empty;

        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void ResetCommand_AfterLoad_RestoresOnDiskRows_NotClearsThem()
    {
        // Reset semantic consistency.  See
        // McpServerListEditorViewModelTests for the rationale and pattern.
        StringMapPropertyEditorViewModel vm = NewVm();
        JsonObject obj = new() { ["sonnet"] = "a", ["opus"] = "b" };
        vm.LoadFromLayered(WithObject("modelOverrides", ConfigScope.User, obj), ConfigScope.User);
        Assert.AreEqual(2, vm.Items.Count, "precondition: load populated 2 rows");

        // User edits transient inputs.
        vm.NewKeyText = "haiku";
        vm.NewValueText = "c";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(2, vm.Items.Count,
            "Reset must restore the original on-disk rows, not wipe to empty.");
        Assert.AreEqual(string.Empty, vm.NewKeyText, "Reset clears transient input.");
        Assert.AreEqual(string.Empty, vm.NewValueText, "Reset clears transient input.");
    }

    [TestMethod]
    public void ResetCommand_WithoutPriorLoad_FallsBackToClear()
    {
        StringMapPropertyEditorViewModel vm = NewVm();
        vm.IsModified = true;
        vm.NewKeyText = "k";
        vm.NewValueText = "v";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.AreEqual(string.Empty, vm.NewKeyText);
        Assert.AreEqual(string.Empty, vm.NewValueText);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void KeySuggestions_PassedThrough()
    {
        string[] suggestions = ["sonnet", "opus", "haiku"];
        StringMapPropertyEditorViewModel vm = NewVm(suggestions);
        CollectionAssert.AreEqual(suggestions, vm.KeySuggestions.ToArray());
    }
}