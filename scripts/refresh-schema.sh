#!/usr/bin/env bash
# refresh-schema.sh — refresh every bundled JSON schema that has an upstream.
#
# The POSIX twin of scripts/refresh-schema.ps1. Both are maintained in parity; the .ps1 is
# what .github/workflows/schema-refresh.yml runs, this one exists so a contributor without
# pwsh has a hand-run path. If you change one, change the other — the schema table, the
# external-$ref strip and the normalised comparison must agree or the two produce different
# bundled files.
#
# WHY THIS EXISTS
# ---------------
# The bundled schemas under src/AgentForge.Core/Assets/Schemas/ are the AUTHORITATIVE
# source the runtime reads — even when the app's HTTP refresh downloads a newer copy into
# the disk cache, the runtime priority (memory cache > bundled embedded > disk cache >
# HTTP fetch > empty fallback) means the bundled file wins.  See CLAUDE.md
# "Schema loading priority".
#
# USAGE
# -----
#     bash scripts/refresh-schema.sh                        # refresh all
#     bash scripts/refresh-schema.sh --dry-run              # preview, write nothing
#     bash scripts/refresh-schema.sh --only opencode-config # one schema by name
#
# Works on Linux, macOS (bash 3.2) and Git Bash on Windows.  Requires curl, awk, and
# either jq or python3 for JSON validation.
#
# ----------------------------------------------------------------------------
# THREE THINGS THAT ARE NOT OBVIOUS
# ----------------------------------------------------------------------------
# 1. claude-desktop-config.json has NO upstream URL — its $id is a bare token, not a
#    resolvable URL.  Hand-maintained in-repo, deliberately absent from the table below.
#
# 2. EXTERNAL $refs ARE STRIPPED, AND THAT IS LOAD-BEARING.
#    Upstream opencode-config.json types four `model` properties with
#    "$ref": "https://models.dev/model-schema.json#/$defs/Model" alongside "type": "string".
#    Leaving it in makes schema evaluation throw a ref-resolution failure through
#    ValidateWorkspaceAsync -> SaveAsync for ANY config that sets a model, and the restore
#    path's evaluate guard does not catch that exception type.  Resolving it instead would
#    impose a 6,688-entry allow-list that rejects custom models.  So every download has the
#    strip re-applied; a refresh that forgot would ship a schema that breaks saving.
#
#    The strip is TEXTUAL and preserves upstream's formatting.  Parsing and re-serialising
#    would reformat all ~1,300 lines and produce a diff no reviewer could read.
#
#    This script does NOT verify the stripped properties are still typed — that is
#    BundledOpenCodeSchemaTests.StrippingTheRefLeftTheModelPropertiesTyped, which checks all
#    four sites by JSON pointer.
#
# 3. COMPARISON IS LINE-ENDING NORMALISED.  .gitattributes sets `* text=auto`, so a Windows
#    checkout holds CRLF while every download is LF — claude-code-settings.json is 4,260
#    bytes larger on disk than upstream for exactly that reason, byte-identical once
#    normalised.  A raw hash compare reported drift on every local Windows run.
#
# ----------------------------------------------------------------------------
# Hand-curated additions live in sibling *.overlay.json files, applied at load time via
# RFC 7396 JSON Merge Patch.  This script only touches the BASE files, so overlay edits
# persist across refreshes.
# ----------------------------------------------------------------------------

set -euo pipefail

DRY_RUN=0
ONLY=''

while [[ $# -gt 0 ]]; do
    case "$1" in
        --dry-run|-n) DRY_RUN=1; shift ;;
        --only)       ONLY="${2:-}"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2
           echo "Usage: refresh-schema.sh [--dry-run] [--only <name>]" >&2
           exit 2 ;;
    esac
done

# Anchor on the script's location so relative target paths work regardless of cwd.
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
REPO_ROOT="$( cd "$SCRIPT_DIR/.." && pwd )"
SCHEMA_DIR="$REPO_ROOT/src/AgentForge.Core/Assets/Schemas"

# ── The table ────────────────────────────────────────────────────────────────
# name|file|url — parallel-array-free so this stays bash 3.2 clean (macOS has no
# associative arrays). Keep name equal to the file's base name.
SCHEMAS=(
    "claude-code-settings|claude-code-settings.json|https://json.schemastore.org/claude-code-settings.json"
    "opencode-config|opencode-config.json|https://opencode.ai/config.json"
    "opencode-tui|opencode-tui.json|https://opencode.ai/tui.json"
)

# ANSI colours — disabled when stdout is not a tty so CI logs stay plain.
if [[ -t 1 ]]; then
    C_CYAN=$'\033[36m'; C_GREEN=$'\033[32m'; C_YELLOW=$'\033[33m'
    C_RED=$'\033[31m';  C_RESET=$'\033[0m'
else
    C_CYAN=''; C_GREEN=''; C_YELLOW=''; C_RED=''; C_RESET=''
fi

# ── Deps ─────────────────────────────────────────────────────────────────────
if ! command -v curl >/dev/null 2>&1; then
    echo "${C_RED}ERROR: curl is required but not found in PATH${C_RESET}" >&2
    exit 2
fi

if command -v jq >/dev/null 2>&1; then
    json_validate() { jq empty "$1" >/dev/null 2>&1; }
elif command -v python3 >/dev/null 2>&1; then
    json_validate() { python3 -c "import json,sys; json.load(open(sys.argv[1]))" "$1" >/dev/null 2>&1; }
else
    echo "${C_RED}ERROR: neither jq nor python3 found — cannot validate downloaded JSON${C_RESET}" >&2
    exit 2
fi

# ── Helpers ──────────────────────────────────────────────────────────────────

REF_PATTERN='^[[:space:]]*"\$ref"[[:space:]]*:[[:space:]]*"https?://'

# Delete every http(s) $ref line, keeping the JSON valid.
#
# Two cases, and getting them backwards produces invalid JSON:
#   * the $ref line ends with a comma -> siblings follow, drop the line alone
#   * it does not                     -> $ref was the LAST key, so the PRECEDING line's
#                                        trailing comma must go too
# All four sites in today's opencode-config.json are the second case.
strip_external_refs() {
    # $1 = 1 when the input ended with a newline, 0 when it did not. awk's `print`
    # ALWAYS terminates a line, so without this the two schemas that carry no trailing
    # newline come back one byte longer and report CHANGED on every single run.
    awk -v trailnl="$1" '
        function rtrim(s) { sub(/[[:space:]]+$/, "", s); return s }
        {
            if ($0 ~ /^[[:space:]]*"\$ref"[[:space:]]*:[[:space:]]*"https?:\/\//) {
                if (rtrim($0) ~ /,$/) { next }
                for (j = out; j >= 1; j--) {
                    s = rtrim(buf[j])
                    if (s == "") { continue }
                    if (s ~ /,$/) { sub(/,$/, "", s); buf[j] = s }
                    break
                }
                next
            }
            buf[++out] = $0
        }
        END {
            for (i = 1; i <= out; i++) {
                if (i < out || trailnl == 1) { print buf[i] } else { printf "%s", buf[i] }
            }
        }
    '
}

# 1 when the file's last byte is a newline. Command substitution strips trailing
# newlines, so an empty result means the last byte was one -- portable, unlike
# `head -c -1`, which is GNU-only.
ends_with_newline() {
    if [[ -z "$(tail -c 1 "$1")" ]]; then echo 1; else echo 0; fi
}

# Count lines the way PowerShell's Split does, so both scripts print the same number.
count_lines() { awk 'END { print NR }' "$1"; }

# Normalise CRLF -> LF so a Windows checkout compares equal to an LF download.
normalise() { tr -d '\r' < "$1"; }

# ── Run ──────────────────────────────────────────────────────────────────────

MATCHED=0
CHANGED=''
FAILED=''

echo
echo "${C_CYAN}Refreshing bundled schema(s)${C_RESET}"
echo "  target dir : $SCHEMA_DIR"
if [[ $DRY_RUN -eq 1 ]]; then
    echo "  ${C_YELLOW}mode       : DRY RUN (no files will be written)${C_RESET}"
fi
echo

TEMP_DIR="$(mktemp -d 2>/dev/null || mktemp -d -t refresh-schema)"
trap 'rm -rf "$TEMP_DIR"' EXIT

for entry in "${SCHEMAS[@]}"; do
    NAME="${entry%%|*}"
    REST="${entry#*|}"
    FILE="${REST%%|*}"
    URL="${REST#*|}"

    if [[ -n "$ONLY" && "$ONLY" != "$NAME" ]]; then
        continue
    fi
    MATCHED=$((MATCHED + 1))

    TARGET="$SCHEMA_DIR/$FILE"
    echo "${C_CYAN}-- $NAME${C_RESET}"
    echo "   upstream : $URL"

    if [[ ! -f "$TARGET" ]]; then
        echo "   ${C_RED}ERROR: no bundled copy at $TARGET - wrong repo root?${C_RESET}" >&2
        FAILED="$FAILED $NAME"
        continue
    fi

    RAW="$TEMP_DIR/$NAME.raw"
    if ! curl --fail --silent --show-error --location --output "$RAW" "$URL"; then
        echo "   ${C_RED}ERROR: download failed${C_RESET}" >&2
        FAILED="$FAILED $NAME"
        continue
    fi

    if ! json_validate "$RAW"; then
        echo "   ${C_RED}ERROR: downloaded file is not valid JSON${C_RESET}" >&2
        head -c 200 "$RAW" >&2; echo >&2
        FAILED="$FAILED $NAME"
        continue
    fi

    # ── The strip. See note 2. ──
    REF_COUNT="$(grep -cE "$REF_PATTERN" "$RAW" || true)"
    CANDIDATE="$TEMP_DIR/$NAME.stripped"
    TRAIL_NL="$(ends_with_newline "$RAW")"

    if [[ "$REF_COUNT" -gt 0 ]]; then
        normalise "$RAW" | strip_external_refs "$TRAIL_NL" > "$CANDIDATE"
    else
        # Nothing to strip: pass the download through untouched rather than round-tripping
        # it through awk, which is both faster and one less chance to alter a byte.
        normalise "$RAW" > "$CANDIDATE"
    fi

    if [[ "$REF_COUNT" -gt 0 ]]; then
        echo "   ${C_YELLOW}stripped $REF_COUNT external \$ref line(s)${C_RESET}"
        if ! json_validate "$CANDIDATE"; then
            echo "   ${C_RED}ERROR: the strip produced invalid JSON - upstream shape changed.${C_RESET}" >&2
            echo "          Inspect the download by hand; do NOT commit this." >&2
            FAILED="$FAILED $NAME"
            continue
        fi
    fi

    if grep -qE '"\$ref"[[:space:]]*:[[:space:]]*"https?://' "$CANDIDATE"; then
        echo "   ${C_RED}ERROR: an http \$ref survived the strip.${C_RESET}" >&2
        echo "          A bundled schema must resolve offline; see note 2." >&2
        FAILED="$FAILED $NAME"
        continue
    fi

    # ── Compare, normalised. See note 3. ──
    CURRENT="$TEMP_DIR/$NAME.current"
    normalise "$TARGET" > "$CURRENT"

    if cmp -s "$CURRENT" "$CANDIDATE"; then
        echo "   ${C_GREEN}already up to date ($(count_lines "$CURRENT") lines)${C_RESET}"
        continue
    fi

    OLD_LINES="$(count_lines "$CURRENT")"
    NEW_LINES="$(count_lines "$CANDIDATE")"
    echo "   ${C_YELLOW}CHANGED: lines $OLD_LINES -> $NEW_LINES${C_RESET}"

    if [[ $DRY_RUN -eq 1 ]]; then
        echo "   ${C_YELLOW}dry run - not written${C_RESET}"
        CHANGED="$CHANGED $NAME"
        continue
    fi

    # Write LF explicitly (the candidate already is). Git normalises on `add` per
    # .gitattributes; writing platform-native here would reintroduce note 3's confusion.
    cp "$CANDIDATE" "$TARGET"
    echo "   ${C_GREEN}written${C_RESET}"
    CHANGED="$CHANGED $NAME"
done

# ── Summary ──────────────────────────────────────────────────────────────────

echo
if [[ -n "$ONLY" && $MATCHED -eq 0 ]]; then
    echo "${C_RED}Unknown schema name: $ONLY${C_RESET}" >&2
    KNOWN=''
    for entry in "${SCHEMAS[@]}"; do KNOWN="$KNOWN ${entry%%|*}"; done
    echo "Known:$KNOWN" >&2
    exit 2
fi

if [[ -n "$FAILED" ]]; then
    echo "${C_RED}FAILED:$FAILED${C_RESET}" >&2
    echo "Nothing was written for those. Fix the cause before committing anything else." >&2
    exit 1
fi

if [[ -z "$CHANGED" ]]; then
    echo "${C_GREEN}All bundled schemas match upstream. Nothing to do.${C_RESET}"
    exit 0
fi

echo "${C_YELLOW}CHANGED:$CHANGED${C_RESET}"
echo
echo "${C_CYAN}Note:${C_RESET} the sibling *.overlay.json files were NOT touched; they are merged onto"
echo "      whichever base wins at load time via RFC 7396 JSON Merge Patch."
echo
echo "${C_CYAN}Next steps:${C_RESET}"
echo "  1. dotnet build ClaudeForge.slnx -c Debug     # the schemas still parse + bundle"
echo "  2. dotnet test ClaudeForge.slnx -c Debug      # BundledOpenCodeSchemaTests is the"
echo "                                                #   guard on the strip staying typed"
echo "  3. git diff -- src/AgentForge.Core/Assets/Schemas/"
echo "  4. commit as: chore(schema): refresh bundled schemas from upstream"
echo
exit 0
