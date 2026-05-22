# Linux Desktop Integration (X11 + Wayland)

> **TL;DR for end users:** the release `.tar.gz` ships with
> `linux-setup.sh`. After extracting, run:
> ```bash
> cd ~/ClaudeForge   # wherever you extracted
> ./linux-setup.sh    # writes per-user .desktop + SVG icon, no sudo
> ```
> Restart ClaudeForge and the icon appears in dock / Alt-Tab / launcher.
> The script is self-documenting (header comments cover WHY / WHAT IT
> WRITES / UNINSTALL); the rest of this document is the manual procedure
> for distro packagers and anyone who wants to install by hand or
> system-wide.
>
> **Note:** the bundled script ships SVG-only — sufficient for KDE 6,
> GNOME 46+, COSMIC, Sway, and XFCE. If you're on an older GTK 2.x
> environment that needs pre-rendered PNGs, follow the manual flow
> below; the SVG-only install will fall back to a generic placeholder
> on those compositors.

> **Status:** Sample `.desktop` template shipped at
> [`assets/linux/claudeforge.desktop`](../assets/linux/claudeforge.desktop).
> Bundled installer at [`assets/linux/linux-setup.sh`](../assets/linux/linux-setup.sh)
> (also ships in every `linux-*` release `.tar.gz`). No system-wide
> installer — a single-file Avalonia binary can't legally / safely write
> to `/usr/share/applications/` without elevation, and that's a distro-
> packager flow with its own conventions (`DESTDIR`, post-install
> scriptlet for the cache refresh). Follow the per-user steps below if
> you want to install by hand, or hand off the template + script to
> your distro packager.

## Why this matters: X11 vs. Wayland

Avalonia's `Window.Icon` property maps to platform-specific window-icon
mechanisms. The two Linux display servers handle it very differently:

| Surface | X11 | Wayland |
|---|---|---|
| Window titlebar icon | ✅ Reads `_NET_WM_ICON` from `Window.Icon` | ❌ No protocol support — always falls back to compositor lookup |
| Dock / taskbar icon | ⚠️ Some WMs read `_NET_WM_ICON`; others use `.desktop` matched via `WM_CLASS` | ❌ ALWAYS uses `.desktop` matched via `app_id` |
| Alt-Tab switcher icon | ⚠️ Same as dock — WM-dependent | ❌ Same as dock |
| Application-menu icon | ❌ Requires `.desktop` file | ❌ Requires `.desktop` file |

The Wayland protocol intentionally does not expose a per-window-icon API
([rationale in the protocol docs][1]) — every modern compositor (KWin,
Mutter, COSMIC, Sway) ignores any in-process icon attempt and instead:

1. Reads the surface's `app_id` (set by Avalonia from the assembly name —
   for our build, `ClaudeForge`).
2. Looks up `app_id.desktop` in `$XDG_DATA_DIRS/applications/` (typically
   `~/.local/share/applications/`, `/usr/share/applications/`,
   `/usr/local/share/applications/`).
3. Reads the `Icon=` field from the `.desktop` file.
4. Resolves that icon name against the system icon theme.

**Bottom line:** if you run ClaudeForge on Wayland (CachyOS / COSMIC,
Fedora 40+ / GNOME, KDE 6, etc.) and see no icon — that's expected
without the `.desktop` install below. The fix is one-time per-user
configuration.

[1]: https://wayland.app/protocols/xdg-shell#xdg_toplevel:request:set_app_id

---

## Per-user install (recommended for development / single-user machines)

```bash
# 1. Drop the .desktop file in the per-user XDG location.
mkdir -p ~/.local/share/applications/
cp assets/linux/claudeforge.desktop ~/.local/share/applications/claudeforge.desktop

# 2. EDIT the Exec= line to point at where you actually installed the
#    ClaudeForge binary.  Desktop-file-spec does NOT expand $HOME — use
#    an absolute path:
#      Exec=/home/USER/.local/bin/ClaudeForge
#    (or wherever your build / install script puts the binary)
${EDITOR:-vi} ~/.local/share/applications/claudeforge.desktop

# 3. Render PNG icons from the SVG source at the standard hicolor sizes.
#    Pick a renderer you have installed:
#
#    Option A — using rsvg-convert (librsvg, usually preinstalled):
mkdir -p ~/.local/share/icons/hicolor/{16x16,32x32,48x48,64x64,128x128,256x256}/apps/
for size in 16 32 48 64 128 256; do
  rsvg-convert -w $size -h $size \
    src/ClaudeForge/Resources/ClaudeForge.svg \
    -o ~/.local/share/icons/hicolor/${size}x${size}/apps/claudeforge.png
done
#    Also drop the SVG itself for vector-aware compositors:
mkdir -p ~/.local/share/icons/hicolor/scalable/apps/
cp src/ClaudeForge/Resources/ClaudeForge.svg \
   ~/.local/share/icons/hicolor/scalable/apps/claudeforge.svg

#    Option B — using ImageMagick:
# convert -background none -resize 256x256 \
#   src/ClaudeForge/Resources/ClaudeForge.svg \
#   ~/.local/share/icons/hicolor/256x256/apps/claudeforge.png

#    Option C — using Inkscape:
# inkscape -w 256 -h 256 src/ClaudeForge/Resources/ClaudeForge.svg \
#   -o ~/.local/share/icons/hicolor/256x256/apps/claudeforge.png

# 4. Refresh the icon + desktop caches so the compositor picks up the
#    new entries without a logout cycle.
gtk-update-icon-cache ~/.local/share/icons/hicolor/ 2>/dev/null || true
update-desktop-database ~/.local/share/applications/ 2>/dev/null || true

# 5. Restart ClaudeForge.  The window should now show the configured
#    icon in dock / Alt-Tab / titlebar (X11) or dock / Alt-Tab (Wayland).
```

## System-wide install (distro-packager flow)

For distro packages, install the same files under `/usr/share/`:

```bash
install -Dm 644 assets/linux/claudeforge.desktop \
  $DESTDIR/usr/share/applications/claudeforge.desktop

# Replace Exec=ClaudeForge with the absolute install path before/during
# the install step (e.g. via sed in the package build script).

# Render and install icons at every hicolor size:
for size in 16 32 48 64 128 256; do
  rsvg-convert -w $size -h $size ClaudeForge.svg -o claudeforge-${size}.png
  install -Dm 644 claudeforge-${size}.png \
    $DESTDIR/usr/share/icons/hicolor/${size}x${size}/apps/claudeforge.png
done
install -Dm 644 ClaudeForge.svg \
  $DESTDIR/usr/share/icons/hicolor/scalable/apps/claudeforge.svg

# Cache refresh runs as a post-install scriptlet:
gtk-update-icon-cache /usr/share/icons/hicolor/
update-desktop-database /usr/share/applications/
```

---

## Verifying it worked

After install, run ClaudeForge and check:

1. **Dock / taskbar icon** — should show the configured ClaudeForge
   icon, not a generic application placeholder.
2. **Alt-Tab switcher** — same.
3. **App launcher / activities overview** — searching for "claudeforge"
   should find the entry with the correct icon and launch the binary.

Diagnostic steps if the icon still doesn't appear:

```bash
# Confirm the .desktop file is parseable.
desktop-file-validate ~/.local/share/applications/claudeforge.desktop

# Confirm the icon name resolves.
gtk-launch claudeforge   # should launch the app
gtk4-icon-browser         # search for "claudeforge"

# Confirm the WM/compositor sees the matching app_id.
# X11:
xprop WM_CLASS   # then click the ClaudeForge window — should show "ClaudeForge"
# Wayland (KWin):
qdbus org.kde.KWin /KWin org.kde.KWin.activeWindowName
# Wayland (Sway):
swaymsg -t get_tree | jq '.. | select(.app_id? // "" | startswith("Claude"))'
```

The `WM_CLASS` / `app_id` reported by these commands MUST match the
`StartupWMClass=` value in the `.desktop` file. Avalonia 12 derives both
from the entry-assembly name, so for this build they're hardcoded to
`ClaudeForge`. If you fork and rename the assembly, update both the
`.desktop` template and any docs accordingly.

---

## Why we don't ship pre-rendered PNG icons in the binary

The published binary is single-file (`ClaudeForge.exe` on Linux too —
.NET self-contained publish wraps everything). Adding PNG assets at
multiple sizes would inflate the binary by ~few hundred KB and only
help the small subset of users who manually copy them out.

The SVG ships as an Avalonia resource (used by `AppIcon.cs` for the
in-process `Window.Icon`); rendering to PNG at install time using
`rsvg-convert` / ImageMagick / Inkscape is the standard distro-packager
pattern and the only path to icons in the system theme — neither
window-icon API nor any in-process trick can populate the icon theme
files.

If you'd prefer not to install system files at all, the in-process icon
still works on X11. The dock / taskbar will show a placeholder; the
window titlebar will show the correct icon. On Wayland, even the
window titlebar relies on the icon-theme lookup, so the install is
required for any icon to appear there.
