# Retest findings — 2026-09-14

Open while the retest runs. Companion to [`MANUAL-RETEST-PLAN.md`](./MANUAL-RETEST-PLAN.md):
that file says what to drive, this one says what came back.

**Status legend:** 🔵 reported, not started · 🟡 in progress · ✅ fixed · ⛔ fixed but unverified

---

## ⛔ F1 · Severity glyph sizing — Critical must outrank Caution

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

### ⛔ Fixed, unverified — and the report understated it

**Measured first**, through Avalonia + Skia, because the fix is a size and the report asserted the
disparity without a number. Ink height at 14pt — the line box is font-metric driven and identical
for all four glyphs, so it cannot express this and is not what the eye compares:

| | `⚠` Caution | `○` None | `⊗` Critical | `●` Info |
|---|---|---|---|---|
| ink height @ 14pt | **10.70** | **10.14** | **8.52** | 6.02 |

⛔ **The inversion was in two places, not one.** `⊗` draws at **0.797×** `⚠` — but it also draws
*smaller than the hollow `○`*, so the glyph meaning "noted, nothing to do" was the larger of the
two. The report caught the Critical-vs-Caution half; this half was invisible without measuring.

⚠ **Which is why the scale looks disproportionate and is not.** ×1.255 buys Critical nothing but
parity with Caution; any lead starts above that, so there is no cheap version of this fix.
Shipped: **Critical ×1.55, Caution ×1.15, circles ×1.00** — Critical's ink lands ~7% above
Caution's and clears `○` comfortably. Chosen from a rendered comparison of six candidate pairs.

ⓘ **The ratio is a Windows measurement.** Neither `U+2297` nor `U+26A0` exists in Segoe UI, Inter
or Arial, so both resolve through the same font fallback — which is why the figure held identically
across every family tried. Another platform's fallback could shift the margin; it cannot invert the
ranking, because the scales are strictly ordered.

| | |
|---|---|
| Added | `AppSeverityToFontSizeConverter` — scale per severity, tier base as `ConverterParameter` |
| Changed | all **nine** literals across **seven** files in both apps |
| Guarded | `AppSeverityToFontSizeConverterTests` (17), `SeverityGlyphFontSizeMarkupTests` (2) |

⭐ **The guard asserts ink, not points.** A test of `SizeFor(Critical) > SizeFor(Caution)` would go
green at ×1.01 with the defect still on screen, so `TheScalesBeatTheMeasuredInkDisparity`
multiplies the scales by the measured ink heights instead. Canaried: dropping Critical to ×1.20 —
still larger than Caution *in points* — reddens it alone.

⚠ **Still to verify by eye at the retest**: that 21.7pt does not disturb row height on the settings
list, and that the 11pt tier (nav, search, effective, save dialog) still reads as a badge.

---

## ⛔ F2 · Light-theme Caution reads brown, not amber

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

### ⛔ Fixed, unverified — and the deliberate decision about that fourth site

**Every contrast figure in the tables above was recomputed and all sixteen reproduce exactly**, so
the reasoning stands as written. Shipped: **`#EA580C`**, the hue shift, in
`AppSeverityCautionBrush` (both apps) and `AppCautionBrush` (ClaudeForge). It measures **3.56:1**
against today's 3.19:1 — the rare fix that reads better *and* measures better.

⚠ **The hue gap to Critical narrows 35° → 24°**, which is the red-green axis. `F1`'s shape
difference is what absorbs it, and it is the reason not to chase orange any further than this.

ⓘ **`InstallBannerCodeBorderBrush` was deliberately left alone**, on two grounds rather than one:
its palette is explicitly **theme-independent** (the install banner is pale goldenrod with dark
text in both variants, by design), and the key is **declared at `MainWindow.axaml:83` and never
referenced anywhere** — the usage nearby is `InstallBannerCodeBgBrush`. It is a dead token in a
palette that is not the caution family, so `F2` does not reach it.

ⓘ **Nothing is missing a border as a result**, which is the obvious next question. The banner roots
and `UpdateBanner` use `InstallBannerBorderBrush`; the post-install note pairs the *code*
background with that same generic border; and `InstallCommandPanel:47` borders its command block
from the `SystemControl*` family. ⚠ That last one looks like two defects and is neither:
`theme-audit-report.md` marks those keys *"dynamic (invisible) — all"*, but that describes the
upstream themes — this repo **defines them itself** in `Resources/Compat/FluentKeys.Semi.axaml`,
merged at `App.axaml:133`. And a theme-dependent brush inside a deliberately theme-independent
banner would be the `F4` mechanism again, except the shim's `SystemBaseLowColor` is **translucent**
(`#33000000` light, `#33FFFFFF` dark), so it composites over the banner's own pale yellow and the
forced dark text stays readable in both variants.

---

## ⛔ F3 · *Share config* succeeds silently — the user cannot tell it did anything

> ⛔ **Fixed, awaiting a look at the running UI.** `IShareService` now returns a `ShareOutcome`
> — clipboard / browser / mail client / file manager / unavailable / failed — and
> `EffectiveSettingsViewModel` picks a sentence per outcome and emits it through a new
> `OnTerminalStatus` hook that `MainWindowViewModel` wires to the centre pill, exactly as
> backup/restore's is. Six resx keys in all nine locale files.
>
> ⭐ **The outcome is measured, not assumed.** `TryStart` returned `void` and swallowed every
> failure, so `Failed` would have been decoration; it now returns `bool` and both failure arms
> read it. `ShareOutcomeTests` pins that with a launcher that reports "did not start" — and a
> canary that made `TryStart` claim success reddened exactly the two tests that assert it.
>
> ✅ **The two sibling surfaces are fixed too, in a second pass.** *Share log* (About) and *Share
> backup archive* had the identical silent-success shape and now report through the same pill.
> About gained its own `OnTerminalStatus` hook, wired at both cached construction sites; backup
> already had one, and its three sentences come from the host's resx through `BackupPageText`,
> the seam the progress phase labels already use. Both go through **one** mapper,
> `FileShareStatus.Describe` — a switch written twice in two assemblies is two chances to answer
> the same outcome differently.
>
> ⛔ **An outcome a file share cannot produce is reported as a FAILURE**, not borrowed as a
> success. `ShareFileAsync` reveals a file on all three platforms; it cannot write a clipboard or
> open a browser, so a service returning one is not honouring its contract — and saying "revealed
> in your file manager" when it was not is the exact lie this finding is about.

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
not an internal tidy. The non-breaking route is an **additional** member returning the outcome,
leaving the existing signature alone; the breaking route is cleaner but should be a deliberate call.

> ⓘ **Corrected 2026-09-16.** This said *"`PublicSurfaceContractTests` covers that surface"*. It
> does not: that class lives in `AgentForge.Sdk.Tests` and pins the SDK, not this package. Nothing
> pinned `IShareService`, and the signature change went through a full green suite without one
> test noticing — which is the evidence, not an argument about it.
>
> ✅ **The breaking route was taken, deliberately, on 2026-09-16.** Nothing is on the feed yet, so
> the change costs a recompile nobody has to do; it will never be this cheap again. The blast
> radius was one interface, one implementation, three call sites and two test fakes.

ⓘ The message itself needs a resx entry — all user-visible text comes from resx, and the parity
contracts in `LOCALIZATION.md` apply.

---

## ⛔ F4 · Danger-banner TEXT fails the contrast floor in light theme

> ⛔ **This was one instance of six, and all six are fixed.** See *"The mechanism, and how far it
> reaches"* at the end of this section — the banner was only the surface it happened to be
> reported against.

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
measures **7.05:1** (computed). ⚠ The tint alone is not enough — `#D97706` on `#FFFBEB` is 3.07:1 —
so the text colour has to move as well.

### ⛔ The mechanism, and how far it reaches

**The defect is not a banner defect.** It is `AppCautionBrush` — a token chosen to clear the
**3.0:1** non-text floor — being used as a **text foreground**, where the floor is **4.5:1**.
That happens at six places, not one:

| Site | Surface | Ratio | |
|---|---|---|---|
| `InstallCommandPanel.axaml:68` | white | 3.19 | ✅ fixed |
| `AboutEditorView.axaml:42` | white | 3.19 | ✅ fixed |
| `AboutEditorView.axaml:70` | tint `#FFFBEB` | 3.07 | ✅ fixed |
| `AboutEditorView.axaml:109` | white | 3.19 | ✅ fixed |
| `MemoryEditorView.axaml:317` | tint `#FFFBEB` | 3.07 | ✅ fixed |
| the `IsDangerNow` banner | white | 3.19 | ✅ fixed — see below |

The six **border** uses of the same token are all fine: 3.19:1 against a 3.0:1 floor.

⭐ **The repo already contained the correct pattern.** `EssentialsView:92-100` draws its caution
panel as tint background + caution **border** + body text in `AppPrimaryTextBrush`.
`AboutEditorView` and `MemoryEditorView` build the same panel and colour the header with the
*border* token. The fix was to make the others match the one that was already right.

**What shipped:** a new `AppCautionTextBrush` — light `#9A3412` (**7.31:1** on white, **7.05:1** on
the tint), dark `#F59E0B`. ⓘ The dark value is the accent **unchanged**, because it already
measures **7.76:1** as text — which is precisely why only light theme was ever reported.

⭐ **Guarded by `CautionBrushIsNotUsedAsTextTests`, which COMPUTES the ratios from the hexes in
`App.axaml` rather than quoting them.** A test asserting "the token equals `#9A3412`" would pass
forever while saying nothing about readability. Canaried by restoring `#D97706`: it reddens
reporting **3.19:1 and 3.07:1** — this finding's own numbers, re-derived.

### ⛔ The banner needed a severity-driven tint, and got one for free

⛔ **A single warm tint was not an option, because the banner is severity-driven.**
`DangerAssessment(Severity, IsDangerNow, …)` computes the two **independently** —
`TableDangerClassifier` takes severity from `EscalatesAt`/`Tier`, whose `EscalatedTier` defaults
to **`Critical`**, and `IsDangerNow` from `rule.Unsafe(currentValue)`. So a **Critical** banner is
the designed-for case, and an amber wash beneath a red-bordered one would say the wrong thing.

The obvious route — an `AppSeverity*BackgroundBrush` family — meant **eight new tints** across two
variants and two apps. ⚠ That collides with a rule this palette states in its own comments: the
severity brushes were reused from already-vetted pairs *"so no unreviewed colour enters the
palette"*.

⭐ **So the tint is DERIVED rather than declared.** `AppSeverityToTintBrushConverter` returns the
existing severity colour at **10% alpha**, composited over whatever surface is behind it. That is
correct for all four severities and both themes, and adds **no colour to the palette at all**.

| | |
|---|---|
| Background | `SeverityToTint` — the severity's own colour at 10% |
| Border + glyph | unchanged, still `SeverityToBrush` |
| Body text | ⭐ **no `Foreground` at all** — it inherits |

ⓘ **Inheriting, rather than naming `AppPrimaryTextBrush`, is what lets BOTH apps render it** —
OpenCodeForge declares no such token, and the banner lives in the shared wrapper. It reaches
14.8:1 or better on every tint.

⛔ **The alpha is bounded above by the BORDER, which is the constraint nobody would guess from the
markup.** The banner draws its border in the same colour the tint is made from, so raising alpha
pulls the two together. On white, Caution's border against its own tint measures **3.23:1 at 8%,
3.15:1 at 10%, 3.07:1 at 12%, and 2.96:1 at 15%** — under the floor, while the tint still looks
perfectly reasonable. `SeverityTintStaysLegibleTests` derives that ceiling rather than quoting it,
and asserts the chosen alpha sits below it.

⚠ **The tint brush is theme-TRACKED, and a fresh brush per `Convert` would have been a bug.**
`BrushHelper` returns one shared mutable brush per key and re-colours it on variant change,
because a converter only re-runs when its binding *source* changes and a severity does not change
because the theme did — a defect this repo already shipped once and fixed. A stale 10% wash reads
as a slightly-off background rather than as a wrong colour, so it would have survived review.

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

## ✅ F6 · 26 nav-tree chevrons announce nothing — FIXED, verified 2026-09-17

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

### ⛔ Fixed by NAMING it — and option 2's premise turned out to be false

**Option 2 was the better-argued fix and it is wrong on this Avalonia.** Measured on a real
templated `TreeViewItem` rather than assumed:

```
ITEM     peer=TreeViewItemAutomationPeer   interfaces: IScrollProvider, ISelectionItemProvider
CHEVRON  peer=ToggleButtonAutomationPeer   interfaces: IToggleProvider
```

⛔ **There is no `IExpandCollapseProvider` on the item.** The chevron's `IToggleProvider` is the
*only* programmatic expand/collapse affordance the tree exposes, so hiding it would have removed
the affordance rather than de-duplicated it — a regression dressed as a cleanup, and one that
reads as more correct than the fix.

⚠ **And the element is a `ToggleButton`, not the `Button` this document reports above.** That
`Button` is the *peer's* control type — what a UIA walk can see — not the element's type. A style
selector written from the walk matches nothing, **builds with zero errors and zero warnings**, and
silently leaves the part unnamed. Canaried exactly that way: the wrong selector builds clean and
the guard reports the chevron announcing `''`.

| | |
|---|---|
| Fix | `TreeViewItem /template/ ToggleButton#PART_ExpandCollapseChevron` in `Themes/AccessibilityNames.axaml`, the file that already exists for parts the markup cannot reach |
| Name | `WrapperStrings.LabelExpandCollapse` → ClaudeForge's `AutoNameExpandCollapse`, in all nine resx files. Deliberately **state-free**: the Toggle pattern already reports expanded or collapsed, so a name that changed with state would be announced twice |
| Guard | `TreeChevronAutomationNameTests` — builds a real templated `TreeViewItem` and reads the peer, which is the only thing that can see a template part |

⭐ **The guard pins the REASONING, not just the behaviour.**
`ThePremiseHolds_TheChevronIsTheOnlyExpandCollapseAffordance` asserts that the item peer still
lacks `IExpandCollapseProvider`. If a later Avalonia adds it, that test reddens and says so — the
choice gets remade on the new facts instead of being inherited from this note.

ⓘ The chevron is `Focusable=False`, so naming it adds no tab stop.

ⓘ **The `Thumb` is accepted noise.** Scrollbar thumbs are conventionally unnamed — the scrollbar
carries the name — and no screen reader expects otherwise.

### ⚠ This covers the MAIN window only

The diagnostics windows were not open when the audit ran, and the script walks every top-level
window that exists at that moment. **Press F12 and Shift+F12, then re-run** to cover them — which
is what plan item `B2` actually asks about.

### ✅ VERIFIED FIXED — 2026-09-17, on the package-mode build `v2026.3.917.1839`

Measured against the running app with `scripts/retest/Find-UiElement.ps1`:

```
pwsh -NoProfile -File scripts/retest/Find-UiElement.ps1 -Needles 'Expand or collapse'
… matched: 26
```

⭐ **The count is the evidence.** This finding named exactly **26** unnamed
`PART_ExpandCollapseChevron` buttons; the running app now exposes exactly **26** chevrons, every
one of them announcing *"Expand or collapse"*. A different number either way would have meant the
two measurements were not looking at the same set — same count, and every one named, is what makes
this conclusive rather than suggestive.

ⓘ Verified incidentally while driving `E3`, not as a dedicated pass.

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

---

## ⛔ F7 · Backup captures project files; restore silently ignores them — FIXED, unverified

Found driving `E3` on 2026-09-17 against the package-mode build `v2026.3.917.1839`.

**A backup taken with a project open contains that project's `.claude/` files. Restoring
that same archive does not put them back, reports no error, and leaves no sidecar.**

### What was measured

The project `C:\c\cl\retest-2026.3.917` was open. A *Settings only (fast, default)* backup
produced `backup-20260917-192520.zip`, whose entry list contains all four of the project's
config files:

```
ClaudeCode/projects/retest-2026.3.917/.claude/settings.json
ClaudeCode/projects/retest-2026.3.917/.claude/settings.local.json
ClaudeCode/projects/retest-2026.3.917/.claude/agents/retest-folded.md
ClaudeCode/projects/retest-2026.3.917/.claude/agents/retest-trailing.md
```

Two changes were then made on disk — one value edited, one file deleted — and the archive
restored from the Restore tab:

| | Before restore | After restore | Expected |
|---|---|---|---|
| project `settings.json` `model` | `e3-CHANGED-after-backup` | **`e3-CHANGED-after-backup`** | `e2-project-write` |
| project `agents/retest-folded.md` | deleted | **still absent** | restored |
| `~/.claude` (user scope) | — | ✅ restored, 5,899 sidecars written | restored |
| `.pre-restore-*.bak` under the project | — | **0** | some, if it had been touched |

Zero sidecars under the project is the tell: the restore did not merely fail to *write*
there, it never *considered* the path at all.

### ⚠ Why this is a defect rather than a documented scope limit

Both halves of the UI text claim the opposite:

- Backup tab — *"Project files (`.claude/` in your repo) are included only when a project
  is open at backup time, or added explicitly."* A project **was** open, and they **were**
  included.
- Restore tab — *"This will overwrite your current configuration files."* No exclusion of
  project scope is stated anywhere, and the Restore tab offers no scope selector or
  checkbox that could have deselected it.

So the product tells the user those files are in the archive, and the archive agrees — and
then restore quietly drops them.

### ⛔ Why it matters more than a missing feature

A user restores a backup precisely when something is broken. The failure mode here is
**believing you have recovered when you have not**: the app reports success, the user-scope
settings visibly come back, and the project's own `.claude/` is silently left in whatever
broken state prompted the restore. The asymmetry is invisible unless someone diffs the
archive against the disk, which is exactly what nobody does mid-incident.

ⓘ Either direction is a defensible fix — restore project entries, or stop capturing them
and say so. **Whichever is chosen, backup and restore must agree**, and the UI text must
match the behaviour.

### ✅ Fixed 2026-09-18 — restore them; the decision was taken, not inferred

⭐ **The root cause was NOT "restore has no project support".** `RestoreEngine.RestoreProjects`
has always existed and always worked. It refused this project because of one line —
`IsUnderUserProfile(livePath)` — and the retest project lived at `C:\c\cl\retest-2026.3.917`,
outside the home folder. Every repository kept outside `~` hit the same refusal.

⛔ **That check was a security rule and a scope limit wearing the same clothes**, and only the
security half was ever written down. The manifest is genuinely untrusted — a crafted zip can
name `C:\Windows\System32` — so the answer was not to delete the check but to ask a source the
zip cannot write to:

| Allow | Source | Why a crafted archive cannot forge it |
|---|---|---|
| Under the user's home | `PlatformPaths.UserProfile` | The running user's own profile |
| A project this machine has opened | keys of `~/.claude.json`'s `projects`, via `KnownProjectsDiscovery` | The user's own file; `C:\Windows\System32` is in nobody's project list |

An **ancestor** match counts, so a project root authorises its `.claude` subtree — with an
explicit separator in the prefix test, because `D:\src\app` must not authorise
`D:\src\app-secrets`. Path comparison is case-insensitive except on Linux.

⚠ **The message was lying too.** A refusal was reported as *"paths are not present on this
machine"*, which sent the reader looking for a missing folder rather than at a rule. The
reason now travels with each skipped entry.

⛔ **A second defect surfaced while making this testable.** `IsUnderUserProfile` read
`Environment.GetFolderPath` directly while every other path in the engine resolves through
`PlatformPaths` — so under the test profile override the predicate disagreed with the rest of
the engine, and the refusal path could not be exercised against a real directory at all. It
now uses `PlatformPaths.UserProfile`. Identical in production; the difference is that the
repro is now measurable, and it was that gap which made both end-to-end tests report
`Inconclusive` on their first run rather than passing vacuously.

### ⛔⛔ The first fix covered ONE of the three sources, and would not have closed this finding

Caught while preparing the re-drive, by checking a premise instead of assuming it: **the
previous retest's fixture project was absent from a 62-entry `~/.claude.json`.** ClaudeForge
does not write that file — Claude Code does — so a project opened only in ClaudeForge is not
in it, and an authorisation set built from it alone still refused the exact archive this
finding was written about.

Backup captures projects from **three** sources. Restore must mirror all three:

| Source | Captured by | Authorised by |
|---|---|---|
| The explicitly-open project | every mode, including *Settings only* | ⛔ **was missing** — now passed in by the host |
| `additionalDirectories` in the live settings files | every mode | ⛔ **was missing** — now resolved live |
| `~/.claude.json` projects | Full mode only | ✅ the first fix |

⭐ **Restore now calls backup's own discovery.** `BackupEngine.CollectSettingsFilesForDiscovery`
became `internal` so `RestoreEngine.BuildAuthorisedRoots` asks the same question rather than
keeping a parallel list — two lists that happen to agree today is how this finding happened in
the first place.

⚠ `BackupEngine.RestoreAsync` gains an optional `openProjectRoots` parameter, and the
public-surface baseline moved with it in the same change. Source-compatible for existing
callers; a caller that omits it simply authorises less.

⚠ **Worktrees keep the old rule, knowingly.** Nothing on this machine independently lists
worktree paths — the archive's own `projectRoot` authorises nothing, since a crafted zip would
simply name a real project beside an arbitrary `worktreePath`. The sound source is
`WorktreeProbe` against the live repositories, which means spawning `git` per project, with a
timeout each, in front of a destructive operation. That is a decision with a cost; it is
recorded here and in the code rather than taken in passing. **See `F11`.**

**Guards:** 8 tests in `RestoreEngineTests` — the six authorisation cases (home-only, project
list, descendant, shared-prefix sibling, system path, UNC) and both halves of the end-to-end
repro. ⚠ Canaried: with the project-list allow removed, exactly three go red, by name.

⛔ **Still unverified in the running app.** `E3` re-runs against a rebuilt package-mode
artifact before this is called done.

---

## ⛔ F8 · A successful restore leaves every `.pre-restore-*.bak` sidecar behind — FIXED, unverified

Found alongside `F7`, same build and run.

**5,899 sidecars** remained under `~/.claude` after the restore completed and the app
reloaded normally. The count is exactly the restored-file count, so nothing is cleaned up
at all — this is not a partial-cleanup edge case.

```
find ~/.claude -name "*.pre-restore-*.bak" | wc -l
5899
```

### What the UI promises, and what it does not

The Restore tab says *"Existing files will be moved aside as `.pre-restore-*.bak` before
being overwritten."* That is accurate about the *mechanism* and silent about the
*lifecycle* — nothing on the page says whether they are temporary or permanent, and no
status text appeared after the restore to say either.

⚠ `E3`'s stated pass condition is **"no `*.bak` sidecars remain"**, and
`--cleanup-restore-sidecars` exists in the product precisely because surviving sidecars
have happened before. On that criterion this run fails.

### ⛔ The cost is silent and compounding

Every sidecar is a full copy of the file it shadows, so a restore roughly **doubles** the
on-disk size of `~/.claude` — here, of a 211 MB payload. Nothing tells the user, nothing
cleans it, and a second restore would double it again. The existing CLI tool is the
sanctioned remedy, but a user who does not know it exists has no reason to look for one:
the restore reported success.

ⓘ The fix is a decision, not just code: either sweep the sidecars once a restore has
committed, or keep them deliberately and **say so on the page**, with the cleanup command
named at the point the cost is incurred.

### ✅ Fixed 2026-09-18 — sweep after the restore commits

A restore now deletes the sidecars **it wrote**, and reports how many in its result message.
Three bounds, each load-bearing:

| Bound | Why |
|---|---|
| Only on a **clean** run | One file failure means a PARTIAL restore, and that is precisely when someone wants the previous bytes back. A partial run keeps every sidecar and says so |
| Only **this run's**, by exact path | A `RestoreJournal` records each sidecar as it is written, so the sweep deletes a known list rather than matching a pattern. An earlier restore's sidecars are someone else's undo trail |
| Only files matching the **pre-restore pattern** | Second lock on the one operation in restore that removes user-visible files. A hand-rolled `notes.md.bak` is the user's |

ⓘ `--cleanup-restore-sidecars` keeps its job: everything written before this sweep existed —
including the **5,899** from this retest — plus anything a partial or interrupted restore
leaves behind.

⚠ **The Restore tab's wording was left alone.** *"Existing files will be moved aside as
`.pre-restore-*.bak` before being overwritten"* is still true; it is what happens during the
restore. What it does not say is that they are then removed, and the result message now does —
at the moment the user is reading about the outcome. Changing it would mean re-translating two
strings across nine locales for something already stated.

⭐ **An existing test had to be INVERTED, deliberately.**
`RoundTrip_RestoreWritesFilesBackAndCreatesBakSidecars` required exactly one surviving
sidecar — the old contract, correctly guarded. It now requires zero *and* asserts the
message's cleanup count, because **zero on its own is not evidence of a sweep**: an engine
that stopped writing sidecars entirely would satisfy it just as well, and that would remove
the undo trail a partial restore still depends on.

**Guards:** 4 tests in `RestoreEngineTests` (ledger completeness, the sweep, the
pattern refusal, a vanished file) plus the inverted round-trip. ⚠ Canaried twice — disabling
the sweep reds one by name; dropping the ledger entry reds two, the second at its premise.

⛔ **Still unverified in the running app.** `E3` re-runs before this closes.

---

## 🔵 F11 · External worktrees are still refused when they sit outside the home folder

Raised 2026-09-18 while fixing [`F7`](#-f7--backup-captures-project-files-restore-silently-ignores-them--fixed-unverified),
which it is the exact sibling of.

`RestoreWorktrees` still gates on `IsUnderUserProfile` alone, so a git worktree outside the
home folder is captured by backup and refused by restore — the same asymmetry, with the same
silence, one level down.

⛔ **It is not fixable by the same move, which is why it was not fixed.** The projects case
works because this machine keeps its own list of the paths the user has opened. Nothing
equivalent exists for worktrees, and the archive's own `projectRoot` field authorises nothing:
a crafted zip would name a real project beside an arbitrary `worktreePath`.

The sound source is `WorktreeProbe.DiscoverExternalAsync` against the live repositories — which
spawns `git` once per known project, with a timeout each, immediately before a destructive
operation. That is a real cost and a real dependency, so it is a decision rather than a
follow-on edit.

ⓘ Narrower than `F7` in practice: external worktrees are only captured in Full mode, and the
refusal is now reported honestly rather than as a missing path.

---

## ⛔ F9 · Editing any env value DELETES every env key the app does not model — FIXED, unverified

Found driving `E4` on 2026-09-17 against the package-mode build `v2026.3.917.1839`.
⛔ **This is data loss in the user's own configuration, and it reproduces every time.**

### What happens

Change a single environment variable in the editor and save. Every env key that is not in
the app's known-variable list is **removed from the file**, including keys the user placed
there deliberately and never touched in this session.

Reproduced twice, the second time with a key name invented specifically to rule out any
contamination from the first run:

```
[Save] Claude Code settings — Project: 3 pending change(s)
[Save]   "Modified" env.ANTHROPIC_API_KEY: [redacted] → [redacted]
[Save]   "Removed"  env.MY_CUSTOM_TOOL_PATH: old = [redacted]
[Save]   "Removed"  env.RETEST_MARKER: old = [redacted]
```

Before and after, on disk:

| Key | In the app's env groups? | Survived the save |
|---|---|---|
| `ANTHROPIC_API_KEY` | yes (`ANTHROPIC · 41`) | ✅ |
| `CLAUDE_CODE_ENABLE_TELEMETRY` | yes | ✅ |
| `RETEST_MARKER` | no | ⛔ **deleted** |
| `MY_CUSTOM_TOOL_PATH` | no | ⛔ **deleted** |
| `RETEST_UNICODE` | no | ⛔ **deleted** (first run) |

ⓘ The rest of the file is untouched — comments, key order and every other setting survive,
so this is specific to the `env` object and not a writer regression. `E1` passes.

⚠ **Only a save that touches `env` triggers it.** Saves that changed `model` left all five
keys intact across several earlier runs, which is why `E1` and `E2` passed over this without
seeing it.

### ⚠ It is surfaced, but not in a form anyone can act on

Credit where due: the removals **are** listed in the save-confirmation dialog as pending
changes, so this is not strictly silent. Two things defeat that in practice:

- ⛔ **The user did not ask for them.** They appear alongside the one edit that *was* made,
  in a dialog whose habitual answer is *Save*.
- ⛔ **The values are `[redacted]`**, correctly per `E4` — so the dialog can tell you
  `MY_CUSTOM_TOOL_PATH` is being removed but not what it contained. The one surface that
  could let a user rescue the value is the one that must not show it.

### Why it matters

`env` is exactly where non-standard keys belong: proxy settings, internal tool paths,
anything an organisation adds that upstream has never heard of. The app's own env editor
advertises 157 known variables across six groups — every variable outside that set is
currently destroyed by editing any variable inside it.

ⓘ Whether the fix is to render unknown keys, or to preserve them untouched while not
rendering them, is a design decision. **Preserving them is the minimum**: an editor may
decline to show a key, but it must not delete what it chose not to show.

### ✅ Fixed 2026-09-17 — the minimum, deliberately

`ObjectPropertyEditorViewModel.ToJsonValue` rebuilt the object from its `Children`, and the
writer diffs that object against the on-disk baseline — so a key with no child arrived at
the writer as a key the user had removed. The editor now captures, on load, the keys present
**at the editing scope** that no child models, and re-emits them verbatim on save.

**Preserving, not rendering** — the minimum the finding asked for. Rendering unknown keys is
a bigger design question (where in the six groups does an unknown variable go?) and it is not
what stops the data loss.

Four things worth knowing about the shape of it:

| | |
|---|---|
| Not just `env` | The defect was in the generic object editor, so **every** settings object with unmodelled keys had it. The fix is generic for the same reason |
| Only the editing scope | Carrying another scope's keys would promote an inherited value into an explicit override the user never asked for |
| Order is safe | The carried keys append to the emitted object, but `JsoncEditWriter` diffs per path — a key that comes back unchanged produces **no edit at all** and keeps its original position, comments and spacing |
| ⚠ Reset still clears them | *Reset to inherited* means "remove this property at this scope". Carrying keys through it would leave a property the user believes they cleared. Deliberate, and asserted |

⭐ **The round-trip contract already required this.** The editors' `AGENTS.md` §7 says
load-then-save must reproduce the value it was given; an object with unmodelled keys never
did, and nothing measured it.

**Guards:** 9 tests in `ObjectPropertyEditorNestedTests` (the carry itself, non-scalar values,
cloning, scope isolation, the empty-object contract, reset, re-load) plus one end-to-end test
in `SettingsGroupEditorViewModelTests` that drives the real `ApplyToWorkspace` flush — the path
the defect actually used. ⚠ All ten were **canaried**: with the re-emit disabled, six of the
nine and the end-to-end one go red, by name, and the end-to-end one fails on the surviving-key
assertion *after* its "the edit reached the workspace" premise passed.

⛔ **Still unverified in the running app.** `E4` re-runs against a rebuilt package-mode
artifact before this is called done.

---

## ✅ F10 · Seven env-group expander headers announce `Avalonia.Controls.Grid` — FIXED and verified

Found while driving `E4`, same build.

Every collapsible group on the Environment page exposes its header as a `Button` whose
accessible name is the **framework type name**:

```
[144] Button  id=ExpanderHeader  Avalonia.Controls.Grid
[147] Button  id=ExpanderHeader  Avalonia.Controls.Grid
…                                        (7 in total)
```

A screen-reader user tabbing the Environment page hears *"Avalonia.Controls.Grid, button"*
seven times, with nothing to distinguish `ANTHROPIC · 41` from `OTEL · 37`. The group's
label exists as a separate `Text` sibling, so the information is on screen and simply not
attached to the control that takes focus.

⚠ **Same class as `F6`, and the same reason the existing guards miss it.** `ExpanderHeader`
is a control-template part supplied by the theme, not markup this repository writes, so the
repo-wide `AutomationProperties.Name` check over `src/**/*.axaml` cannot see it — by
construction. `F6` was 26 chevrons; this is 7 expander headers, and the fix is the same
shape.

ⓘ Worse than `F6` in one respect: a chevron announcing nothing is unhelpful, but a control
announcing `Avalonia.Controls.Grid` actively asserts something false about what it is.

### ⛔ `Audit-Accessibility.ps1` cannot see this, and that is the lesson

The `B2` audit run on 2026-09-17 visited **291 elements across all three windows and reported
exactly ONE finding** — the scrollbar `Thumb`. It did not report these seven, because its rule is
*focusable with **no** accessible name*, and these have one. A name that is present but wrong is
invisible to it.

⚠ So a green accessibility audit means "nothing is unnamed", **not** "everything is named
usefully". Worth a second rule: flag any accessible name that matches a framework type
(`Avalonia.*`, `System.*`), since no such string is ever a legitimate user-facing name — that check
would have caught both this and the `PathIcon` case recorded previously.

### ⛔⛔ Correction 2026-09-18 — that rule ALREADY EXISTS, and the diagnosis above is wrong

`Audit-Accessibility.ps1` has carried `Test-LooksLikeTypeName` all along, and it is checked
**first**, ahead of the unnamed checks. Exercised directly rather than read:

| Name | Flagged |
|---|---|
| `Avalonia.Controls.Grid` | ✅ yes |
| `Avalonia.Controls.PathIcon` | ✅ yes |
| `ANTHROPIC  ·  41` | correctly no |
| `Advanced` | correctly no |

⭐ **The real reason the `B2` run reported one finding is that the audit only visits what is on
screen.** The Environment page was not open, so those seven elements were never walked. That is a
materially different lesson, and a more useful one: a green audit means *"nothing wrong on the
pages that happened to be showing"*, not *"nothing wrong in the app"*. Re-running it today **with
the Environment page open** visited 196 elements and reported the scrollbar `Thumb` and nothing
else.

### ✅ Fixed 2026-09-18 — the name was on the wrong element, not missing

⭐ **The view was already correct.** `PropertyEditorWrapper.axaml` sets
`AutomationProperties.Name` on the Expander; the Expander's own peer had it (the UIA walk showed
`Group  ANTHROPIC · 41` all along). Focus goes to the `ExpanderHeader` part, whose content is the
template's Grid, so `ContentControlAutomationPeer` fell back to `Content.ToString()`. Same shape as
the `PART_TextBox` cases already in `Themes/AccessibilityNames.axaml`, and the fix is one style
there.

⛔ **The measurement corrected the plan twice:**

1. **A plain `Header="some text"` announces the type name too.** The first draft assumed string
   headers were fine and that copying a name down might *overwrite* a good fallback; a guard
   written to assert that failed. The theme wraps the header in a Grid either way, so **all
   twelve** Expanders were affected, not the seven complex ones — and there was no fallback to
   protect.
2. **The selector needs `ToggleButton`, not `Button`.** UIA reports the part as `Button`; that is
   the peer's answer. The theme's own `ExpanderHeaderToggleButtonTheme` is the fact. A selector
   written from the walk matches nothing, builds clean, and leaves the header exactly as broken.

**Verified in the running app** (`v2026.3.918.957`): `Button id=ExpanderHeader` now announces
`ANTHROPIC · 41`, `OTEL · 37`, `Other · 33`, and a scan for `Avalonia.*` / `System.*` names across
the page returns **zero** where it returned seven. ⭐ Checked on the **Permissions** page too,
which `F10` never examined — its string-header `Advanced` expander announces `Advanced`, confirming
the "all twelve were broken" measurement rather than just the reported seven.

**Guards:** 4 peer tests in `TemplatePartAutomationNameTests` (complex header, string header, the
unnamed limit, and no-framework-type-names), plus `ExpanderAutomationNameTests` requiring every
Expander in markup to declare a name — the style's precondition, which fails **silently** when
absent. ⚠ Canaried both: breaking the selector reds exactly 3 peer tests by name and leaves the
other 6 green; removing one Expander's name reds the markup guard with the file and line.

---

## 🔵 F12 · `F9`'s fix landed in the app's object editor only; the shared library's still drops unmodelled keys

Found during **C0** (plans/00003 Phase C), not during the retest — C0 asks for proof that the two
branches' shared-library trees are identical before relying on the parked suite as neutrality
evidence. They are:

```
git diff release/claudeforge-on-packages feat/agentforge-opencodeforge \
  -- src/AgentForge src/LayeredEditors src/JsonC      ->  empty
```

⭐ **The empty diff is the finding.** It is empty because `F9`'s fix never reached the shared layer.
`1077e95` changed exactly one editor file — `src/ClaudeForge/ViewModels/Editors/ObjectPropertyEditorViewModel.cs`.

⚠ **There are TWO classes by that name and the repository already says so**, at
[`IChildEditorHost.cs`](../src/LayeredEditors.ViewModels/IChildEditorHost.cs) — *"one here in the
library and one in the app, and the app's does not derive from this one."* That comment exists
because a **type test** against either class covers only half the object editors in play. The same
split makes a **fix** against either class cover only half.

| | App copy | Library copy |
|---|---|---|
| File | `src/ClaudeForge/ViewModels/Editors/ObjectPropertyEditorViewModel.cs` | `src/LayeredEditors.ViewModels/ObjectPropertyEditorViewModel.cs` |
| Namespace | `Bennewitz.Ninja.ClaudeForge.ViewModels.Editors` | `Bennewitz.Ninja.LayeredEditors.ViewModels` |
| Lines | 331 | 118 |
| Constructed by | `DefaultEditorFactory.cs:311` | `DefaultPropertyEditorFactory.cs:93` |
| Carries `F9`'s fix | ✅ yes (`1077e95`) | ⛔ **no** |

The library copy still rebuilds the object from its schema-derived children, which is `F9`'s exact
shape — a key the schema never modelled has no child, so it has no way to survive:

```csharp
// src/LayeredEditors.ViewModels/ObjectPropertyEditorViewModel.cs:51
public override object? ToValue()
{
    Dictionary<string, object?> dict = new(StringComparer.Ordinal);
    foreach (PropertyEditorViewModel child in Children)
    { ... }
}
```

⛔ **What is NOT established, and must not be asserted without measuring it.** `F9` was destructive
in the app because `ToJsonValue` fed a writer that treated the rebuilt object as a whole-object
replacement. Whether the library's `ToValue()` reaches an equivalent path — rather than being
funnelled through the edit-based JSONC writer, which emits only changed keys — has **not** been
traced. Until it is, this is a matching *shape*, not a reproduced defect. The decisive experiment is
the one `F9` itself used: drive a save over an object holding a key the schema does not name, then
read the file.

⚠ **Why the suite cannot answer it.** `F9`'s 233 lines of new coverage
(`ObjectPropertyEditorNestedTests`, `SettingsGroupEditorViewModelTests`) were written against the
**app's** class and live in `ClaudeForge.Tests`. A green 4,429-test parked run says nothing about
the library copy, and did not: C0 passed at **4,429 · 0 · 11** with this present. A copy inherits
the original's defect, and the original's tests cannot see the copy — the same pair of halves as
`PathRuleMatcher.GlobBody` against `GitignoreReader.PatternToRegex`.

**Bearing on C2.** Not a blocker on its own reading of `00003`:51 — publishing `2026.3.918` and a
later `2026.3.925` is ordinary, so a defect shipped in one package version is fixed by the next
rather than stranded by immutability. What immutability removes is the option of *quietly* fixing
`2026.3.918` in place.
