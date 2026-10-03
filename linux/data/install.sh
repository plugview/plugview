#!/bin/sh
# Installs plugview for this user: the command in ~/.local/bin, the app in the app grid.
# Run it again to update; run it with --uninstall to remove everything it installed.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
bin="${XDG_BIN_HOME:-$HOME/.local/bin}"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
desktop="$data/applications/app.plugview.plugview.desktop"
icon="$data/icons/hicolor/scalable/apps/app.plugview.plugview.svg"

if [ "${1:-}" = "--uninstall" ]; then
  rm -f "$bin/plugview" "$desktop" "$icon"
  rm -f "${XDG_CONFIG_HOME:-$HOME/.config}/autostart/app.plugview.plugview.desktop"
  echo "plugview removed. Your settings stay in ${XDG_CONFIG_HOME:-$HOME/.config}/plugview."
  exit 0
fi

mkdir -p "$bin" "$(dirname "$desktop")" "$(dirname "$icon")"
cp "$here/plugview" "$bin/plugview"
chmod 755 "$bin/plugview"
cp "$here/app.plugview.plugview.svg" "$icon"
# The app grid starts the installed binary by its full path, so it works even when ~/.local/bin is not on PATH.
sed "s|^Exec=plugview |Exec=\"$bin/plugview\" |" "$here/app.plugview.plugview.desktop" > "$desktop"
command -v update-desktop-database >/dev/null && update-desktop-database "$data/applications" 2>/dev/null || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$data/icons/hicolor" 2>/dev/null || true

echo "plugview $("$bin/plugview" --version) installed."
echo "Open it from the app grid, or run 'plugview' in a terminal."
case ":$PATH:" in *":$bin:"*) ;; *) echo "Add $bin to your PATH to run 'plugview' from any terminal." ;; esac
# Desktop sessions usually grant the logged-in user the cameras; only say something when they did not.
for device in /dev/video*; do
  if [ -e "$device" ] && [ ! -r "$device" ]; then
    echo "To open capture cards, add yourself to the video group, then log out and in:"
    echo "  sudo usermod -aG video \$USER"
    break
  fi
done
