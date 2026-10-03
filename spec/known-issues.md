# Known field issues

What goes wrong when people play a console through a USB capture card on a computer, collected from user reports
(OBS forums, vendor support pages, kernel and GStreamer trackers, Microsoft and Apple documentation) in October 2026,
and what plugview does about each one. Every entry is checked on real hardware in
[hardware-test-plan.md](hardware-test-plan.md) (column **Test**).

**Status:** **Done** means implemented and tested. **Core** means the shared logic is in the spec and in all three
cores, but the platform still has to wire it up. **Planned** names the phase in `plugview-product/PHASES.md`.
**Help** means plugview can only explain it; the fix is on the console or the computer.

Platforms: M macOS, W Windows, L Linux, D Steam Deck.

## Picture

| ID | What users see | Where | What plugview does | Status | Test |
| --- | --- | --- | --- | --- | --- |
| K01 | Black picture from a PlayStation (sound may still work) | all | HDCP is on by default on PS4/PS5 and always on over PS3 HDMI. `signalHints` shows the HDCP help after 3 s of black frames | Core; wire up: M 1.12, W 6.4, L 6.8 | M5, W5, L5 |
| K02 | Picture works on the home screen, then drops out or flickers in a game | all | The console switched to 4K, 120 Hz or VRR, which the card cannot capture. `signalWatch` reopens the card and, after 3 tries, shows the checklist (1080p, 120 Hz and VRR off) | Core; wire up: M 1.12, W 6.4, L 6.8 | M4, W4, L4 |
| K03 | Picture freezes on the last frame after a resolution change, a console wake, or the computer's sleep | all | Show "No signal", never a frozen frame, and reopen with backoff (`signalWatch`). Windows: handle `MF_SOURCE_READERF_*MEDIATYPECHANGED` and `WM_POWERBROADCAST`. Linux: logind `PrepareForSleep`, then reopen by stable identity, never by `/dev/videoN` | Core; wire up: M 1.12, W 6.2, L 6.7 | M4, W4, L4, D3 |
| K04 | Washed-out, grey picture from PS5, Xbox or Switch 2 | all | The console sends HDR; USB cards pass it as SDR. Help text: turn HDR off on the console. Detection needs real captured frames first | Help; detection after the test plan collects frames | M6, W6 |
| K05 | Grey blacks or crushed shadows | all | Wrong RGB/YUV range. M: Full-range colors switch. W: set the input color space on the D3D11 video processor (default limited BT.709 at 720p and up, BT.601 below; MJPEG is full range), with Auto/Limited/Full per card. L: same defaults through GStreamer colorimetry | Done (M); Planned W 6.4, L 6.8 | M6, W6, L6 |
| K06 | Box says 1080p60, picture is choppy | all | Cheap cards list modes they cannot deliver. `--measure` flags the delivered rate (`lowFrameRate`) | Done | M2, W2, L2 |
| K07 | Low frame rate or no picture behind a hub, a dock or a USB 2 port | all | Mode choice prefers a smooth compressed mode over a slow raw one (`video-modes.json`). Planned: read the USB link speed and say "this card is on USB 2" | Done (mode choice); Planned W 6.2, L 6.7, M 1.12 | M2, W2, L2 |
| K08 | High CPU, dropped frames on MJPEG-only cards | W, L | W: Source Reader with a DXGI device manager and hardware transforms, NV12 out as D3D11 textures. L: `vajpegdec` when VA reports JPEG baseline, else `jpegdec`, chosen once per card and GPU; NVIDIA uses software decode | Planned W 6.2, L 6.7 | W2, L2 |
| K09 | Viewers lag 100-300 ms behind the TV | all | The newest frame always wins: at most one pending read, one frame in flight. W: flip-model swap chain, waitable, maximum frame latency 1. L: sink `sync=false`, leaky queue of 1. M: preview layer, no queue | Done (M); Planned W 6.2, L 6.7 | M1, W1, L1 |
| K10 | Judder at 144 Hz, a hitch every few seconds at 59.94 fps | all | Present frames as they arrive, never on a timer. `--measure` flags a refresh rate that is not a multiple of the frame rate | Done (advice); Planned W 6.2, L 6.7 | M7, W7, L7 |
| K11 | Stretched or blurry picture: 4:3 retro consoles, Steam Deck's 16:10, pixel art | all | The window keeps the video's aspect ratio. Planned: aspect presets (4:3, 8:7, 16:10) and integer scaling with nearest-neighbor filtering, both zero-copy | Done (aspect); Planned 4.2 | M8 |
| K12 | Odd timings from retro scalers, or 480i combing | all | Help text: use the scaler's CEA 720p/1080p output. Deinterlacing is out of scope until there is demand | Help | — |

## Devices

| ID | What users see | Where | What plugview does | Status | Test |
| --- | --- | --- | --- | --- | --- |
| K13 | The card shows twice, or opening it gives "Invalid argument" or no formats | L | uvcvideo adds a metadata node. Keep only nodes whose `device_caps` have `VIDEO_CAPTURE` and `STREAMING` and that list at least one format; group by USB parent | Planned L 6.7 (rule in spec README) | L1 |
| K14 | The laptop camera, an iPhone, a Windows Hello IR camera or a virtual camera opens instead of the card | all | Transport classification (`spec/README.md`): built-in, virtual and phone cameras are never auto-picked. W: IR sensor cameras count as built-in | Done (rules); Planned per platform | M3, W3, L3 |
| K15 | Two identical cards swap, or one plays the other's sound | all | Audio pairs by model ID first (ContainerId on W, USB parent on L). The pick is remembered by unique ID, which encodes the port | Done | W3, L3 |
| K16 | "The device is in use", or a black picture while OBS, Discord or a browser has the card | all | Map the error to "The card is open in another app (OBS, Discord, Camera). Close it, then plugview retries." W: `0xC00D3704`, `0xC00D3EA3`, `0xC00D3EA2`; L: `EBUSY`; plus a retry | Planned W 6.2, L 6.7; M check | M9, W9, L9 |
| K17 | Unplug and replug, and the app needs the card picked again | all | Reopen automatically when the same card returns. L in Flatpak: udev events do not reach the sandbox, so watch `/dev` with inotify and debounce 1 s | Done (M); Planned W 6.5, L 6.9 | M3, W3, L3 |
| K18 | The card does not show up at all (original Elgato HD60 S, older AVerMedia and Razer cards) | all | Those cards need their maker's driver and are not UVC. Help text plus the cards page on the site | Help; site 6.11 | — |
| K19 | Cam Link 4K hangs when the format changes; the format list depends on the input signal | all | Re-query formats on every open, never cycle formats while probing, and reopen fully after a failed format change | Planned W 6.2, L 6.7 | W2, L2 |
| K20 | MS2109 appears only as a sound card (old Linux kernels) | L | Detect "MacroSilicon audio without a video node" and explain the kernel cause; Flatpak cannot install udev rules | Planned L 6.7 | L1 |
| K21 | No picture: USB bandwidth (`ENOSPC`, "No space left on device") with a webcam on the same controller | L, W | Say "USB bandwidth: unplug other cameras or use another port", then retry once with a lighter mode | Planned W 6.2, L 6.7 | L9 |

## Sound

| ID | What users see | Where | What plugview does | Status | Test |
| --- | --- | --- | --- | --- | --- |
| K22 | Picture but no sound: the app captures the card's audio without playing it | all | Card audio plays at once on the default output and follows output changes. No "Listen to this device" | Done (M); Planned W 6.2, L 6.7 | M10, W10, L10 |
| K23 | Sound slowly falls behind the picture, or crackles, over a long session | all | Clock drift. W: `AudioDriftController` (tested over 2 h at ±300 ppm). L: PipeWire adaptive resampling. M: AVFoundation | Done (W core); Planned W 6.2, L 6.7 | M11, W11, L11 |
| K24 | Crackles from a sample-rate mismatch (44.1 vs 48 kHz) | W | Read both mix formats, never assume a rate, convert in the engine | Planned W 6.2 | W11 |
| K25 | Mono, swapped or wrong-pitch sound from an MS2109 dongle | W, M?, old L | It declares 96 kHz mono but sends 48 kHz stereo, swapped. W: `CardAudioQuirks`. L: kernel 5.9+ fixes it; warn on older kernels. M: unknown, test M12 | Done (W core); M test | M12, W12, L12 |
| K26 | No game sound: the console sends Dolby, DTS or surround, or a headset is plugged into the controller | all | `signalHints` shows the sound help after 5 s of digital silence with a picture | Core; wire up: M 1.12, W 6.4, L 6.8 | M13, W13 |
| K27 | Sound noticeably late on Bluetooth headphones | all | `--measure` flags Bluetooth. Video is never delayed automatically; an optional audio-delay slider is planned | Done (advice); Planned 4.x | M14 |
| K28 | Card audio stops after hours (MS2109 turns into a flat square wave) | all | The silence hint covers it. Planned: reopen the audio once, then ask for a replug | Planned M 1.12, W 6.4, L 6.8 | M15 |
| K29 | Another app holds the card's audio in exclusive mode | W | Shared mode only; say which app class to close; retry | Planned W 6.2 | — |

## System and platform

| ID | What users see | Where | What plugview does | Status | Test |
| --- | --- | --- | --- | --- | --- |
| K30 | Black picture or empty list: camera or microphone access is off | all | M: onboarding and Privacy deep link. W: declare `webcam` and `microphone` capabilities, map `E_ACCESSDENIED` to a screen that opens `ms-settings:privacy-webcam`; keep video if only the microphone is denied | Done (M); Planned W 6.3 | M16, W16 |
| K31 | Black picture only on PCs with ESET or Kaspersky | W | Antivirus webcam protection. If the card opens but sends nothing for 3 s while Windows allows the app, name the antivirus (Security Center) in the help | Planned W 6.4 | W17 |
| K32 | Screen dims or the computer sleeps during a cutscene | all | Keep the display awake while frames flow, release on no signal or close | Done (M); Planned W 6.4, L 6.8 | M17 |
| K33 | Confetti or balloons over the game (macOS Reactions) | M | Reaction gestures start off (`NSCameraReactionEffectGesturesEnabledDefault`) | Done | M18 |
| K34 | No tray icon on GNOME | L | Show the tray only when a StatusNotifierItem host exists; otherwise quit on close | Planned L 6.9 | L13 |
| K35 | Flatpak rejected, or the camera portal shows black | L | Direct V4L2 with `--device=all`, justified as OBS's precedent (the portal lacks MJPEG coverage and dmabuf); permissions settled before the first release | Planned L 6.10 | L14 |
| K36 | Video costs power and latency on Linux although the GPU could scan it out | L | Keep the video the only child of `GtkGraphicsOffload` with `black-background`, no rounded corners or opacity over it, controls outside it. Report dmabuf/gl/sysmem and offload state in diagnostics. X11 and NVIDIA take the GL path | Planned L 6.7 | L1 |
| K37 | Steam Deck Game Mode: no window controls, controller arrives as mouse/keys | D | Detect gamescope: start full screen, single-key actions (F, M, S, Q, Esc), no pointer precision needed | Planned L 6.8 | D2 |
| K38 | Card disconnects on the Deck's single USB-C port | D | Treat as a replug (K17); docs recommend a powered dock | Help; Planned 6.11 | D3 |
| K39 | Windows on ARM: vendor apps do not run | W | Native arm64 build; prefer uncompressed formats; `--measure` shows decode cost | Planned W 6.6 | W18 |

## Where plugview must stand out

Users already compare plugview with OBS (too heavy), QuickTime (no sound), Windows Camera (no sound, vague errors),
vendor utilities (heavy, x64-only), and small viewers (Consolation, Bisory, Console Capture). The acceptance bar from
these reports: the card is picked automatically, its sound plays at once, glass-to-glass latency is no worse than any
of them on the same computer, replug, sleep and console mode changes recover without a click, and every failure
becomes a plain sentence with an action.

## Sources

Representative sources per area; full research notes are kept with the phase records.

- HDCP and console settings: [Elgato 4K Pro PS5 setup](https://help.elgato.com/hc/en-us/articles/23706314592269-Elgato-Game-Capture-4K-Pro-PlayStation-5-Setup),
  [Elgato: no game sound](https://help.elgato.com/hc/en-us/articles/360041852811-Troubleshooting-No-Game-Sound-Is-Being-Captured),
  [AVerMedia: Switch 2](https://www.avermedia.com/support/faq/avermedia-and-switch-2-support),
  [Nintendo: RGB range](https://en-americas-support.nintendo.com/app/answers/detail/a_id/68188)
- Freezes and replug: [OBS: freeze on resolution change](https://obsproject.com/forum/threads/video-capture-device-freezing-on-resolution-change.154620/),
  [Media Foundation: video device loss](https://learn.microsoft.com/en-us/windows/win32/medfound/handling-video-device-loss),
  [OBS: re-select after disconnect](https://obsproject.com/forum/threads/re-select-device-after-disconnect.186046/)
- MS2109: [kernel quirk](https://lkml.iu.edu/hypermail/linux/kernel/2007.1/07921.html),
  [ms2109-player](https://github.com/yume-chan/ms2109-player),
  [missing video node](https://www.mjt.me.uk/posts/fixing-missing-macrosilicon-ms2109/)
- Windows: [MF_LOW_LATENCY](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-low-latency),
  [Source Reader attributes](https://learn.microsoft.com/en-ca/windows/win32/medfound/source-reader-attributes),
  [low-latency audio](https://learn.microsoft.com/en-us/windows-hardware/drivers/audio/low-latency-audio),
  [camera privacy](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/camera-privacy-setting),
  [OBS: 44.1 kHz crackling](https://obsproject.com/forum/threads/capture-card-audio-crackling-obs-detecting-incorrect-sample-rate-from-elgato-hd60-x.191254)
- Linux: [Flathub requirements](https://docs.flathub.org/docs/for-app-authors/requirements),
  [GTK graphics offload](https://blog.gtk.org/2024/04/),
  [OBS works, ffmpeg fails](https://dev.to/wixom/why-a-usb-capture-card-works-in-obs-but-fails-in-ffmpeg-on-linux-4h0f),
  [MS2130 formats](https://github.com/awawa-dev/HyperHDR/discussions/499),
  [Magewell: status -71](https://www.magewell.com/kb/detail/302050046/All),
  [nvidia-vaapi-driver 0.0.17](https://ubuntuhandbook.org/index.php/2026/05/nvidia-va-api-driver-0-0-17/)
- macOS: [system video effects](https://developer.apple.com/documentation/avfoundation/system-video-effects-and-microphone-modes),
  [Elgato: macOS privacy](https://help.elgato.com/hc/en-us/articles/360036388751-Capture-Device-not-working-with-macOS-Adjusting-Privacy-Settings)
- Latency complaints: [OBS: capture buffering](https://obsproject.com/forum/threads/help-me-understand-buffering-on-a-video-capture-device.109264),
  [Genki ShadowCast review](https://www.inverse.com/input/reviews/genki-shadowcast-review-nintendo-switch-games-on-laptop-not-worth-crazy-lag/amp)
