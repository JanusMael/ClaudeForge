# Retest findings — 2026-09-14

Open while the retest runs. Companion to [`MANUAL-RETEST-PLAN.md`](./MANUAL-RETEST-PLAN.md):
that file says what to drive, this one says what came back. Nothing here is fixed yet.

**Status legend:** 🔵 reported, not started · 🟡 in progress · ✅ fixed · ⛔ fixed but unverified

---

## 🔵 F1 · Severity glyph sizing — Critical must outrank Caution

**Reported:** Critical needs to be **bigger than** Caution; Caution needs to be **one step bigger**
than it is now; the circles (Info / None) stay exactly as they are.

**Where:** visible on the settings Properties list — `⊗` currently reads *smaller* than `⚠`, which
inverts the hierarchy the glyphs exist to express.

### What the code does today

⛔ **There is no per-severity size.** Each render site sets one `FontSize` for whatever glyph the
converter returns, so Critical and Caution are necessarily the same size and `⊗` simply has smaller
glyph metrics than `⚠` at equal points. This is not a value to bump — the dimension does not exist.

⚠ **And the size is hardcoded at NINE sites**, in two tiers:

| Size | Sites |
|---|---|
| `14` | `ClaudeForge/Controls/PropertyEditorWrapper.axaml` ×2, `LayeredEditors.Avalonia/Controls/PropertyEditorWrapper.axaml` ×2 |
| `11` | `ClaudeForge/Views/EffectiveSettingsView.axaml`, `GroupEffectiveView.axaml`, `MainWindow.axaml`, `SaveChangesDialog.axaml`, `OpenCodeForge/Views/MainWindow.axaml` |

The screenshots are the `14` tier. A fix that only edits that tier leaves the nav, search, effective
view and save dialog inconsistent with it.

### Shape of the fix

A per-severity size that travels with the glyph, rather than nine literals — the same argument
`AppSeverityToGlyphConverter` already makes for the glyph itself: severity is a type, and what it
looks like is a function of it. The two tiers stay (a row is not a nav badge), so the size wants to
be a *scale* per severity applied to a per-site base, not an absolute.

⚠ **`EveryGlyphIsASingleBmpCharacterNotAnEmoji` and the themed-brush guards both touch this area** —
whatever lands has to keep them green.

---

## 🔵 F2 · Light-theme Caution reads brown, not amber

**Reported:** in **light** theme Caution is a tad too dark and reads brown. **Dark theme is fine.**

**Current values:** light `#D97706`, dark `#F0A03A`. Only the light token is in question.

### ⛔ Lightening it is the one move that cannot work

Measured as WCAG contrast against white, and against `AppCautionBackgroundBrush` (`#FFFBEB`):

| Colour | HSL | vs white | vs `#FFFBEB` | |
|---|---|---|---|---|
| `#D97706` | 32° 95% 44% | **3.19:1** | 3.07:1 | current light |
| `#E07C02` | 33° 98% 44% | 2.98:1 | 2.87:1 | same lightness, more chroma |
| `#E8850B` | 33° 91% 48% | **2.70:1** | 2.60:1 | one step lighter |
| `#ED8B00` | 35° 100% 46% | 2.53:1 | 2.44:1 | two steps lighter |
| `#F59E0B` | 38° 92% 50% | 2.15:1 | 2.07:1 | Tailwind amber-500, the "classic" amber |
| `#F0A03A` | 34° 86% 58% | 2.14:1 | 2.07:1 | the **dark**-theme value, for reference |
| `#EA580C` | 21° 90% 48% | **3.56:1** | 3.43:1 | hue shift toward orange, not lighter |
| `#C2410C` | 17° 88% 40% | 5.18:1 | 4.99:1 | darker — worse for brown-ness |

Floors: **4.5:1** text, **3.0:1** non-text/icon.

⚠ **The current value already sits at 3.19:1 — barely over the non-text floor and well under the
text floor.** Every step lighter drops under 3.0.

⛔ **One step lighter lands on exactly `2.70:1`** — the same ratio this repo has already recorded as
a defect, in `AppChangeKindTokenCoverageTests` and `SaveChangesDialogViewModel`: *"White-on-#F57C00
is 2.70:1 — under 4.5:1 for text and under even the 3.0:1 non-text one."* Doing the obvious thing
here would re-introduce a number the codebase already calls out by name.

ⓘ **The amber tint pill does not buy headroom either.** `AppCautionBackgroundBrush` is `#FFFBEB`,
near enough to white that it *lowers* the ratio slightly rather than raising it.

### The direction that does work: shift hue, do not lighten

Brown is dark orange. `#D97706` sits at **32°**, and the way out is to move toward orange rather
than up in lightness. `#EA580C` reads distinctly orange **and measures better than today**
(3.56:1 against 3.19:1).

⚠ **The cost, stated so it is a choice and not a surprise:** Critical is `#CE2029` at ~357°. Caution
today is 35° away; at 21° it would be 24° away. The two hues get closer, which is the axis a
red-green colour-blind reader is weakest on. ⭐ The dual coding absorbs this — `⊗` and `⚠` are
different shapes, confirmed working in A2 — but it is the reason not to chase orange further than
this.

### Where it lives

Light-theme `#D97706` is declared in three places, and they are separate keys, not one:

| | |
|---|---|
| `src/ClaudeForge/App.axaml:159` | `AppCautionBrush` |
| `src/ClaudeForge/App.axaml:179` | `AppSeverityCautionBrush` |
| `src/OpenCodeForge/App.axaml:84` | `AppSeverityCautionBrush` |

⚠ `src/ClaudeForge/Views/MainWindow.axaml:83` also carries `#FFD97706` as
`InstallBannerCodeBorderBrush` — a **border**, a different role at a different contrast floor.
Decide about it deliberately rather than sweeping it up in a find-and-replace.

---

## 🔵 F3 · *Share config* succeeds silently — the user cannot tell it did anything

**Reported:** the copy works, but nothing acknowledges it. And **a modal is the wrong answer** —
which matches this codebase: the centre status pill is the non-modal, auto-clearing channel.

**Where:** `EffectiveSettingsViewModel.ShareConfigAsync` awaits the share and returns. The failure
path only logs, under the comment *"Share is best-effort; log without surfacing a dialog"* — the
right instinct about modals, but it leaves **nothing at all** in either direction.

### The precedent is already in the repo, and it is the same bug one step earlier

`BackupRestoreViewModel.OnTerminalStatus` is an `Action<string, bool isFailure>` that the host wires
to `SetStatusSuccess` / `SetStatusFailure`. Its own remarks describe this exact class of defect:

> Pre-fix, the BackupRestoreVM only set its page-local `StatusMessage`; the centre pill never fired
> on backup/restore outcomes. Users on other nav nodes therefore missed the success signal entirely.

Share is that, one step further along: it sets nothing anywhere. The pill lifecycle is already what
is wanted here — **Success auto-clears after ~6 s; Failure sticks until dismissed**, which would
also retire the log-only catch.

⚠ Emissions must go through the typed helpers. Writing the legacy `StatusMessage` setter still
compiles and routes to `StatusKind.State` — grey plain text, no icon, no auto-clear.

### ⛔ The hard part is that the service cannot say what it did

```csharp
Task ShareTextAsync(string title, string text, string? uri = null);
```

**No outcome, by construction** — and the three platforms do genuinely different things:
clipboard on Windows, share sheet on macOS, `mailto:` on Linux. So:

- A generic *"Shared"* would be **wrong on Windows**, where nothing was shared and something was
  copied.
- ⭐ **This is also why the Windows no-op survived so long.** A void-returning share cannot
  distinguish "did nothing" from "did something", so neither the caller nor a test could see it.
  Adding the message without adding the outcome would paper over the same blind spot.

The fix therefore has to start at the interface: report which action was taken
(clipboard / share sheet / mail client / unavailable), and let the view-model choose the sentence.

⚠ **`IShareService` lives in `LayeredEditors.Avalonia.Services`, which is one of the eleven
published packages** — so this is a public-surface change with a version and consumers behind it,
not an internal tidy. `PublicSurfaceContractTests` covers that surface. The non-breaking route is an
**additional** member returning the outcome, leaving the existing signature alone; the breaking
route is cleaner but should be a deliberate call.

ⓘ The message itself needs a resx entry — all user-visible text comes from resx, and the parity
contracts in `LOCALIZATION.md` apply.

---

## 🔵 F4 · Danger-banner TEXT fails the contrast floor in light theme

**Reported:** the danger callout's orange text works on black, does not work on white. Same
component, both themes.

**Where:** `ClaudeForge/Controls/PropertyEditorWrapper.axaml` — the `IsDangerNow` banner. Its
explanation `TextBlock` is `FontSize="11"`, coloured from `AppSeverityCautionBrush` via
`SeverityToBrush`.

### ⛔ This is TEXT, so the floor is 4.5:1 — not the 3.0:1 that F1 and F2 live under

| | |
|---|---|
| Dark `#F0A03A` on `#1E1E1E` | **7.78:1** ✅ |
| Dark `#F0A03A` on `#121212` | 8.74:1 ✅ |
| Light `#D97706` on `#FFFFFF` | **3.19:1** ❌ |
| Light `#D97706` on a grey card `#F3F3F3` | **2.87:1** ❌ under even the non-text floor |

The dark theme has **more than double** the light theme's contrast. The token was chosen well for
dark and not for light.

⛔ **The markup's own comment states the rule it is breaking.** It explains at length why the banner
is coloured from the severity tokens rather than `LE.DangerText`:

> `LE.DangerText` is a single flat `#C62828` … which measures roughly 3:1 on the dark surface,
> under the 4.5:1 UI-STYLE-GUIDE requires for text

The replacement measures **3.19:1 on the light surface** — the same failure, mirrored into the other
theme, for the same underlying reason: one flat value judged against one surface.

ⓘ **`AppCautionBackgroundBrush` exists for exactly this and is not used here.** It is documented as
*"warm tint for bordered caution panels"* (`#FFFBEB` light, `#292010` dark), but the banner sets a
`BorderBrush` and **no `Background`**, so the text sits on the raw page surface. In dark that
happens to be fine; in light it is white.

### ⚠⚠ F2 and F4 pull the SAME token in OPPOSITE directions

- **F2** wants light Caution **lighter** — `#D97706` reads brown.
- **F4** needs it **darker** — the values reaching 4.5:1 as text are `#B45309` (5.02:1),
  `#C2410C` (5.18:1), `#9A3412` (7.31:1): all darker, and browner still.

⛔ **No single value satisfies both, and a fix that treats them as one ticket will make one of them
worse.** The banner currently uses one brush for three roles — glyph, border, body text — whose
floors differ.

**The shape that resolves it is to split by ROLE, not by theme:**

| Role | Floor | Wants |
|---|---|---|
| Glyph + border | 3.0:1 | the brighter, less-brown amber F2 asks for |
| Banner body text | 4.5:1 | a darker amber, or the tint treatment below |

⭐ **Or give the banner the background it was designed to have.** Dark text on a light warm tint is
the light-theme mirror of what dark mode already does, and is why dark works: `#9A3412` on `#FFFBEB`
measures ~7:1. ⚠ The tint alone is not enough — `#D97706` on `#FFFBEB` is 3.07:1 — so the text
colour has to move as well.

---

## ✅ F5 · REFUTED — the published app's accessibility tree was never missing

> ⓘ **This entry was a release blocker and is no longer one.** It is kept in full, with the
> original reasoning intact below, because the way it went wrong is more useful than the finding
> ever was: every step of the diagnosis was sound except the measurement it rested on.

**Original claim.** The published build exposed **1** UIA descendant — the OS-supplied `TitleBar` —
against Debug's 173, root-caused to `TrimMode=link`, which Avalonia does not support.

**It does not reproduce.** Re-measured 2026-09-14 on the same shipped configuration:

| Build | UIA descendants of the main window |
|---|---|
| Release published, trimmed, single-file, self-contained — `TrimMode=link` | **168** |
| The same, `TrimMode=partial` | **168** |
| Original F5 reading | 1 |

⭐ **The build was proven identical, not merely similar.** ILLink ran, and
`obj/Release/net10.0/win-x64/linked/Avalonia.Win32.Automation.dll` is **92,672 bytes** — the exact
figure the evidence table below quotes. Same bytes, opposite conclusion, so the divergence is in
the **instrument**, not the artifact.

⭐ **`scripts/Audit-Accessibility.ps1` agrees and is not vacuous**: 168 elements visited, 37
findings, including the 26 unnamed `PART_ExpandCollapseChevron` buttons that F6 records. F6 was
measured off the very tree F5 says is absent.

### Why it read 1

The tree **builds incrementally**, and F5 sampled it once. Cold launch, extraction cache cleared,
polling every 120 ms:

| t | descendants |
|---|---|
| 5.29 s | **72** |
| 7.13 s | **COMException** mid-construction |
| 8.03 s | **168**, stable thereafter |

A sample before ~8 s under-reports; early enough, only the OS `TitleBar` exists, which counts as 1.

⚠ **This also explains the "one variable" control that made F5 look airtight.**
`PublishTrimmed=false` → 227 against `true` → 1 holds trimming as the only difference, but a
trimmed **single-file compressed** build self-extracts on first run and reaches a ready tree
*seconds* later than an untrimmed multi-file one. One fixed delay, two different readiness times —
so the control varied startup latency, not just trimming.

⛔ **The proposed cure could not have worked either, and that was checkable without running
anything.** `Microsoft.NET.ILLink.targets:45` defaults `BuiltInComInteropSupport` to false under
`Condition="'$(PublishTrimmed)' == 'true'"`. **`TrimMode` is not in that condition**, so no trim
mode restores built-in COM interop. The upstream COM explanation was load-bearing for the fix and
does not survive reading the targets file.

### What was done about it

- ✅ **`scripts/Audit-Accessibility.ps1` now settles before it walks** — it polls until the count is
  unchanged across consecutive samples, treats a mid-construction `COMException` as "not ready",
  and treats a count of 1 or 0 as not-ready rather than as an answer. If it never settles it says
  so and labels its output a floor. `-NoSettle` opts out.
- ✅ **Both apps moved to `TrimMode=partial` anyway** — see below. Not to fix this, since there was
  nothing to fix, but because `link` is genuinely unsupported.

### ⚠ What is NOT refuted: `link` really is unsupported

[AvaloniaUI/Avalonia#16697](https://github.com/AvaloniaUI/Avalonia/issues/16697) — *"COM interop is
not supported with `TrimMode=link`"* — reports access-violation crashes for users running Magnifier
or a screen reader. Nothing measured here contradicts that; this repo simply was not exhibiting it.
Both apps now set `partial`, and the measured cost is **~76 KB** on win-x64 (27,647,430 →
27,725,447 bytes), not the meaningful growth this document assumed.

⭐ **The move needed one line, not the suppression campaign proposed below.** Under `partial` a
non-trimmable assembly is copied whole, so `Avalonia.DesignerSupport`'s unreachable remote-designer
entry point stops being dead code and its `IL2026` / `IL2072` / `IL2075` escalate to
`NETSDK1144`. `<TrimmableAssembly Include="Avalonia.DesignerSupport"/>` restores the `link`
treatment for that assembly alone — the dead code is removed again rather than the warnings
silenced over code that would still ship. **Six-RID two-app matrix: 12/12 green, zero IL
diagnostics.**

### ⛔ The lesson, which is the part worth keeping

**A measurement tool whose default can silently under-report will eventually manufacture a defect.**
Everything downstream of the bad number was competent: the cause was isolated with a control, the
mechanism was researched against upstream, two candidate fixes were tried and rejected with
evidence, and the whole thing was written up. None of that could rescue a first number taken before
the thing being measured existed.

Two guards against the repeat, both now in place: the tool settles by default, and a count of 1 is
treated as "not ready" rather than reported as fact.

---

<details>
<summary>The original F5 investigation, kept verbatim for its evidence trail</summary>

**Found by automating what could not be tested by hand.** A screen-reader pass was not available,
so the running app was driven through UI Automation — the same API a screen reader uses.

| Build | UIA descendants of the main window |
|---|---|
| **Debug** (untrimmed) | **173** |
| **Release published** (trimmed, single-file, self-contained) | **1** |

That one element is the **OS-provided `TitleBar`**.

### Why nothing caught it

⚠ **The existing guards assert the MARKUP, and the markup is fine.** `AutomationProperties.Name`
is present on every interactive control — that is checked repo-wide, and it passes. What none of
them can check is what the platform ends up *exposing*, because the headless test app is stripped
of the App's resource dictionaries and cannot instantiate views.

ⓘ **Keyboard reach is unaffected** — focus is not UIA, which is why `A6` genuinely passes on the
published build.

### Cause isolated: it is TRIMMING

| Release publish, self-contained, single-file | UIA descendants |
|---|---|
| `PublishTrimmed=true` — what ships | **1** |
| `PublishTrimmed=false` | **227** |

⚠ **No trim warning was emitted.**

### Mechanism: `TrimMode=link` is unsupported by Avalonia

Both apps set it. Upstream states COM interop is not supported with `TrimMode=link`, and describes
runtime crashes when UI automation is invoked — access violations calling
`UiaReturnRawElementProvider`, with users running magnifiers or screen readers crashing on startup.

The reporter's own complaint is the part that aged best:

> If `TrimMode=link` is enabled, some part of the build process should inform the developer of the
> peril they will face in a more obvious way, ideally failing the build.

### The evidence, in order

| Observation | What it rules in or out |
|---|---|
| Debug 173 descendants vs published 1 | something about the published build |
| `PublishTrimmed=false`, all else equal → 227 | **trimming**, one variable |
| `Avalonia.Win32.Automation.dll` **is present** in the trimmed output | not a missing-assembly problem |
| …but trimmed from **123,904 → 92,672 bytes** | ~25% of it removed, consistent with the COM surface going |
| `Avalonia.Win32` statically references it | not a dynamic-load problem either |
| No embedded `ILLink.Substitutions.xml` in either assembly | Avalonia is not deliberately stubbing it out |
| No trim warning, no runtime log entry | silent by construction |

ⓘ **The fourth row is the one that settles the refutation.** 92,672 bytes is what a correct,
fully-working build also produces — so the row proved the assembly was trimmed, which was true, and
was read as evidence that its COM surface had been removed, which was not.

### ⛔ Rejected: `BuiltInComInteropSupport=true`

Publishing with that property **fails the build outright**:

```
IL2026: Built-in COM support is not trim compatible.  https://aka.ms/dotnet-illink/com
NETSDK1144: Optimizing assemblies for size failed.
```

⭐ Recorded because it is the first idea anyone will have, and it is a dead end by design. ⓘ Still
true, and still worth keeping — but see above: the property it controls is gated on
`PublishTrimmed`, so this was never the lever.

</details>

---

## 🔵 F6 · 26 nav-tree chevrons announce nothing

**Found by** `scripts/Audit-Accessibility.ps1` against a build that exposes its tree (see `F5` —
the shipped one does not). 227 elements walked, two distinct findings.

| Count | Element | Problem |
|---|---|---|
| **26** | `Button`, `AutomationId=PART_ExpandCollapseChevron` | interactive, **no accessible name** |
| 1 | `Thumb` (scrollbar) | focusable, no name — conventional, see below |

A screen reader reaches each of the 26 expand/collapse chevrons in the navigation tree and can say
only *"button"*.

### ⚠ Why every existing guard missed it: `PART_` means it is not our markup

The repo checks that every interactive control in `src/**/*.axaml` sets
`AutomationProperties.Name`, repo-wide, and that check passes. This element is a **control-template
part** supplied by the theme the app consumes, not markup this repository writes — so it is outside
what that guard can see, by construction.

⭐ **The reusable lesson:** a markup guard covers the markup you write. Template parts from a
consumed theme are a second surface, and only a runtime audit reaches them.

### Two defensible fixes, and they differ in kind

1. **Name it** — a style setting `AutomationProperties.Name` on the chevron from a resx string.
   Safe, and consistent with how the repo treats every other affordance.
2. **Hide it** — `AutomationProperties.AccessibilityView="Raw"`. Arguably *more* correct: the
   `TreeViewItem` itself already exposes the ExpandCollapse pattern, so the chevron is an
   implementation detail and announcing it as a separate button duplicates a control the reader
   already has.

ⓘ **The `Thumb` is accepted noise.** Scrollbar thumbs are conventionally unnamed — the scrollbar
carries the name — and no screen reader expects otherwise.

### ⚠ This covers the MAIN window only

The diagnostics windows were not open when the audit ran, and the script walks every top-level
window that exists at that moment. **Press F12 and Shift+F12, then re-run** to cover them — which
is what plan item `B2` actually asks about.

---

## Observations from the retest

Not defects, and not asked about — recorded because they answer open questions in the plan.

### ✅ `⚠` renders as a text glyph on Windows — plan item A2's unknown

`AppSeverityToGlyphConverter` carries the comment *"⚠ NOT yet confirmed by rendering on any
platform"*, and the code deliberately omits the `U+FE0E` text-variation selector on the reasoning
that `U+26A0` already defaults to text presentation. The screenshots show a themed triangle, not a
colour emoji and not tofu, so **the reasoning holds on Windows**. Linux and macOS are still
unconfirmed, which is where the tofu risk actually lives.

That comment should be updated when F1 is fixed, since the fix touches the same file.

### ✅ `⊗` draws in the Critical red — answered

Confirmed 2026-09-14. I could not call it from the screenshots and raised it as a question; the
answer is that the colour is correct.

⭐ **This bounds F1.** Critical differs from Caution in **both** shape and hue, so the dual coding
the design exists for is working and a red-green colour-blind reader is not stranded. F1 is
therefore a **visual-hierarchy** defect, not an accessibility one: the glyphs say the right things,
the louder one is drawn smaller. Worth knowing before anyone reaches for an urgent fix.
