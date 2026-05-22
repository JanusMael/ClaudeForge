# Trimming & ILLink Notes

This document captures hard-won knowledge about `PublishTrimmed` with Avalonia
12.0.0 (Fluent + Semi.Avalonia 12.0.0 + `Svg.Skia 3.0.2`) on .NET 10. The goal
is zero ILLink warnings in the Release publish across all six RIDs:
`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.

> **Avalonia 12 dependency note:** `Avalonia.Svg.Skia` has no Avalonia 12
> release. The app references `Svg.Skia 3.0.2` directly instead — same
> underlying `SKSvg` API, no Avalonia version dependency. The trim
> suppression list below was tuned for `Avalonia.Svg.Skia 11.x` and may
> need re-auditing post-bump; in particular, the historical `Svg.Custom`
> entry exists only in the Avalonia.Svg.Skia 11.x ship list and does not
> apply to the current `Svg.Skia 3.0.2` dependency tree. Re-run a Release
> publish for each RID after touching this file to confirm the suppression
> set still produces zero warnings.

If you are chasing a new IL2xxx warning after a dependency bump, **read this
first** — the wiring below is fragile and every piece has a non-obvious
failure mode.

---

## TL;DR — the wiring that works

1. `src/ClaudeForge/ClaudeForge.csproj` references the suppression
   XML via `<_ILLinkSuppressions>` — the only MSBuild item that
   `Microsoft.NET.ILLink.targets` actually reads and expands into the ILLink
   `link-attributes` arg (see `PrepareForILLink` target in the
   `Microsoft.NET.ILLink.Tasks` NuGet package, ~line 230). **Not**
   `<TrimmerRootDescriptor>`, **not** `<LinkAttributesXml>` (doesn't exist),
   **not** `<EmbeddedResource>`:

   ```xml
   <ItemGroup Condition="'$(Configuration)' == 'Release'">
     <_ILLinkSuppressions Include="ILLink.Suppressions.xml" />
   </ItemGroup>
   ```

   The leading underscore is MSBuild convention for "internal", but no
   public alias exists. Users of the SDK needing cross-assembly attribute
   injection have to populate this item directly.

2. `src/ClaudeForge/ILLink.Suppressions.xml` uses `<attribute>` children
   of `<assembly>` with `Scope="module"` + `Target="<assembly-name>"`:

   ```xml
   <assembly fullname="Semi.Avalonia">
     <attribute fullname="System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute">
       <argument>Trimming</argument>
       <argument>IL2026</argument>
       <property name="Scope">module</property>
       <property name="Target">Semi.Avalonia</property>
     </attribute>
   </assembly>
   ```

3. Release-only diagnostic aids in the csproj keep each trim warning tied to
   its originating `.axaml` file + line number rather than collapsing to a
   single opaque "single warn" per type:

   ```xml
   <TrimmerSingleWarn>false</TrimmerSingleWarn>
   <TrimmerRemoveSymbols>false</TrimmerRemoveSymbols>
   <DebugType>embedded</DebugType>
   <DebugSymbols>true</DebugSymbols>
   <AvaloniaXamlIlVerboseExceptions>true</AvaloniaXamlIlVerboseExceptions>
   ```

4. ILLink's own `--verbose` flag is appended to `_ExtraTrimmerArgs` so every
   Release publish prints the final ILLink command line (including which
   `--link-attributes` files were consumed). This is the fastest way to
   confirm the suppression XML actually reached the linker — see the
   dedicated section below.

   ```xml
   <_ExtraTrimmerArgs>$(_ExtraTrimmerArgs) --verbose</_ExtraTrimmerArgs>
   ```

   Note: this is the ILLink flag, **not** MSBuild's `-v:detailed`.

Anything other than that combination silently breaks — see the failure modes
below.

---

## The wiring forms and why only one works

ILLink in .NET 10 can theoretically consume suppression XML via several
MSBuild items, but only one of them is actually wired up by
`Microsoft.NET.ILLink.targets`:

| MSBuild item | ILLink arg | Attribute injection? | Cross-assembly? |
|--------------|-----------|----------------------|-----------------|
| `<_ILLinkSuppressions>` | `--link-attributes` | **Yes** | **Yes** ✅ |
| `<LinkAttributesXml>` | (none — not read by the targets) | N/A | N/A |
| `<TrimmerRootDescriptor>` | `-x` | No (file parsed as preservation descriptor) | N/A |
| `<EmbeddedResource>` + `LogicalName=ILLink.LinkAttributes.xml` | (embedded) | Yes | **No — host assembly only** |

Only the first form does what we need. The others look plausible — and
`<LinkAttributesXml>` in particular is mentioned in older trimming guides —
but:

### `<TrimmerRootDescriptor>` silently discards `<attribute>` elements

When ILLink receives the file via `-x`, it parses it as a **preservation
descriptor**. That schema cares about `<type>`, `<method>`, `<field>`
preservation entries. `<attribute>` children are not part of that schema, so
ILLink silently ignores them. Worse, because the descriptor parser is
trying to resolve every `<assembly fullname="X">` into the link closure, you
get spurious warnings like:

> `ILLink.Suppressions.xml(140,4): warning IL2007: Could not resolve assembly 'Svg'.`

That `IL2007` is the diagnostic that finally pinned this down — it proved
ILLink was reading the file but as the wrong format.

### `<EmbeddedResource>` with `LogicalName=ILLink.LinkAttributes.xml` is host-assembly only

This is the form most trimming guides recommend because for an app that
*owns* the code being suppressed it is the simpler wiring:

```xml
<!-- Works only for the HOST assembly. -->
<EmbeddedResource Include="ILLink.LinkAttributes.xml">
  <LogicalName>ILLink.LinkAttributes.xml</LogicalName>
</EmbeddedResource>
```

ILLink only injects the attributes into the assembly that *embeds* the XML.
Every `<assembly fullname="X">` entry that targets a *different* assembly
triggers:

> `IL2101: Embedded XML in assembly 'ClaudeForge' contains assembly
> "fullname" attribute for another assembly 'Semi.Avalonia'.`

For cross-assembly suppression — which is what we need for Semi.Avalonia,
ExCSS, Svg.Custom, Avalonia.Controls.DataGrid, Serilog, Svg — this form does
not work.

### `<LinkAttributesXml>` is NOT read by the targets

Despite appearing in older trimming guides and StackOverflow answers, the
.NET 10 `Microsoft.NET.ILLink.targets` does not reference an item named
`LinkAttributesXml` anywhere. Setting it in a csproj is a silent no-op —
no warnings, no diagnostics, and the file is never passed to ILLink.

### `<_ILLinkSuppressions>` is the form that works

Grep the ILLink targets in
`~/.nuget/packages/microsoft.net.illink.tasks/<version>/build/Microsoft.NET.ILLink.targets`
and the only item that feeds `--link-attributes` is `_ILLinkSuppressions`:

```xml
<PropertyGroup Condition="'@(_ILLinkSuppressions->Count())' != '0'">
  <_ExtraTrimmerArgs>$(_ExtraTrimmerArgs) --link-attributes "@(_ILLinkSuppressions->'%(Identity)', '" --link-attributes "')"</_ExtraTrimmerArgs>
</PropertyGroup>
```

The leading underscore is MSBuild convention for "SDK-internal", but no
public alias exists in the 10.x generation of the targets. Projects that
need cross-assembly link-attributes XML populate `_ILLinkSuppressions`
directly.

---

## Scope values in the XML

`SuppressMessageAttribute.Scope` accepts six values, all of which work in
`UnconditionalSuppressMessage` via `_ILLinkSuppressions`:

- `"module"`
- `"namespace"`
- `"namespaceanddescendants"`
- `"resource"`
- `"type"`
- `"member"`

For "suppress every occurrence of IL2xxx in this third-party assembly",
`Scope="module"` + `Target="<assembly-name>"` is the right combination. It
covers all code in the assembly including nested compiler-generated types
such as `CompiledAvaloniaXaml.!AvaloniaResources/XamlClosure_N`, without
having to enumerate Scope/Target per type.

(An earlier pass through this file claimed `"module"` was not a valid value
and tried bare `<attribute>` children without any `Scope` property. That is
wrong — `"module"` is valid; the bare form injects
`[assembly: UnconditionalSuppressMessage]` with no scope, which only
suppresses warnings attributed to the assembly manifest itself, not warnings
inside types/methods. Don't strip the `<property>` children.)

---

## `TrimmerSingleWarn=true` (the default) hides the source of every XAML binding warning

By default ILLink collapses all IL2026 hits inside a single type into one
"single warn" line, which for compiled Avalonia XAML looks like:

> `IL2026: The method Build_M() has 'RequiresUnreferencedCodeAttribute' …
> inside XamlClosure_13`

No file. No line number. No way to know whether the reflection binding is in
`PermissionsEditorView.axaml`, the Fluent theme, or Semi.Avalonia.

Set `<TrimmerSingleWarn>false</TrimmerSingleWarn>` + keep debug symbols
embedded, and each warning carries the originating `.axaml` file and line
number. This turns a 1-line "something somewhere" warning into a specific
"line 173 of `PermissionsEditorView.axaml`, `{Binding}` with no DataType".

The binary size cost is small and there is no runtime cost — safe to ship.

---

## ILLink verbose logging is on by default

The csproj sets `<_ExtraTrimmerArgs>$(_ExtraTrimmerArgs) --verbose</_ExtraTrimmerArgs>`
in the Release `PropertyGroup`. That appends ILLink's own `--verbose` flag
to the linker's command line — **not** MSBuild's `-v:detailed`, which would
dump every build task's parameters and balloon the log to megabytes.

ILLink's verbose mode prints:

- The final command line as invoked, including every `--link-attributes`,
  `-x`, and `--substitutions` file that was consumed. This is the fastest
  way to confirm that `ILLink.Suppressions.xml` is actually being passed —
  if the file doesn't appear, the csproj item name is wrong (see the
  comparison table above).
- The reason each type/method/field is being kept or removed, which helps
  when a dependency bump introduces an unexpected reachability path.

The cost is a larger publish log but no build-time penalty worth caring
about. Leave it on.

## How to diagnose `XamlClosure_N` warnings

Every `.axaml` file compiles into a `XamlClosure_N` nested type. The closure
lives either inside the code-behind class (for a `UserControl` / `Window`) or
inside `CompiledAvaloniaXaml.!AvaloniaResources` (for styles / themes loaded
as resources). The `!` is a mangling prefix Avalonia uses to avoid user-type
collisions — it is not a typo.

**Key insight**: the *number* N is assigned in compile order and is not
stable across assemblies or versions. `XamlClosure_13` in Semi.Avalonia is
not the same as `XamlClosure_13` in your app.

To pinpoint which assembly a warning comes from, use
`src/publish/Analyze-XamlClosures.ps1`:

```powershell
pwsh src/publish/Analyze-XamlClosures.ps1 `
  -AssemblyPath src/ClaudeForge/bin/Release/net10.0/ClaudeForge.dll `
  -IncludeReferences
```

It enumerates `XamlClosure_N` types in every assembly via
`System.Reflection.Metadata` (metadata-only, does not load the assemblies)
and prints where each closure lives. A warning at
`CompiledAvaloniaXaml.!AvaloniaResources.XamlClosure_13` will map
unambiguously to one DLL in the output.

Sample output when the warning is *not* in your code:

```
ClaudeForge.dll
  CompiledAvaloniaXaml.!AvaloniaResources
    XamlClosure_3      ← only this one

Semi.Avalonia.dll
  CompiledAvaloniaXaml.!AvaloniaResources
    XamlClosure_1 … XamlClosure_78   ← closure 13 and 41 are here
```

That tells you definitively that the warning is upstream — don't go hunting
through your own XAML looking for a match.

---

## When you *do* own the XAML — reflection binding fallbacks

Avalonia's compiled-binding path in 11.x falls back to reflection (emits
IL2026) in at least these cases, even with
`AvaloniaUseCompiledBindingsByDefault=true`:

1. **Bare `{Binding}` on `x:String`** inside a `DataTemplate`:

   ```xml
   <!-- Falls back to reflection. -->
   <DataTemplate x:DataType="x:String">
     <TextBlock Text="{Binding}" />
   </DataTemplate>
   ```

   Fix: project strings into a typed record and bind the property:

   ```csharp
   public record SuggestionItem(string Value);
   ```

   ```xml
   <DataTemplate x:DataType="vm:SuggestionItem">
     <TextBlock Text="{Binding Value}" />
   </DataTemplate>
   ```

2. **`StringFormat` with a path chain on a collection property**:

   ```xml
   <!-- Falls back to reflection on IList<T>.Count traversal. -->
   <TextBlock Text="{Binding AllowList.Count, StringFormat='({0} rule(s))'}" />
   ```

   Fix: compute the formatted string in the view-model, bind the scalar:

   ```csharp
   public string AllowCountLabel => $"({AllowList.Count} rule(s))";
   ```

   ```xml
   <TextBlock Text="{Binding AllowCountLabel}" />
   ```

   Remember to raise `PropertyChanged(nameof(AllowCountLabel))` from whatever
   list-change handler exists (e.g. `CollectionChanged`).

3. **`$parent[T].DataContext.Command` in an `ItemsControl` item template** —
   cannot be compiled because `DataContext` is typed as `object?` at the
   AXAML compiler level. Use a code-behind event handler instead of a
   `{Binding}` to a command; see `Views/PermissionsEditorView.axaml.cs` for
   the pattern.

---

## Assembly name ≠ type namespace

`<assembly fullname="X">` in the suppression XML must match the physical
**assembly** name, not the type's namespace. This bites hardest when the two
diverge.

Concrete trap we hit: `Svg.SvgAspectRatio.ToString()` emits `IL2026`, and the
obvious instinct is to add `<assembly fullname="Svg">`. There is no `Svg.dll`
in our link closure — `Avalonia.Svg.Skia` 11.3.x ships the type inside
`Svg.Custom.dll`. ILLink reacts to the mismatch with:

> `ILLink.Suppressions.xml(166,4): warning IL2007: Could not resolve assembly 'Svg'.`

Key signal: `IL2007` referencing a *line in `ILLink.Suppressions.xml`* (not a
line in source) always means an `<assembly fullname="...">` in our file does
not correspond to any DLL the linker is processing. Not the same as the
`<TrimmerRootDescriptor>` failure-mode IL2007 (which also cites
`ILLink.Suppressions.xml` but fires because the file was passed through
`-x`, not `--link-attributes` — see the comparison table above). Tell them
apart by checking csproj wiring first.

To find the owning assembly of a type when only the namespace-qualified name
appears in the warning, metadata-probe the linked output:

```powershell
pwsh -NoProfile -Command "
Add-Type -AssemblyName 'System.Reflection.Metadata' | Out-Null
foreach (`$dll in Get-ChildItem 'src/ClaudeForge/obj/Release/net10.0/win-x64/linked' -Filter '*.dll') {
  `$s = [System.IO.File]::OpenRead(`$dll.FullName)
  `$pe = [System.Reflection.PortableExecutable.PEReader]::new(`$s)
  if (`$pe.HasMetadata) {
    `$md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader(`$pe)
    `$asm = `$md.GetString(`$md.GetAssemblyDefinition().Name)
    foreach (`$th in `$md.TypeDefinitions) {
      `$td = `$md.GetTypeDefinition(`$th)
      if (`$md.GetString(`$td.Name) -eq 'SvgAspectRatio') { `"$asm -> SvgAspectRatio`" }
    }
  }
  `$pe.Dispose(); `$s.Dispose()
}"
```

Swap `SvgAspectRatio` for whichever unresolved type the warning cites.

---

## The list of suppressed assemblies (and why)

These are all *upstream* libraries we cannot modify. Each suppression in
`ILLink.Suppressions.xml` has a justifying comment next to it; the summary:

| Assembly | Warnings | Safety mechanism | Why it's safe |
|----------|----------|------------------|---------------|
| `Semi.Avalonia` | `IL2026` | Suppression + style-engine reachability | Theme XAML emits reflection bindings for control-template styles resolved at runtime via the style engine. The control types reflected over are reachable through Avalonia's compiled-XAML loader, so the trimmer keeps them. |
| `ExCSS` | `IL2026`, `IL2067`, `IL2104` | Suppression + try/catch fallback | CSS parser used by Svg.Skia. Uses `Type.GetType` internally. AppIcon rendering is `try/catch`-guarded — failures degrade to "no icon" rather than crashing. |
| `Svg.Custom` | `IL2026`, `IL2104` | Suppression + try/catch fallback | (Historical entry from the Avalonia 11.x toolchain.) SVG parser (IL2104) **and** `Svg.SvgAspectRatio.ToString()` → `TypeDescriptor.GetConverter` (IL2026). On Avalonia 12 the assembly graph is different — `Avalonia.Svg.Skia` was replaced by `Svg.Skia 3.0.2` directly; the row is retained for context but no current trim warning maps to it. |
| `Avalonia.Controls.DataGrid` | `IL2026`, `IL2067`, `IL2070`, `IL2075`, `IL2104` | Suppression + typed-binding rooting | Pre-existing trim-unsafe reflection over column types; reachability widens when SVG dependencies are added. Column metadata is rooted by our typed (`x:DataType`) bindings. |
| `Serilog` | `IL2072` | Suppression + caller discipline | `PropertyValueConverter` reflects over captured object types. We log only strings, primitives, exceptions, and our own VMs. |
| `Markdown.Avalonia` (+ `ColorTextBlock.Avalonia`) | `IL2026`, `IL2072`, `IL2075` | **`<TrimmerRootAssembly>` + suppression** | Memory page markdown viewer.  `SetupInfo.Builtin()` does `Assembly.GetType(string)` + `Activator.CreateInstance` over its OWN BUILT-IN PLUGIN TYPES — suppression alone is NOT safe (the trimmer would happily drop those types).  `<TrimmerRootAssembly>` in the csproj keeps every type in both assemblies rooted; suppressions are belt-and-suspenders to silence warnings that fire abstractly.  IL2075 covers the `InterassemblyUtil` cross-assembly reflection used to optionally talk to other Markdown.Avalonia.* plugin packages (e.g. SyntaxHigh) — we use Tight only, so that path isn't reached at runtime. |

When a dependency bumps and new trim warnings appear, prefer **fixing them in
our own code** (section above) over adding suppressions. Only add a
suppression after confirming with `Analyze-XamlClosures.ps1` (or
`dotnet publish` with full diagnostics enabled) that the warning originates
outside our assemblies.

### When suppression alone is NOT enough

A suppression silences the warning but does NOT preserve any types. Use
`<TrimmerRootAssembly>` (in `ClaudeForge.csproj`, not the suppressions
file) when **any** of the following is true for the upstream package:

1. The package uses `Assembly.GetType(string)` or `Type.GetType(string)`
   to load **its own internal types** by string name — those types could
   be trimmed if no other code path roots them.
2. The package uses `Activator.CreateInstance(Type)` on types it
   discovers via reflection-based plugin enumeration.
3. The package's reflection patterns are not annotated with
   `[DynamicallyAccessedMembers]` and the `[RequiresUnreferencedCode]`
   guarantees do not propagate to known-safe call sites.

`<TrimmerRootAssembly>` opts the named assembly out of method-level
trimming. The cost is a few hundred KB of binary size; the benefit is
guaranteed runtime correctness. The IL2026/IL2072/IL2075 warnings still
fire (they describe the call's risk in the abstract), so a matching
suppression is still needed for a clean build — but the suppression is
now silencing a warning whose underlying risk has been mitigated.

**Rule of thumb:** if the upstream package's reflection touches types
ONLY inside its own assembly (e.g. plugin discovery), `<TrimmerRootAssembly>`
is sufficient. If it reflects across assembly boundaries (like Markdown.Avalonia's
`InterassemblyUtil`), you also need a runtime-behaviour story — usually
"that code path isn't actually exercised by our usage", documented in
the suppression comment.

---

## Our code is trim-clean — keep it that way

The following rules keep `ClaudeForge`, `ClaudeForge.Core`, and the
`LayeredEditors.*` projects out of this file:

- **Use source-generated `JsonSerializerContext`** — see `CoreJsonContext` /
  `AppJsonContext`. Every `JsonSerializer` call path must flow through a
  `JsonTypeInfo<T>`. Reflection-based overloads emit IL2026 and break AOT.
- **`x:DataType` on every `DataTemplate` and `UserControl`** — bare
  `{Binding}` without a known DataType drops to reflection.
- **Don't bind `StringFormat` with a path chain** — compute the display
  string in the view-model and bind the scalar.
- **`[RequiresUnreferencedCode]` propagates** — if you call a reflective API,
  either annotate the caller or refactor so the reflection is not reachable.

---

## Verifying a publish

```powershell
# Interactive: step through every RID with a [Y/n/a/q] prompt per architecture.
pwsh src/publish/publish.ps1

# Unattended (CI / release-cut): build every RID without prompting.
pwsh src/publish/publish.ps1 -All

# Subset: only the two Windows RIDs (still prompts unless combined with -All).
pwsh src/publish/publish.ps1 -Rids win-x64,win-arm64

# Single-RID shortcuts (each delegates to Publish-Rid.ps1 with -Clean):
pwsh src/publish/publish-win-x64.ps1
pwsh src/publish/publish-win-arm64.ps1
pwsh src/publish/publish-linux-x64.ps1
pwsh src/publish/publish-linux-arm64.ps1
pwsh src/publish/publish-osx-x64.ps1
pwsh src/publish/publish-osx-arm64.ps1
```

Target: **zero** ILLink warnings across all six RIDs. Anything above zero
means either (a) a dependency change widened the reachability graph into a
new path, (b) our code introduced a new reflection-binding fallback, or
(c) the suppression wiring was silently broken by one of the failure modes
above.

### Runtime smoke after every trim-affecting change

Zero warnings is necessary but not sufficient — a suppression silences
the build-time check but does not preserve any types. After any change
to `ILLink.Suppressions.xml`, `<TrimmerRootAssembly>`, package versions,
or features that touch reflection-heavy packages (Markdown.Avalonia,
Semi.Avalonia DataGrid templates, Svg.Skia for AppIcons), launch the
**published `ClaudeForge.exe`** (not `dotnet run`!) and verify each
affected feature actually renders:

| Package | Smoke target | What "broken" looks like |
|---|---|---|
| Markdown.Avalonia | Memory page → click any `.md` file | Empty viewer pane, "No converter found" exception in logs, or unstyled raw markdown text. |
| Semi.Avalonia | Any nav node with controls (every page) | Default Fluent look (white-grey rectangles instead of Semi's rounded blue accents) means the Semi theme failed to apply. |
| Avalonia.Controls.DataGrid | (currently no DataGrid in app — placeholder) | n/a |
| Svg.Skia (App icons) | Any toolbar with an icon | Missing icons; `try/catch` swallows the failure but log shows `Svg.Skia` exceptions. |

Symptoms of trim breakage often manifest only after navigation — Avalonia
lazy-loads control templates on first use. Click through every nav node
once before declaring the trimmed build healthy.

If the warning count is non-zero, before editing anything:

1. **Grep the publish log for `--link-attributes`.** Because `--verbose` is
   permanently on, ILLink prints its final command line. If
   `ILLink.Suppressions.xml` does not appear after a `--link-attributes`
   token, the csproj wiring is wrong — the file never reached the linker,
   and no amount of editing the XML will help. This is the single most
   informative diagnostic and should be step one.
2. Confirm the csproj uses `<_ILLinkSuppressions>` (not
   `<LinkAttributesXml>`, not `<TrimmerRootDescriptor>`, not
   `<EmbeddedResource>`) for `ILLink.Suppressions.xml`.
3. Confirm every `<attribute>` in the XML has
   `<property name="Scope">module</property>` + `<property name="Target">…</property>`.
4. Run `src/publish/Analyze-XamlClosures.ps1` against the freshly-built DLLs to find
   the originating assembly for each `XamlClosure_N` warning — only useful
   once you've confirmed the suppression file is actually being consumed.
5. Fallback diagnostic: look for `IL2007: Could not resolve assembly 'X'`
   warnings in the publish log. If you see one, the file is being parsed as
   a preservation descriptor rather than as attribute XML — fix the csproj
   wiring (step 2). (With `_ILLinkSuppressions` this should not happen; it
   is a signature of the old `<TrimmerRootDescriptor>` misconfiguration.)

That diagnosis path catches ~every regression we have seen so far.
