# Performance

plugview's goal is the lowest delay between the console and your eyes and ears. This page records what
plugview does about it, the measurements behind each choice, and what only you can change.

## Where the delay comes from

```
console → capture card → USB → driver → plugview → compositor → display
                                └── what plugview controls ──┘
```

The card (encoding, buffering) and the display (refresh rate, processing) add delay that software cannot
see or change. Everything between the driver and the compositor is tuned below.

## What plugview does

| Choice | Why | Measured effect (UGREEN 25773, MS2130 chip, M5 Pro) |
| --- | --- | --- |
| Draw with `AVCaptureVideoPreviewLayer` | Frames go from the driver straight to the GPU compositor; no copy, no CPU pass. | ~4 % CPU at 1080p60 |
| Keep the device locked until the session runs | `AVCaptureSession.startRunning()` otherwise resets the frame rate to the device default. | 20 → 60 fps; capture → app 56 → 18 ms |
| Prefer the card's native packed 4:2:2 format (`yuvs`/`2vuy`) | `420v` makes macOS convert every frame. | 22.7 → 18.0 ms, ~17 % less CPU |
| Prefer smooth frame rates (≥ 50 fps) over resolution | 1080p60 feels faster than 4K30. | — |
| 128-frame Core Audio buffers on card input and playback | macOS defaults to 512 frames (10.7 ms at 48 kHz). 64 frames caused spikes. | Card audio 12.4 → 4.3 ms; speakers 27 → 19 ms |
| `userInteractive` capture queue and `latencyCritical` activity | Keeps App Nap and timer coalescing away from capture. | Not measured separately |
| Display sleep disabled while the window is open | A controller is not keyboard/mouse input. | — |
| Screenshot output attached but disabled | Its connection is enabled for one frame only, so no frame is copied while you play and the session is never reconfigured. | App CPU 5.6–8.5 % (0.2.0) vs 6.5–6.7 % (with it), 10 s windows at 1080p60: no difference |
| Full-range colors is opt-in | A Core Image filter on the preview layer, applied by the window server on the GPU. | Off by default; not measured |

The buffer and scheduling changes apply to plugview's process only, and macOS undoes them when plugview exits.

## Measure your setup

```sh
plugview --measure
```

Sample output:

```
Video    UGREEN 25773 · 1920×1080 · uncompressed 4:2:2 (native)
         device set to 60.0 fps, delivered 60.0 fps (272 frames)
         frame interval  median 16.7 ms · p95 16.7 ms · worst 16.7 ms
         capture → app   median 19.1 ms · p95 19.3 ms · worst 19.6 ms
Audio    UGREEN 25773 · buffer 128 frames
         capture → app   median 4.3 ms · p95 4.3 ms · worst 5.3 ms
Output   MacBook Pro Speakers · reports 19.3 ms
Display  100.0 Hz

To lower latency:
  • The display runs at 100 Hz, not a multiple of 60 fps, so frames are shown for uneven times. …
```

- **delivered fps** below the mode's rate means the card or the USB link can't keep up.
- **capture → app** is the time from the driver's capture timestamp until the frame reaches plugview.
- **Output** is the latency the playback device reports to Core Audio. Bluetooth is far higher than it reports.

## Measure glass to glass

To include the card and the display, film both screens with a phone in slow motion (240 fps):

1. Put the console's own screen (Switch handheld, or a TV on an HDMI splitter) next to the Mac.
2. Record both while pressing a button that causes an instant change.
3. Step through the video and count frames between the two screens reacting. At 240 fps, one frame is 4.2 ms.

## What only you can change

1. **USB 3 port, no hub.** USB 2 forces compressed formats and lower frame rates.
2. **Full screen.** A windowed app shares the compositor with every other window.
3. **Wired audio.** Bluetooth headphones add 100–300 ms.
4. **Refresh rate.** Use a multiple of the game's frame rate (60 or 120 Hz for 60 fps). At 100 Hz, 60 fps frames
   are shown for 10 or 20 ms in turn, which looks uneven.
5. **Close other capture apps.** OBS, Zoom, or a browser using the card compete for it.

## Ideas that were evaluated

- **Drawing frames with Metal or `AVSampleBufferDisplayLayer`** instead of the preview layer. This could
  save up to one display frame, but only a glass-to-glass test can prove it. Contributions with such a
  measurement are welcome.
- **Audio buffers below 128 frames.** 64 frames produced irregular 13 ms spikes on the test card.
