namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Locks the marketplace allow/block list editor's contract:
/// - hydrates rows from a JsonArray of source-discriminated objects
/// - rebuilds the schema-correct {source, &lt;primary&gt;: …} JsonObject on save
/// - preserves opaque per-variant fields (ref / path / headers) across round-trip
/// - tolerates pre-existing bad data (bare strings, missing source)
/// - skips blank rows on save
/// - returns null when Items is empty (preserves the schema's
///   "undefined = no restriction" semantic — never emits `[]` by accident)
/// </summary>
[TestClass]
public class MarketplaceListEditorViewModelTests
{
    private static SchemaNode ArraySchema(string name = "strictKnownMarketplaces")
    {
        return new SchemaNode(name, name) { ValueType = SchemaValueType.Array };
    }

    private static LayeredValue Empty(string key = "strictKnownMarketplaces")
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

    private static MarketplaceListEditorViewModel NewVm()
    {
        return new MarketplaceListEditorViewModel(ArraySchema(), ConfigScope.User);
    }

    // -----------------------------------------------------------------------

    [TestMethod]
    public void Initial_NoLayeredEntry_NoRows_NotModified()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        Assert.AreEqual(0, vm.Items.Count);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void LoadFromLayered_HydratesRowsFromAllSources()
    {
        JsonArray arr =
        [
            new JsonObject { ["source"] = "github", ["repo"] = "acme/plugins" },
            new JsonObject { ["source"] = "git", ["url"] = "https://x/r.git" },
            new JsonObject { ["source"] = "npm", ["package"] = "@acme/plugins" },
            new JsonObject { ["source"] = "url", ["url"] = "https://x/m.json" },
            new JsonObject { ["source"] = "file", ["path"] = "/srv/m.json" },
            new JsonObject { ["source"] = "directory", ["path"] = "/srv/m" },
            new JsonObject { ["source"] = "hostPattern", ["hostPattern"] = "^github\\.com$" },
            new JsonObject { ["source"] = "pathPattern", ["pathPattern"] = "^/srv/.*" },
        ];

        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        Assert.AreEqual(8, vm.Items.Count);
        Assert.AreEqual("acme/plugins", vm.Items.Single(i => i.Source == "github").PrimaryValue);
        Assert.AreEqual("https://x/r.git", vm.Items.Single(i => i.Source == "git").PrimaryValue);
        Assert.AreEqual("@acme/plugins", vm.Items.Single(i => i.Source == "npm").PrimaryValue);
        Assert.AreEqual("/srv/m.json", vm.Items.Single(i => i.Source == "file").PrimaryValue);
        Assert.AreEqual("/srv/m", vm.Items.Single(i => i.Source == "directory").PrimaryValue);
        Assert.AreEqual("^github\\.com$", vm.Items.Single(i => i.Source == "hostPattern").PrimaryValue);
        Assert.AreEqual("^/srv/.*", vm.Items.Single(i => i.Source == "pathPattern").PrimaryValue);
    }

    [TestMethod]
    public void ToJsonValue_RebuildsSchemaCorrectObjects()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);

        vm.NewSource = "github";
        vm.NewPrimaryValue = "acme/plugins";
        vm.AddEntryCommand.Execute(null);

        vm.NewSource = "npm";
        vm.NewPrimaryValue = "@acme/plugins";
        vm.AddEntryCommand.Execute(null);

        JsonArray written = (JsonArray)vm.ToJsonValue()!;
        Assert.AreEqual(2, written.Count);

        JsonObject github = (JsonObject)written[0]!;
        Assert.AreEqual("github", github["source"]?.GetValue<string>());
        Assert.AreEqual("acme/plugins", github["repo"]?.GetValue<string>());

        JsonObject npm = (JsonObject)written[1]!;
        Assert.AreEqual("npm", npm["source"]?.GetValue<string>());
        Assert.AreEqual("@acme/plugins", npm["package"]?.GetValue<string>());
    }

    [TestMethod]
    public void OpaqueExtraFields_PreservedAcrossRoundTrip()
    {
        // A managed-policy admin's hand-curated entry may include optional
        // fields the GUI doesn't surface (ref/path on github; headers on
        // url). The editor must preserve them on save.
        JsonArray arr =
        [
            new JsonObject
            {
                ["source"] = "github",
                ["repo"] = "acme/plugins",
                ["ref"] = "v2.0",
                ["path"] = "marketplaces/main",
            },

            new JsonObject
            {
                ["source"] = "url",
                ["url"] = "https://x/m.json",
                ["headers"] = new JsonObject
                {
                    ["Authorization"] = "Bearer xyz",
                },
            },

        ];

        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        JsonArray written = (JsonArray)vm.ToJsonValue()!;
        JsonObject gh = (JsonObject)written[0]!;
        Assert.AreEqual("v2.0", gh["ref"]?.GetValue<string>());
        Assert.AreEqual("marketplaces/main", gh["path"]?.GetValue<string>());

        JsonObject urlEntry = (JsonObject)written[1]!;
        JsonObject headers = (JsonObject)urlEntry["headers"]!;
        Assert.AreEqual("Bearer xyz", headers["Authorization"]?.GetValue<string>());
    }

    [TestMethod]
    public void EmptyItems_ReturnsNull_PreservingUndefinedSemantics()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void BareStringScope_HydratesEmpty_NoCrash()
    {
        JsonArray arr = [JsonValue.Create("bare-string-from-old-fallback")];
        MarketplaceListEditorViewModel vm = NewVm();
        LayeredValue lv = new("strictKnownMarketplaces",
            [new ScopeEntry(ConfigScope.User, arr.DeepClone(), "/fake")])
        {
            EffectiveValue = arr.DeepClone(),
            EffectiveScope = ConfigScope.User,
        };
        vm.LoadFromLayered(lv, ConfigScope.User);
        Assert.AreEqual(0, vm.Items.Count);
    }

    [TestMethod]
    public void ItemMissingSourceDiscriminator_Skipped()
    {
        JsonArray arr = [new JsonObject { ["repo"] = "acme/plugins" }];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);
        Assert.AreEqual(0, vm.Items.Count);
    }

    [TestMethod]
    public void ItemWithUnknownSource_Skipped()
    {
        // We don't surface a row for a source we don't know how to render —
        // the user re-adds it through a known variant.
        JsonArray arr =
        [
            new JsonObject { ["source"] = "future-source-kind", ["something"] = "x" },
        ];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);
        Assert.AreEqual(0, vm.Items.Count);
    }

    [TestMethod]
    public void Add_DisabledWhenPrimaryValueBlank()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        Assert.IsFalse(vm.AddEntryCommand.CanExecute(null));
        vm.NewPrimaryValue = "  ";
        Assert.IsFalse(vm.AddEntryCommand.CanExecute(null));
        vm.NewPrimaryValue = "acme/plugins";
        Assert.IsTrue(vm.AddEntryCommand.CanExecute(null));
    }

    [TestMethod]
    public void BlankRow_SkippedOnSave()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "github";
        vm.NewPrimaryValue = "acme/plugins";
        vm.AddEntryCommand.Execute(null);

        vm.Items[0].PrimaryValue = string.Empty;

        Assert.IsNull(vm.ToJsonValue());
    }

    [TestMethod]
    public void Source_Change_FlagsModified()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        JsonArray arr = [new JsonObject { ["source"] = "github", ["repo"] = "a/b" }];
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarketplaceListEditorViewModel.IsModified))
            {
                fired++;
            }
        };
        vm.Items[0].Source = "git";
        Assert.IsTrue(fired > 0);
    }

    [TestMethod]
    public void Remove_ShrinksItemsAndFlagsModified()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        JsonArray arr =
        [
            new JsonObject { ["source"] = "github", ["repo"] = "a/b" },
            new JsonObject { ["source"] = "github", ["repo"] = "c/d" },
        ];
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarketplaceListEditorViewModel.IsModified))
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
        // Reset semantic consistency.  See
        // McpServerListEditorViewModelTests.ResetCommand_AfterLoad_RestoresOnDiskRows_NotClearsThem
        // for the rationale and pattern.
        MarketplaceListEditorViewModel vm = NewVm();
        JsonArray arr = [new JsonObject { ["source"] = "github", ["repo"] = "a/b" }];
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);
        Assert.AreEqual(1, vm.Items.Count, "precondition: load populated 1 row");

        // User edits transient inputs.
        vm.NewSource = "npm";
        vm.NewPrimaryValue = "@acme/plugins";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(1, vm.Items.Count,
            "Reset must restore the original on-disk row, not wipe to empty.");
        Assert.AreEqual(string.Empty, vm.NewPrimaryValue, "Reset clears transient input.");
        Assert.AreEqual("github", vm.NewSource, "Reset clears transient input.");
    }

    [TestMethod]
    public void ResetCommand_WithoutPriorLoad_FallsBackToClear()
    {
        // Edge case: Reset before LoadFromLayered runs.
        MarketplaceListEditorViewModel vm = NewVm();
        vm.IsModified = true;
        vm.NewSource = "npm";
        vm.NewPrimaryValue = "@x/y";

        vm.ResetToInheritedCommand.Execute(null);

        Assert.AreEqual(0, vm.Items.Count);
        Assert.AreEqual(string.Empty, vm.NewPrimaryValue);
        Assert.AreEqual("github", vm.NewSource);
        Assert.IsFalse(vm.IsModified);
        Assert.IsNull(vm.ToJsonValue());
    }

    // -----------------------------------------------------------------------
    // Surfaced Advanced sub-fields (ref / path) on github / git variants.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RefAndPath_HydratedIntoDedicatedProperties_NotExtraFields()
    {
        // Pre-2026-05-05 behaviour: ref/path were preserved opaquely via
        // ExtraFields. After surfacing them as dedicated properties the
        // round-trip is identical, but the user can now see / edit them.
        JsonArray arr =
        [
            new JsonObject
            {
                ["source"] = "github",
                ["repo"] = "acme/plugins",
                ["ref"] = "v2.0",
                ["path"] = "marketplaces/main",
            },

        ];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        Assert.AreEqual("v2.0", entry.Ref);
        Assert.AreEqual("marketplaces/main", entry.Path);
        Assert.IsTrue(entry.ShowGitFields);
    }

    [TestMethod]
    public void Edit_RefAndPath_RoundTripsToOnDiskShape()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "github";
        vm.NewPrimaryValue = "acme/plugins";
        vm.AddEntryCommand.Execute(null);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        entry.Ref = "main";
        entry.Path = "subdir";

        JsonArray arr = (JsonArray)vm.ToJsonValue()!;
        JsonObject obj = (JsonObject)arr[0]!;
        Assert.AreEqual("main", obj["ref"]?.GetValue<string>());
        Assert.AreEqual("subdir", obj["path"]?.GetValue<string>());
    }

    [TestMethod]
    public void EmptyRefOrPath_OmittedFromOnDiskShape()
    {
        // User clearing the field should remove it from the saved shape —
        // the schema treats `ref` / `path` as optional, and an empty string
        // here is "unset", not "set to empty".
        JsonArray arr =
        [
            new JsonObject
            {
                ["source"] = "github",
                ["repo"] = "acme/plugins",
                ["ref"] = "v2.0",
            },

        ];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        // Simulate the user clearing the ref field.
        vm.Items.Single().Ref = string.Empty;

        JsonArray written = (JsonArray)vm.ToJsonValue()!;
        JsonObject obj = (JsonObject)written[0]!;
        Assert.IsFalse(obj.ContainsKey("ref"),
            "Empty ref must be omitted from the on-disk shape, not written as an empty string.");
    }

    [TestMethod]
    public void RefAndPath_IgnoredOnSave_WhenSourceDoesNotAcceptThem()
    {
        // Set Ref/Path while Source is github (visible). Then switch
        // Source to npm (which has no ref/path field in the schema).
        // ToVariantObject must omit the GUI-tracked ref/path because
        // ShowGitFields is now false — the npm variant must round-trip
        // as `{source: npm, package: ...}` with no extra keys.
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "github";
        vm.NewPrimaryValue = "acme/plugins";
        vm.AddEntryCommand.Execute(null);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        entry.Ref = "v2.0";
        entry.Path = "subdir";
        // User pivots the variant.
        entry.Source = "npm";
        entry.PrimaryValue = "@scope/pkg";

        JsonArray written = (JsonArray)vm.ToJsonValue()!;
        JsonObject obj = (JsonObject)written[0]!;
        Assert.AreEqual("npm", obj["source"]?.GetValue<string>());
        Assert.AreEqual("@scope/pkg", obj["package"]?.GetValue<string>());
        Assert.IsFalse(obj.ContainsKey("ref"),
            "ref must not be emitted on npm variant — it's not in the schema for npm.");
        Assert.IsFalse(obj.ContainsKey("path"),
            "path must not be emitted on npm variant — it's not in the schema for npm.");
    }

    [TestMethod]
    public void RefAndPath_FlagModified_WhenEdited()
    {
        JsonArray arr = [new JsonObject { ["source"] = "github", ["repo"] = "a/b" }];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarketplaceListEditorViewModel.IsModified))
            {
                fired++;
            }
        };
        vm.Items.Single().Ref = "main";
        Assert.IsTrue(fired > 0, "Editing Ref must re-fire IsModified for the live-write loop.");
    }

    // -----------------------------------------------------------------------
    // Surfaced HTTP headers sub-editor on the url variant.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Headers_OnUrlVariant_HydratedIntoDedicatedCollection()
    {
        // ref/path surfacing: `headers` was
        // previously preserved opaquely via ExtraFields. Now hydrated
        // into a dedicated Headers ObservableCollection so the user can
        // edit the entries.
        JsonArray arr =
        [
            new JsonObject
            {
                ["source"] = "url",
                ["url"] = "https://x/m.json",
                ["headers"] = new JsonObject
                {
                    ["Authorization"] = "Bearer xyz",
                    ["X-Trace-Id"] = "abc",
                },
            },

        ];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        Assert.IsTrue(entry.ShowHeadersField);
        Assert.AreEqual(2, entry.Headers.Count);
        StringMapEntryViewModel auth = entry.Headers.Single(h => h.Key == "Authorization");
        Assert.AreEqual("Bearer xyz", auth.Value);
    }

    [TestMethod]
    public void Headers_RoundTripFromOnDiskShape_IsLossless()
    {
        // A managed-policy admin's hand-curated entry: the on-disk shape
        // must survive an editor round-trip even when the user makes no
        // changes. Lock that headers don't mutate during hydrate→save.
        JsonArray arr =
        [
            new JsonObject
            {
                ["source"] = "url",
                ["url"] = "https://x/m.json",
                ["headers"] = new JsonObject { ["Authorization"] = "Bearer xyz" },
            },

        ];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        JsonArray written = (JsonArray)vm.ToJsonValue()!;
        JsonObject obj = (JsonObject)written[0]!;
        JsonObject headers = (JsonObject)obj["headers"]!;
        Assert.AreEqual("Bearer xyz", headers["Authorization"]?.GetValue<string>());
    }

    [TestMethod]
    public void AddHeader_AppendsRow_AndFiresModifiedOnParent()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "url";
        vm.NewPrimaryValue = "https://x/m.json";
        vm.AddEntryCommand.Execute(null);

        MarketplaceListEntryViewModel entry = vm.Items.Single();

        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarketplaceListEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        entry.NewHeaderName = "Authorization";
        entry.NewHeaderValue = "Bearer xyz";
        entry.AddHeaderCommand.Execute(null);

        Assert.AreEqual(1, entry.Headers.Count);
        Assert.AreEqual(string.Empty, entry.NewHeaderName);
        Assert.AreEqual(string.Empty, entry.NewHeaderValue);
        Assert.IsTrue(fired > 0,
            "Adding a header must propagate IsModified through the entry's "
            + "Headers PropertyChanged forwarding.");
    }

    [TestMethod]
    public void EditHeaderRow_FiresModifiedOnParent()
    {
        JsonArray arr =
        [
            new JsonObject
            {
                ["source"] = "url",
                ["url"] = "https://x/m.json",
                ["headers"] = new JsonObject { ["Authorization"] = "old" },
            },

        ];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        int fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarketplaceListEditorViewModel.IsModified))
            {
                fired++;
            }
        };

        entry.Headers[0].Value = "new";
        Assert.IsTrue(fired > 0, "Editing a header row's Value must re-fire IsModified.");

        JsonObject obj = (JsonObject)((JsonArray)vm.ToJsonValue()!)[0]!;
        Assert.AreEqual("new", ((JsonObject)obj["headers"]!)["Authorization"]?.GetValue<string>());
    }

    [TestMethod]
    public void RemoveHeader_ShrinksAndUpdatesOnDiskShape()
    {
        JsonArray arr =
        [
            new JsonObject
            {
                ["source"] = "url",
                ["url"] = "https://x/m.json",
                ["headers"] = new JsonObject
                {
                    ["A"] = "1",
                    ["B"] = "2",
                },
            },

        ];
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(WithArray("strictKnownMarketplaces", ConfigScope.User, arr), ConfigScope.User);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        StringMapEntryViewModel b = entry.Headers.Single(h => h.Key == "B");
        entry.RemoveHeaderCommand.Execute(b);

        JsonObject obj = (JsonObject)((JsonArray)vm.ToJsonValue()!)[0]!;
        JsonObject headers = (JsonObject)obj["headers"]!;
        Assert.AreEqual(1, headers.Count);
        Assert.IsTrue(headers.ContainsKey("A"));
        Assert.IsFalse(headers.ContainsKey("B"));
    }

    [TestMethod]
    public void EmptyHeadersMap_OmittedFromOnDiskShape()
    {
        // User adds a url entry, no headers — on-disk shape must NOT
        // include `headers: {}` (schema lets the field be absent).
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "url";
        vm.NewPrimaryValue = "https://x/m.json";
        vm.AddEntryCommand.Execute(null);

        JsonObject obj = (JsonObject)((JsonArray)vm.ToJsonValue()!)[0]!;
        Assert.IsFalse(obj.ContainsKey("headers"),
            "Empty headers must be omitted from the on-disk shape, not "
            + "written as an empty `headers: {}`.");
    }

    [TestMethod]
    public void Headers_IgnoredOnSave_WhenSourceIsNotUrl()
    {
        // Set Headers while Source is url (visible). Then switch to git.
        // ToVariantObject must omit headers because ShowHeadersField is
        // now false — git source has no headers in the schema.
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "url";
        vm.NewPrimaryValue = "https://x/m.json";
        vm.AddEntryCommand.Execute(null);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        entry.NewHeaderName = "Authorization";
        entry.NewHeaderValue = "Bearer xyz";
        entry.AddHeaderCommand.Execute(null);

        // User pivots variant.
        entry.Source = "git";
        entry.PrimaryValue = "https://x/repo.git";

        JsonObject obj = (JsonObject)((JsonArray)vm.ToJsonValue()!)[0]!;
        Assert.AreEqual("git", obj["source"]?.GetValue<string>());
        Assert.AreEqual("https://x/repo.git", obj["url"]?.GetValue<string>());
        Assert.IsFalse(obj.ContainsKey("headers"),
            "headers must not be emitted on git variant — the schema "
            + "doesn't define headers there.");
    }

    [TestMethod]
    public void AddHeader_DuplicateName_OverwritesValue()
    {
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "url";
        vm.NewPrimaryValue = "https://x/m.json";
        vm.AddEntryCommand.Execute(null);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        entry.NewHeaderName = "Authorization";
        entry.NewHeaderValue = "first";
        entry.AddHeaderCommand.Execute(null);

        entry.NewHeaderName = "Authorization";
        entry.NewHeaderValue = "second";
        entry.AddHeaderCommand.Execute(null);

        Assert.AreEqual(1, entry.Headers.Count);
        Assert.AreEqual("second", entry.Headers[0].Value);
    }

    [TestMethod]
    public void BlankHeaderName_SkippedOnSave()
    {
        // Direct mutation simulates a row whose name was blanked after
        // add — ToVariantObject must drop it rather than emit `"":""`.
        MarketplaceListEditorViewModel vm = NewVm();
        vm.LoadFromLayered(Empty(), ConfigScope.User);
        vm.NewSource = "url";
        vm.NewPrimaryValue = "https://x/m.json";
        vm.AddEntryCommand.Execute(null);

        MarketplaceListEntryViewModel entry = vm.Items.Single();
        entry.NewHeaderName = "Authorization";
        entry.NewHeaderValue = "Bearer xyz";
        entry.AddHeaderCommand.Execute(null);
        entry.Headers[0].Key = string.Empty;

        JsonObject obj = (JsonObject)((JsonArray)vm.ToJsonValue()!)[0]!;
        Assert.IsFalse(obj.ContainsKey("headers"),
            "All-blank-name headers must reduce to an empty map, which "
            + "is then omitted from the on-disk shape.");
    }
}