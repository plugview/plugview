# plugview

[![CI](https://github.com/plugview/plugview/actions/workflows/ci.yml/badge.svg)](https://github.com/plugview/plugview/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/plugview/plugview)](https://github.com/plugview/plugview/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Website](https://img.shields.io/badge/web-plugview.app-FFB000)](https://plugview.app)

Plug in a capture card, type `plugview`, play.

A tiny, open-source macOS command that shows your HDMI capture device (Nintendo Switch, PlayStation, Xbox,
camera, …) in a resizable window with sound, as fast as the Mac can draw it. No scenes, no sources, no
streaming setup.

- **Low latency.** Frames go straight from the capture driver to the GPU compositor. plugview never copies a
  frame. It picks the card's native pixel format, keeps the frame rate locked, and uses 2.7 ms audio buffers.
  [Measured numbers](docs/PERFORMANCE.md).
- **Smooth by default.** Prefers the highest frame rate: 1080p60 beats 4K30.
- **Sound included.** Finds the card's own audio input and plays it on your current output. Never your microphone.
- **Plug and play.** Start it before or after plugging in. Unplug and replug and it re-attaches; it also
  recovers after the Mac wakes from sleep.
- **Stays awake.** The display won't sleep while you play with a controller.
- **Self-check.** `plugview --measure` reports the real frame rate and latency and tells you what to fix.
- **Zero dependencies.** One ~250 KB native binary, Apple frameworks only.

Requires macOS 14 Sonoma or later, on Apple silicon or Intel.

## Install

**Homebrew** (recommended):

```sh
brew install plugview/tap/plugview
```

**Prebuilt binary** from [Releases](https://github.com/plugview/plugview/releases/latest):

```sh
curl -fsSL https://github.com/plugview/plugview/releases/latest/download/plugview-macos-universal.tar.gz | tar -xz
sudo install plugview-*/plugview /usr/local/bin/
```

Downloading with `curl` avoids the Gatekeeper prompt. If you downloaded the archive in a browser, run
`xattr -d com.apple.quarantine plugview` once before starting it.

**From source** (needs Xcode 16.4 or later, or its Command Line Tools):

```sh
git clone https://github.com/plugview/plugview.git && cd plugview
make install            # installs to ~/.local/bin; use `sudo make install PREFIX=/usr/local` for all users
```

## Use

```sh
plugview                 # first USB capture device, with its audio
plugview -f              # start in full screen
plugview --list          # show detected video and audio devices
plugview --measure       # measure frame rate and latency, with advice
plugview -d "Cam Link" -a "Cam Link"   # pick devices by name
plugview --no-audio      # video only
plugview -v 50           # start at 50 % volume
```

| Key | Action |
| --- | --- |
| `F` / `⌃⌘F` | Toggle full screen |
| `Esc` | Leave full screen |
| `M` | Mute / unmute |
| `↑` `↓` | Volume ±10 % |
| `⌘M` | Minimize |
| `Q` / `⌘Q` | Quit |

The window is titled with the device name, and the subtitle shows the running mode
(e.g. `1920×1080 @ 60 fps · audio from UGREEN 25773`). Resize it by dragging; it keeps the video's aspect ratio
and remembers its size and position.

## First run: permissions

macOS asks once for **Camera** (video) and **Microphone** (the card's audio) access. Because plugview runs
from a terminal, the permission is granted to your terminal app (Terminal, iTerm, Warp, …). If you said no,
re-enable it in **System Settings › Privacy & Security › Camera / Microphone**.

## Lowest latency checklist

`plugview --measure` checks most of these for you:

1. Plug the card straight into a **USB 3** port on the Mac, not a hub.
2. Use **full screen** (`-f` or `F`).
3. Play sound on the **Mac speakers or wired headphones**. Bluetooth adds 100–300 ms.
4. Use a display refresh rate that is a **multiple of the game's frame rate** (60 or 120 Hz for 60 fps games).
   At 100 Hz, frames are shown for uneven times.

## Troubleshooting

- **No sound:** run `plugview --list` and pass the card's audio input with `--audio <name>`. plugview does
  not guess when several USB audio inputs are connected.
- **Choppy picture or low fps:** run `plugview --measure`. It shows the frame rate the card really delivers.
- **Washed-out or too-dark colors (Switch):** set *System Settings › TV Output › RGB Range* on the console to
  match your card (try *Limited* first, then *Full*).
- **"cannot be added to the capture session":** another app (OBS, Zoom, …) is using the card. Close it.

Found a bug? [Open an issue](https://github.com/plugview/plugview/issues/new/choose) and include the output
of `plugview --list` and `plugview --measure`.

## Contributing

Pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the project layout, the checks every
change must pass, and how releases are made. Changes are listed in [CHANGELOG.md](CHANGELOG.md).

## License

[MIT](LICENSE) © Mauricio León. The bundled Geist and Geist Mono fonts are under the
[SIL Open Font License 1.1](Sources/PlugviewUI/Fonts/OFL.txt).
