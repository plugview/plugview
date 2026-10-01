# Changelog

All notable changes to plugview are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/plugview/plugview/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/plugview/plugview/releases/tag/v0.1.0
