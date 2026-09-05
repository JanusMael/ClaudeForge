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
`src/LayeredEditors.Avalonia`) reference seven keys that resolve to nothing in every variant:

| Key | With the compat dictionaries |
|---|---|
| `SystemControlBackgroundBaseLowBrush` | resolves (Fluent key) |
| `SystemControlBackgroundChromeMediumLowBrush` | resolves |
| `SystemControlErrorTextForegroundBrush` | resolves |
| `SystemControlForegroundBaseLowBrush` | resolves |
| `SystemControlForegroundBaseMediumBrush` | resolves |
| `SystemControlHighlightListLowBrush` | resolves |
| `SystemAccentColorBrush` | **still undefined** — no theme defines this key (Fluent has `SystemAccentColor` and `SystemControlHighlightAccentBrush`); the reference is a typo to fix in the view |

The compat dictionaries do not change what Semi defines: a key both define is Semi's, per
variant, including the high-contrast overrides.

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
