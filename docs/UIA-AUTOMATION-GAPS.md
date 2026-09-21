# UIA automation gaps

> Gaps found while driving ClaudeForge through UI Automation during the plans/00003 Phase B
> retest. **Every one of these has a workaround** — they are recorded anyway, because a
> workaround in a retest script is a cost paid again by every future harness, and because
> several of them are accessibility gaps wearing automation clothes.
>
> Status vocabulary: **Confirmed** — measured directly. **Candidate** — observed once, not yet
> isolated.

---

## G1 · No stable `AutomationId` on the controls automation must target — **Confirmed**

`Editing scope`, `Active profile` and `effortLevel` combo boxes, and the `Save all settings`,
`Reset`, `Add` and `Open project folder` buttons, all expose an **empty** `AutomationId`. The only
selector left is `Name`.

⛔ **`Name` is localized here, by policy.** This repo requires every user-visible string *and every
`AutomationProperties.Name`* to come from resx. So a harness that matches `Name -eq 'Editing scope'`
works in `en-US` and silently finds nothing under `--culture fr-FR` — and "finds nothing" is the
failure mode that reads as "the control is missing" rather than "the selector is wrong".

⭐ An `AutomationId` is **not** user-visible and is therefore exempt from localization. Setting one
on each interactive control costs nothing at runtime and makes every harness locale-proof.

**Workaround in use:** match on the `en-US` `Name`, and never run the harness under another culture.

---

## G2 · `PART_TextBox` is not unique — **Confirmed**

Every templated text editor exposes the template part's id, `PART_TextBox`. In one window dump the
model editor and the `modelOverrides` "model id" editor both carried it; they were told apart only
because their `Name` differed (`model` vs `model id`), and `Name` is again the property title.

Two properties whose titles collide would be indistinguishable to automation. ⓘ This is the same
template-part blind spot already recorded for accessibility: the name that matters sits on the
templated parent, and the focusable element is the inner part.

**Workaround in use:** disambiguate by `Name`, relying on titles being unique on the open page.

---

## G3 · Layered-value annotations are loose, unassociated `Text` nodes — **Confirmed**

When a property is shadowed by a higher scope, the tree renders the winning scope and value as
**sibling `Text` elements with no `AutomationId` and no relationship** to the property they describe:

```
[136] Text  PropertyNameLabel  model
[137] Text                     LOCAL
[138] Text                     (overridden)
[148] Edit  PART_TextBox       VALUE=[]
[150] Text                     LOCAL
[151] Text                     local-scope-wins
```

Nothing in the tree says that `[137]`–`[138]` and `[150]`–`[151]` belong to `[136]`. Association is
available only by **document order**, which is exactly the thing a tree is supposed to make explicit.

⚠ **This is an accessibility gap, not only an automation one.** The visual design conveys "LOCAL
wins, you are overridden" through proximity and styling; a screen-reader user gets four unattached
strings. `LabeledBy`, `ControllerFor`, or folding the annotation into the editor's
`AutomationProperties.Name`/`HelpText` would carry the meaning to both audiences at once.

⭐ The information itself is modelled properly in the library — `IEditorValue` carries
`EffectiveScope` and every editor view model sets `IsOverridden`. Only the *exposure* is missing.

**Workaround in use:** read by index proximity to the `PropertyNameLabel`, which is fragile.

---

## G4 · `ExpandCollapsePattern.Expand()` silently no-ops after repeated use — **Confirmed**

After several expand/collapse cycles on the `Editing scope` combo, `Expand()` returns without
error, `ExpandCollapseState` stays `Collapsed`, and the item list never realises — polled for 4s
across 8 samples, 0 items every time, on a combo reporting `IsEnabled=True` and
`IsOffscreen=False`. A `FindAll` issued afterwards threw
*"Unexpected HRESULT has been returned from a call to a COM component."*

⚠ **The silence is the problem.** A pattern call that fails loudly costs one retry; one that
succeeds while doing nothing produces `Could not select scope: User`, which reads like a missing
menu item and sends the next person looking in the wrong place.

ⓘ Consistent with the previously-measured instability of this app's UIA tree during and after
heavy traversal. Not yet isolated to Avalonia's automation peer versus the harness.

**Workaround in use:** restart the app to get a fresh tree, then perform the scope switch first.

---

## G5 · Severity is exposed as a bare glyph — **Candidate**

Risk/severity cells surfaced as `Text` nodes whose entire accessible name is `⚠`. If that is the
accessible name rather than decorative text, a screen reader announces the character, not
"Caution" or "Critical" — and automation cannot assert on severity without matching glyphs, which
is precisely the comparison already known to be unsafe here (equal point sizes are not equal drawn
sizes, and the glyphs were re-picked once for that reason).

Not yet confirmed: the node may be decorative with the severity carried elsewhere on the row.

**Next step:** dump one Properties-table row's full subtree and check whether any element carries a
severity *word*.

---

## G6 · The backup `Output folder` is read-only to automation — **Confirmed**

`Output folder` is an `Edit` exposing a `ValuePattern` whose `SetValue` throws
**"Value is read-only."** The only way to populate it is the adjacent
*Browse for backup output folder* button, which opens a native folder picker.

⛔ **This blocks the whole of E3 from automation**, because `Create Backup` is
`IsEnabled=False` until a destination exists — so a retest harness cannot take a
backup unaided, and backup/restore is the largest and most destructive piece of
shared machinery in the product.

⚠ A read-only field is a defensible UI choice (it stops a user typing a bad path).
What makes it a gap is that there is **no second route**: no `AutomationId` to
target, no command-line switch, and no writable alternative. One of those would
make the destructive path testable without a human in the loop.

**Workaround in use:** a human clicks Browse once per session; automation resumes
after.

---

## G7 · Invoking a disabled control throws a bare `System.Exception` — **Confirmed**

`Create Backup` while disabled:

```
Exception calling "Invoke" with "0" argument(s):
  "Exception of type 'System.Exception' was thrown."
```

The message names no control, no reason and no state. The caller only learns the
truth by separately reading `IsEnabled`, which is easy to forget precisely when a
script is long.

⚠ This is how a harness ends up reporting "Create Backup is broken" when the real
answer is "you never chose an output folder". An `ElementNotEnabledException` —
which UIA defines for exactly this — would say so.

**Workaround in use:** check `IsEnabled` before every `Invoke`, and treat a bare
`System.Exception` as "probably disabled".

---

## G8 · Virtualized lists are invisible to automation — **Confirmed**

The *Agents & Skills* page reports `rows=111 (agents+skills+commands, headers excluded)` while
exposing only the realized handful to UIA. Searching for seven known user-scope skills returned
**0 matches**, and the sanity check proved the measurement rather than the app was at fault: a
*known* key in an expanded group also returned 0. The same applies to the `Other · 33` env group,
whose children never entered the tree even after `ExpandCollapseState` reported `Expanded`.

⛔ **So "not found" carries no information on any virtualized surface here**, which removes the
one thing a retest harness needs: the ability to assert absence. It also means a screen-reader
user's experience of these lists is untested and untestable by the current tooling.

**Workaround in use:** a filter box, where one exists, to force the wanted rows to realize. It
does not scale — it cannot enumerate, only confirm something already suspected.

---

## G9 · A filter box accepts programmatic text but does not filter — **Confirmed**

`ValuePattern.SetValue` on the *Agents & Skills* filter sets the text (verified by reading it
back) and the list does not react: the tree still held 219 descendants of unfiltered plugin rows,
and a search for the filtered term returned **0 matches**.

⚠ **This is the workaround for `G8` failing.** The filter is the only route to force a virtualized
row to realize, so when it ignores programmatic input, user-scope artifacts become unverifiable by
automation in both directions — cannot enumerate, cannot filter.

ⓘ Likely a debounce or `TextInput`-event dependency rather than a binding fault: the same
`SetValue` approach *does* commit on the settings editors, where the window title's dirty marker
confirms the binding fired. Worth confirming before choosing a fix.

**Workaround in use:** none that works. A human reads the list.

---

# ▶ Proposal — a reusable automation-surface helper

⭐ **Raised by the maintainer, 2026-09-17, and worth doing.** Most gaps above are the same defect
wearing different clothes: information the UI has, that never reaches the automation tree. Rather
than patching each site, expose it once through a shared helper.

## The virtualization half already has standard contracts

UIA defines exactly this case, so nothing needs inventing:

| Contract | What it buys |
|---|---|
| `IItemContainerProvider` (**ItemContainerPattern**) | `FindItemByProperty` — ask a list for an item by name **without** realizing the whole list |
| `IVirtualizedItemProvider` (**VirtualizedItemPattern**) | `Realize()` — bring one found item into the tree on demand |

A harness could then say *"find the row named `handoff`, realize it, read its source label"* and get
a real answer, instead of scrolling and hoping. **`G8` disappears, and absence becomes assertable.**

⚠ **Verify before committing:** whether Avalonia 12.1's automation layer exposes the hooks to
implement these two providers from application code. That is the load-bearing unknown — the rest of
this proposal is straightforward either way, and this repo should not assert it until measured.

## It generalises well beyond virtualization

The same helper is the natural home for several gaps already recorded here:

| Gap | What the helper would supply |
|---|---|
| `G1` | A stable `AutomationId` on interactive controls — **not** user-visible, so exempt from the localization rule that makes `Name` an unsafe selector |
| `G3` | `LabeledBy` / `ControllerFor` tying the winning-scope chip and `(overridden)` marker to the property they describe — an accessibility fix first, an automation fix second |
| `F6` / `F10` | Names for theme-supplied template parts, which the repo-wide AXAML guard cannot see by construction |

## Two shapes, and the dependency question

- **Attached property + custom `AutomationPeer`** — no new package. `Avalonia.Xaml.Behaviors` is
  **not** currently referenced by this repo, so the Behavior form would add a dependency to eleven
  shipped packages; that is a decision, not a detail.
- **A `Behavior`** — more declarative at the XAML call site, and the more natural fit if the
  package is wanted anyway.

⭐ **Either shape belongs in a neutral library, not an app.** `LayeredEditors.Avalonia` or
`AgentForge.Avalonia.Shell` — it must make sense for both products, and an automation-surface
helper plainly does.

## ⭐ It is not Windows-only

Avalonia's automation abstraction maps to **UIA on Windows** and **AT-SPI on Linux**, with
NSAccessibility on macOS. A helper written against Avalonia's own automation types therefore
carries to the Linux driver rather than being a Windows-shaped patch — which matters here, because
the trim matrix already ships six RIDs and the accessibility audit has only ever run on one.
