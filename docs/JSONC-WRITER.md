# The JSONC writer — contract and guarantees

`AgentForge.Jsonc` plus `JsoncEditWriter` replace whole-document re-serialization with
minimal in-place edits, so a save changes the bytes the user changed and leaves the rest
alone.

This is the highest-consequence code path in the product: a bug corrupts config for both
apps. This page is the contract it is held to.

---

## What it guarantees

| Preserved across a save | Why it works |
|---|---|
| `//` and `/* */` comments | Never rewritten — only changed value spans are replaced |
| Blank lines and vertical spacing | Same |
| Key order | Same; the writer never reorders |
| Indentation style (tabs vs spaces, width) | Detected from the document, and inserted text matches |
| Line endings (LF vs CRLF) | Detected from the first line break; inserted text matches |
| Unknown keys the tool does not model | Same as any other untouched span |

**A save with no pending edits is byte-identical.** The file is not written at all — see
*The save stamp* below for the decision that made this statable.

## What it does not guarantee

- **Arrays are replaced wholesale, not diffed element-wise.** Changing one permission rule
  rewrites the whole `allow` array, so a comment *inside* an array is lost. Deliberate:
  these arrays are lists the user edits as a unit, and index-addressed element edits are
  much harder to verify than one replacement. Revisit only if a real file turns up with
  comments between array elements.
- **A file that does not parse is re-serialized, not preserved.** The writer falls back and
  logs a warning. See *Refusal* below for why that is the safe direction.
- **Nothing is promised about a file the tool has never read.** A new config file is
  written by the serializer, because there is no formatting to preserve yet.

## Refusal is the safety property

`JsoncEditor` **throws rather than edit a document that did not parse cleanly.**
`JsoncDocument.IsEditable` is false whenever there are parse errors, and every entry point
checks it.

This is a direct response to the bug it replaces. `ConfigFileLoader.LoadAsync` used to
parse with default options, which **throw on a comment**; the exception was caught and
turned into an *empty* `JsonObject`; and the next save serialized that empty document over
the user's file. **A single comment, or one stray character, was enough to lose a config
file.** Both halves are now fixed — the reader skips comments and allows trailing commas,
and the writer refuses to guess at anything it could not read.

An empty or comments-only document is a different case and stays editable. Refusing that
would make it impossible to write a config file that does not exist yet.

## The save stamp — maintainer decision

`MainWindowViewModel.MakeHeaderComment()` embeds `DateTime.Now` to the second. Written
unconditionally, it makes "no edits → identical bytes" impossible: every save rewrites that
line.

Three options were on the table. **Chosen: write the stamp only when something else
changed.**

- A save with nothing dirty is genuinely byte-identical, and the file is not touched.
- A save that changes something updates the stamp as before.
- `ConfigFileLoader.SaveAsync` gates on `SettingsDocument.HasActualChanges()`.

Rejected: excluding the stamp from the byte-stability claim (cheapest, but accepts a
permanent one-line git diff on every save), and making the stamp opt-out (most
user-friendly, but needs a persisted setting, a UI row, and 9-locale resx strings —
revisit if users with git-tracked config ask).

Guarded by `SaveAsync_CleanDocument_WritesNoStampAndLeavesBytesIdentical`. Without that
test the conditional is an unverified claim, and "simplifying" the stamp back to
unconditional would pass everything else.

## The `--writer legacy` escape hatch

`IConfigWriter` (in `AgentForge.Abstractions`) has two implementations:

| Writer | Behaviour |
|---|---|
| `JsoncEditWriter` | **Default.** Minimal edits; preserves everything above. |
| `LegacySerializingWriter` | Pre-Phase-2 behaviour: re-serialize the whole document with `WriteIndented = true`. Lossy by construction. |

`ConfigFileLoader.SaveAsync` takes an optional `IConfigWriter`, defaulting to
`DefaultWriter`. The interface lives in `AgentForge.Abstractions` because the selection
happens in the app while the call site is deep in Core, and neither may reference the other.

> ### ⚠ Remove the hatch after one clean release
>
> Two writers means every future save-path change has to be correct twice, and the lossy
> one is the one nobody will remember to test.
> `LegacyWriter_StillReSerializes_SoTheContrastIsExplicit` asserts the fallback *is* lossy,
> so the cost of reaching for it is documented rather than discovered.

**Still to wire:** the `--writer legacy` command-line flag itself. The seam and both
implementations exist and are tested; the app does not yet parse the flag or thread its
selection to the save call sites, so today the hatch is reachable from code but not from
the command line.

## How the diff works

The writer needs to know which paths changed. It does not need new change-tracking:
`SettingsDocument` already keeps `BaselineRoot`, a snapshot taken at load and refreshed on
save. Baseline versus current **is** the path-level change set.

```
JsoncEditWriter.Render(originalText, baselineRoot, root, headerComment)
  → diff baselineRoot vs root       → set-at-path / remove-at-path list
  → JsoncEditor.SetValue / .Remove  → TextEdits against originalText
  → new text
```

Recursing into objects present on both sides is what keeps edits narrow: changing
`permissions.defaultMode` emits one leaf change rather than replacing the whole
`permissions` object and destroying the comments inside it.

Changes are applied one at a time, re-parsing between each. Batching all the edits against
a single parse can produce two insertions at the same offset, which `TextEdit.Apply`
correctly rejects as overlapping. Config files are kilobytes and saves are user-initiated,
so re-parsing costs nothing measurable and removes a class of ordering bug.

## Path semantics

Deliberately identical to the SDK's existing traversal
(`AgentConfigClientCore.SetNested` / `ResolveByPath`):

- Split on `.`
- Objects only — no array indexing
- Missing intermediate objects are created on set
- A key containing a `.` is therefore unreachable

Divergence here would produce paths that write to one place and read from another — a bug
that survives a green suite.

## Why this was built rather than taken as a dependency

- `System.Text.Json` cannot do it. `dotnet/runtime#98865` proposes
  `JsonCommentHandling.Allow` for `JsonNode`; still a proposal.
- `microsoft/JsonPlus` does preserve trivia, but has no published releases or NuGet
  packages, and decodes ~2× slower. Vendoring an unreleased repo into a project that ships
  signed binaries is a supply-chain risk that outweighs the saved effort.
- The rest of the JSONC-for-.NET field is strip-only — lossy by construction.

The design follows `microsoft/node-jsonc-parser`: scan to tokens with offsets, parse to a
tree with spans, return text edits.

## Tests

| Area | Where |
|---|---|
| Scanner, incl. gapless-coverage property over a nasty corpus | `AgentForge.Jsonc.Tests/JsoncScannerTests` |
| Comment / formatting / key-order preservation | `…/JsoncEditorPreservationTests` |
| Insert, remove, nested-path creation | `…/JsoncEditorMutationTests` |
| Refusal on unparseable input | `…/JsoncEditorSafetyTests` |
| `TextEdit.Apply` edge cases | `…/TextEditTests` |
| End-to-end through the real loader | `AgentForge.Core.Tests/FileIO/ConfigFileLoaderPreservationTests` |

The gapless-coverage test is the load-bearing one: every edit is a span replacement, so a
scanner that loses or double-counts a character would silently misplace edits.

**All of the above were canaried** — the refusal, style detection, whitespace tokenization,
and the writer default were each broken deliberately to confirm the expected tests fail.
That exposed one real weakness: `TabIndentation_IsNotConvertedToSpaces` and
`CrlfLineEndings_Survive` both passed with style detection disabled entirely, because
replacing one scalar with another never consults the style.
`InsertedMultiLineValue_UsesTheDocumentsTabsAndCrlf` was added to cover the case that
actually breaks a user's file.
