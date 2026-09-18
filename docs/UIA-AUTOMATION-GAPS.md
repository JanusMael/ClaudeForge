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
