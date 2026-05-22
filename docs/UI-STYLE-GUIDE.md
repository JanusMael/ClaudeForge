# UI style guide — reusable styling primer

**Audience:** someone forking the ClaudeForge visual layer as the starting
point for a different Avalonia 12 app.  This is not aesthetic theory; it
is the set of token decisions, naming patterns, and Avalonia 12 gotchas
that fell out of iterating on this codebase across many polish passes.

If you're a contributor to ClaudeForge itself, `CLAUDE.md` is the
narrative entry point and `AGENTS.md` carries the hard invariants.  This
doc focuses on the **visual system**: brushes, accents, borders,
typography, dialog layout.

> 🎨 **Theme stack**: Avalonia 12 + Semi.Avalonia 12.  This app uses
> Semi as the underlying theme rather than `Avalonia.Themes.Fluent`.  A
> handful of tokens below exist specifically *because* Semi's
> `SystemControl*` family is unreliable across themes — see "Why we
> have our own tokens" below.

---

## 1. Token taxonomy

All custom tokens live in `App.axaml`'s `ThemeDictionaries` (one
ResourceDictionary per theme variant: `Light` and `Dark`).  A few
domain-specific tokens are scoped tighter — scope chiclet brushes ship
in `Resources/ScopeTheme.axaml`, the page-title accent ships in
`Views/MainWindow.axaml`'s `Window.Resources` because nothing outside
the main window needs it.

Naming pattern:

```
App<Purpose>{Foreground|Background|Border}Brush       — app-wide primitives
App<Surface><Layer>{Foreground|Background|Border}Brush — surface-specific
AppStatus<Severity>{Foreground|Background}Brush       — status-bar pill family
Bar<Role>{Foreground|Background}Brush                 — top-bar / bottom-bar buttons
scope-brush-<scope-id>                                — scope chiclet (lowercase id)
<Feature><Accent>Brush                                — feature-scoped accent (e.g. NavSelectedAccentBrush)
```

The `App*` prefix is load-bearing — it disambiguates from Semi.Avalonia's
own `SystemControl*` tokens that we deliberately don't trust.

---

## 2. Why we have our own tokens (Semi.Avalonia friction)

Semi.Avalonia ships its own brush family using the WinUI-ish
`SystemControl*` naming (e.g. `SystemControlForegroundBaseHighBrush`,
`SystemControlBackgroundBaseLowBrush`).  Through iteration we found:

- **Inconsistent per-theme resolution.**  Some `SystemControl*` keys
  resolve to a colour in light theme but to nothing (or to a
  near-invisible value) in dark — producing dark-on-dark text on dark
  theme for the same XAML.
- **Inconsistent across packages.**  Some keys are defined on Semi's
  ResourceDictionary chain; others were defined by Fluent and aren't
  carried into Semi.  Whether `SystemControlForegroundBaseMediumBrush`
  resolves correctly depends on which theme variant is loaded AND which
  upstream packages were last published — too fragile for a UI tier
  shipping to users.
- **Markdown.Avalonia + Semi compatibility hole.**  Components like
  `AvaloniaEdit` ship per-theme XAML resources that some packages (e.g.
  `Markdown.Avalonia.SyntaxHigh`) only auto-load when
  `ThemeDetector.IsFluentUsed` returns true.  Under Semi, auto-detect
  bails; templates use `StaticResource` (hard-fail) instead of
  `DynamicResource`, so adding the missing theme XAML surfaces a cascade
  of `KeyNotFoundException`s on Fluent typography tokens that Semi
  doesn't define.

**Convention**: define an `App<Purpose>Brush` for every user-visible
foreground / background / border that needs explicit per-theme control.
Refer to the `App*` tokens in AXAML, not to `SystemControl*` keys.
Falling back to `SystemControlForegroundBaseMediumBrush` for "muted
text" works on light theme and disappears on dark.

---

## 3. App-wide colour primitives

These are the foundational text/surface brushes everything else builds
on.  All are theme-aware (per-theme entry in App.axaml).

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `AppPrimaryTextBrush` | `#1A1A1A` | `#E8E8E8` | Body text — the safe replacement for `SystemControlForegroundBaseHighBrush` |
| `AppSecondaryTextBrush` | `#666666` | `#AAAAAA` | Muted text (descriptions, "Old value" column, helper paragraphs) |
| `AppLinkBrush` | `#005D7F` | `#4DBFE0` | Hyperlinks, clickable accent text, "Browse suggestions" expander labels |
| `AppCautionBrush` | `#D97706` | `#F59E0B` | Amber warning foreground (PATH warning row, no-restore-dir warning) |
| `AppCautionBackgroundBrush` | `#FFFBEB` | `#292010` | Soft amber tint behind warning rows |
| `AppPanelBorderBrush` | `#E0E0E0` | `#2E2E2E` | Subtle row/panel dividers — replaces `SystemControlForegroundBaseLowBrush` where Semi resolves to near-invisible |

**Contrast targets:** the `Primary` / `Secondary` text brushes are tuned
for ≥7:1 (Primary, WCAG AAA) and ≥4.5:1 (Secondary, AA Normal) against
their natural backgrounds.  Don't substitute a "looks fine" hex without
checking — Semi's variant tweaks can drop you below threshold quietly.

---

## 4. Surface-specific tokens

### 4a. Code-block surface

Used by markdown fenced code blocks (Memory page viewer) AND by any
dialog that frames a structured-text pane (the schema-validation
errors-pane lives here).

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `AppCodeBlockBackgroundBrush` | `#F4F4F4` | `#1F1F1F` | Subtle bg tint that reads as "structured / monospace content" |
| `AppCodeBlockBorderBrush` | `#D0D0D0` | `#3A3A3A` | 1px outline that delimits the pane from page bg |

**Pattern**: wrap the framed content in a `<Border>` with both tokens +
`CornerRadius=3` + `Padding=10,8`.  At runtime, use
`DynamicResourceExtension` from `Avalonia.Markup.Xaml.MarkupExtensions`
so the pane re-tints on theme switch:

```csharp
border[!Border.BackgroundProperty]  = new DynamicResourceExtension("AppCodeBlockBackgroundBrush");
border[!Border.BorderBrushProperty] = new DynamicResourceExtension("AppCodeBlockBorderBrush");
```

### 4b. Property-heading pill

The per-row property-name pill in `PropertyEditorWrapper`.  Distinct
visual layer above the description and editor controls.

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `AppPropertyHeadingBrush` | `#1E5573` | `#74C2DE` | Deep-teal text |
| `AppPropertyHeadingBackgroundBrush` | `#B8DCE2` | `#1B3A45` | Pale-teal pill bg (light), deep-teal pill bg (dark) |

**Don't reuse this pair anywhere else.**  The pill is reserved for "this
is a property name" — overusing the teal dilutes the signal.  For other
"this is a heading"-style call-outs use a different hue family.

---

## 5. Status-bar pill family (centre slot)

Five-kind state machine; each kind has a paired foreground + background
brush.  Driven by `MainWindowViewModel.Status` (a `StatusController`
instance) via typed `SetStatusXxx` helpers.

| Kind | Foreground (light/dark) | Background (light/dark) | Visual | Lifecycle |
|---|---|---|---|---|
| Active | `#0050B3` / `#6BB1F2` | `#E6F0FB` / `#102B45` | blue pill, "…" glyph | no auto-clear |
| Success | `#1B7F2A` / `#5BD17B` | `#E8F5EC` / `#143820` | green pill, ✓ glyph | auto-clear ~6s |
| Warning | `#A35200` / `#F0A03A` | `#FFF4E5` / `#3A2B12` | amber pill, ⚠ glyph | auto-clear ~10s |
| Failure | `#A8071A` / `#F76262` | `#FFE7E7` / `#3A1717` | red pill, ✗ glyph, × dismiss | sticks until dismissed |
| State | — | — | quiet plain text, no pill | replaced by next Set |

**Design principle**: each pill kind is dual-coded (colour + glyph) so
colour-blind / colourblind-mode users still see the severity.

**Anti-pattern**: writing to a legacy `StatusMessage` setter that routes
to `StatusKind.State` makes a failure render as quiet gray text. The
typed-helper invariant in `AGENTS.md` forces the right surface; tests in
`StatusControllerTests` lock the lifecycle.

---

## 6. Scope chiclets

Coloured tag that appears next to a property name to indicate which
config scope owns the effective value.  Keys: `scope-brush-<id>` where
`<id>` is the lowercase `IEditorScope.Id`.

| Scope | Brush | Colour | Meaning |
|---|---|---|---|
| Managed | `scope-brush-managed` | `#D32F2F` (red) | Admin-controlled, read-only — overrides everything |
| Local | `scope-brush-local` | `#F57F17` (amber) | `<project>/.claude/settings.local.json` — machine-local |
| Project | `scope-brush-project` | `#388E3C` (green) | `<project>/.claude/settings.json` — shared in git |
| User | `scope-brush-user` | `#1976D2` (blue) | `~/.claude/settings.json` — lowest-priority user-editable |

The environment-layer chiclets are sibling tokens in the same file:

| Layer | Brush | Colour |
|---|---|---|
| Process | `scope-brush-process` | `#6A1B9A` (purple) |
| Claude env | `scope-brush-claude-env` | `#00695C` (teal) |
| User (HKCU) | `scope-brush-user` | `#1976D2` (blue) |
| Machine (HKLM) | `scope-brush-machine` | `#5D4037` (brown) |

**Anti-pattern**: hand-picking a colour at the call-site (`Background="#388E3C"`)
breaks themability and creates drift between rows.  Always route through
the `ScopeToBrushConverter` so the per-scope colour stays in one place.

---

## 7. Kind pills (Save Changes dialog)

Coloured `+` / `-` / `~` glyph in the leftmost column of the SaveChanges
dialog.  Hard-coded in `SaveChangeEntryViewModel.KindBackground` since
they only appear in one place.

| Kind | Glyph | Colour | Material name |
|---|---|---|---|
| Added | `+` | `#2E7D32` | Green 800 |
| Removed | `-` | `#C62828` | Red 700 |
| Modified | `~` | `#F57C00` | Orange 700 |

**Why not Orange 900 (`#E65100`)?**  It's named "orange" but R230/G81/B0
visually reads as red — easy to confuse with the Removed pill.  Orange
700 keeps the Material palette but has a clearly more orange hue.

**Why `~` for Modified?**  `~` is the diff-syntax marker for "changed";
familiar to anyone who reads `git diff`.  All three glyphs are plain
ASCII so they render correctly without an emoji-capable font.

**Accessibility**: every pill has both `ToolTip.Tip` and
`AutomationProperties.Name` bound to a localised
`SaveDialogKind<Added|Removed|Modified>` resx string — "Added property",
"Removed property", "Modified property".  See § 13 below.

---

## 8. Page-title and nav-selected accent

A single high-vibrancy blue used as the page-title pill (every
top-level page heading sits inside a `NavSelectedAccentBrush` Border)
and as the navigation-tree selected-row background.

| Brush | Light | Dark | Where |
|---|---|---|---|
| `NavSelectedAccentBrush` | `#FF1664F0` | `#FF3375E0` | `MainWindow.axaml`'s `Window.Resources` |

**Reserved semantic**: "you are here" / "this is the page heading".
Don't reuse the accent for unrelated emphasis — it's a navigation signal.

---

## 9. Toolbar / status-bar button tokens

Top-bar and bottom-bar buttons that need to read distinctly from the
window chrome.

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `BarButtonBackgroundBrush` | `#FFD0D0D5` | `#FF2A2A2E` | Button surface tint, distinct from bar chrome |
| `BarButtonForegroundBrush` | `#FF111111` | `#FFF2F2F2` | Button label text |

Convention: apply both when rendering a button inside the top toolbar
or bottom status bar.  Standard `Button` styles inherit Semi's defaults
which can blend into the bar.

---

## 10. Borders, corners, padding — the surface system

| Surface | Outer radius | Padding | Border |
|---|---|---|---|
| Page-title pill | `6` | `14,8` | none (filled accent) |
| Property-heading pill | `3` | `8,2` | none |
| Scope chiclet | `4` | `6,2` | none |
| Kind pill | `3` | `4,1` | none |
| Status pill | `10` | `6,2` | none |
| Code/error pane | `3` | `10,8` | 1px `AppCodeBlockBorderBrush` |
| Section row | `3` | `8,5` to `8,4` | 1px `SystemControlForegroundBaseLowBrush` |

**Pattern**: smaller pills (chiclets) use `CornerRadius=3` to read as
"tag-like"; larger panels (page heading, content frames) use `6` to
read as "card-like".  Keep the family consistent — mixing `8` and `12`
across the same view looks unintentional.

---

## 11. Typography

| Surface | Family | Size | Weight |
|---|---|---|---|
| Body text | inherits Semi default | 14 | Normal |
| Property-heading pill text | inherits | 14 | SemiBold |
| Descriptions / muted text | inherits | 12 | Normal |
| Scope chiclet text | inherits | 10 | Bold |
| Kind pill glyph | `Consolas,Menlo,monospace` | 11 | SemiBold |
| Status pill text | inherits | 11–12 | Normal |
| Toolbar button label | inherits | inherits | Normal (`AccessText` for `_` mnemonics) |
| Code blocks / paths / JSON tooltips | `Consolas,Menlo,monospace` | 11 | Normal |

**Monospace family**: `Consolas,Menlo,monospace`.  Fallback chain: Consolas
(Windows), Menlo (macOS), system monospace (Linux).  Don't substitute
"Courier New" — its glyph metrics are noticeably wider than the modern
defaults and breaks alignment in side-by-side diff columns.

**Different fonts on the same line**: don't put two `TextBlock`s with
different `FontFamily` next to each other expecting `VerticalAlignment=Center`
to align them.  Center-alignment uses each TextBlock's BBox centre, and
fonts with different ascender/descender ratios visually mis-align by
1–2 pixels.  **Use `Run` elements inside a single `TextBlock`** — Runs
share one baseline regardless of FontFamily.  See `AboutDialog.axaml`'s
version-label/version-number pair for the canonical pattern.

---

## 12. Dialog layout rules

### Resizable dialogs

A dialog with `CanResize=true` MUST use **DockPanel layout**, not
StackPanel.  StackPanel doesn't constrain child width; resizing
horizontally leaves the body unstretched.  StackPanel also doesn't
glue the bottom-most child to the bottom edge; resizing vertically
leaves an empty band between the content and the OK button.

Canonical layout:

```xml
<DockPanel Margin="16" LastChildFill="True">
    <!-- Optional top elements (header, summary) -->
    <SomeHeader DockPanel.Dock="Top" Margin="0,0,0,12" />
    <!-- Bottom-docked button row -->
    <SomeButton DockPanel.Dock="Bottom" Margin="0,12,0,0"
                HorizontalAlignment="Center" MinWidth="70" />
    <!-- Last child fills remaining space -->
    <ScrollViewer />
</DockPanel>
```

Window-level constraints: `MinHeight=160`, `MinWidth=360`,
`MaxHeight=720` (or higher for content-heavy dialogs).  Without `Min*`
the resize handles can drag the dialog into a degenerate state.

**Don't put a fixed `MaxHeight` on the inner ScrollViewer** — that
breaks resize.  Let the window's `MaxHeight` cap the outer extent and
the ScrollViewer fill whatever's available.

### Modal-confirm dialogs (non-resizable)

`CanResize=false` + `SizeToContent=Height` + `Width=480`.  StackPanel
layout is fine for these since the height auto-fits the content and
the user can't drag.

### Mixed-content dialogs

When a dialog message contains BOTH `Code` segments AND surrounding
explanatory text, only the Code segments should sit inside a framed
bordered pane; the surrounding text stays outside.  The
`BuildSegmentedDialogContent` helper in `AvaloniaDialogService` does
this segmentation automatically — call it instead of
`BuildSegmentedTextBlock` whenever the dialog can carry Code.

### Universal X-dismiss contract

`IDialogService.ShowConfirmAsync` returns `Task<bool?>`:

| Return | Meaning |
|---|---|
| `true` | Confirm button clicked |
| `false` | Cancel button clicked (or Escape via `IsCancel=true`) |
| `null` | User dismissed via the window-close (X) without choosing |

**X-close never proceeds.**  For binary destructive yes/no callers
collapse both via `if (result != true) return;`.  For trinary prompts
(Include / Omit / X-to-abort) callers explicitly branch on `null`.

---

## 13. Accessibility — never colour or glyph alone

Every interactive control in `Views/*.axaml` MUST have
`AutomationProperties.Name`.  Guard test
`AxamlAccessibilityCoverageTests` fails CI when a new control regresses
coverage.  See the AutomationProperties invariant in `AGENTS.md`.

**Sibling principle for non-interactive cues**: anywhere colour or a
single-glyph cue is the only signal of meaning — the kind pill, scope
chiclets, severity badges — add both:

- `ToolTip.Tip` for sighted-user hover discovery
- `AutomationProperties.Name` for screen-reader announcement

Bind both to the same localised string (e.g.
`Strings.SaveDialogKindModified` → "Modified property").  WCAG 1.4.1:
colour is never the sole indicator of information.

**`AccessText` ≠ `AutomationProperties.Name`**.  `AccessText` is for
Alt-key mnemonics (keyboard navigation); `AutomationProperties.Name` is
for screen readers.  Both belong on the same button.  Avalonia 12's
`ContentPresenter` does NOT honour `_` in plain string `Content` (the
underscore renders literally) — wrap label text in `<AccessText
Text="{x:Static loc:Strings.Xxx}"/>` as nested button content.

---

## 14. Avalonia 12 gotchas (iteration receipts)

These came out of this codebase's polish iterations.  Future Avalonia
versions may resolve some; check before assuming they still apply.

### Drag-drop payload API
The legacy `IDataObject` / `DataFormats.Files` surface is GONE in
Avalonia 12.  New surface: `IDataTransfer` / `DataFormat.File`, accessed
via `DragEventArgs.DataTransfer`, with `TryGetFiles()` extension on
`DataTransferExtensions`.  The `FileDropBehavior` in
`LayeredEditors.Avalonia.Behaviors` insulates consumers from this delta
— attach the behaviour rather than handle raw events.

### AutoCompleteBox dropdown filtering
After selecting an item, clicking the dropdown chevron filters the
suggestion list to items containing the current `Text` — usually just
the selected one.  Fix in the chevron click handler: temporarily set
`FilterMode = AutoCompleteFilterMode.None`, open dropdown, restore
original mode via a one-shot `DropDownClosed` handler.  See
`PropertyEditorWrapper.axaml.cs`.

### Inheritance display: treat empty string as "not set"
`IEditorValue.EffectiveValue` can be an empty string (e.g. from a
TwoWay AutoCompleteBox binding pushing transient `""` mid-dropdown).
Code that builds an "(inherits: X)" watermark should fall through to
the schema default when X formats empty; rendering "(inherits: )" with
no value is a worse UX than "(inherits: <default>)" or "(not set)".
See `PropertyEditorViewModel.UpdateInheritedDisplay`.

### ContentControl auto-derives `AutomationProperties.Name` from Content
But it picks up emoji glyphs and `_` mnemonic prefixes verbatim
("Underscore S a v e" / "Floppy disk save" — useless to a screen
reader).  Always set the property explicitly; never rely on
auto-derivation.

### `Run.Text` accepts bindings
A single `TextBlock` with multiple `Run` inlines shares one baseline
regardless of per-Run `FontFamily`.  Use this when you need a label +
monospace value on the same line and `VerticalAlignment=Center` of two
TextBlocks is visually mis-aligning.

### `Semi.Avalonia` locale lazy-loads
On first control creation it reads `CultureInfo.CurrentUICulture`.
Call `LocalizationService.ApplyCulture()` *before* `BuildAvaloniaApp()`
in `Program.Main` so localized strings are available when Semi
initialises.

### `Window.Icon` on Wayland is a no-op
`Window.Icon` writes `_NET_WM_ICON` on X11 (works) but Wayland's
protocol has no per-window-icon API.  Compositors read
`app_id.desktop` from `$XDG_DATA_DIRS/applications/` and resolve the
`Icon=` field.  A Wayland user without an installed `.desktop` file
sees a generic placeholder no matter what `AppIcon.cs` does.  See
`docs/LINUX-DESKTOP-INTEGRATION.md`.

### Markdown.Avalonia + Fluent-coupled controls under Semi
Components like `AvaloniaEdit` ship per-theme XAML resources that some
packages (`Markdown.Avalonia.SyntaxHigh`) only auto-load when
`ThemeDetector.IsFluentUsed` is true.  Under Semi the auto-detect bails
+ the control's template never applies.  Adding the missing theme XAML
manually then surfaces a cascade of `KeyNotFoundException`s on Fluent
typography tokens Semi doesn't define.  **Recommendation**: avoid
Fluent-coupled 3rd-party controls under Semi.  When we hit this with
`Markdown.Avalonia.SyntaxHigh` we downgraded to
`Markdown.Avalonia.Tight` which doesn't depend on AvaloniaEdit.

### Tooltips don't propagate from parent Border to child TextBlock
Setting `ToolTip.Tip` on a parent `Border` does NOT cover child
`TextBlock`s the user hovers — Avalonia tooltip resolution doesn't
walk up the visual tree.  Set the tooltip on BOTH the parent (for the
padding area) AND the inner text element.

### `<NumericUpDown.IsAllowSpin>` renamed; `X11PlatformOptions.WmClass` gone
Avalonia 11 APIs that don't exist in 12.  Check
`~/.nuget/packages/avalonia/12.0.0/lib/net10.0/Avalonia.*.xml` for the
actual surface when you suspect drift.

---

## 15. Composition rules — combining elements

### A row that needs all three a11y signals

Property-editor row with a scope chiclet, a name pill, an "(overridden)"
note, and an inheritance row.  Order matters:

```
[Property name pill]   [Scope chiclet]   [(overridden)]   [🔒 read-only]   [✨ NEW badge]
[Description text — Linkified]
[Editor control]
[Currently effective from: [scope chiclet] {truncated value}]
```

Each element has a defined role; don't introduce a 6th badge type
without first justifying why one of the existing slots can't carry it.

### A modal that wants visual hierarchy

Page heading → 2-line muted summary → main content → secondary
information → action buttons.  Always vertical, always
`Spacing=8|12|16` depending on density.

### A row that doesn't fit any pattern

Before inventing new tokens, ask: which existing token's *semantics* is
closest?  Reusing `AppCautionBrush` for a non-warning yellow accent
dilutes the warning signal; reusing `NavSelectedAccentBrush` for a
secondary highlight dilutes the "you are here" signal.  When semantic
overload is unavoidable, define a NEW token rather than overloading an
existing one.

---

## 16. What to copy when forking this style set

If you want to use this visual system as a starting point for an
entirely different Avalonia 12 app:

1. **Copy `App.axaml`'s `<Application.Resources>` block in full** —
   it's self-contained and per-theme correct.  Rename the App* tokens
   freely; the prefix is a hint, not load-bearing for the framework.

2. **Copy `Resources/ScopeTheme.axaml`** if you have a "layered config
   scopes" concept; otherwise drop the file and the
   `ScopeToBrushConverter`.

3. **Copy the `LayeredEditors.Avalonia/Behaviors/` folder** — `FileDrop`
   and `FocusOnRequest` are framework-agnostic and isolate you from
   Avalonia 12 API churn.

4. **Copy `docs/AVALONIA-GOTCHAS.md`** — context that saved hours of
   iteration.

5. **Don't copy** `Resources/ScopeTheme.axaml` colours blindly — they're
   semantic to ClaudeForge's scope hierarchy.  Rename or replace.

6. **Watch out for**: the Semi.Avalonia bundle import in
   `LayeredEditors.Avalonia/Themes/SemiBundle.axaml`.  Switching to
   Fluent or another theme is a heavier refactor than just swapping the
   bundle reference — the `App*` token values were tuned against Semi's
   surrounding palette.

7. **Run the accessibility coverage test** with your own AXAML to see
   which interactive controls lack `AutomationProperties.Name`.  The
   guard pattern is the most reusable a11y artefact in the codebase.

---

## Appendix — quick index of tokens by where they live

| File | Tokens defined |
|---|---|
| `src/ClaudeForge/App.axaml` | All `App*` brushes, `BarButton*`, `SuggestionGroupHeaderBrush` |
| `src/ClaudeForge/Views/MainWindow.axaml` | `NavSelectedAccentBrush`, `SearchPopup*Brush` |
| `src/ClaudeForge/Resources/ScopeTheme.axaml` | `scope-brush-*` family |
| `src/LayeredEditors.Avalonia/Themes/EditorColors.axaml` | Library-side editor colours |
| `src/LayeredEditors.Avalonia/Themes/SemiBundle.axaml` | Semi.Avalonia bundle import |
