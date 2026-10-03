#!/usr/bin/env bash
# Tray, close-to-tray, second launch and launch at login, natively on Fedora 42 arm64 under Xvfb.
set -e
cd /src/linux
export CARGO_TARGET_DIR=/src/linux/target/linux-arm64
dnf install -y -q gcc git gtk4-devel libadwaita-devel gstreamer1-devel gstreamer1-plugins-base-devel gstreamer1-plugins-good xorg-x11-server-Xvfb ImageMagick dbus-x11 dbus-daemon xdotool python3-gobject procps-ng >/dev/null 2>&1
[ -x /root/.cargo/bin/cargo ] || curl -sSf https://sh.rustup.rs | sh -s -- -y -q --profile minimal >/dev/null
/root/.cargo/bin/cargo build --release -q
B=$CARGO_TARGET_DIR/release/plugview
rm -f /out/items.txt /out/tray.log
Xvfb :99 -screen 0 1920x1080x24 >/dev/null 2>&1 &
sleep 1
export DISPLAY=:99 GDK_BACKEND=x11 NO_AT_BRIDGE=1 GSK_RENDERER=cairo
eval "$(dbus-launch --sh-syntax)"
log() { echo "$*" >> /out/tray.log; }
python3 /src/linux/scripts/watcher.py &
sleep 1
"$B" --app > /out/app.log 2>&1 &
P=$!
sleep 4
read -r SENDER ITEM < /out/items.txt
log "registered: $SENDER $ITEM"
log "tooltip: $(gdbus call --session -d "$SENDER" -o /StatusNotifierItem -m org.freedesktop.DBus.Properties.Get org.kde.StatusNotifierItem ToolTip | head -c 200)"
MENU=$(gdbus call --session -d "$SENDER" -o /StatusNotifierItem -m org.freedesktop.DBus.Properties.Get org.kde.StatusNotifierItem Menu | grep -o "/[A-Za-z/]*")
log "menu labels: $(gdbus call --session -d "$SENDER" -o "$MENU" -m com.canonical.dbusmenu.GetLayout -- 0 -1 '["label","toggle-state"]' | grep -o "'label': <'[^']*'>\|'toggle-state': <[0-9]*>" | tr '\n' ' ')"
log "visible windows before close: $(xdotool search --onlyvisible --name plugview | wc -l)"
W=$(xdotool search --onlyvisible --name plugview | head -1)
xdotool windowfocus "$W" 2>/dev/null || true
xdotool key --window "$W" ctrl+w
sleep 1
log "after Ctrl+W: alive=$(kill -0 $P 2>/dev/null && echo yes || echo no) visible=$(xdotool search --onlyvisible --name plugview | wc -l)"
"$B" --app >> /out/second.log 2>&1 && log "second launch exited at once (activated the first)"
sleep 1
log "after second launch: visible=$(xdotool search --onlyvisible --name plugview | wc -l) processes=$(pgrep -fc 'release/plugview --app')"
# Launch at login: the menu item with that label, clicked through dbusmenu.
ID=$(gdbus call --session -d "$SENDER" -o "$MENU" -m com.canonical.dbusmenu.GetLayout -- 0 -1 '["label"]' | grep -o "<([0-9]*, {'label': <'Launch at login'>" | grep -o "^<([0-9]*" | tr -d '<(')
gdbus call --session -d "$SENDER" -o "$MENU" -m com.canonical.dbusmenu.Event -- "$ID" clicked '<"">' 0 >/dev/null
sleep 1
log "autostart: $(cat ~/.config/autostart/app.plugview.plugview.desktop 2>/dev/null | grep Exec)"
log "settings: $(tr -d ' \n' < ~/.config/plugview/settings.json)"
xdotool key --window "$(xdotool search --onlyvisible --name plugview | head -1)" ctrl+w; sleep 1
gdbus call --session -d "$SENDER" -o /StatusNotifierItem -m org.kde.StatusNotifierItem.Activate -- 0 0 >/dev/null
sleep 1
log "after tray Activate: visible=$(xdotool search --onlyvisible --name plugview | wc -l)"
import -window root /out/tray-window.png
QID=$(gdbus call --session -d "$SENDER" -o "$MENU" -m com.canonical.dbusmenu.GetLayout -- 0 -1 '["label"]' | grep -o "<([0-9]*, {'label': <'Quit plugview'>" | grep -o "^<([0-9]*" | tr -d '<(')
gdbus call --session -d "$SENDER" -o "$MENU" -m com.canonical.dbusmenu.Event -- "$QID" clicked '<"">' 0 >/dev/null
sleep 1
log "after Quit: alive=$(kill -0 $P 2>/dev/null && echo yes || echo no)"
# Without a tray host: closing quits; the command mode always quits.
pkill -f watcher.py; sleep 1
"$B" --app > /out/notray.log 2>&1 &
P=$!; sleep 3
xdotool key --window "$(xdotool search --onlyvisible --name plugview | head -1)" ctrl+w; sleep 1
log "no tray, after Ctrl+W: alive=$(kill -0 $P 2>/dev/null && echo yes || echo no)"
