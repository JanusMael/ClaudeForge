namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Locks the MCP server allow/deny list editor's contract:
/// - hydrates Name / Command / URL rows from a JsonArray scope value
/// - rebuilds a JsonArray on ToJsonValue with the right discriminator key
/// - tolerates pre-existing bad data (bare strings, missing discriminators)
/// - skips blank rows on save
/// - returns null (RemoveValue) when Items is empty so the schema's
///   "undefined = no restriction" semantic is preserved
/// - Add / Remove flow propagates IsModified
/// - Reset clears rows and Add inputs
/// </summary>
[TestClass]
public class McpServerListEditorViewModelTests
{
    private static SchemaNode ArraySchema(string name = "allowedMcpServers")
    {
        return new SchemaNode(name, name) { ValueType = SchemaValueType.Array };
    }

    private static LayeredValue Empty(string key = "allowedMcpServers")
    {
        return new LayeredValue(key, []);
    }

    private static LayeredValue WithArray(string key, ConfigScope scope, JsonArray arr)
    {
        ScopeEntry entry = new(scope, arr.DeepClone(), "/fake");
        return new LayeredValue(key, [entry])
        {
            EffectiveValue = arr.DeepClone(),
            EffectiveScope = scope,
        };
    }

    private static McpServerListEditorViewModel NewVm()
    {
        return new McpServerListEditorViewModel(ArraySchema(), ConfigScope.User);
    }

    // -----------------------------------------------------------------------

    [TestMethod]
    public void Initial_NoLayeredEntry_NoRows_NotModified()
    {
        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void LoadFromLayered_HydratesAllThreeKinds()
    {
        JsonArray arr =
        [
            new JsonObject { ["serverName"] = "alpha-mcp" },
            new JsonObject
            {
                ["serverCommand"] = new JsonArray { "node", "/path/to/server.js" },
            },

            new JsonObject { ["serverUrl"] = "https://*.example.com/*" },

        ];

        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("allowedMcpServers", ConfigScope.User, arr), ConfigScope.User);

        Assert.AreEqual(3, vm.Items.Count);

        McpServerListEntryViewModel byName = vm.Items.Single(i => i.Kind == McpServerMatchKind.ByName);
        Assert.AreEqual("alpha-mcp", byName.Text);

        McpServerListEntryViewModel byCommand = vm.Items.Single(i => i.Kind == McpServerMatchKind.ByCommand);
        Assert.AreEqual("node\n/path/to/server.js", byCommand.Text);

        McpServerListEntryViewModel byUrl = vm.Items.Single(i => i.Kind == McpServerMatchKind.ByUrl);
        Assert.AreEqual("https://*.example.com/*", byUrl.Text);
    }

    [TestMethod]
    public void ToJsonValue_RebuildsArrayWithCorrectDiscriminators()
    {
        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewKind = McpServerMatchKind.ByName;
        vm.NewText = "alpha-mcp";
        vm.AddEntryCommand.Execute(null);

        vm.NewKind = McpServerMatchKind.ByCommand;
        vm.NewText = "node\n/srv/a.js";
        vm.AddEntryCommand.Execute(null);

        vm.NewKind = McpServerMatchKind.ByUrl;
        vm.NewText = "https://x.example/*";
        vm.AddEntryCommand.Execute(null);

        JsonArray written = (JsonArray)vm.ToJsonValue()!;
        Assert.AreEqual(3, written.Count);
        Assert.AreEqual("alpha-mcp", ((JsonObject)written[0]!)["serverName"]?.GetValue<string>());
        JsonArray cmdArr = (JsonArray)((JsonObject)written[1]!)["serverCommand"]!;
        Assert.AreEqual("node", cmdArr[0]?.GetValue<string>());
        Assert.AreEqual("/srv/a.js", cmdArr[1]?.GetValue<string>());
        Assert.AreEqual("https://x.example/*", ((JsonObject)written[2]!)["serverUrl"]?.GetValue<string>());
    }

    [TestMethod]
    public void EmptyItems_ReturnsNull_PreservingUndefinedSemantics()
    {
        // Per the schema description: undefined = no restriction (anything
        // allowed) vs `[]` = lockdown (nothing allowed). Returning null
        // routes through RemoveValue → undefined, which is the right default
        // for "no restrictions configured via this editor".
        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void BareStringScope_HydratesEmpty_NoCrash()
    {
        // Pre-existing bad data from the old StringArray fallback could have
        // written e.g. ["alpha"] with a string element; the typed editor must
        // ignore those and start with an empty row list.
        McpServerListEditorViewModel vm = NewVm();
        JsonArray arr = [JsonValue.Create("alpha")];
        LayeredValue lv = new("allowedMcpServers",
            [new ScopeEntry(ConfigScope.User, arr.DeepClone(), "/fake")])
        {
            EffectiveValue = arr.DeepClone(),
            EffectiveScope = ConfigScope.User,
        };

        vm.LoadFromLayered(lv, ConfigScope.User);

        // Element is a string, not an object — skipped during hydration.
        Assert.AreEqual(0, vm.Items.Count);
    }

    [TestMethod]
    public void ItemWithMultipleDiscriminators_Skipped()
    {
        // Schema requires exactly one of {serverName, serverCommand, serverUrl}.
        // A row that declares two would fail anyOf — the editor refuses to
        // surface a half-corrupt row the user can't fix without raw JSON.
        JsonArray arr =
        [
            new JsonObject
            {
                ["serverName"] = "alpha",
                ["serverUrl"] = "https://x",
            },

        ];
        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("allowedMcpServers", ConfigScope.User, arr), ConfigScope.User);
        Assert.AreEqual(0, vm.Items.Count);
    }

    [TestMethod]
    public void Add_DisabledWhenTextBlank()
    {
        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        Assert.IsFalse(vm.AddEntryCommand.CanExecute(null));
        vm.NewText = "  ";
        Assert.IsFalse(vm.AddEntryCommand.CanExecute(null));
        vm.NewText = "alpha";
        Assert.IsTrue(vm.AddEntryCommand.CanExecute(null));
    }

    [TestMethod]
    public void BlankRow_SkippedOnSave()
    {
        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewText = "alpha";
        vm.AddEntryCommand.Execute(null);

        // Blank out the row's payload directly to simulate an in-progress edit.
        vm.Items[0].Text = string.Empty;

        // Save must not emit `{ "serverName": "" }` — the editor returns null
        // (RemoveValue) when no row has a non-empty payload.
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void ChangingKind_FlagsModified()
    {
        McpServerListEditorViewModel vm = NewVm();
        JsonArray arr = [new JsonObject { ["serverName"] = "alpha" }];
        vm.LoadFromLayered(WithArray("allowedMcpServers", ConfigScope.User, arr), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServerListEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.Items[0].Kind = McpServerMatchKind.ByUrl;

        Assert.IsTrue(fired > 0);
    }

    [TestMethod]
    public void Remove_ShrinksItemsAndFlagsModified()
    {
        McpServerListEditorViewModel vm = NewVm();
        JsonArray arr =
        [
            new JsonObject { ["serverName"] = "a" },
            new JsonObject { ["serverName"] = "b" },
        ];
        vm.LoadFromLayered(WithArray("allowedMcpServers", ConfigScope.User, arr), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(McpServerListEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        vm.RemoveEntryCommand.Execute(vm.Items[0]);

        Assert.AreEqual(1, vm.Items.Count);
        Assert.IsTrue(fired > 0);
    }

    [TestMethod]
    public void ResetCommand_AfterLoad_RestoresOnDiskRows_NotClearsThem()
    {
        // Reset semantic consistency.  Prior shape called
        // Items.Clear() unconditionally, wiping every saved entry the user
        // had on disk when they hit Reset after editing.  The fix mirrors
        // the top-level compound editors' restore-on-reset pattern:
        // LoadFromLayered(_lastLayered, _lastScope) restores the at-load
        // entries; transient inputs (NewText / NewKind) ARE cleared (the
        // user explicitly chose to discard their unfinished input by
        // clicking Reset).
        McpServerListEditorViewModel vm = NewVm();
        JsonArray arr = [new JsonObject { ["serverName"] = "alpha" }];
        vm.LoadFromLayered(WithArray("allowedMcpServers", ConfigScope.User, arr), ConfigScope.User);
        Assert.AreEqual(1, vm.Items.Count, "precondition: load populated 1 row");

        // User edits: type into the new-row inputs + add a second row.
        vm.NewKind = McpServerMatchKind.ByUrl;
        vm.NewText = "https://x";
        vm.AddEntryCommand.Execute(null);
        Assert.AreEqual(2, vm.Items.Count);

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Items.Count,
            "Reset must restore the original on-disk row (1), not wipe to empty.");
        Assert.AreEqual(McpServerMatchKind.ByName, vm.Items[0].Kind);
        Assert.AreEqual("alpha", vm.Items[0].Text);

        Assert.AreEqual(string.Empty, vm.NewText, "Reset must clear the transient new-row input.");
        Assert.AreEqual(McpServerMatchKind.ByName, vm.NewKind, "Reset must clear the transient new-row kind.");
    }

    [TestMethod]
    public void ResetCommand_WithoutPriorLoad_FallsBackToClear()
    {
        // Edge case: Reset is called on a freshly-constructed VM where
        // LoadFromLayered was never invoked.  _lastLayered is null, so the
        // fallback else-branch runs: Items cleared, transient inputs cleared,
        // IsModified=false.
        McpServerListEditorViewModel vm = NewVm();

        // Manually set IsModified so the command is enabled.
        vm.IsModified = true;
        vm.NewKind = McpServerMatchKind.ByUrl;
        vm.NewText = "https://staging";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.AreEqual(string.Empty, vm.NewText);
        Assert.AreEqual(McpServerMatchKind.ByName, vm.NewKind);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void Command_TrimsAndDropsBlankLines()
    {
        // Trailing newlines + whitespace shouldn't produce phantom "" array elements.
        McpServerListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewKind = McpServerMatchKind.ByCommand;
        vm.NewText = "  node \n\n /srv/a.js \n";
        vm.AddEntryCommand.Execute(null);

        JsonArray written = (JsonArray)vm.ToJsonValue()!;
        JsonArray cmdArr = (JsonArray)((JsonObject)written[0]!)["serverCommand"]!;
        Assert.AreEqual(2, cmdArr.Count);
        Assert.AreEqual("node", cmdArr[0]?.GetValue<string>());
        Assert.AreEqual("/srv/a.js", cmdArr[1]?.GetValue<string>());
    }
}