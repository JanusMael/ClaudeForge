# Theme audit

ClaudeForge runs Semi.Avalonia. Controls templated for Avalonia's Fluent or Simple theme —
AvaloniaEdit's search panel, anything `Markdown.Avalonia` styles through Fluent's keys — reach
for resource keys Semi does not define, and a `DynamicResource` that finds nothing paints an
invisible control ([UI-STYLE-GUIDE §2](UI-STYLE-GUIDE.md#2-why-we-have-our-own-tokens-semiavalonia-friction)).
`theme-audit`, a dotnet tool from the [DiffView](https://github.com/JanusMael/DiffView)
repository, inventories every key each theme defines per variant exactly as Avalonia resolves
it, scans a consumer for the keys it references, and reports what is undefined — and generates
a compat dictionary that closes the gap.

## What is here

| File | What |
|---|---|
| `src/ClaudeForge/Resources/Compat/FluentKeys.Semi.axaml` | Every key Fluent 12.1.2 defines that Semi 12.1.0.1 lacks: the 14 opaque `System*` colours and 7 accent shades aliased to Semi's own tokens (`SemiBackground0Color`, `SemiGrey*`, `SemiBlue*`), the alpha ramps and 1,912 control resources carried verbatim. Generated; never hand-edited |
| `src/ClaudeForge/Resources/Compat/SimpleKeys.Semi.axaml` | The Simple-theme sibling; Semi's high-contrast variants keep their own `HighlightColor` |
| `App.axaml` | Merges both after the app's own resource dictionaries |
| `docs/theme-audit-report.md` | The generated report: themes and variants, consumers, undefined keys per (consumer, theme, variant), the contrast matrix for DiffView's tokens, and the ledger of what each compat dictionary mapped, copied, restored and skipped |
| `.config/dotnet-tools.json` | The tool as a local dotnet tool |

## What the report says about ClaudeForge

Under Semi, ClaudeForge's views (`src/ClaudeForge`, `src/ClaudeForge.Avalonia`,
`src/LayeredEditors.Avalonia`) referenced seven keys that resolve to nothing in every variant:

| Key | With the compat dictionaries |
|---|---|
| `SystemControlBackgroundBaseLowBrush` | resolves (Fluent key) |
| `SystemControlBackgroundChromeMediumLowBrush` | resolves |
| `SystemControlErrorTextForegroundBrush` | resolves |
| `SystemControlForegroundBaseLowBrush` | resolves |
| `SystemControlForegroundBaseMediumBrush` | resolves |
| `SystemControlHighlightListLowBrush` | resolves |
| `SystemAccentColorBrush` | **unreachable, so the reference is gone** — no theme defines this key (Fluent has `SystemAccentColor` and `SystemControlHighlightAccentBrush`), so no compat dictionary could close it. Both uses, the "NEW" badge in each `PropertyEditorWrapper`, now take owned tint-pill pairs: `AppAccentBrush` / `AppAccentBackgroundBrush` in `App.axaml`, `LE.AccentBrush` / `LE.AccentBackgroundBrush` in `LayeredEditors.Avalonia/Themes/EditorColors.axaml` |

The compat dictionaries do not change what Semi defines: a key both define is Semi's, per
variant, including the high-contrast overrides.

`docs/theme-audit-report.md` is a snapshot: it is generated from DiffView against the
`../cl/ClaudeForge` checkout, so its "ClaudeForge under Semi" table still lists
`SystemAccentColorBrush` until the next regeneration picks the removal up.

## The other half: tokens this repo declares and nobody uses

`theme-audit` answers *"referenced here, defined by no theme"* — the undefined key that paints an
invisible control. The mirror question is *"declared here, referenced by nothing"*: a dead token.
Both are the same declared × referenced relation, read from opposite corners.

| | Declared here | Not declared |
|---|---|---|
| **Referenced** | healthy | ⛔ `theme-audit` — invisible control |
| **Not referenced** | ⛔ `NoDeadBrushTokensTests` — dead token | — |

⚠ **The dead half is enforced HERE, not by `theme-audit`, and deliberately.** The tool is a dotnet
tool in the DiffView repository, needs a checkout beside this one plus the `../../nuget-local`
feed that this repository's NuGet configuration deliberately does not carry, and is run on theme
pin bumps. Its report says of itself that it is a snapshot which goes stale until the next
regeneration. None of that can gate a change on the day it is made, and both tokens the first run
of the guard found — `InstallBannerCodeBorderBrush` and `SuggestionGroupHeaderBrush` — had been
dead long enough that one of them had started generating questions about whether the install
banner was missing a border.

ⓘ **A test rather than an MSBuild task, which is the closer local precedent.**
`CheckUnusedResxKeys` in `Directory.Build.targets` is the same shape of guard and solved the same
two traps first — it blanks comments so a `<see cref="…"/>` cannot keep a dead key alive, and it
trips on dynamic access with the note *"if a dynamically-built key family is genuinely intended,
extend this guard with an allowlist"*. ⚠ It is also **per-project**, taking one `ProjectDir` and
one `ResxPath`. Brush tokens are declared in `ClaudeForge/App.axaml` and referenced from
OpenCodeForge and the shared library, so the scan has to be repo-wide — the same reason
`DangerSurfaceMarkupTests` reads source text across assemblies instead of reflecting.

⭐ **What WOULD belong upstream is the inventory.** The tool already walks every consumer for the
keys it references, so it holds one side of the relation already; what its report lists is themes,
variants, consumers, undefined keys, the contrast matrix and the compat ledger — there is no
"declared and unreferenced" section. Adding one would make the dead-token picture available for
every consumer DiffView audits, not just this one, and this repository's test would stay as the
gate.

## Regenerating

The report and the dictionaries are produced from the theme sources at their pinned versions,
which the DiffView repository keeps as read-only checkouts under `reference/`. From a DiffView
checkout beside this one:

```bash
dotnet run --project src/ThemeAudit -- compat
```

```bash
dotnet run --project src/ThemeAudit -- report
```

DiffView's `theme-audit.json` names this repository as a consumer through
`../cl/ClaudeForge/src/…`; its report is the source of `docs/theme-audit-report.md`. The
dictionaries here are generated with Semi's own variant keys (`{x:Static semi:SemiTheme.…}`),
which compile because this project references Semi; DiffView's copies key them through a
stand-in type because it does not.

To run the tool here directly (the quick per-directory key count needs no theme sources):

```bash
dotnet tool restore --add-source ../../nuget-local
```

```bash
dotnet tool run theme-audit inventory src/ClaudeForge
```

`../../nuget-local` is the sibling feed the package is packed into
(`scripts/pack-theme-audit.sh` in DiffView); it is not in this repository's NuGet configuration,
so pass it explicitly.
