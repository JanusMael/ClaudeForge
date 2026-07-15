# Model catalog — `model` / `effortLevel` / `permissions.defaultMode`

The allowed values for these three inter-related settings, and the rules tying
them together, live in **one bundled config file** rather than scattered
hardcoded lists. This doc is the canonical reference for the data, the SDK
contract, the editor behavior, and how the file is kept honest.

## Why

`model`, `effortLevel`, and `permissions.defaultMode` are not independent:

- A model determines which effort levels are valid — **Opus 4.6 / Sonnet 4.6
  don't support `xhigh`**; **Haiku exposes no effort at all**; `max` is
  **session-only** (never persisted to `settings.json`); `[1m]` is a *suffix*,
  not a separate model.
- `permissions.defaultMode = auto` is **gated**: it needs an auto-capable model
  (Opus 4.6+/Sonnet 4.6+) and is silently ignored outside **User** scope.

Encoding these relationships once, as data, lets every editor stay consistent
and lets the lists evolve without touching view-model code.

## Layers (SDK-first)

```
ClaudeForge.Core/Assets/ModelCatalog/        ← bundled data (embedded resource)
  model-catalog.json            base — the source of truth
  model-catalog.schema.json     Draft 2020-12 schema validating the base
  model-catalog.overlay.json    hand-curated RFC-7396 overrides (ships empty)
ClaudeForge.Core/Catalog/        ModelCatalog domain + ModelCatalogLoader
        ▲
ClaudeForge.Sdk/Models/          IModelCatalogAccessor  →  IClaudeConfigClient.Models
        ▲                        (queries + coercion rule; the public contract)
ClaudeForge (app)                view-models consume client.Models; localized
                                 labels via ViewModels/Catalog/CatalogLocalization
```

The catalog is a structural source of truth; **localized display text is a GUI
concern** kept app-side (Core/Sdk never reference `Strings`). Brand model labels
("Opus 4.8") are product names and live in the data.

The runtime JSON **schema** (`claude-code-settings.json`) stays separate from the
catalog; a parity test (`ModelCatalogSchemaParityTests`) locks the schema's
`effortLevel` / `permissions.defaultMode` enums to the catalog so drift fails CI.

## Data shape

```jsonc
{
  "schemaVersion": 1,
  "models": [
    { "id": "claude-opus-4-8", "alias": "opus", "label": "Opus 4.8",
      "legacy": false, "supports1m": true,
      "supportedEffortLevels": ["low","medium","high","xhigh","max"],
      "defaultEffortLevel": "high", "supportsAutoMode": true },
    { "id": "claude-haiku-4-5", "alias": "haiku", "label": "Haiku 4.5",
      "legacy": false, "supports1m": false,
      "supportedEffortLevels": [], "defaultEffortLevel": null,
      "supportsAutoMode": false }
    // … sonnet 5, fable, legacy opus-4-7/4-6 + sonnet-4-6
  ],
  "aliases": { "opus": "claude-opus-4-8", "sonnet": "claude-sonnet-5", … },
  "effortLevels": [ { "id": "low", "order": 0, "persists": true }, …,
                    { "id": "max", "order": 4, "persists": false } ],
  "defaultModes": [ { "id": "default", … },
                    { "id": "auto", "requiresAutoCapableModel": true, "userScopeOnly": true }, … ]
}
```

`[1m]` is a per-model `supports1m` flag (the UI synthesizes `opus[1m]`
suggestions), not a separate row.

## SDK contract — `IClaudeConfigClient.Models`

Mirrors the `IEnvAccessor` / `IPermissionsAccessor` precedent. Key methods:

| Member | Purpose |
|---|---|
| `Resolve(idOrAlias)` | strip `[1m]` → alias → model (or `null`) |
| `SupportedEffortLevels(model)` / `PersistableEffortLevels(model)` | raw / persistable (drops `max`) effort set; unknown model → lenient (all); Haiku → empty |
| `IsEffortSupported(model, effort)` | membership |
| `NearestAnalogEffort(model, effort)` | the **coercion rule** — nearest supported persistable level (tie-break lower); `null` when the model has no effort |
| `SupportsAutoMode(model)` | model gate for `auto` |
| `IsDefaultModeAllowed(mode, model, scope)` | mode eligibility (`auto` needs a *known* auto-capable model AND User scope; unknown/unset model is lenient) |
| `ModelSuggestions(includeLegacy, include1m)` | editable model-picker suggestions |
| `AllModels` / `AllDefaultModes` / `AllEffortLevels` | catalogued entries + metadata |

The relationship queries and coercion rule are SDK domain logic (CLI/MCP-usable,
unit-tested headlessly). The lenient-unknown-model behavior keeps a hand-typed
custom id from blanking a dropdown.

## Editor behavior

- **Essentials (model + effort cards)** — the effort dropdown is filtered to the
  effective model's persistable levels; an invalidated effort **auto-coerces** to
  the nearest analog (e.g. `xhigh`/`max` → `high` on Sonnet 4.6) as an
  editing-scope override, shown in the Save-Changes preview. Haiku disables the
  effort control. A read-only "current model — supports …" indicator sits beside
  it. The model card is an editable AutoCompleteBox (catalog entries are
  suggestions). See `EssentialsViewModel.ApplyModelEffortConstraint`.
- **Permissions (`defaultMode`)** — `auto` is filtered out of the dropdown when
  ineligible (non-auto model or non-User scope) and an ineligible selection is
  coerced to `default` with an advisory. See
  `PermissionsEditorViewModel.ApplyDefaultModeConstraint`. `DefaultModeInfos`
  is projected from `client.Models.AllDefaultModes` (labels via
  `CatalogLocalization`).

Coercion writes always target the **editing scope** so the *effective* value
becomes valid; the Save-Changes preview surfaces every auto-edit.

## Keeping it honest

- `scripts/validate-model-catalog.ps1` — structural + cross-relationship checks
  (read-only; exits non-zero on any inconsistency).
- `.github/workflows/model-catalog-refresh.yml` — runs the validator + the
  `ModelCatalog` tests on every PR/push touching the catalog, plus weekly. The
  curated file is the source of truth (no scraping); edits flow through normal
  PRs. See [WORKFLOWS.md](../.github/WORKFLOWS.md).
- `model-catalog.overlay.json` is the refresh-safe seam for local overrides
  (RFC 7396 merge), exactly like the schema overlay.

## Updating the catalog

1. Edit `model-catalog.json` (verify model capabilities against the current
   Claude Code docs — esp. effort support and auto-capability).
2. `pwsh scripts/validate-model-catalog.ps1` → must pass.
3. `dotnet test ClaudeForge.slnx --filter "FullyQualifiedName~ModelCatalog"`.
4. If you added a model id/alias, mirror it into the schema overlay's
   `model.examples` if you want it in the generic editor's suggestion list.
