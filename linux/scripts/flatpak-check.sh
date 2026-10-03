#!/usr/bin/env bash
# Runs the Flatpak bundle against vivid, the kernel's virtual capture device, under Xvfb: --version, --list, the app
# window, then the device unplugged and plugged back in while it runs (rmmod / modprobe). vivid offers only ranges of
# frame sizes, which no card does, so the app shows "reports no video formats" instead of playing; each refresh after
# a plug or unplug repeats that line in flatpak-app.log, which shows the sandbox heard it. Needs a Linux VM kernel
# with vivid (Ubuntu: linux-modules-extra-$(uname -r); colima: `colima ssh`, then apt install it). Results land in /out:
#   docker run --rm --privileged --network host --platform linux/arm64 -v /dev:/dev -v /run/udev:/run/udev:ro \
#     -v /lib/modules:/lib/modules:ro -v plugview-flatpak:/var/lib/flatpak -v "$PWD":/src \
#     -v "$PWD/linux/target/flatpak":/out fedora:42 bash /src/linux/scripts/flatpak-check.sh
set -uo pipefail
dnf install -y -q flatpak kmod xorg-x11-server-Xvfb dbus-x11 dbus-daemon xdotool ImageMagick procps-ng >/dev/null 2>&1
log() { echo "$*" | tee -a /out/flatpak-check.log; }
rm -f /out/flatpak-check.log /out/flatpak-*.png
bundle="$(ls /out/plugview-linux-*.flatpak | head -1)"
flatpak remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install -y --noninteractive --reinstall --bundle "$bundle" >/dev/null 2>&1 || { log "install failed"; exit 1; }
modprobe vivid n_devs=1 node_types=0x1 num_inputs=1 input_types=0x3 || { log "no vivid module in this kernel"; exit 1; }
sleep 1
run() { flatpak run app.plugview.plugview "$@"; }
log "version: $(run --version 2>&1)"
log "list:"; run --list 2>&1 | tee -a /out/flatpak-check.log

Xvfb :99 -screen 0 1600x1000x24 >/dev/null 2>&1 &
sleep 1
export DISPLAY=:99 GDK_BACKEND=x11 NO_AT_BRIDGE=1 GSK_RENDERER=cairo
eval "$(dbus-launch --sh-syntax)"
run --app >/out/flatpak-app.log 2>&1 &
sleep 8
shot() { import -window root "/out/flatpak-$1.png"; log "$1: windows=$(xdotool search --onlyvisible --name plugview | wc -l)"; }
shot started
rmmod vivid; sleep 4
shot unplugged
modprobe vivid n_devs=1 node_types=0x1 num_inputs=1 input_types=0x3; sleep 6
shot replugged
pkill -f "plugview --app"; sleep 1
rmmod vivid 2>/dev/null
log "refreshes that tried the card: $(grep -c 'reports no video formats' /out/flatpak-app.log)"
