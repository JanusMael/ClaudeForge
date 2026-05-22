#!/bin/bash
# ============================================================================
# allow-app-to-run.sh — macOS Gatekeeper quarantine remover for ClaudeForge
# ============================================================================
#
# WHAT THIS SCRIPT DOES
# ─────────────────────
# Recursively removes the `com.apple.quarantine` extended attribute from
# every file in this script's own directory.  After it runs, ClaudeForge
# can be launched normally — macOS will no longer refuse it as "from an
# unidentified developer" or "cannot be opened because Apple cannot check
# it for malicious software."
#
# WHY YOU NEED THIS
# ─────────────────
# When you download a file from the internet (web browser, curl, etc.),
# macOS attaches a `com.apple.quarantine` extended attribute (xattr) to
# it.  On first launch, Gatekeeper inspects this xattr and verifies the
# binary is BOTH:
#
#   1. signed by an Apple-registered developer, AND
#   2. notarized by Apple's notary service.
#
# If either check fails (and on an unsigned binary BOTH fail), Gatekeeper
# refuses to run it.  No menu option is offered in modern macOS to
# override per-launch — the only path is to remove the quarantine xattr.
#
# ClaudeForge v1 ships UNSIGNED and UNNOTARIZED.  Code-signing and
# notarization both require an active Apple Developer Program membership
# ($99/year), which we've chosen not to take on for the v1 open-source
# release.  This script is the workaround: it removes the quarantine
# xattr so Gatekeeper's "you didn't sign this" objection no longer
# applies.
#
# This is the SAME mechanism the OS uses when you right-click → Open and
# confirm in the Gatekeeper dialog — but it works without you needing to
# discover the right-click trick, and it scopes cleanly to the directory
# you extracted the release into.  The recursive walk also defends
# against any future change to the publish layout that drops supporting
# `.dylib` files alongside the binary (Apple's Gatekeeper inspects each
# file's quarantine xattr independently — clearing only the launched
# binary's xattr leaves any sibling .dylib quarantined, which surfaces
# at runtime as "Library not loaded: ... operation not permitted").
# Today's build is a `PublishSingleFile=true` bundle so there's just one
# file to worry about, but `xattr -dr` is cheap insurance against that
# changing.
#
# SECURITY IMPLICATIONS
# ─────────────────────
# Running this script ONLY removes the quarantine xattr from files in
# THIS directory.  It does NOT:
#   - disable Gatekeeper system-wide,
#   - preauthorize any other binary you download,
#   - change any system policy or security settings,
#   - require kernel modifications or SIP changes.
#
# Each new file you download gets its own fresh quarantine xattr and
# Gatekeeper checks it from scratch.  This script is the equivalent of
# you saying "I trust THIS specific extracted directory" — same trust
# you give when you accept the right-click → Open dialog, just applied
# to a multi-file release at once.
#
# You should ONLY run this script on archives you've verified.  For
# ClaudeForge, the corresponding trust step is one of:
#
#   - Verifying the SHA256 of the downloaded `.tar.gz` against the value
#     published on the GitHub release page, OR
#   - Building from source — see CONTRIBUTING.md.
#
# If you'd rather not run an unsigned binary at all, build from source
# is the recommended path.  The source is on GitHub and uses standard
# `dotnet publish` with no proprietary tooling.
#
# WHEN TO RUN AS SUDO
# ───────────────────
# `xattr -d` operates on file ownership permissions.  If you extracted
# ClaudeForge into your home directory (or anywhere you own), this
# script works WITHOUT sudo.  If you extracted into a system location
# (`/Applications`, `/usr/local/bin`, etc.), `xattr -d` fails with
# permission errors and you'll need to re-run with sudo:
#
#     sudo ./allow-app-to-run.sh
#
# The script detects this case and tells you to re-run with sudo if
# the strip fails.
#
# HOW IT WORKS
# ────────────
# 1. Verifies the host is macOS (refuses to run on Linux / Windows).
# 2. Verifies the `xattr` utility is available (it ships with macOS).
# 3. Locates this script's own directory — that's the place you
#    extracted the release into.
# 4. Confirms the `ClaudeForge` binary is alongside this script (so the
#    script can't accidentally run from the wrong place).
# 5. Runs `xattr -dr com.apple.quarantine <dir>` to recursively strip
#    the named xattr from every file beneath that directory.
#       -d  : delete attribute
#       -r  : recurse into subdirectories
#       Files without the xattr are silently skipped (no-op).
#       Files we don't have permission to modify produce an error.
# 6. Verifies the strip worked by re-querying xattr on the main binary.
#
# Both flags are documented in `man xattr` if you want to verify.
#
# AFTER RUNNING
# ─────────────
# Launch ClaudeForge from this directory:
#
#     ./ClaudeForge
#
# or by double-clicking it in Finder.  Subsequent launches don't need
# this script — once the quarantine xattr is gone, it stays gone.
#
# UNINSTALL
# ─────────
# Just `rm -rf` this directory.  Nothing was installed system-wide.
#

set -euo pipefail

# ----------------------------------------------------------------------------
# 1. Sanity checks
# ----------------------------------------------------------------------------

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "This script is for macOS only.  Detected: $(uname -s)" >&2
    exit 1
fi

if ! command -v xattr >/dev/null 2>&1; then
    echo "Error: 'xattr' command not found." >&2
    echo "xattr ships with macOS — its absence indicates an unusual system" >&2
    echo "configuration this script can't help with.  See `man xattr` (or" >&2
    echo "consult Apple's documentation) for the underlying mechanism." >&2
    exit 2
fi

# Resolve the script's own directory.  Using ${BASH_SOURCE[0]} (rather
# than $0) handles the case where the script is sourced from another
# script.  cd + pwd resolves any symlinks the user might have created.
SCRIPT_DIR="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
APP_BINARY="$SCRIPT_DIR/ClaudeForge"

if [[ ! -f "$APP_BINARY" ]]; then
    echo "Error: ClaudeForge binary not found at $APP_BINARY" >&2
    echo "This script must live alongside the ClaudeForge binary in the" >&2
    echo "directory where you extracted the release archive.  If you've" >&2
    echo "moved the script, move it back next to ClaudeForge and try again." >&2
    exit 3
fi

# ----------------------------------------------------------------------------
# 2. Strip the quarantine attribute
# ----------------------------------------------------------------------------

echo "Removing com.apple.quarantine from files under:"
echo "    $SCRIPT_DIR"
echo

# Capture the exit code so we can surface a useful message rather than
# letting `set -e` kill the script with no explanation if xattr fails
# due to permissions.
if ! xattr -dr com.apple.quarantine "$SCRIPT_DIR" 2>&1; then
    echo >&2
    echo "Failed to remove the quarantine attribute from at least one file." >&2
    echo "Most likely cause: the files are owned by another user (e.g. root)." >&2
    echo "Try re-running this script with sudo:" >&2
    echo >&2
    echo "    sudo $0" >&2
    exit 4
fi

# ----------------------------------------------------------------------------
# 3. Verify the strip succeeded on the main binary
# ----------------------------------------------------------------------------

# `xattr -p <attr> <file>` prints the attribute's value if present, or
# returns non-zero with "No such xattr" on stderr if absent.  We WANT
# the absent case.  We discard stderr because the success path is
# noisy in shell scripts ("xattr: No such xattr: com.apple.quarantine").
if xattr -p com.apple.quarantine "$APP_BINARY" >/dev/null 2>&1; then
    echo "WARNING: quarantine attribute still present on $APP_BINARY." >&2
    echo "If you weren't running as sudo, try:" >&2
    echo >&2
    echo "    sudo $0" >&2
    exit 5
fi

echo "✓ Quarantine attribute removed."
echo
echo "You can now launch ClaudeForge:"
echo
echo "    cd '$SCRIPT_DIR'"
echo "    ./ClaudeForge"
echo
echo "Or double-click ClaudeForge in Finder.  Subsequent launches don't"
echo "need this script — once the quarantine attribute is gone, it stays gone."
