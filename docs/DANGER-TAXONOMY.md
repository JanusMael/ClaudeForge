# Danger taxonomy

How ClaudeForge and OpenCodeForge decide that a setting deserves your attention, and what stops
the two products' answers drifting apart.

> **Status:** Phase 11.5, complete. Four surfaces render severity; six guards keep the tables and
> the rendering honest. Numbers below were counted from the source, not estimated.

---

## 1. The tenet

**A settings editor that can turn off a safety boundary owes the user a warning *before* they turn
it off, not after.**

Everything here follows from that one sentence. In particular it forces the shape of the core
type: a single "is this dangerous?" flag cannot distinguish a permission switch sitting at its safe
default from an ordinary cosmetic setting — both answer *no* — so an editor built on it can only
warn once the damage is done. Two fields are needed.

```csharp
public sealed record DangerAssessment(AppSeverity Severity, bool IsDangerNow, string? Explanation);
```

| Field | Question it answers | Present when the value is safe? |
|---|---|---|
| `Severity` | *How much does this knob matter, in principle?* | **Yes** — this is what lets a tree dot a knob nobody has touched |
| `IsDangerNow` | *Is the value it currently holds the unsafe one?* | No |
| `Explanation` | *What happens if it is?* — the consequence, not the mechanism | Yes, whenever the tier is non-neutral |

The two fields drive two different pieces of UI, and conflating them breaks both:

- **The dot** renders from `Severity`. It is always on for a triaged knob, which is the point.
- **The banner** renders from `IsDangerNow`. It is rare, so it never becomes wallpaper — on a page
  of safe defaults, no banner appears at all.

⚠ **The same split governs aggregates.** The save dialog's headline counts `IsDangerNow`, never
the tier. Counting tiers would fire on nearly every real save, and a headline that always shows is
a headline nobody reads. Measured on the running app: three toggled settings, all three dotted,
headline says **1**.

### Why a classifier and not a schema annotation

An earlier draft of the plan put danger on `IEditorSchema` as a per-property field. That shape
cannot express what the tables actually need:

1. **`IEditorSchema` is per-property and scope-independent; danger is neither.**
   `provider.*.options.apiKey` is Caution in a user-global file and Critical in a project file,
   because a project file is committed to git. `share` is dangerous only when its value is `auto`.
   A static field expresses neither the escalation nor the value predicate.
2. **`IEditorSchema.Metadata` had zero consumers.** The plan called it "an open extensibility bag
   for exactly this" and implied plumbing to follow; `grep '.Metadata\['` across `src/` returned
   nothing.

So policy is supplied by the host as an `IDangerClassifier`, and the editor library owns none of
it. Which knobs are dangerous is a statement about *an agent's threat model*, and the two products'
models differ — OpenCode has `share` and a bindable HTTP server; Claude has neither.

---

## 2. The tiers

`AppSeverity` (in `LayeredEditors.Abstractions`), ordered so a numeric comparison is meaningful.

| Tier | Value | Means | Glyph | Light | Dark |
|---|---|---|---|---|---|
| `Neutral` | 0 | Triaged, nothing notable | `○` | `#666666` | `#AAAAAA` |
| `Info` | 1 | Worth knowing, changes nothing about safety | `●` | `#0050B3` | `#6BB1F2` |
| `Caution` | 2 | Weakens a boundary, or widens what the agent may reach | `◆` | `#874400` | `#F0A03A` |
| `Critical` | 3 | Removes a boundary, or publishes a secret | `▲` | `#A8071A` | `#F99090` |

⛔ **Dual coding is normative, not advisory.** The glyph differs per tier as well as the colour,
because colour alone excludes colour-blind users and **Critical-vs-Caution sits exactly on the
red–green axis** — the single most common deficiency. The shape carries the tier on its own.

⛔ **Geometric shapes, never emoji.** Emoji need a system emoji font and render as tofu without
one. See [`AVALONIA-GOTCHAS.md`](./AVALONIA-GOTCHAS.md).

⛔ **A coloured shape conveys nothing to a screen reader.** Every severity indicator carries
`AutomationProperties.HelpText` naming the tier *and* the consequence. `HelpText`, never `Name`:
on a `TextBlock` the `Text` always wins and an explicit `Name` is ignored outright — measured via
UIA, and the reason 15 such attributes elsewhere in this repo are no-ops.

### Colours live in the theme, never in a view-model

Tokens are declared per theme variant in **both** apps' `App.axaml` and resolved through
`AppSeverityToBrushConverter`. A themed key looked up with a null variant resolves to *nothing*, so
a flat lookup silently falls back to the converter's backstop hex — reintroducing the single
hardcoded colour the token migration existed to delete.

This is enforced, because it has already gone wrong twice:

- `LE.DangerText` was referenced nine times and declared zero times. A missing `DynamicResource` is
  not a build error and logs nothing.
- `SaveChangeEntryViewModel.KindBackground` returned `#F57C00` for a change pill, giving its white
  glyph **2.70:1** — under the 4.5:1 text floor and under even the 3.0:1 non-text one. The source
  comment defending the choice reasoned entirely about hue and measured nothing. *A colour nobody
  can see in the theme files is a colour nobody re-measures.* It is now
  `AppChangeKindModifiedBrush` = `#B45309`, which keeps the hue argument (26°, still clearly not
  red's 0°) and measures **5.02:1**.

---

## 3. The matcher

`TableDangerClassifier` turns a product's `DangerRule` table into an `IDangerClassifier`. Mechanism
lives in the shell; data lives in each product's `Adapters/` folder — the same split as
`SchemaPageLayout`.

**Patterns** are dotted, with `*` matching exactly one segment:
`permission.bash`, `provider.*.options.apiKey`, `agent.*.permission.*`.

**Specificity:** most segments wins; ties break toward fewest wildcards. So `permission.bash` beats
`permission.*` beats `permission`.

⭐ **The tier is inherited by descendants; the value predicate is not.**

A path with no rule of its own takes the tier and explanation of its nearest ancestor — which is
what lets a per-top-level-key table cover a whole nested schema, and is why the coverage guard over
top-level keys is meaningful. But `Unsafe` and `EscalatesAt` run **only on an exact match**,
because a predicate written for an object cannot be handed one of that object's leaf values. An
inherited assessment therefore always reports `IsDangerNow: false`: it says *this area deserves
attention*, never *this specific value is wrong*.

### ⚠ Value currency

Predicates receive values in the editor value currency (`IEditorValue`), never raw serialization
types:

| Expect | Never |
|---|---|
| `long` | `int` |
| `IReadOnlyList<object?>` | `JsonArray` |
| `IReadOnlyDictionary<string, object?>` | `JsonObject`, `JsonNode` |

A rule that pattern-matches the wrong type **never fires and reports safe**. Callers holding JSON
must convert with `JsonCurrency.FromJsonNode` first.

---

## 4. The scope-sensitivity rule

The same path can be `Caution` at one scope and `Critical` at another. The canonical case is a
secret: an API key in a user-global file is a local secret; the identical key in a project file is
committed to git and published to everyone with repo access.

```csharp
EscalatesAt = id => string.Equals(id, ConfigScope.Project.Id, StringComparison.OrdinalIgnoreCase),
EscalatedTier = AppSeverity.Critical,
```

⛔ **Compare against the production scope supplier, never a literal.** Slice 2's `apiKey`
escalation shipped comparing against `"Project"` while the real scope id is lower-cased
`"project"`, so it **never fired in the app** — and 85 green tests missed it because each one fed
the rule the same literal the rule compared against. A test whose input is the constant the code
compares to cannot fail on casing.

⚠ **A `null` scope must never *raise* severity.** An unknown scope is not evidence of danger.

### Which scope a surface passes — the load-bearing rule

> **A surface asks the editor *iff* it lacks the inputs.**

An assessment is a function of *path + scope + value*. A surface holding all three classifies with
them; a surface holding only a path must delegate, or it will contradict the row it points at.

| Surface | Holds | Therefore | Scope it passes |
|---|---|---|---|
| Settings row | all three | classifies | the editing scope |
| Search hit | path only | **delegates** via `IDangerAnnotatedEditor` | — |
| Effective row | all three | classifies | the **winning** scope |
| Save preview | all three | classifies | the **target file's** scope |

⭐ **Their dots may legitimately disagree, and that is correct.** A key can be Caution at the scope
you are editing and Critical once a committed project file wins the merge. Do not "unify" them.

⛔ **A diff surface resolves its value from the document root, never from the diff.** `JsonDiff`
files an array change under the *array's* path but carries only the changed **element** as its
value. A rule written for `permissions.allow` expects a list, would be handed one element's string,
match no type pattern, and answer *nothing wrong right now* — a silent false negative on exactly
the keys the save dialog exists to catch.

⛔ **`null` and `Unremarkable` are different answers** from `IDangerAnnotatedEditor.AssessDanger`,
even though both render as no dot. `null` means *not mine, keep looking*, which is what lets a
caller walk a list of editors; `Unremarkable` means *mine, and triaged as nothing notable*.
Collapsing them makes the walk stop at the first editor it asks.

---

## 5. Both products' tables

Policy travels **per product**, never per app. One ClaudeForge window hosts Claude Code *and*
Claude Desktop, and its save dialog renders both at once.

| Table | Rules | Critical | Caution | Info | Neutral | Value predicates | Scope escalations |
|---|---|---|---|---|---|---|---|
| `ClaudeDangerTable.Settings` | 66 | 27 | 28 | 8 | 3 | 22 | 3 |
| `OpenCodeDangerTable.Config` | 62 | \* | \* | \* | \* | \* | 1 |
| `OpenCodeDangerTable.Tui` | 13 | \* | \* | \* | \* | \* | 0 |

<sub>\* OpenCode's two tables share a source file; the combined split is 27 Critical / 24 Caution /
22 Info / 2 Neutral across 75 rules.</sub>

Claude's 66 rules cover **142 top-level schema keys** — the count surprised the plan, which
estimated "~25". Nested paths are covered by tier inheritance.

⭐ **Every top-level key gets an entry even when the answer is "unremarkable".** Silence and
"triaged as harmless" are different states, and only the explicit entry survives a schema refresh
without a guard failure.

⚠ **Claude Desktop has no table, deliberately.** Nobody has triaged its keys. Absence renders as
no severity anywhere, which is honest; inheriting Claude Code's would be a *false claim*, not a
convenient default — the two schemas share almost no key names, so it would be blank on most rows
and confidently wrong on any that collide (`env` is in both).

The policy is stated **once per product** and read by every consumer:

| Carrier | Read by |
|---|---|
| `ProductSection.Danger` | the settings pages, via `NavigationTreeBuilder.BuildGroups` |
| `DirtySource.Danger` | the save dialog, via `SaveDialogBuilder.Build` |

⛔ `ClaudeEditorFactoryConfig.CreateDefault` must **not** default a table: one factory type serves
both Claude products, so a default labels Claude Desktop with Claude Code's policy.

---

## 6. The guards

Six, because slice 6 added one the original five did not anticipate.

| # | Guard | Catches | Where |
|---|---|---|---|
| 1 | Non-nullable `AppSeverity` | An untyped or absent severity | Compile error — no test possible |
| 2 | **Schema-key coverage** | A schema refresh smuggling in an untriaged key | `ClaudeDangerTableTests.EverySettingsSchemaKeyIsClassified`, plus `NoClassifiedPathIsAStaleSchemaKey` for the reverse |
| 3 | **Per-surface behaviour** | A surface classifying with the wrong scope or value | `EffectiveRowDangerTests`, `SavePreviewDangerTests`, `SearchDangerAgreementTests` |
| 4 | **Dual-coding + markup scan** | A severity bound to a brush with no glyph, or annotated with `Name` instead of `HelpText` | `DangerSurfaceMarkupTests` — 4 view-model types across 7 markup files, each with a minimum-file-count assertion so it cannot pass vacuously |
| 5 | **No raw hex in view-models** | A colour placed beyond the theme's reach | `GuardRawHexInViewModels` in `Directory.Build.targets` — build error |
| 6 | **End-to-end wiring** | The table never reaching the real window | `DangerWiringEndToEndTests` |

### ⭐⭐ Why guard 6 exists

Guard 6 was added because a canary with a **deliberately empty prediction** measured a gap. Breaking
`MainWindowViewModel`'s wiring — passing `null` to `BuildGroups` instead of the section's table —
reddened **nothing**. Guard 3 drives the *factory* directly; guard 4 reads the *markup*; between
them sat the wiring joining the two, and it was unguarded. **Every row in the app would have
rendered with no severity while the whole suite stayed green.**

`DangerWiringEndToEndTests` builds the real window, loads real workspaces, and asserts **both**
directions — Claude Code's editors carry a classifier, Claude Desktop's carry none. Asserting only
the first would pass a change that hands Desktop the wrong table.

### ⚠ Guard 5's scope is what makes it enforceable

A repo-wide hex ban is unshippable: ~230 literals live in AXAML (`App.axaml` **is** the token
declaration) and 45 more in C#, essentially all legitimate — syntax colouring in
`JsonHighlightBlock`, the scope and status brush converters, `CodeInline`, and every `BrushHelper`
fallback, which exists precisely so a missing token still renders. Those are colour *definitions*
and resolution *backstops*.

A view-model returning a colour is different in kind: it puts presentation in the layer that has no
view, and it puts the value beyond the theme's reach in both variants at once. So the guard scans
`*ViewModel.cs` and `ViewModels/` only, stripping comments first — prose citing an old hex is
documentation, not a colour decision.

Companion coverage tests assert the tokens exist in both variants of both apps, and — for the
change-kind pills — that each fill keeps its white glyph at or above 4.5:1.

⚠ **`AppChangeKind*` tokens are identical in light and dark, on purpose**, and are the one family
where that is correct. A severity token is a *foreground* on a themed surface, so one literal
serving both themes is the bug that moved it into the theme in the first place. A change-kind token
is a *fill* behind a white glyph: the pair that must hold is glyph-vs-fill, and lightening the fill
for dark mode trades that away for fill-vs-surface, which is redundant with the `+`/`-`/`~` glyph
and its accessible name anyway.

---

## 7. Adding a rule

1. Add the entry to the product's table in its `Adapters/` folder. Write `Why` as the
   **consequence**, not the mechanism — *"auto-approves every tool"*, not *"sets permission to
   allow"*. Guard 2 fails on a missing or empty explanation.
2. If the value matters, add `Unsafe`. Take the editor value currency (`long`, not `int`;
   `IReadOnlyList<object?>`, not `JsonArray`) and remember it fires only on an exact path match.
3. If the scope matters, add `EscalatesAt` — comparing against `ConfigScope.<Name>.Id`, never a
   string literal.
4. Add a test asserting **both** the safe and unsafe values, and for a scope-sensitive rule
   **both** scopes. Assert against the production supplier, not a local copy of the constant.
5. Run the suite. Guard 2 will tell you if you have left a sibling key untriaged.

## See also

- [`UI-STYLE-GUIDE.md`](./UI-STYLE-GUIDE.md) §3b — the rendering rules and the full token table
- [`AVALONIA-GOTCHAS.md`](./AVALONIA-GOTCHAS.md) — automation-name traps, emoji/tofu, themed lookup
- [`../AGENTS.md`](../AGENTS.md) §1 — the danger family stated as a hard invariant
