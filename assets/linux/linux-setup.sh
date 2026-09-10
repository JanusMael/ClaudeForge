#!/usr/bin/env bash
# ============================================================================
# linux-setup.sh — Linux desktop integration installer
# ============================================================================
#
# WHAT THIS SCRIPT DOES
# ─────────────────────
# Registers this app with your Linux desktop environment so it appears
# in your application launcher / menu / dock with a proper icon, on both
# X11 and Wayland.  Pass `--uninstall` to remove the registration.
#
# WHICH APP?  The one whose archive you extracted.  This repository ships
# more than one, and rather than shipping a copy of this script per app
# (which is how two copies drift apart), the script reads the single
# `<app>.desktop` template staged beside it and takes the app's id from
# that filename, its binary from the template's Exec= line, and its
# display name from Name=.  Publish-Rid.ps1 stages exactly one.
#
# WHY YOU NEED THIS
# ─────────────────
# Avalonia 12 sets the in-process window icon via Window.Icon, which on
# X11 writes the _NET_WM_ICON property and works for the titlebar.  But
# the Wayland protocol intentionally does not expose a per-window-icon
# API — every modern compositor (KWin, Mutter, COSMIC, Sway) ignores
# the in-process icon attempt and instead:
#
#   1. Reads the surface's app_id (Avalonia derives this from the
#      assembly name — e.g. "ClaudeForge" or "OpenCodeForge").
#   2. Looks up app_id.desktop in $XDG_DATA_DIRS/applications/.
#   3. Reads the Icon= field from that file.
#   4. Resolves the icon name against the system icon theme.
#
# So if you run the app on Wayland (CachyOS / COSMIC, Fedora 40+ /
# GNOME, KDE 6, Sway) and see no icon — that's expected without a
# .desktop file installed.  This script does the install for you,
# scoped to your user account.  No sudo / root required.
#
# WHAT THIS SCRIPT WRITES
# ───────────────────────
# Two files under your XDG data directory (default: ~/.local/share/):
#
#   1. ${XDG_DATA_HOME:-$HOME/.local/share}/applications/<app>.desktop
#       — desktop entry pointing Exec= at THIS directory's binary,
#         with StartupWMClass set so the compositor associates the
#         launched window with this entry.
#
#   2. ${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/scalable/apps/<app>.svg
#       — vector icon resolved by the Icon=<app> theme name.
#         Modern compositors (KDE 6, GNOME 46+, COSMIC, Sway, XFCE)
#         handle SVG natively from the icon theme.  Older GTK 2.x
#         environments may need pre-rendered PNGs — see
#         docs/LINUX-DESKTOP-INTEGRATION.md for the rsvg-convert flow.
#
# Both writes target the user's per-user XDG paths.  The script does NOT:
#   - Touch /usr/share/, /etc/, /opt/, or any other system path.
#   - Modify your shell rc (no PATH edits — use the in-app
#     "Add to PATH" button on the Version Information page if you want
#     the binary callable from anywhere).
#   - Move or copy the app binary itself — the .desktop entry
#     points at the binary IN THIS DIRECTORY, so deleting or moving
#     this directory breaks the launcher entry until you re-run the
#     script from the new location.
#
# CACHE REFRESH
# ─────────────
# After writing the files, the script tries (best-effort, never fatal):
#   - update-desktop-database  : refreshes the desktop-entry MIME cache.
#   - gtk-update-icon-cache    : refreshes the icon-theme cache.
# If neither tool is installed, the cache picks up the new entry on the
# next desktop-environment login or compositor restart.
#
# UNINSTALL
# ─────────
#     ./linux-setup.sh --uninstall
# Removes both files written above and refreshes the caches.  Does NOT
# remove the app binary or anything else in the extracted directory.
#
# AFTER RUNNING
# ─────────────
# Restart the app.  The launched window should now show the
# configured icon in your dock / Alt-Tab / activities overview.
# Searching for the app's name in your application launcher should also
# find the entry and launch it.
#
# Verifying it worked:
#   desktop-file-validate ~/.local/share/applications/<app>.desktop
#   gtk-launch <app>   # should launch the app
# See docs/LINUX-DESKTOP-INTEGRATION.md for full diagnostic steps,
# including the WM_CLASS / app_id check via xprop / swaymsg / qdbus.
#
# WHY NOT SYSTEM-WIDE
# ───────────────────
# This script writes ONLY to per-user XDG paths.  System-wide install
# (under /usr/share/) is a distro-packager flow with different
# conventions (DESTDIR-staged install, post-install scriptlet for the
# cache refresh, possibly hicolor PNG variants for legacy environments).
# See docs/LINUX-DESKTOP-INTEGRATION.md "System-wide install" for that
# procedure — this script intentionally stays out of /usr/share.
#

set -euo pipefail

# ----------------------------------------------------------------------------
# 1. Sanity checks
# ----------------------------------------------------------------------------

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "This script is for Linux only.  Detected: $(uname -s)" >&2
    echo "On macOS, see allow-app-to-run.sh in this directory instead." >&2
    exit 1
fi

# Resolve the script's own directory.  Using ${BASH_SOURCE[0]} (rather
# than $0) handles the case where the script is sourced from another
# script.  cd + pwd resolves any symlinks the user might have created.
SCRIPT_DIR="$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"

# ----------------------------------------------------------------------------
# 2. Identify the app from the .desktop template staged beside this script
# ----------------------------------------------------------------------------
# An unmatched glob expands to the literal pattern in bash, so the -f test
# is what distinguishes "no .desktop present" from "one .desktop present".
# More than one is an error rather than a first-match-wins guess: picking
# arbitrarily would register one app under another app's launcher entry.

DESKTOP_TEMPLATE=""
for candidate in "$SCRIPT_DIR"/*.desktop; do
    [[ -f "$candidate" ]] || continue
    if [[ -n "$DESKTOP_TEMPLATE" ]]; then
        echo "Error: more than one .desktop template found next to this script." >&2
        echo "Expected exactly one, naming the app this archive contains." >&2
        exit 3
    fi
    DESKTOP_TEMPLATE="$candidate"
done

if [[ -z "$DESKTOP_TEMPLATE" ]]; then
    echo "Error: no .desktop template found at $SCRIPT_DIR" >&2
    echo "This script must live alongside the bundled .desktop template" >&2
    echo "in the extracted release directory." >&2
    exit 3
fi

APP_ID="$( basename "$DESKTOP_TEMPLATE" .desktop )"

# Anchored at start-of-line: the template's comment block shows example
# Exec= paths, and those lines begin with '#'.
EXEC_NAME="$( grep -m1 '^Exec=' "$DESKTOP_TEMPLATE" | cut -d= -f2- )"
APP_NAME="$( grep -m1 '^Name=' "$DESKTOP_TEMPLATE" | cut -d= -f2- )"
: "${APP_NAME:=$APP_ID}"

if [[ -z "$EXEC_NAME" ]]; then
    echo "Error: $DESKTOP_TEMPLATE has no Exec= line, so the app's binary" >&2
    echo "name cannot be determined." >&2
    exit 3
fi

APP_BINARY="$SCRIPT_DIR/$EXEC_NAME"
ICON_SOURCE="$SCRIPT_DIR/$APP_ID.svg"

if [[ ! -f "$APP_BINARY" ]]; then
    echo "Error: $APP_NAME binary not found at $APP_BINARY" >&2
    echo "This script must live alongside the $APP_NAME binary in the" >&2
    echo "directory where you extracted the release archive.  If you've" >&2
    echo "moved the script, move it back next to the binary and try again." >&2
    exit 2
fi

# ----------------------------------------------------------------------------
# 3. XDG paths (per the Base Directory Specification)
# ----------------------------------------------------------------------------
# https://specifications.freedesktop.org/basedir-spec/latest/

XDG_DATA_HOME_RESOLVED="${XDG_DATA_HOME:-$HOME/.local/share}"
DESKTOP_DIR="$XDG_DATA_HOME_RESOLVED/applications"
ICON_DIR="$XDG_DATA_HOME_RESOLVED/icons/hicolor/scalable/apps"

DESKTOP_FILE="$DESKTOP_DIR/$APP_ID.desktop"
ICON_FILE="$ICON_DIR/$APP_ID.svg"

# ----------------------------------------------------------------------------
# 4. Uninstall path
# ----------------------------------------------------------------------------

if [[ "${1:-}" == "--uninstall" ]]; then
    removed=0
    for f in "$DESKTOP_FILE" "$ICON_FILE"; do
        if [[ -f "$f" ]]; then
            rm -f "$f"
            echo "  removed: $f"
            removed=1
        fi
    done
    if [[ $removed -eq 0 ]]; then
        echo "Nothing to remove (no per-user $APP_NAME entries found)."
    else
        # Best-effort cache refresh.  gtk-update-icon-cache returns
        # non-zero when the index is already up-to-date — || true
        # absorbs that without tripping `set -e`.
        update-desktop-database "$DESKTOP_DIR"                        >/dev/null 2>&1 || true
        gtk-update-icon-cache  "$XDG_DATA_HOME_RESOLVED/icons/hicolor/" >/dev/null 2>&1 || true
        echo "$APP_NAME desktop integration removed."
    fi
    exit 0
fi

if [[ "${1:-}" != "" ]]; then
    echo "Unknown argument: $1" >&2
    echo "Usage: $0 [--uninstall]" >&2
    exit 64  # EX_USAGE
fi

# ----------------------------------------------------------------------------
# 5. Install: .desktop file with corrected Exec= path
# ----------------------------------------------------------------------------

mkdir -p "$DESKTOP_DIR"

# Patch the template's Exec= line to point at the absolute path of THIS
# directory's binary.  desktop-file-spec does not expand $HOME — so we
# hard-bake the absolute path that resolved at install time.  Bash
# parameter expansion ${var//pat/replacement} avoids sed escaping
# concerns (regex metacharacters and `&` in the replacement would all
# need escaping with sed).  The template contains exactly one occurrence
# of the literal "Exec=<name>" line; the surrounding documentation
# comments use absolute-path examples which won't match.
#
# ⚠ $EXEC_NAME is used as a PATTERN here, so a glob character in an
# assembly name would change the match.  Both current apps are plain
# alphanumerics; a future app with a '*', '?' or '[' in its assembly
# name would need this escaped.
#
# We also strip comment lines (^[[:space:]]*#) and blank lines so the
# installed runtime entry is concise — leaving in the "REPLACE the path
# below" guidance next to the already-replaced Exec= value would be
# confusing for anyone inspecting the installed file later.
content=$(grep -v '^[[:space:]]*#' "$DESKTOP_TEMPLATE" | grep -v '^[[:space:]]*$')
content="${content//Exec=$EXEC_NAME/Exec=$APP_BINARY}"
printf '%s\n' "$content" > "$DESKTOP_FILE"
chmod 644 "$DESKTOP_FILE"

# ----------------------------------------------------------------------------
# 6. Install: icon SVG into the per-user hicolor scalable apps dir
# ----------------------------------------------------------------------------

if [[ -f "$ICON_SOURCE" ]]; then
    mkdir -p "$ICON_DIR"
    cp "$ICON_SOURCE" "$ICON_FILE"
    chmod 644 "$ICON_FILE"
else
    echo "Note: $APP_ID.svg not found alongside script — skipping icon" >&2
    echo "       install.  Launcher entry will still work; icon will fall" >&2
    echo "       back to a generic placeholder." >&2
fi

# ----------------------------------------------------------------------------
# 7. Cache refresh (best-effort, never fatal)
# ----------------------------------------------------------------------------

update-desktop-database "$DESKTOP_DIR"                        >/dev/null 2>&1 || true
gtk-update-icon-cache  "$XDG_DATA_HOME_RESOLVED/icons/hicolor/" >/dev/null 2>&1 || true

# ----------------------------------------------------------------------------
# 8. Validate (warn-only) + report
# ----------------------------------------------------------------------------

if command -v desktop-file-validate >/dev/null 2>&1; then
    if ! desktop-file-validate "$DESKTOP_FILE" >/dev/null 2>&1; then
        echo "Warning: desktop-file-validate found issues in $DESKTOP_FILE." >&2
        echo "         Entry may not be honoured by all desktop environments." >&2
        echo "         Run \`desktop-file-validate $DESKTOP_FILE\` for details." >&2
    fi
fi

echo "✓ $APP_NAME desktop integration installed."
echo
echo "  Launcher entry: $DESKTOP_FILE"
if [[ -f "$ICON_FILE" ]]; then
    echo "  Icon:           $ICON_FILE"
fi
echo "  Binary:         $APP_BINARY"
echo
echo "Restart $APP_NAME (or log out / log in) to see the icon in the"
echo "dock / Alt-Tab / activities overview.  Search for \"$APP_NAME\""
echo "in your application launcher to verify the entry."
echo
echo "To remove: $0 --uninstall"
