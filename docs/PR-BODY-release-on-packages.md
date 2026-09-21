# PR body — `release/claudeforge-on-packages` → `main`

> Paste the section below as the pull request description.
>
> ⛔ **The text of this PR body becomes the release notes.** `release.yml` reads the **merged PR's
> body** first, then the tagged commit's message body, then auto-generated notes — it never reads
> `CHANGELOG.md`. Editing it after merge does not change a release already cut.

---

## Summary

- **ClaudeForge is built from the eleven published `Bennewitz.Ninja.*` packages** rather than from
  project references — the claim two comments in this repository made, and which was false for
  every release ever cut.
- **Enterprise policy is read from the directory Claude Code actually reads it from**, instead of
  `~/.claude/`.
- **`CLAUDE_CONFIG_DIR` is honoured** everywhere the Claude home is resolved. The app documented
  this variable in its own tooltips and then ignored it.

## Motivation / linked issue

Two defects found on 2026-09-17, both silent, neither reported by anything:

1. **The release had never been built from the shared packages.** `ci.yml` and
   `package-canary.ps1` both said it was. The only `dotnet publish` in the release chain passed
   none of the flags that would select package mode, so every RID of every release used
   `ProjectReference`. The canary stayed green throughout, because the canary is not the release.
2. **Managed settings were read from the wrong directory entirely.** Claude Code reads enterprise
   policy from a per-OS *system* directory; ClaudeForge read `~/.claude/managed-settings.json`. It
   failed **both ways**: a machine with real policy showed no managed layer at all — so the
   effective view told the user their own value won where policy overrides it — and a file the
   user placed in `~/.claude/` displayed as enforced while doing nothing.

Plans: [`plans/00002`](https://github.com/JanusMael/ClaudeForge/blob/main/plans/00002-claude-code-real-config-locations.md),
[`plans/00003`](https://github.com/JanusMael/ClaudeForge/blob/main/plans/00003-release-built-from-shared-packages.md).

## Changes

**The release consumes the packages** (plan 00003, Phase D)

- `Publish-Rid.ps1` selects package mode at the pinned `SharedPackageVersion`; `release.yml` gains
  `packages: read` **and** the feed credentials, which are two independent halves — the first two
  release attempts failed on each of them in turn.
- `GuardShippingPublishUsesPackages` fails a bare Release publish, with a recorded escape hatch
  (`-p:AllowProjectReferencePublish=true`) for gates that have no feed credentials.
- A `published-version` CI job runs the suite and a trimmed publish against the **feed**, with no
  local feed and no package cache.

**The resolved Claude home** (plan 00002)

- `ClaudeEnvironment` is a value read once at composition. `PlatformPaths.ClaudeHome` and the
  thirteen members derived from it, and `ClaudeArtifactPaths`, now take it as a **required**
  parameter. Required is the mechanism: it turned every stale call site into a compile error,
  which is the only thing that finds all ~104 of them.
- `BackupEngine.Default`, `SchemaRegistry.ClaudeCodeProduct` and `ClaudeArtifactPaths.Default` are
  **deleted** rather than kept beside their environment-taking replacements. Keeping any of them
  would leave existing call sites compiling while silently resolving `~/.claude`.
- `ProductDescriptor` equality is now Id-based. The synthesized record equality had already
  stopped being meaningful — the backup layout holds `Func` destinations, which compare by
  reference — and only appeared to work because each product was a single static instance.
- Managed policy resolves from the per-OS system directory, and `managed-mcp.json` is handled.

**Guards added**

- `ResolvedHomeBypassTests` — production code may not read `SpecialFolder.UserProfile` or
  `CLAUDE_CONFIG_DIR`, or compose `".claude"` onto a profile root or a managed-settings path,
  outside four named owners.
- `ProductDescriptorEqualityTests`, `ClaudeArtifactPathsTests` relocation coverage, and
  `ShippingPublishModeTests`.

## Breaking changes

⚠ **Breaking for consumers of `Bennewitz.Ninja.AgentForge.Core` and `.AgentForge.Sdk`.** Properties
became methods taking a `ClaudeEnvironment`, and the three statics named above are gone. The
public-surface baselines are regenerated in the same change so the diff is reviewable. These
packages publish to an immutable feed, so this cannot be revised after a publish.

## Test plan

- [x] `dotnet build ClaudeForge.slnx -c Debug` — 0 errors, 0 warnings
- [x] `dotnet test ClaudeForge.slnx -c Debug` — **3,586 passed · 0 failed · 13 skipped**
- [x] CI green on **windows-latest, ubuntu-latest and macos-latest**, plus the package canary,
      the feed restore and the trim check
- [x] Release `win-x64` self-contained trimmed publish — zero IL diagnostics
- [x] New tests added for new behaviour, and **canaried**: each new guard was shown to redden
      against a planted defect, and the source scan was additionally shown to redden when a rule
      stops matching its sanctioned sites
- [ ] Manually tested on: <!-- fill in -->

⛔ **One CI job is red and is expected to be**: `Published Version` builds against the *published*
package version, which does not yet contain this branch's shared libraries. It greens with the
`2026.3.921` package release and not before.

## Checklist

- [x] XML doc comments added for any new public API
- [x] No hardcoded secrets or credentials
- [x] Any new `JsonSerializer` usage goes through a source-gen context (trimming safety)
