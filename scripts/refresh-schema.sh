#!/usr/bin/env bash
# refresh-schema.sh — manually refresh the bundled Claude Code JSON schema.
#
# WHY THIS EXISTS
# ---------------
# The bundled schema at src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.json
# is the AUTHORITATIVE source the runtime reads — even when the app's HTTP
# refresh downloads a newer copy into ~/.claude/cache/schemas/, the runtime
# priority (memory cache > bundled embedded > disk cache > HTTP fetch >
# empty fallback) means the bundled file wins.  See CLAUDE.md
# "Schema loading priority".
#
# Consequence: if Anthropic ships a new model id, hook trigger, or settings
# property and we don't refresh THIS file, the editor never surfaces it.
#
# This script is the hand-run path; the weekly drift-check workflow at
# .github/workflows/schema-refresh.yml runs the .ps1 sibling unchanged
# and opens a PR if the working tree differs.  You can also run this
# locally (between releases, or whenever a missing field is reported by
# a user) and commit the diff as:
#
#     chore: refresh bundled claude-code-settings.json from schemastore.org
#
# USAGE
# -----
#     bash scripts/refresh-schema.sh           # apply
#     bash scripts/refresh-schema.sh --dry-run # preview the diff, do not write
#
# Works on Linux, macOS, and Git Bash on Windows.  Requires `curl` (universal)
# and either `jq` (preferred) or `python3` (fallback) for JSON validation.
#
# claude-desktop-config.json has no upstream URL ($id is a bare token, not a
# resolvable URL).  It is hand-maintained in-repo.  This script only refreshes
# claude-code-settings.json.
#
# ----------------------------------------------------------------------------
# Hand-curated additions live in a sibling overlay file
# ----------------------------------------------------------------------------
# Hand-curated additions to the bundled schema live in a separate file:
# `claude-code-settings.overlay.json`, applied at load time by
# SchemaRegistry via RFC 7396 JSON Merge Patch.  This refresh script
# only touches `claude-code-settings.json` — the overlay is NEVER affected
# by a refresh.  Edits there persist across refreshes; this script is
# idempotent for the overlay's contents.
#
# Today the overlay carries `model.default`, `model.examples`, and an
# enriched `model.description` because upstream schemastore.org omits
# them (the model alias list churns faster than the schema does).  If a
# future upstream schema carries `examples` natively, simply delete the
# matching key from the overlay — the merge will then surface upstream's
# value unchanged.
# ----------------------------------------------------------------------------

set -euo pipefail

DRY_RUN=0
if [[ "${1:-}" == "--dry-run" || "${1:-}" == "-n" ]]; then
    DRY_RUN=1
fi

# Anchor on the script's location so the relative target path works regardless
# of cwd.
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"
TARGET_PATH="$REPO_ROOT/src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.json"
UPSTREAM_URL='https://json.schemastore.org/claude-code-settings.json'

# ANSI colour helpers — disable when stdout is not a tty (CI logs, redirected
# output) so plain-text consumers don't see escape codes.
if [[ -t 1 ]]; then
    C_CYAN=$'\033[36m'
    C_GREEN=$'\033[32m'
    C_YELLOW=$'\033[33m'
    C_RED=$'\033[31m'
    C_RESET=$'\033[0m'
else
    C_CYAN=''
    C_GREEN=''
    C_YELLOW=''
    C_RED=''
    C_RESET=''
fi

echo
echo "${C_CYAN}Refreshing Claude Code schema${C_RESET}"
echo "  upstream : $UPSTREAM_URL"
echo "  target   : $TARGET_PATH"
if [[ $DRY_RUN -eq 1 ]]; then
    echo "  ${C_YELLOW}mode     : DRY RUN (no files will be written)${C_RESET}"
fi
echo

# ---------------------------------------------------------------------------
# 0. Verify deps.
# ---------------------------------------------------------------------------
if ! command -v curl >/dev/null 2>&1; then
    echo "${C_RED}ERROR: curl is required but not found in PATH${C_RESET}" >&2
    exit 2
fi

# Pick a JSON validator: jq preferred, python3 fallback.  Both are universal
# on modern Linux + macOS; jq might be absent on minimal containers, hence
# the fallback.
if command -v jq >/dev/null 2>&1; then
    json_validate() { jq empty "$1" >/dev/null 2>&1; }
elif command -v python3 >/dev/null 2>&1; then
    json_validate() { python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$1" >/dev/null 2>&1; }
else
    echo "${C_RED}ERROR: neither jq nor python3 found — cannot validate downloaded JSON${C_RESET}" >&2
    exit 2
fi

# ---------------------------------------------------------------------------
# 1. Download to a temp file (atomic-write pattern).
# ---------------------------------------------------------------------------
TEMP_PATH="$(mktemp -t claude-code-settings.XXXXXX.json 2>/dev/null \
              || mktemp /tmp/claude-code-settings.XXXXXX.json)"

# Clean up the temp file on any exit path (success, failure, Ctrl-C).
trap 'rm -f "$TEMP_PATH"' EXIT

if ! curl --fail --silent --show-error --location \
          --output "$TEMP_PATH" \
          "$UPSTREAM_URL"; then
    echo "${C_RED}ERROR: failed to download schema from $UPSTREAM_URL${C_RESET}" >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# 2. Verify the download is valid JSON before touching the target file.
# ---------------------------------------------------------------------------
if ! json_validate "$TEMP_PATH"; then
    echo "${C_RED}ERROR: downloaded file is not valid JSON${C_RESET}" >&2
    echo "First 200 bytes for diagnostic:" >&2
    head -c 200 "$TEMP_PATH" >&2
    echo >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# 3. Compare against the current bundled copy.
# ---------------------------------------------------------------------------
if [[ ! -f "$TARGET_PATH" ]]; then
    echo "${C_RED}ERROR: target schema file not found at $TARGET_PATH — wrong repo root?${C_RESET}" >&2
    exit 1
fi

# Use sha256sum if available (Linux) or shasum -a 256 (macOS / Git Bash).
sha256_of() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    else
        shasum -a 256 "$1" | awk '{print $1}'
    fi
}

OLD_HASH="$(sha256_of "$TARGET_PATH")"
NEW_HASH="$(sha256_of "$TEMP_PATH")"

OLD_BYTES="$(wc -c < "$TARGET_PATH" | tr -d ' ')"
NEW_BYTES="$(wc -c < "$TEMP_PATH"   | tr -d ' ')"
OLD_LINES="$(wc -l < "$TARGET_PATH" | tr -d ' ')"
NEW_LINES="$(wc -l < "$TEMP_PATH"   | tr -d ' ')"

if [[ "$OLD_HASH" == "$NEW_HASH" ]]; then
    echo "${C_GREEN}Already up to date ($OLD_LINES lines, $OLD_BYTES bytes).${C_RESET}"
    exit 0
fi

# ---------------------------------------------------------------------------
# 4. Show a brief diff summary so the operator sees the magnitude of change.
# ---------------------------------------------------------------------------
delta_lines=$((NEW_LINES - OLD_LINES))
delta_bytes=$((NEW_BYTES - OLD_BYTES))
echo "Change summary:"
printf "  lines : %s -> %s  (delta %+d)\n" "$OLD_LINES" "$NEW_LINES" "$delta_lines"
printf "  bytes : %s -> %s  (delta %+d)\n" "$OLD_BYTES" "$NEW_BYTES" "$delta_bytes"
echo

# If `git` is available, show the actual diff truncated to ~50 lines so the
# operator sees WHAT changed before committing.
if command -v git >/dev/null 2>&1; then
    echo "${C_CYAN}Diff (first 50 lines):${C_RESET}"
    git --no-pager diff --no-index --stat -- "$TARGET_PATH" "$TEMP_PATH" 2>/dev/null || true
    echo
    git --no-pager diff --no-index -- "$TARGET_PATH" "$TEMP_PATH" 2>/dev/null \
        | head -n 50 \
        || true
    echo
fi

# ---------------------------------------------------------------------------
# 5. Write through (or stop on dry-run).
# ---------------------------------------------------------------------------
if [[ $DRY_RUN -eq 1 ]]; then
    echo "${C_YELLOW}Dry run — target file NOT modified.  Re-run without --dry-run to apply.${C_RESET}"
    exit 0
fi

# Atomic replace: mv from temp to target.  Same filesystem (both inside the
# repo's tempdir scope on most systems), so this is a single rename syscall.
mv -f "$TEMP_PATH" "$TARGET_PATH"
# Disarm the cleanup trap so we don't try to delete the now-renamed file.
trap - EXIT

echo "${C_GREEN}Bundled schema updated.${C_RESET}"
echo

# ---------------------------------------------------------------------------
# 6. Note about the sibling overlay file.
# ---------------------------------------------------------------------------
echo "${C_CYAN}Note:${C_RESET} hand-curated additions live in"
echo "      src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.overlay.json"
echo "      and are applied at load time via RFC 7396 JSON Merge Patch."
echo "      This refresh did NOT touch them; they will surface in the merged"
echo "      runtime schema unchanged."
echo
echo "${C_CYAN}Next steps:${C_RESET}"
echo "  1. dotnet build              # verify the refreshed schema still parses + bundles."
echo "  2. dotnet test               # verify dependent tests still pass."
echo "  3. git diff -- src/ClaudeForge.Core/Assets/Schemas/claude-code-settings.json"
echo "  4. git add + commit with: 'chore: refresh bundled claude-code-settings.json from schemastore.org'"
echo
