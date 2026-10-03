#!/usr/bin/env bash
# Regenerates the committed icons in src/Plugview.Windows/Assets (needs rsvg-convert and ImageMagick):
#   plugview.ico                   the app and window icon, from the brand kit's plugview-icon.svg
#   tray-<state>-<theme>.ico       the tray icon, drawn from spec/fixtures/brand.json like the Mac's menu bar icon:
#                                  state empty (port only), signal (triangle outlined), playing (triangle filled);
#                                  theme light (bone, for a dark taskbar) or dark (cathode, for a light taskbar)
# Runs on macOS bash 3.2.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DESIGN="${DESIGN_DIR:-$ROOT/../plugview-design}"
OUT="$ROOT/windows/src/Plugview.Windows/Assets"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The brand tile fills 480 of 512 px; a Windows icon tile is drawn edge to edge, so it is used as is.
for size in 16 20 24 32 40 48 64 256; do rsvg-convert -w "$size" -h "$size" "$DESIGN/plugview-icon.svg" -o "$WORK/app-$size.png"; done
magick "$WORK"/app-{16,20,24,32,40,48,64,256}.png "$OUT/plugview.ico"

python3 - "$ROOT/spec/fixtures/brand.json" "$WORK" <<'PY'
import json, sys
brand = json.load(open(sys.argv[1]))
points = lambda pts: " ".join(f"{x},{y}" for x, y in pts)
colors = {"light": brand["palette"]["bone"], "dark": brand["palette"]["cathode"]}
for theme, color in colors.items():
    for state in ("empty", "signal", "playing"):
        triangle = ""
        if state != "empty":
            fill = color if state == "playing" else "none"
            triangle = f'<polygon points="{points(brand["geometry"]["triangle"])}" fill="{fill}" stroke="{color}" stroke-width="2.6" stroke-linejoin="round"/>'
        # The port spans 50×30 of the kit's 64 units; a little margin keeps its stroke inside the icon.
        svg = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="5 15 54 34">'
               f'<polygon points="{points(brand["geometry"]["port"])}" fill="none" stroke="{color}" stroke-width="4.2" stroke-linejoin="round"/>'
               f'{triangle}</svg>')
        open(f"{sys.argv[2]}/tray-{state}-{theme}.svg", "w").write(svg)
PY
for svg in "$WORK"/tray-*.svg; do
  name="$(basename "$svg" .svg)"
  for size in 16 20 24 32 40 48; do
    rsvg-convert -w "$size" "$svg" | magick - -background none -gravity center -extent "${size}x${size}" "$WORK/$name-$size.png"
  done
  magick "$WORK/$name"-{16,20,24,32,40,48}.png "$OUT/$name.ico"
done
ls -l "$OUT"
