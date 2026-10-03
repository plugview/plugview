#!/usr/bin/env bash
# Native arm64 Fedora 42 check of the window keys under Xvfb.
set -e
cd /src/linux
export CARGO_TARGET_DIR=/src/linux/target/linux-arm64
dnf install -y -q gcc git gtk4-devel libadwaita-devel gstreamer1-devel gstreamer1-plugins-base-devel gstreamer1-plugins-good xorg-x11-server-Xvfb ImageMagick dbus-x11 xdotool adwaita-icon-theme >/dev/null 2>&1
[ -x /root/.cargo/bin/cargo ] || curl -sSf https://sh.rustup.rs | sh -s -- -y -q --profile minimal >/dev/null
/root/.cargo/bin/cargo build --release -q
Xvfb :99 -screen 0 1920x1080x24 >/dev/null 2>&1 &
sleep 1
export DISPLAY=:99 GDK_BACKEND=x11 NO_AT_BRIDGE=1 GSK_RENDERER=cairo
dbus-launch --exit-with-session "$CARGO_TARGET_DIR/release/plugview" > /out/keys.log 2>&1 &
sleep 5
W=$(xdotool search --onlyvisible --name plugview | head -1)
xdotool windowfocus --sync "$W" || true
xdotool getwindowgeometry "$W" > /out/geometry.txt
for key in Down Down m m ctrl+minus; do xdotool key --window "$W" "$key"; sleep 0.7; done
xdotool getwindowgeometry "$W" >> /out/geometry.txt
import -window root /out/keys.png
