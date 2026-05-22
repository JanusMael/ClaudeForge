# Avalonia + .NET 10 Gotchas

Distillation of foot-guns hit while building this app. Future contributors (and AI sessions) repeat these mistakes if they're not warned. Each entry: the symptom, the cause, the fix.

This doc covers Avalonia 12 + .NET 10 + Semi.Avalonia + the supporting packages used by ClaudeForge. Pair with [TRIMMING.md](../TRIMMING.md) (trim-specific concerns) and [LINUX-DESKTOP-INTEGRATION.md](LINUX-DESKTOP-INTEGRATION.md) (Linux platform integration).

---

## XAML / layout

### `Orientation="Horizontal" StackPanel` doesn't constrain children — `TextWrapping="Wrap"` never engages

**Symptom:** Long text overflows the right edge of a horizontal `StackPanel` even with `TextWrapping="Wrap"` set on the inner `TextBlock`.

**Cause:** `StackPanel.MeasureOverride` passes `double.PositiveInfinity` in its stack direction (horizontal in this case). The child receives infinite available width during measure, so wrapping never has a width to wrap against.

**Fix:** Use `DockPanel LastChildFill="True"` instead. Dock the bullet glyph (or icon) `Left`, the wrappable text takes the constrained remaining width.

```xml
<!-- BAD: text overflows -->
<StackPanel Orientation="Horizontal" Spacing="6">
    <TextBlock Text="•" />
    <TextBlock Text="long text..." TextWrapping="Wrap" />
</StackPanel>

<!-- GOOD: text wraps -->
<DockPanel LastChildFill="True">
    <TextBlock DockPanel.Dock="Left" Text="•" Margin="0,0,6,0" />
    <TextBlock Text="long text..." TextWrapping="Wrap" />
</DockPanel>
```

Same caveat applies to `Orientation="Vertical" StackPanel` in the vertical direction — child gets infinite available height. Less commonly a problem because vertical scrolling is the usual escape hatch.

### Tooltips don't propagate from child to parent

**Symptom:** `ToolTip.Tip` set on a parent `Border` works when hovering the empty padding/fill area but NOT when hovering the inner `TextBlock` content.

**Cause:** Avalonia tooltip resolution doesn't walk up the visual tree. The hit-tested control either has a tooltip or it doesn't — child controls don't inherit from ancestors.

**Fix:** Set `ToolTip.Tip` on BOTH the parent and the inner `TextBlock` (or any child the user is likely to hover):

```xml
<Border ToolTip.Tip="{x:Static loc:Strings.TipNew}">
    <TextBlock Text="NEW" ToolTip.Tip="{x:Static loc:Strings.TipNew}" />
</Border>
```

This pattern is documented in `PropertyEditorWrapper.axaml`'s scope-badge with the comment "set on BOTH so the entire coloured chiclet triggers the tooltip on hover."

### XML-comment double-dash (`--`) breaks AXAML

**Symptom:** Build fails with `Avalonia error AVLN1001: An XML comment cannot contain '--'`.

**Cause:** XML 1.0 disallows `--` inside `<!-- ... -->`. Easy to hit when writing comment text that mentions a CLI flag (`--showAllNew`).

**Fix:** Reword to avoid the literal `--`. e.g. "the showAllNew flag" instead of "the `--showAllNew` flag".

### `IsVisible="False"` collapses to zero space inside `StackPanel`

This one is GOOD — it's how the nav-tree icon column hides on sub-items without leaving an indent. Documented here so contributors don't reach for `Visibility=Collapsed` (WPF idiom) which doesn't exist in Avalonia.

```xml
<!-- Hides the icon column AND collapses width to zero -->
<TextBlock Text="{Binding Icon}"
           IsVisible="{Binding IsTopLevel}"
           Width="18" />
```

---

## Bindings / view-model

### Compiled bindings don't reliably re-evaluate manual `OnPropertyChanged` for getter-only properties

**Symptom:** A computed `bool IsXyz => predicate(this);` property with a manual `OnPropertyChanged(nameof(IsXyz))` notification doesn't update bindings on Linux (and sometimes Windows). Works after a workspace reload but not on first edit.

**Cause:** Possibly an Avalonia compiled-binding subscription quirk; reproduced on CachyOS Linux with the `IsDanger` flag on `EssentialsCardViewModel`. The binding sees `INotifyPropertyChanged.PropertyChanged` events but doesn't re-evaluate the getter consistently.

**Fix:** Convert the computed getter to an `[ObservableProperty]`-backed field set imperatively from the value-changed partial methods:

```csharp
// BAD: computed getter + manual notify
public bool IsDanger => _predicate?.Invoke(this) ?? false;

partial void OnBoolValueChanged(bool? value)
{
    OnPropertyChanged(nameof(IsDanger));   // unreliable on Linux
}

// GOOD: ObservableProperty field + Recompute helper
[ObservableProperty] private bool _isDanger;

private void RecomputeIsDanger()
{
    IsDanger = _predicate?.Invoke(this) ?? false;
}

partial void OnBoolValueChanged(bool? value)
{
    RecomputeIsDanger();   // CTK-generated change pipeline; reliable
}
```

Notes: `[ObservableProperty]`'s setter only raises `PropertyChanged` when the value actually changed (equality-checked), so multiple `Recompute` calls with no change are cheap. Don't forget an initial `RecomputeIsDanger()` at the end of the constructor — without it the flag starts stale (always false) until the first user edit.

### `ShowDialogAgain` (and other `bool`-typed `init`/`get;set;` props) need `INotifyPropertyChanged` for two-way bindings

**Symptom:** A `<CheckBox IsChecked="{Binding ShowDialogAgain}" />` binds to a plain `bool ShowDialogAgain { get; set; }` property and the user's toggle isn't reflected back in the source.

**Cause:** TwoWay bindings need `INotifyPropertyChanged` for the source to be notified-aware. Without it, the binding writes back successfully but doesn't re-fetch on subsequent reads.

**Fix:** Either inherit `ObservableObject` and use `[ObservableProperty]`, or implement the interface manually. For one-shot dialogs where the property is read once after the dialog closes, plain `{ get; set; }` is fine — but be aware that compiled binding behaviour differs.

---

## Lifetime / native interop

### `SKSvg` owns the `SKPicture` — disposing one disposes the other

**Symptom:** `0xC0000005` access violation in `SkiaApi.sk_picture_get_cull_rect` on app startup (or any subsequent SKPicture access).

**Cause:** Returning `svg.Picture` out of a method whose `using var svg = SKSvg.CreateFromStream(...)` block has ended. The `SKSvg` is disposed at the end of the using-block, which tears down the native `SKPicture` handle. The next access to the returned `SKPicture` (e.g. `picture.CullRect`) reads through a dangling handle.

**Fix:** Do all renders INSIDE the `using` scope. Return PNG byte arrays (independent of native lifetime) instead of `SKPicture` references.

```csharp
// BAD: SKSvg disposed before caller renders
private static SKPicture? TryLoadPicture()
{
    using var svg = SKSvg.CreateFromStream(stream);
    return svg.Picture;   // ← dangling on return
}

// GOOD: render inside the using scope
private static byte[]? RenderToPng(int targetSize)
{
    using var svg = SKSvg.CreateFromStream(stream);
    if (svg.Picture is not { } picture) return null;
    return RenderPictureToPng(picture, targetSize);   // copies pixel data into byte[]
}
```

Same caveat applies to any wrapper type that owns native resources via `IDisposable`.

### `WindowIcon` size sweet-spot is 64×64 for dialog titlebars

**Symptom:** Titlebar icon looks blurry or aliased when source SVG is rendered at 256×256 and the OS scales down to titlebar size.

**Cause:** OS titlebar icon size is typically 16–32 px. Scaling 256→16 produces aliasing artefacts; scaling 64→16 stays crisp.

**Fix:** Render dialog titlebar icons at 64×64 PNG. For the main window icon (taskbar / dock surfaces), 256×256 is appropriate because those surfaces render larger.

---

## Trim safety (Release publish only)

See [TRIMMING.md](../TRIMMING.md) for the full set. Highlights repeated for discoverability:

### `JsonArray.Add<T>(T)` is `RequiresUnreferencedCode` — cast to `JsonNode?`

**Symptom:** `IL2026` error in trimmed Release publish: `Using member 'JsonArray.Add<T>(T)' which has 'RequiresUnreferencedCodeAttribute'`.

**Cause:** Overload resolution picks the generic `Add<T>(T)` (which uses reflection at runtime) over the non-generic `Add(JsonNode?)` (safe) when the argument is a concrete `JsonValue` / `JsonObject` (exact match wins over upcast).

**Fix:** Explicit `(JsonNode?)` cast forces the safe overload:

```csharp
// BAD: compiles in Debug, fails trim in Release
arr.Add(JsonValue.Create(s));

// GOOD: forces non-generic Add(JsonNode?)
arr.Add((JsonNode?)JsonValue.Create(s));
```

### `<TrimmerRootAssembly>` for packages that do string-typename reflection on their own internal types

**Symptom:** Suppression silences the build warning but the rooted-by-string types aren't actually preserved by the trimmer; runtime access fails (empty viewer pane, plugin not found).

**Cause:** `[UnconditionalSuppressMessage]` only silences the warning. It doesn't tell the trimmer to keep types alive.

**Fix:** Add `<TrimmerRootAssembly Include="The.Package" />` in the .csproj for any package whose code path includes `Assembly.GetType(string)` + `Activator.CreateInstance` over its own types (Markdown.Avalonia is the example in this codebase). See TRIMMING.md for the full safety mechanism table.

---

## Linux platform integration

### `Window.Icon` is a no-op on Wayland

**Symptom:** Setting `Window.Icon = AppIcon.Instance` doesn't change the dock / titlebar / Alt-Tab icon on Wayland (CachyOS / COSMIC, modern GNOME, KDE 6, Sway).

**Cause:** Wayland protocol intentionally does not expose a per-window-icon API. Compositors read icons via `app_id` → `.desktop` file lookup in `$XDG_DATA_DIRS/applications/`.

**Fix:** Ship a `.desktop` file template. See [LINUX-DESKTOP-INTEGRATION.md](LINUX-DESKTOP-INTEGRATION.md) for the per-user / packager install procedures. `Window.Icon` continues to work on X11 (writes `_NET_WM_ICON`).

### Emoji glyphs require system emoji-font fallback on Linux

**Symptom:** `⭐` (U+2B50) renders as a missing-glyph box on CachyOS even though `⚙` (U+2699) and `🖥` (U+1F5A5) work.

**Cause:** Different emoji codepoints have different system-font coverage. U+2B50 is in the Symbols block but commonly rendered via emoji presentation; some Linux systems lack the matching emoji font.

**Fix:** For nav icons, prefer `★` (U+2605, BLACK STAR) over `⭐` — basic Unicode Dingbats block, supported by Inter and most system fonts directly without emoji fallback.

### `XDG_SESSION_TYPE` distinguishes Wayland from X11

**Useful for diagnostics:** `Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")` returns `"wayland"` / `"x11"` / `"tty"` etc. on systemd-logind systems. ClaudeForge's `AppIcon.EnsureLoaded` logs this on Linux startup so bug reports self-identify the session type.

---

## .NET 10 / build

### Two-token CLI flags need explicit index advance in args parser

**Symptom:** `--culture en-US --showInstallBanner` parses `en-US` but not `--showInstallBanner` (because the loop re-scans `en-US` and trips on something).

**Cause:** Naive `foreach (var arg in args)` can't consume an extra arg for two-token flags.

**Fix:** Index-based loop with manual advance:

```csharp
for (var i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--culture":
            if (i + 1 >= args.Length) { ... break; }
            var value = args[++i];   // advance past consumed value
            // ... process value
            break;
    }
}
```

### `CultureInfo.GetCultureInfo(name, predefinedOnly: true)` is the strict gate

**Symptom:** Naive culture validation accepts arbitrary BCP-47 codes (`xx-XX`, `not-a-real-code`) because .NET's "any culture is valid" fallback is permissive.

**Fix:** Pass `predefinedOnly: true` (added in .NET 6). Throws `CultureNotFoundException` for cultures not in the OS's predefined list.

```csharp
try
{
    var ci = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
    if (ci.IsNeutralCulture) return false;   // also reject "en" alone
    return true;
}
catch (CultureNotFoundException) { return false; }
```

### Initialise debug-flag parsing BEFORE Serilog if any flag affects culture or logging itself

**Symptom:** A `--culture` flag is parsed but doesn't take effect, or a `--debug-help` log line is silently dropped.

**Cause:** `LocalizationService.ApplyCulture` runs early in `Program.Main` (before any UI). If `DebugFlags.Initialize` runs AFTER it, the flag value isn't available. Also, Serilog logs emitted from `Initialize` are dropped because the pipeline isn't configured yet.

**Fix:** Two-phase split:

```csharp
DebugFlags.Initialize(args);                          // parse only, no logs
LocalizationService.ApplyCulture(DebugFlags.CultureOverride);
// AppDomain handler + Serilog ConfigureLogging here
DebugFlags.LogActiveFlags();                          // flush deferred warnings
```

---

## When in doubt

- The codebase uses **compiled bindings** + **[ObservableProperty]** + **CTK source generators** wherever possible. Hand-rolled `INotifyPropertyChanged` should be a last resort.
- `dotnet build -warnaserror` and `dotnet test` both pass on a clean tree. If your change breaks either, fix it before shipping.
- `dotnet publish -c Release -r win-x64` is the trim canary. New code paths should pass that without warnings — see TRIMMING.md.
- Boot the published binary at least once before declaring work done. The SkiaSharp use-after-free in this codebase's history would have been caught by a 5-second smoke run.
