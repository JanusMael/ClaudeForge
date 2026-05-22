# AGENTS.md — compound-editor contract

> Sidecar to the root [`AGENTS.md`](../../../../AGENTS.md). Scope: every editor
> in this directory that manages a compound JSON shape (object, array, or
> nested combination) — currently MCP servers, Hooks, Permissions, Enabled
> Plugins, Marketplaces, plus `JsonRaw`, `StringMap`, `McpServerList`,
> `MarketplaceList`. Generic leaf editors (Boolean, Number, String, Path,
> Enum, StringArray) live in `src/LayeredEditors.Avalonia/ViewModels/` and
> are NOT subject to this contract — they migrated to the library base. The
> App-bridge `PropertyEditorViewModel` is the base for compound and
> Claude-specific shape editors only.

Every paragraph below either teaches a contract or shows a paste-ready code
template. No narrative.

---

## 1. The force-fire `MarkModified()` pattern

Why this exists: `[ObservableProperty]`'s generated setter elides equal
assignments. After `LoadFromLayered` sets `IsModified = true` for an
already-populated scope, a bare `IsModified = true;` on the next user mutation
is a NO-OP. The live-write subscription on
`SettingsGroupEditorViewModel.OnEditorPropertyChanged` (which performs the
actual disk write) and the Save-button-enable subscription on
`MainWindowViewModel.OnAnyWorkspaceChanged` both watch
`PropertyChanged(IsModified)`. If the event doesn't fire, neither runs.

Canonical paste-ready template:

```csharp
/// <summary>
/// Force-fire PropertyChanged(IsModified) on every user mutation, even when
/// the underlying flag was already true from the prior load. See
/// McpServersEditorViewModel.MarkModified for the full bug rationale.
/// </summary>
private void MarkModified()
{
    if (_isLoading) return;
    if (IsModified)
        OnPropertyChanged(nameof(IsModified));
    else
        IsModified = true;
}
```

Live copies: `McpServersEditorViewModel.MarkModified`, `HooksEditorViewModel.MarkModified`,
`PermissionsEditorViewModel.MarkModified`, `EnabledPluginsEditorViewModel.MarkModified`,
`MarketplacesEditorViewModel.MarkModified`. They are byte-identical except for the
`if (_isLoading) return;` line, which is conditional — see §2.

---

## 2. The `_isLoading` guard — when do you need it?

You need a `private bool _isLoading;` guard around `LoadFromLayered`'s body
(and an early-return in `MarkModified`) IF AND ONLY IF subscriptions you have
already wired up will fire while `LoadFromLayered` mutates collections.

| Editor                             | Has `_isLoading`? | Reason                                                                                                                                                                                                               |
|------------------------------------|-------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `McpServersEditorViewModel`        | Yes               | `Servers.CollectionChanged` is wired in the constructor; `LoadFromLayered` calls `Servers.Clear()` and `Servers.Add(...)`, both of which fire the handler.                                                           |
| `PermissionsEditorViewModel`       | Yes               | `AllowList/DenyList/AskList.CollectionChanged` wired in the constructor; load mutates all three.                                                                                                                     |
| `EnabledPluginsEditorViewModel`    | Yes               | `Plugins.CollectionChanged` wired in the constructor.                                                                                                                                                                |
| `MarketplacesEditorViewModel`      | Yes               | `Marketplaces.CollectionChanged` wired in the constructor.                                                                                                                                                           |
| `StringMapPropertyEditorViewModel` | Yes               | `Items.CollectionChanged` wired in the constructor. Also re-uses the guard inside `OnResetToInherited` to silence the rebuild's `Items.Clear()` so the just-cleared `IsModified=false` from the base class survives. |
| `McpServerListEditorViewModel`     | Yes               | Same as StringMap: `Items.CollectionChanged` wired in ctor; reset path re-uses the guard.                                                                                                                            |
| `MarketplaceListEditorViewModel`   | Yes               | Same as StringMap.                                                                                                                                                                                                   |
| `JsonRawPropertyEditorViewModel`   | Yes               | No collection — but `LoadFromLayered` assigns `Text` and the partial `OnTextChanged` handler would otherwise fire `MarkModified`. Guard suppresses the bulk-load assignment.                                         |
| `HooksEditorViewModel`             | **NO**            | Subscriptions are wired at the END of `LoadFromLayered`, AFTER `IsModified` is set. No spurious calls during load. Documented in the editor's own `MarkModified` xmldoc.                                             |

If you write a sixth editor that subscribes in the constructor → use the
guard. If you defer subscription until after the load body → you can omit it,
but document why explicitly (Hooks does, see its `MarkModified` xmldoc).

---

## 3. `OnResetToInherited` contract

Sequence in the base class (`src/LayeredEditors.Avalonia/ViewModels/PropertyEditorViewModel.cs`):

```csharp
protected virtual void ResetToInherited()
{
    IsModified = false;        // base class clears the flag FIRST
    OnResetToInherited();      // your override runs SECOND
}
```

Implication: your override should NOT set `IsModified = false` again — it's
already cleared. Override semantics:

- **Cache `_lastLayered` and `_lastScope` in `LoadFromLayered`**, then re-call
  `LoadFromLayered(_lastLayered, _lastScope)` in `OnResetToInherited` so reset
  restores the on-disk state instead of clearing in-memory.
- Live reference: `McpServersEditorViewModel.OnResetToInherited`,
  `HooksEditorViewModel.OnResetToInherited`, `PermissionsEditorViewModel.OnResetToInherited`.
- The `IsModified=false` write inside `LoadFromLayered` (when the scope is
  empty) and the base class's pre-call `IsModified = false` are mutually
  consistent for the empty case.

---

## 4. Child-subscription pattern

Compound editors with editable child rows (MCP servers' Args/Env/Headers,
Hook entries' Matcher/CommandValue, Permissions' rules, Marketplaces'
Name/SourceType/SourceValue) must hook `PropertyChanged` on every child so
inline edits trigger `MarkModified`.

Two-level template (outer collection + per-item subscription):

```csharp
public MyEditorViewModel(...)
{
    Items = [];
    Items.CollectionChanged += OnItemsChanged;
}

private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    if (e.NewItems != null)
        foreach (MyItem item in e.NewItems)
            item.PropertyChanged += OnItemChanged;
    if (e.OldItems != null)
        foreach (MyItem item in e.OldItems)
            item.PropertyChanged -= OnItemChanged;

    MarkModified();
}

private void OnItemChanged(object? sender, PropertyChangedEventArgs e) =>
    MarkModified();
```

**Trap (the one that bit MCP)**: `LoadFromLayered` populates the collection
via `Items.Add(...)`. Each Add fires `OnItemsChanged` with `NewItems` set, so
NEW items get hooked correctly — but if your editor exposes nested collections
on each item (e.g. `McpServerEntry.Args/Env/Headers`), you must also hook
those during the Add. The pattern in `McpServersEditorViewModel.SubscribeEntry`
is the canonical fix:

```csharp
private void SubscribeEntry(McpServerEntry entry)
{
    entry.PropertyChanged += OnEntryPropertyChanged;

    // Subscribe NESTED collections.
    entry.Args.CollectionChanged    += OnNestedCollectionChanged;
    entry.Env.CollectionChanged     += OnNestedCollectionChanged;
    entry.Headers.CollectionChanged += OnNestedCollectionChanged;

    // Subscribe items already PRESENT in the nested collections at hook time
    // (LoadFromLayered populates Args/Env/Headers BEFORE the entry is added).
    foreach (var arg in entry.Args)    arg.PropertyChanged += OnNestedItemChanged;
    foreach (var ev  in entry.Env)     ev.PropertyChanged  += OnNestedItemChanged;
    foreach (var hdr in entry.Headers) hdr.PropertyChanged += OnNestedItemChanged;
}
```

Mirror in `UnsubscribeEntry`. Symmetry matters: every Subscribe
must be matched by an Unsubscribe in the corresponding `OldItems` branch, or
reload accumulates handlers and `MarkModified` fires N times per mutation.

---

## 5. Transient input field filter

Some `[ObservableProperty]` fields exist purely to back input boxes (the
"new entry name" textbox above an Add button). Their `PropertyChanged`
notifications MUST NOT mark the editor modified — otherwise the Save button
flickers on/off per keystroke.

The filter list lives in each editor's `OnEntryPropertyChanged` (or the
equivalent). Existing fields to filter:

| Editor                          | Transient fields                                                                                               |
|---------------------------------|----------------------------------------------------------------------------------------------------------------|
| `McpServersEditorViewModel`     | `NewServerName` (top-level), `McpServerEntry.NewArg`, `McpServerEntry.NewEnvKey`, `McpServerEntry.NewEnvValue` |
| `PermissionsEditorViewModel`    | `NewAllowText`, `NewDenyText`, `NewAskText`                                                                    |
| `EnabledPluginsEditorViewModel` | `NewPluginRef`                                                                                                 |
| `MarketplacesEditorViewModel`   | `NewName`, `NewSourceType`, `NewSourceValue`                                                                   |
| `HooksEditorViewModel`          | `HookEntry.NewHeaderKey`, `HookEntry.NewHeaderValue`, `HookEntry.NewAllowedEnvVar`                             |

Pattern (from `McpServersEditorViewModel.OnEntryPropertyChanged`):

```csharp
private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName is nameof(McpServerEntry.NewArg)
                       or nameof(McpServerEntry.NewEnvKey)
                       or nameof(McpServerEntry.NewEnvValue))
        return;

    MarkModified();
}
```

If you add a new transient field to an existing entry type, you MUST add it to
the filter list of every editor that subscribes to that type.

---

## 6. Five-editor parity table

Spotting a missing column on a sixth editor immediately surfaces a likely bug.

| Editor         | Force-fire `MarkModified` |  `_isLoading` guard       | Caches `_lastLayered/_lastScope` for reset | Subscribes outer `CollectionChanged` | Subscribes child `PropertyChanged`  | Subscribes nested collections  |
|----------------|:-------------------------:|:-------------------------:|:------------------------------------------:|:------------------------------------:|:-----------------------------------:|:------------------------------:|
| McpServers     |             ✓             |             ✓             |                     ✓                      |            ✓ (`Servers`)             |        ✓ (`McpServerEntry`)         |   ✓ (`Args`/`Env`/`Headers`)   |
| Hooks          |             ✓             | — (deferred subs, see §2) |                     ✓                      |    ✓ (per-`HookEventGroup.Hooks`)    | ✓ (`HookEntry` + `HookHeaderEntry`) | ✓ (`Headers`/`AllowedEnvVars`) |
| Permissions    |             ✓             |             ✓             |                     ✓                      |             ✓ (3 lists)              |    ✓ (`PermissionRuleViewModel`)    |              n/a               |
| EnabledPlugins |             ✓             |             ✓             |            — (clears on reset)             |            ✓ (`Plugins`)             |          ✓ (`PluginEntry`)          |              n/a               |
| Marketplaces   |             ✓             |             ✓             |            — (clears on reset)             |          ✓ (`Marketplaces`)          |       ✓ (`MarketplaceEntry`)        |              n/a               |

If the next editor you add has any blank cell in this table that should be
filled, that's a bug. Update the table when you add a sixth row.

---

## 7. `ToJsonValue` / `LoadFromLayered` symmetry

Round-trip contract: for any `JsonNode v`, the sequence
`LoadFromLayered(layered_with(v))` → `ToJsonValue()` must produce a value
structurally equal to `v` (`JsonNode.DeepEquals`). Round-trip tests live
alongside each editor's tests (e.g.
`tests/ClaudeForge.Tests/ViewModels/Editors/HooksRoundTripTests.cs`,
`McpServersRoundTripTests.cs`).

`ToJsonValue` MUST return `null` (not an empty `JsonObject`) when the editor
has no content — that's how the workspace differentiates "remove the key
from this scope" from "write an empty object". Wrong:

```csharp
public override JsonNode? ToJsonValue() => new JsonObject();  // never persists as removal
```

Right (canonical pattern from `McpServersEditorViewModel.ToJsonValue`):

```csharp
public override JsonNode? ToJsonValue()
{
    if (Servers.Count == 0) return null;
    var obj = new JsonObject();
    foreach (var server in Servers) obj[server.Name] = server.ToJson();
    return obj;
}
```

---

## 8. Test-pattern templates

### Edit-after-load

```csharp
[TestMethod]
public void EditingFieldOnLoadedEntry_FiresIsModifiedPropertyChanged()
{
    var vm = new MyEditorViewModel(SchemaRegistry.Empty, ConfigScope.User);
    vm.LoadFromLayered(LayeredWith(ConfigScope.User, populatedJson), ConfigScope.User);
    Assert.IsTrue(vm.IsModified);

    var fired = 0;
    vm.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(MyEditorViewModel.IsModified)) fired++;
    };

    vm.MyCollection[0].SomeField = "new value";

    Assert.IsTrue(fired >= 1,
        "Inline edit on a loaded entry must fire PropertyChanged(IsModified) " +
        "even though the flag was already true.");
}
```

### Delete-after-load

```csharp
[TestMethod]
public void DeletingEntryAfterLoad_FiresIsModifiedPropertyChanged()
{
    var vm = new MyEditorViewModel(SchemaRegistry.Empty, ConfigScope.User);
    vm.LoadFromLayered(LayeredWith(ConfigScope.User, populatedJson), ConfigScope.User);

    var fired = 0;
    vm.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(MyEditorViewModel.IsModified)) fired++;
    };

    vm.MyCollection.RemoveAt(0);

    Assert.IsTrue(fired >= 1);
}
```

### Reset round-trip

```csharp
[TestMethod]
public void ResetAfterEdit_RestoresOnDiskState()
{
    var vm = new MyEditorViewModel(SchemaRegistry.Empty, ConfigScope.User);
    vm.LoadFromLayered(LayeredWith(ConfigScope.User, originalJson), ConfigScope.User);
    var originalCount = vm.MyCollection.Count;

    vm.MyCollection.Add(new MyItem("transient"));
    Assert.AreNotEqual(originalCount, vm.MyCollection.Count);

    vm.ResetToInheritedCommand.Execute(null);

    Assert.AreEqual(originalCount, vm.MyCollection.Count,
        "OnResetToInherited must restore the on-disk state, not clear.");
    Assert.IsFalse(vm.IsModified);
}
```

Live references: the `*AfterLoad_FiresIsModifiedPropertyChanged` tests in
`tests/ClaudeForge.Tests/ViewModels/Editors/McpServersEditorViewModelTests.cs`
for the fired-count templates; `HooksEditorViewModelTests.cs` for the editor with
the deferred-subscription variant.
