# Essentials Page — Pinned High-Importance Settings

> **Status:** Shipped. Cards are hand-curated; promoting / demoting a card means editing `EssentialsViewModel.BuildCards` directly.

## Why this page exists

A handful of Claude Code settings have outsized impact on cost, quality, security, or behaviour stability. Without prominent surfacing they get buried — sometimes deep in nested schema groups, sometimes (for the token-budget env vars) not in the JSON schema at all. New users land in the GUI and have no clear "things to set up first" surface; experienced users miss recently-added knobs that materially change runtime behaviour.

The **Essentials** nav node — synthetic, pinned to the top of the navigation tree, sibling pattern to Memory / Effective Settings / About — gathers these settings as one-glance editable cards.

## What's pinned (11 cards, four severity tiers)

| # | Card | Surface | Severity | Why it matters |
|---|---|---|---|---|
| 1 | **Max Thinking Tokens** (`MAX_THINKING_TOKENS`) | settings.json `env` + OS env var | amber (quality) | Default may truncate complex reasoning. |
| 2 | **Max Output Tokens** (`CLAUDE_CODE_MAX_OUTPUT_TOKENS`) | settings.json `env` + OS env var | amber (quality) | Default may truncate long file edits / generated code. |
| 3 | **Auto-trust project MCP servers** (`enableAllProjectMcpServers`) | settings.json | red (security) | Hostile project repo could ship a malicious MCP server. |
| 4 | **Sandbox enabled** (`sandbox.enabled`) | settings.json | red (security) | Disabling lifts file-system + network restrictions on every shell command. |
| 5 | **Sandbox allowed domains** (`sandbox.network.allowedDomains`) | settings.json | red (security) | Without entries, network operations inside the sandbox fail. |
| 6 | **Model** (`model`) | settings.json | red (cost) | Picking opus when sonnet would do is the difference between a cheap and a costly month. |
| 7 | **Effort level** (`effortLevel`) | settings.json | amber (cost) | Higher levels can cost dramatically more on the same task. |
| 8 | **Fast mode** (`fastMode`) | settings.json | amber (cost) | Trade-off between latency / cost and answer quality. |
| 9 | **Auto-updates channel** (`autoUpdatesChannel`) | settings.json | blue (behaviour) | "latest" gets new features sooner but ships occasional regressions. |
| 10 | **Auto-memory enabled** (`autoMemoryEnabled`) | settings.json | blue (behaviour) | Captures session-level "memory" facts that may not be desired in shared environments. |
| 11 | **Disable bypass-permissions mode** (`permissions.disableBypassPermissionsMode`) | settings.json | red (security) | Locks out the "skip every prompt" mode; recommended for shared / managed environments. |

Card #11 mirrors the existing precedent — `disableBypassPermissionsMode` already had its own synthetic search hit and amber callout — so new users see the security knob without having to discover the Permissions page first.

## Architecture — three layers, SDK-first

### Layer 1 — SDK: `IEnvAccessor`

```
src/ClaudeForge.Sdk/Env/
├── IEnvAccessor.cs       (interface)
├── EnvAccessor.cs        (impl over ClaudeConfigClientCore.GetScopeValue("env"))
└── EnvVarKey.cs          (well-known key constants)
```

Mirrors the shape of `IPermissionsAccessor` / `IHooksAccessor`: typed convenience properties for the well-known high-importance keys (`MaxThinkingTokens` / `MaxOutputTokens` / `DisableAutoMemory` / `DisableAutoUpdater` / `AnthropicModel`) plus a generic dictionary surface for arbitrary env vars. Reads are lenient (returns `null` on parse failure rather than throwing); writes route through the standard `SetValue` / `RemoveValue` path so the workspace lock and `Changed` event are honoured uniformly.

The OS-level env-var surface (Windows registry, shell profiles) is intentionally NOT inside the SDK — that's owned by `EnvironmentEditorViewModel` + `IEnvironmentProvider` in the GUI assembly. Keeping the SDK accessor focused on the persisted-config slice means non-GUI consumers (the MCP server, future CLI dump) can use it cleanly without dragging in `Environment.GetEnvironmentVariable` and its registry-walking platform constraints.

Wired into `IClaudeConfigClient.Env` alongside `Permissions` / `Hooks` / `McpServers` / `Marketplaces` / `Plugins`. 23 unit tests pin the contract end-to-end against a real on-disk workspace.

### Layer 2 — GUI: synthetic Essentials nav page

```
src/ClaudeForge/ViewModels/
├── EssentialsViewModel.cs           (orchestrator — builds 11 cards, dispatches read/write)
├── EssentialsCardViewModel.cs       (per-card model — value, source labels, danger predicate)
└── EssentialsCardKindConverters.cs  (Kind → bool converters for AXAML IsVisible bindings)

src/ClaudeForge/Views/
├── EssentialsView.axaml             (card list + per-Kind editor templates)
└── EssentialsView.axaml.cs          (empty code-behind)
```

`EssentialsCardKind` is a flat enum (Bool / Int / EnumString / StringList) discriminating which inline editor surface a card renders. Kept as an enum (rather than a polymorphic class hierarchy) so the AXAML can switch on it via Kind→bool converters and so the card list is a single `ObservableCollection<EssentialsCardViewModel>` instead of a polymorphic list.

Each card carries:

- A pre-built **`SeverityBrush`** (red / amber / blue) for the severity-dot in the header.
- A localised **title + body** ("why this matters").
- A **`ViewInGroupTitle`** that powers the "View in &lt;group&gt;" deep-link button.
- An **`IsDangerPredicate`** evaluated reactively — when the value enters a known-unsafe state (e.g. `enableAllProjectMcpServers = true` or `sandbox.enabled = false`), a standing red banner appears above the editor surface.
- A **one-time amber callout** (`ShowAmberCallout`) that appears when the user arrives at the card via a synthetic search result. Auto-dismissable on first interaction.
- For env-var cards, an **"Effective source" sub-row** showing which of (settings.json env, OS user, OS machine) is contributing.

The orchestrator (`EssentialsViewModel`) owns the curated list. It hands each card a pair of read / write delegate closures so the card stays type-agnostic about which underlying accessor it talks to (some go through `IEnvAccessor`, some through `IClaudeConfigClient.SetValue<T>` for plain JSON paths, some through the future-typed accessors).

The VM is cached as `_essentialsVm` in `MainWindowViewModel` and survives workspace reloads (H-2 contract) so:

- Synthetic-search amber callouts aren't dismissed before the user reads them.
- In-flight integer / string-list edits aren't lost mid-keystroke when a file-watcher reload arrives.

A reload calls `RefreshAsync(newClient)`, which re-binds every card's read delegate to the post-reload SDK client and re-reads the values.

### Layer 3 — synthetic search + contextual amber callout

`SearchViewModel.EssentialsTriggers` is a static dictionary mapping each card id (the `EssentialsCardViewModel.Id`, which doubles as the search-deep-link key) to a list of trigger phrases. When the search query contains (or is contained by, for partial typing) any trigger phrase, a synthetic search hit is added pointing at the Essentials node with the card id in `PropertyKey`.

`MainWindowViewModel.SelectSearchResult` picks up `IsSynthetic` + `PropertyKey` for results whose `Node.Editor` is the Essentials VM and calls `ActivateAmberCalloutFor(cardId)` — same flow as the existing `PermissionsEditorViewModel.ActivateDangerHint()` for the `--dangerouslySkipPermissions` synthetic.

A `SearchViewModelTests` fixture (`EssentialsTriggers_TableCovers_EveryCardId`) asserts every card id has at least one trigger phrase — so a future card promotion isn't silently un-searchable.

### Localization

All new user-visible strings (~40 keys: page header + card titles + bodies + danger banners + amber callout + source labels) flow through `Strings.resx` + `Strings.zh-CN.resx` (placeholders with `TODO zh-CN translation` comments) + `Strings.Designer.cs`.

## Testing

| Suite | Coverage |
|---|---|
| `tests/ClaudeForge.Sdk.Tests/Env/EnvAccessorTests.cs` | 23 tests — Get/Set/All round-trip, typed property setters (int parse, bool parse, null = remove), per-scope reads, save+reload survival, argument validation. |
| `tests/ClaudeForge.Tests/ViewModels/EssentialsViewModelTests.cs` | 19 tests — card list shape, value bindings, save round-trip through SDK, danger-banner toggle, env-var source attribution, amber-callout deep-link, reload contract. |
| `tests/ClaudeForge.Tests/ViewModels/SearchViewModelTests.cs` (extended) | 5 new tests — synthetic-hit production per trigger phrase + table-coverage guard. |
| `tests/ClaudeForge.Tests/Headless/ReloadHardeningTests.cs` (extended) | 1 new test — `_essentialsVm` survives reload (H-2 contract). |

## Adding a new card

1. Add the `Strings.resx` entries (Title / Body / optionally DangerBanner). Mirror in `Strings.zh-CN.resx` with `TODO zh-CN translation`.
2. Append the `Designer.cs` accessor properties.
3. In `EssentialsViewModel.BuildCards`, append a new `list.Add(new EssentialsCardViewModel(...))` block.
4. Add the card id to `SearchViewModel.EssentialsTriggers` with at least one trigger phrase.
5. Update `EssentialsViewModelTests.Cards_PinnedSet_HasElevenCards` to the new count.
6. Update this doc's "What's pinned" table.

## Promoting a card to the Essentials page (vs. just editing it in its home group)

A setting earns Essentials placement when **at least one** of the following applies:

- Mis-configuration produces a silent bad outcome the user only notices later (truncated reasoning, runaway costs, MCP servers auto-trusted, sandbox accidentally disabled, …).
- The setting has both a `settings.json` and OS env-var surface, and the user needs to see both at once to debug "why isn't my override taking effect?".
- The setting is security-critical (red severity).

Settings that don't meet these bars stay in their schema-driven home group; the Essentials page is deliberately small (11 cards) so the "things to set up first" framing isn't diluted.
