# ClaudeForge.Sdk.Claude — Agent Operational Guide

The Claude-domain half of the SDK. Read alongside the root
[`AGENTS.md`](../../AGENTS.md) and the neutral half's sidecar,
[`src/AgentForge.Sdk/AGENTS.md`](../AgentForge.Sdk/AGENTS.md).

---

## §1 Why this project exists

`AgentForge.Sdk` is meant to be the foundation two agent-tool GUIs sit on. Until
Phase 1 step 1f it was shared in name only: `IAgentConfigClient` declared five
accessors whose config surfaces exist purely because Claude defines them, so a
second product deriving from it would have inherited a contract it cannot
implement.

Everything here is on the Claude side because it failed a specific test — not
because of where its directory happened to sit. **Read the bodies, not the
names** (plan Risk 7); every reuse estimate this project made from a directory
name turned out wrong, and every one made after reading the code held.

| Surface | Why it does not generalize |
|---|---|
| `Hooks/` | The string `hook` appears **zero times** in either OpenCode schema. No `hooks` key, no event/matcher/command shape. OpenCode's nearest equivalent is *code* — plugins exporting handlers for ~28 named events — not config. |
| `Marketplaces/`, `Plugins/` | Claude's marketplace registry and the `plugin@marketplace` reference form, including the schema's array-of-strings component-selection variant. |
| `Models/` | The model ↔ effort-level ↔ auto-mode relationships and the nearest-analog coercion rule. The *relationships* are the product knowledge; a list of ids would not be. |
| `Permissions/` | Allow/Deny/Ask over Claude's rule syntax (`Bash(git commit:*)`, `mcp__server__tool`, `WebFetch(domain:...)`) evaluated **first-match**. OpenCode uses per-tool glob keys evaluated **last-match** — see §3. |

A shared permission *vocabulary* (the concepts, not the syntax) is Phase 6, and
it is much smaller than early drafts assumed. Do not pre-generalize toward it.

## §2 The one-directional dependency, and how it is held

```
ClaudeForge.Sdk.Claude  ──→  AgentForge.Sdk  ──→  AgentForge.Core
        (product)                (shared)             (shared)
```

`AssemblyLayeringTests` fails the moment any `AgentForge.*` project references a
`ClaudeForge.*` or `OpenCode.*` one, checking both the `.csproj` XML and the
compiled reference tables. For *this* pair, a direct wrong-way `ProjectReference`
is additionally caught by MSBuild as a restore-time circular dependency, because
every shared project is upstream of this one.

Three pieces make the split work; changing any of them without the others
re-couples the assemblies:

| Type | Role |
|---|---|
| `IClaudeConfigClient` | `IAgentConfigClient` + the five Claude accessors. The neutral interface no longer names a Claude type. |
| `ClaudeConfigClientBase` | Sits between the concrete clients and `AgentConfigClientCore`, holding the accessor wiring so both clients share one copy. This is what keeps the dependency one-directional instead of circular — a second product derives its own equivalent from the same core. |
| `InternalsVisibleTo("ClaudeForge.Sdk.Claude")` in `AgentForge.Sdk.csproj` | The accessors project the workspace through `GetEffectiveNode` / `GetScopeValue` and report mutations via `RaiseChangedFromAccessor`. Those are `internal` deliberately — they traffic in `JsonNode`, which the public SDK surface keeps out. An assembly attribute, not a reference. |

**If you add an accessor here**, wire it on `ClaudeConfigClientBase`, declare it
on `IClaudeConfigClient`, and take `AgentConfigClientCore` in its constructor
unless it genuinely needs Claude-specific base members — `HooksAccessor` takes
`ClaudeConfigClientBase` precisely because it does (see §4).

## §3 Permission rule order is semantically load-bearing

Claude evaluates the **first** matching rule; OpenCode evaluates the **last**.
Any future merge or round-trip path must not re-serialize permission keys into a
different order — a higher layer's broad `"*"` placed before a lower layer's
narrow `deny` silently inverts the user's intent. This bit the OpenCode spike and
is the reason the plan treats key order as data, not formatting.

## §4 Schema-declared hook vocabulary lives on the client, not the accessor

`ClaudeConfigClientBase.SchemaHookEvents()` / `SchemaHookCommandVariants()` feed
`IHooksAccessor.KnownEvents` / `KnownCommandTypes`, so headless callers and the
GUI editor read one source of truth — descriptions and per-field tooltips
included, not just names.

Two things to know before touching them:

- They were on `AgentConfigClientCore` and moved here because they *return Hooks
  types*. They read the flattened schema tree via `protected CachedSchemaNodes`,
  which stays on the neutral core because a schema node tree carries no product
  knowledge.
- `SchemaHookEvents` has a **second source** for a reason. When a client is built
  via `FromExistingWorkspace` (the GUI's path) `OpenAsync` never runs, so
  `CachedSchemaNodes` is null; it then reads names + descriptions straight from
  the bundled schema. Delete that fallback and the editor's per-event labels go
  blank in the GUI while every headless test stays green.

## §5 Opaque-JSON preservation — do not "simplify" these

`HookEvent` and `MarketplaceEntry` carry `internal JsonObject?` bags
(`PreservedFields`, and on `HookEvent` also `OpaqueInnerJson`) so a save
round-trip cannot silently drop schema fields the SDK does not model. They are
`internal` to keep `JsonObject` off the public surface; the GUI reaches them
through `InternalsVisibleTo`.

`OpaqueInnerJson` specifically preserves the whole inner object when the `type`
discriminator is unrecognized (`agent`, `http`, future additions). Without it,
`ParseCommandType` falls back to `Command` and the type is lost on save —
`PreservedFields` alone did not cover this, because the discriminator itself was
the thing being dropped.

## §6 Test seams

Same seams as the neutral half, with the clients now living here:

| Seam | How to use |
|---|---|
| `internal ClaudeCodeClient(ConfigScope, SchemaRegistry)` | Inject a test-controlled `SchemaRegistry` |
| `ClaudeCodeClient.FromExistingWorkspace(workspace, scope, registry)` | Supply a pre-built workspace (GUI migration tests) |
| `InternalsVisibleTo("ClaudeForge.Sdk.Claude.Tests")` | The intended home for this project's tests |
| `InternalsVisibleTo("ClaudeForge")` / `("ClaudeForge.Tests")` | Expiring grants, same lifetime as the `AgentForge.Sdk` ones — remove when 4.3.7 retires the pre-loaded-workspace wrap path |

Tests live in `tests/ClaudeForge.Sdk.Claude.Tests` — the accessor round-trips, the
permission matchers, the schema-driven hook vocabulary, and the client lifecycle.
They also hold `InternalsVisibleTo` on `AgentForge.Sdk`, because round-trip tests
read the raw stored node back through the internal `GetScopeValue`.

> Keep the split honest when adding tests. A test that asserts neutral behaviour —
> the scope model, save/validate, the threading contract, backup, MCP servers, env —
> belongs in `AgentForge.Sdk.Tests` against its local `TestConfigClient`, not here.
> `AssemblyLayeringTests` scans `tests/` as well as `src/`, so a shared test project
> reaching for a real client to save effort fails the build.
