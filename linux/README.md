# plugview for Linux

A preview (phase 6 in `plugview-product/PHASES.md`): the same app as on the Mac, on GTK 4 and GStreamer.

| Crate | What it is |
| --- | --- |
| `crates/plugview-core` | The shared decisions, tested against `spec/fixtures` |
| `crates/plugview` | The app and the command: device classification from sysfs, GStreamer pipelines into a GTK 4 window (graphics offload), card audio, the launcher, controls, signal watch and tray |

## Install the preview

From `plugview-linux-x86_64.tar.gz` on plugview.app:

```sh
tar xzf plugview-linux-x86_64.tar.gz && cd plugview-*/ && ./install.sh
```

It puts `plugview` in `~/.local/bin` and the app in the app grid; `./install.sh --uninstall` removes both.

- **From the app grid** (`plugview --app`): one window, choices remembered, and a tray icon on KDE, the Steam Deck's
  desktop, and GNOME with the AppIndicator extension. Closing the window releases the card and keeps plugview in
  the tray, where it can open by itself when a card is plugged in and start at login. Without a tray, closing quits.
- **From a terminal** (`plugview`): the command, with the Mac's options and messages; closing the window quits.

## Build it

Needs GTK 4.16+, libadwaita 1.6+, Pango 1.56+ and GStreamer 1.24+ with their development files, and Rust 1.92+
(Fedora 42, Ubuntu 25.04, Debian 13, Arch or newer):

```sh
# Fedora
sudo dnf install gcc gtk4-devel libadwaita-devel gstreamer1-devel gstreamer1-plugins-base-devel gstreamer1-plugins-good gstreamer1-plugins-bad-free
# Ubuntu 25.04 or later, Debian 13 or later
sudo apt install build-essential libgtk-4-dev libadwaita-1-dev libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev gstreamer1.0-plugins-good gstreamer1.0-plugins-bad
# macOS (development only: AVFoundation stands in for V4L2)
brew install gtk4 libadwaita gstreamer
```

## Checks

```sh
cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test
```

## Run it

```sh
cargo run --release -- --list
cargo run --release -- --measure
cargo run --release
```

Keys in the window: `F` or `F11` full screen, `Esc` leaves it, `M` mutes, `↑` `↓` volume, `S` screenshot,
`Ctrl+0` actual size, `Ctrl+=` `Ctrl+-` zoom, `Ctrl+W` closes the window, `Q` quits. Add `--app` to run it as the
app (tray, remembered choices).

What to check first on a Linux PC (phase 6.7; L1 and L2 in `spec/hardware-test-plan.md`):

1. `--list` shows the card once, marked `[usb]`, and its audio input with the same `id`-matching USB parent.
2. `--measure` reports the full frame rate, `Path zero-copy (dmabuf to GTK)` on Wayland, and for an MJPEG card
   the decoder (`vajpegdec` with VA-API, else `jpegdec`).
3. The window plays with sound; `GDK_DEBUG=offload cargo run --release` shows the video offloaded on GNOME Wayland.
4. Glass to glass against OBS and `mpv av://v4l2:/dev/video0 --profile=low-latency --untimed`.
