# plugview behavior spec

plugview runs on macOS, Windows and Linux, each built natively. No code is shared between them, but this behavior
is: every port must produce the same results for the same input. The macOS app is the reference. Its tests read
the fixtures in this folder, so a change here fails `make check` until the Swift code follows, and the other ports
fail their own checks the same way.

Change behavior here first, then in every port.

## Fixtures

Each file in `fixtures/` holds cases for one pure function of the Mac's `PlugviewCore`. A port loads the file in
its tests, runs every case and compares the result exactly, unless the file gives a `tolerance`.

| File | Function (Swift name) | Covers |
| --- | --- | --- |
| `device-selection.json` | `playableVideoDevices`, `selectVideoDevice`, `selectAudioDevice`, `preferredFirst`, `shouldAutoPlay` | Which capture device plays, which audio input belongs to it, and when the app plays by itself |
| `video-modes.json` | `bestModeIndex`, `pickableModes`, `modeIndex`, `ModeChoice`, `VideoMode.summary` | Picking the capture mode, the resolution menu, and the saved mode key `1920x1080@60` |
| `launcher.json` | `nextLauncherState` | The launcher state machine |
| `command-line.json` | `parseCommand` | Every option and the exact error messages |
| `release-feed.json` | `ReleaseFeed.update` | The update check, including the download host rules |
| `versions.json` | `isVersion(_:newerThan:)` | Version comparison |
| `latency-advice.json` | `latencyIssues` | What `--measure` flags. Each port words the advice for its own hardware |
| `percentile.json` | `percentile` | Nearest-rank percentiles in `--measure` |
| `tuning.json` | `Tuning` | Timings every port uses (controls fade-out, volume step, card-audio wait, update interval) |
| `capture-tools.json` | `screenshotFileName`, `LimitedRangeExpansion` | Screenshot file names and the full-range color fix |
| `easing.json` | `CubicBezier`, `Keyframes` | The brand animations' timing curves (CSS `cubic-bezier` semantics) |

Conventions:

- `description` names the rule a case checks. Use it as the test's failure message.
- Devices and failures are defined once at the top of a file and named by key in its cases.
- Enum values use the Swift case names: transports `usb`, `builtIn`, `virtual`, `wireless`, `other`; encodings
  `native`, `converted`, `compressed`; permissions `camera`, `microphone`.
- `null` means absent (no query, no expected device, unknown measurement). In `command-line.json` a missing option
  keeps its value from `defaults`. In `latency-advice.json` a case's `facts` override `healthy`.
- Name matching ignores case (`"VIRTUAL"` matches `Streaming Virtual Camera`); unique IDs match exactly.
- Numbers are the values shown to people: frame rates in a mode key are rounded, so 59.94 fps is `@60`.

Three rules that standard libraries disagree on, so every port implements them itself:

- **Rounding is half away from zero** (Swift `rounded()`, Rust `f64::round`). C#'s `Math.Round` rounds halves to
  even by default; pass `MidpointRounding.AwayFromZero`.
- **Typed numbers are ASCII digits only** (`--volume`, `--mode`, version parts): no sign, no spaces, no decimal
  point. Most `parse` functions accept `+40` or ` 40`.
- **Download URLs** must use the `https` scheme and the host `plugview.app`, both compared ignoring case; user info,
  subdomains and a trailing dot are other hosts.

The brand's geometry, colors and animation keyframes are not copied here. Their source is the brand kit
(`plugview-design/*.svg`, kept outside this repository); `easing.json` pins down how its curves are evaluated.

## Rules the fixtures cannot check

### Command

- Same options, help layout and messages on every OS. Errors go to standard error as `plugview: <message>`, then
  `Run 'plugview --help' for usage.`
- Exit codes: `0` success; `64` bad arguments (sysexits `EX_USAGE`); `1` when playback or `--measure` cannot start
  (no device, no camera permission).
- `--list` prints this layout, with `(none)` under an empty section and the transport's case name in brackets:

  ```
  Video devices:
    USB3.0 Video  [usb]  id: <uniqueID>
      modes: 1920x1080@60, 1280x720@60

  Audio inputs:
    USB Digital Audio  [usb]  id: <uniqueID>
  ```
- `--measure` prints the Mac's report layout. The advice text names the platform's own hardware ("the PC speakers").

### How a port describes devices

`DeviceInfo.transport` decides auto-selection, so each port must classify devices the same way:

| Transport | macOS | Windows | Linux |
| --- | --- | --- | --- |
| `usb` | external USB camera device | device on the USB bus, not part of the PC's enclosure | V4L2 device whose parent is a USB device on a removable port |
| `builtIn` | built-in camera, microphone | camera or microphone in the enclosure (panel location set), even if wired over internal USB | V4L2 or ALSA device on a fixed (non-removable) port: laptop webcams are USB inside |
| `virtual` | DAL/CMIO virtual camera | software or virtual camera (no physical bus) | `v4l2loopback` and other virtual drivers |
| `wireless` | Continuity Camera | phone camera (Phone Link) | network or phone camera |
| `other` | Thunderbolt, PCIe | PCIe capture cards | PCIe capture cards |

`modelID` groups the video and audio halves of one card, so `selectAudioDevice`'s same-model rule pairs them:

- macOS: the AVFoundation model ID.
- Windows: the device's `ContainerId`, which is shared by every function of one physical device.
- Linux: the sysfs path of the parent USB device, shared by the V4L2 node and the ALSA card.

### Keys in the window

| Action | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Toggle full screen | `F`, `⌃⌘F` | `F`, `F11`, `Alt+Enter` | `F`, `F11` |
| Leave full screen | `Esc` | `Esc` | `Esc` |
| Mute / unmute | `M` | `M` | `M` |
| Save a screenshot | `S` | `S` | `S` |
| Volume ±10 % | `↑` `↓` | `↑` `↓` | `↑` `↓` |
| Actual size / bigger / smaller | `⌘0` `⌘+` `⌘-` | `Ctrl+0` `Ctrl+=` `Ctrl+-` | `Ctrl+0` `Ctrl+=` `Ctrl+-` |
| Keep on top | `⌥⌘T` | `Ctrl+Shift+T` | `Ctrl+Shift+T` |
| Settings | `⌘,` | `Ctrl+,` | `Ctrl+,` |
| Close window | `⌘W` | `Ctrl+W` | `Ctrl+W` |
| Quit | `Q`, `⌘Q` | `Q`, `Ctrl+Q` | `Q`, `Ctrl+Q` |

Minimize uses the system's own shortcut. Scrolling over the controls bar changes the volume; moving the wheel or
trackpad up is always louder, whatever the system's scroll direction.

### App behavior

- **Launch mode.** Opened from the desktop (Start menu, app grid, Dock), plugview shows the launcher window.
  Started from a terminal, it is the command and behaves exactly as the Mac's. Windows: the Start menu entry and the
  `plugview` console alias are two executables. Linux: the desktop entry passes a launcher marker that the port
  removes before `parseCommand`.
- **Remembered choices.** The picked device is remembered by `uniqueID`; the capture mode is remembered per device
  as its `ModeChoice` key. Volume is not remembered.
- **Screenshots.** `<Pictures>/plugview/<screenshotFileName>`: `~/Pictures` on macOS, the Pictures known folder on
  Windows, `XDG_PICTURES_DIR` on Linux.
- **Closing the window** releases the card, so its light goes off. With the tray or menu bar icon on, the app keeps
  waiting there; otherwise it quits.
- **The display stays awake** while video plays; a game controller is not keyboard or mouse input.
- **Updates.** Store and Flathub builds leave updates to the store and never read the feed. Direct downloads read
  `https://plugview.app/releases/latest.json` at most once per `updateCheckIntervalSeconds`, with their platform
  (`macos`, `windows`, `linux`) and kind (`app`, `cli`, `flatpak`). Architectures are `universal`, `x86_64` and
  `arm64`.

### Signal watch and picture help

While a card is open, each port ticks `signalWatch` a few times a second (`signal-watch.json`):

- **Frames stop** while the card is still connected (a console changing resolution, waking, or an HDMI replug):
  after `noSignalAfterSeconds` the window shows "No signal" over a black background, never a frozen frame, and
  reopens the card at once, then after each `reopenBackoffSeconds` wait. After `signalHelpAfterAttempts` it also
  shows the console checklist below. A frame resets the attempts.
- **Black frames** (`isBlackFrame` on a sampled frame, off the display path) for `blackPictureSeconds` show the
  black-picture help. **Digital silence** with a picture for `silentAudioSeconds` shows the sound help. Only one
  hint shows at a time.
- Sampling never touches the display path: at most one small frame every half second, measured with `--measure`.

The text is the same on every platform (menu names are the consoles' own; check them on hardware before a release):

**No picture: black screen**
- PlayStation 5: Settings › System › HDMI › turn off **Enable HDCP**. PlayStation 4: Settings › System › turn off
  **Enable HDCP**. PlayStation 3 always protects its HDMI output and cannot be captured over HDMI.
- Set the console to **1080p** (cards with 4K passthrough still capture at most 1080p or 1440p).
- Turn off **120 Hz** and **VRR** output on the console.
- Nintendo Switch: it must sit in its dock. Switch Lite has no video output.
- Unplug the HDMI cable from the card and plug it back in.

**No game sound**
- PlayStation 5: Settings › Sound › Audio Output › Audio Format (Priority) › **Linear PCM**.
- Xbox: Settings › General › Volume & audio output › HDMI audio › **Stereo uncompressed**.
- Nintendo Switch 2: set TV sound to **Stereo**.
- Unplug headphones from the controller or the console: game sound then goes back to HDMI.
