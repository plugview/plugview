# Changelog

All notable changes to plugview are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.0] - 2026-10-02

### Added

- **plugview for Windows and Linux, as previews.** The same command (`--list`, `--measure`, and a window that plays
  the card with its sound) on Windows 10 2004 or later and on Linux with GTK 4.16 or later. They are early: the
  Windows download is not signed yet, so Windows shows a SmartScreen warning, and neither has the launcher, the
  controls bar or the menu bar icon of the Mac app yet. Reports welcome.
- When the picture stops while the card is still connected (a console changing resolution or waking up),
  plugview says **No signal** instead of showing a frozen frame and reconnects the card by itself; after a few
  tries it lists the console settings to check. A black picture from the start (PlayStation HDCP) or no sound from
  the start (Dolby or surround output) shows what to change on the console. Only when opened as an app; the
  command is unchanged.

### Fixed

- Hand gestures in a game (a thumbs-up, a peace sign) could set off macOS Reactions, confetti or balloons over
  the picture. plugview now starts with reaction gestures off; you can still turn them on in Control Center.
- `--mode` accepted malformed values such as `1920x1080@@60` or `+1920x1080@60`. `--mode` and
  `--volume` now take plain digits only.

## [0.3.1] - 2026-10-02

### Changed

- Update checks read plugview's own release feed, `https://plugview.app/releases/latest.json`, instead of
  GitHub, and **Download** fetches the new app directly. Releases are published to plugview.app.
- A card found by the app plays right away instead of after the 2.8 s plug-in intro (the picture is up in
  about 0.2 s). The intro now plays in a small banner over the live picture.
- A replugged card starts once, with sound, instead of silent and then restarted: plugview waits up to a
  second for the card's audio input, which appears about half a second after its video.

### Fixed

- Unplugging the card while it played could crash plugview on macOS 27. The card's audio input now keeps the
  macOS default buffer; playback keeps the short one.
- After unplugging the card, the window could stay black instead of showing the start screen again.
- No more "capture stopped … Restarting…" lines after the card is unplugged.

## [0.3.0] - 2026-10-01

### Added

- A menu bar icon (app launch only) that shows whether a card is waiting, ready, or playing, with a panel for the
  status, the video choices, and the settings below.
- Closing the window releases the capture card and keeps plugview in the menu bar; plugging a card in opens the
  window and plays. Both can be turned off. Launch at login.
- Resolution and frame rate: pick any mode the card offers, or Automatic (the smoothest, as before). The choice is
  remembered per card. The terminal command keeps always using the best mode.
- Update check: once a day, and from **Check for Updates…** in the app menu, plugview asks GitHub for the latest
  release and offers the download. It can be turned off; it is plugview's only network request.
- About plugview, Settings… (⌘,), Hide, and Close (⌘W) in the menus.
- Screenshots: `S` or the camera button saves the current frame at full resolution as a PNG in
  `~/Pictures/plugview`, with a notice to show it in Finder.
- Full-range colors, per card, for cards that send washed-out video (gray blacks).
- View menu: Actual Size (⌘0), Zoom In (⌘+), Zoom Out (⌘-), and Keep on Top (⌥⌘T, remembered).
- `--mode WxH@fps` picks the capture mode from the terminal (also for `--measure`); `--list` shows each video
  device's modes.
- The app says when it plays without sound because microphone access is off, with a button to System Settings.

### Changed

- Easier controls bar: every button is 32 points tall and lights up on hover, the volume responds to a click or
  drag anywhere on its track and to scrolling over the bar, and the speaker icon follows the volume.
- Smoother motion: the controls rise and fade in, the launcher screens crossfade, and the launcher fades out to
  reveal the video instead of cutting to it.
- The display stays awake only while the window is open.

## [0.2.0] - 2026-10-01

plugview is now also a Mac app. Typed in a terminal, the command behaves exactly as in 0.1.0.

### Added

- `plugview.app`, installed with `brew install --cask plugview/tap/plugview` or downloaded as
  `plugview-macos-app.zip`. It has its own icon and links the `plugview` command into Homebrew's `bin`, so the
  terminal command keeps working. The command-only formula stays available.
- Opened from Finder or the Dock instead of a terminal, plugview shows a launcher window: it explains the camera
  and microphone permissions before macOS asks, and if camera access is refused it says how to turn it back on,
  with a button that opens the right System Settings page. Typed in a terminal, plugview behaves as before.
- The window now uses the plugview look: dark title bar, Geist fonts, the connector animation while waiting for
  a card, and the plug-in animation when one is found (Play starts it right away). Reduce Motion stops them.
- No title bar over the video: moving the mouse shows the window buttons and a controls bar (mute, volume,
  full screen, capture device), which fade out when the mouse rests. The video can be dragged to move the window.
- Opened as an app with several capture devices connected, plugview lets you pick one and remembers it.

### Changed

- The bundle identifier is now `app.plugview.plugview` (was `io.github.mauricioleon54.plugview`). The saved
  window size and position and the chosen capture device carry over once.

### Fixed

- After unplugging and replugging the capture card, sound now comes back: the card's audio input appears a
  moment after its video, and plugview adds it as soon as it does.
- The window's status messages ("Waiting for a capture device…") were invisible in light mode.
- When a capture device fails to start, the window now says why instead of pointing to the terminal.

### Known limitations

- The app is not notarized yet: the first time it opens, macOS blocks it until you click **Open Anyway** in
  System Settings › Privacy & Security, and each update asks for camera and microphone access again. Until
  the app is approved, macOS also stops the `plugview` command installed with it.

## [0.1.0] - 2026-10-01

First public release.

### Added

- Live view of a USB capture device in a resizable window that keeps the video's aspect ratio and remembers
  its size and position; full screen with `-f` or `F`.
- Automatic format choice: smooth frame rate first, then resolution, then the card's native pixel format
  (packed 4:2:2), so 1080p60 wins over 4K30.
- Sound from the card's own audio input, with mute (`M`) and volume (`↑` `↓`, `--volume`).
- Plug and play: attaches when the card appears, re-attaches after unplugging or a capture error
  (e.g. after the Mac wakes from sleep).
- Keeps the display awake while playing with a controller.
- `--list` to show detected devices; `--device` and `--audio` to pick them by name or ID.
- `--measure` to report the delivered frame rate, capture latency, and playback-device latency, with advice.
- Window title shows the device; the subtitle shows the running mode and audio source.

### Performance

- Frame rate stays locked at the chosen mode. Without this, macOS silently ran MS2130-based cards at 20 fps;
  capture-to-app latency dropped from 56 ms to 18 ms.
- Card audio and playback use 128-frame Core Audio buffers (card capture latency 12.4 ms → 4.3 ms,
  MacBook speakers 27 ms → 19 ms).
- Capture runs at the highest scheduling priority and is marked latency-critical, which keeps App Nap
  and timer coalescing away from it.

[Unreleased]: https://github.com/plugview/plugview/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/plugview/plugview/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/plugview/plugview/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/plugview/plugview/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/plugview/plugview/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/plugview/plugview/releases/tag/v0.1.0
