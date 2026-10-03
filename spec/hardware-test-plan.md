# Hardware test plan

Checks that only a person with a real card and a real console can do. Each one points at an entry in
[known-issues.md](known-issues.md). Run the Mac checks on every release; run the Windows and Linux checks once
their app exists (phases 6.2 onward), and again before each release of that platform.

How to run a check: do the steps in order, compare with **Pass when**, and write the result (pass, fail, or what
you saw) under the phase in `plugview-product/PHASES.md`, with the card, console, computer and plugview version.
When a check says **Record**, the result decides a number or a rule in the spec, so note it exactly.

## What you need

| Item | Why |
| --- | --- |
| UGREEN 25773 (MS2130, USB 3) | The reference card: raw 4:2:2 at 1080p60 |
| An MS2109 USB 2 dongle (the ~$10 kind) | The most common card: MJPEG only, and an audio quirk |
| Nintendo Switch with its dock; a PlayStation 5; an Xbox if possible | Different HDMI behavior: HDCP, HDR, 120 Hz, audio formats |
| A phone that films 240 fps slow motion | Glass-to-glass latency |
| A laptop or phone with HDMI out (or an adapter) | Plays a "left right stereo test" video through the card |
| Wired headphones, Bluetooth headphones, a USB 2 hub, a USB webcam | Output switching, latency, bandwidth, device choice |
| Windows PC (USB 3), Linux PC (GNOME on Wayland), Steam Deck | Each platform's checks |

Before every session: run `plugview --list` and `plugview --measure` with the card plugged in and keep the output.

## macOS

**M1 Latency (K09).** Needs: Switch, card, phone at 240 fps, the TV or the Switch handheld screen.
1. Connect the console to the TV through an HDMI splitter, and to the card, or film the Switch in its dock next to the Mac.
2. Open plugview in full screen.
3. Film both screens while pressing a button that changes the picture at once.
4. Count frames between the two screens reacting (one frame at 240 fps is 4.2 ms). Repeat with OBS's preview and QuickTime.

Pass when: plugview is equal to or faster than both. Record the numbers in `docs/PERFORMANCE.md`.

**M2 Cheap cards and hubs (K06, K07).** Needs: MS2109, UGREEN, USB 2 hub.
1. Plug in the MS2109 directly. Run `plugview --measure`.
2. Do the same through the USB 2 hub, then with the UGREEN directly and through the hub.

Pass when: each run picks a smooth mode if one exists, and `--measure` says when the card delivers fewer frames than
promised. Record the modes `--list` shows for each card.

**M3 Right camera, replug (K14, K17).** Needs: card, iPhone with Continuity Camera on, a USB webcam.
1. With the iPhone nearby and the webcam plugged in, open plugview from Applications.
2. Unplug the card and plug it back in, ten times, waiting for the picture each time.

Pass when: the card plays every time; the iPhone and the webcam are never picked by themselves.

**M4 Console mode changes and sleep (K02, K03).** Needs: Switch, PS5.
1. Play the Switch's home screen, then start a game (it may change resolution).
2. Put the PS5 in rest mode, then wake it.
3. Close the Mac's lid for a minute while playing, then open it.

Pass when: the picture comes back by itself each time. Record how long it took and whether the picture froze
(today the Mac has no frame watchdog; phase 1.12 adds it).

**M5 PlayStation HDCP (K01).** Needs: PS5 with Settings › System › HDMI › Enable HDCP turned on.
1. Open plugview with the PS5 connected.
2. Wait 10 seconds, then turn HDCP off on the PS5.

Pass when: with HDCP on the picture is black (expected; after phase 1.12 a help line names the setting), and with HDCP
off the picture appears. Record whether sound played while the picture was black.

**M6 Colors (K04, K05).** Needs: Switch, PS5 or Xbox with HDR.
1. On the Switch, set System Settings › TV Output › RGB Range to Full, then Limited. Press `S` in each.
2. On the PS5 or Xbox, turn HDR on, then off. Press `S` in each.
3. Try the Full-range colors switch in the resolution menu in each case.

Pass when: one setting looks right (black is black, white is white). Keep every screenshot: they become the
fixtures for washed-out detection.

**M7 Smooth motion (K10).** Needs: a game that scrolls steadily.
1. Play at 60 Hz, then set the display to 100 Hz or 120 Hz if it offers them.

Pass when: motion is even at 60 and 120 Hz; at 100 Hz, `--measure` explains the uneven motion.

**M8 Aspect ratio (K11).** Needs: Steam Deck in its dock, or any 4:3 source.
1. Play the source and resize the window by its edges and corners.

Pass when: the picture is never stretched. Record the Deck's output mode (1280×800 or 1920×1080).

**M9 Card in use (K16).** Needs: OBS.
1. Open the card in OBS, then open plugview.
2. Close OBS.

Pass when: plugview says another app is using the card, then plays once OBS closes. Record the exact message.

**M10 Sound plays and follows the output (K22).** Needs: card, wired headphones, Bluetooth headphones.
1. Play a game with sound. Plug in wired headphones, then connect the Bluetooth ones.

Pass when: sound plays from the start without any setting and moves to each new output.

**M11 Two-hour sync (K23).** Needs: a game or video with a visible beat (a clap, a footstep).
1. Play for two hours. Every 30 minutes, film the screen and speaker with the phone.

Pass when: the delay between picture and sound is the same at the end as at the start, with no crackles.

**M12 MS2109 sound channels (K25).** Needs: MS2109, the HDMI-out laptop or phone, wired headphones.
1. Play a "left right stereo test" video through the MS2109.
2. Listen with headphones.

Pass when: "left" comes from the left ear only, at normal pitch. Record what happens: this decides whether the Mac
needs `card-quirks.json` too.

**M13 Silence from the console (K26).** Needs: PS5, a headset for the DualSense.
1. Set Settings › Sound › Audio Output › Audio Format (Priority) to Bitstream (Dolby). Play.
2. Set it back to Linear PCM. Then plug the headset into the controller.

Pass when: sound plays with Linear PCM; with Dolby, a "No game sound" notice appears about 20 seconds after the start.

**M14 Bluetooth (K27).** Needs: Bluetooth headphones.
1. Connect them and run `plugview --measure`.

Pass when: the report says Bluetooth adds delay.

**M15 Long session with an MS2109 (K28).** Needs: MS2109.
1. Leave it playing a source with sound for 4 hours or more.

Pass when: sound still plays. Record the time if it stops, and what it sounded like.

**M16 Permissions (K30).**
1. Run `tccutil reset Camera app.plugview.plugview` and `tccutil reset Microphone app.plugview.plugview`.
2. Open plugview and click **Don't Allow** for the camera. Then follow the screen to allow it.

Pass when: the screen explains what to do and plugview plays after the restart macOS asks for.

**M17 Display stays awake (K32).**
1. Play for 15 minutes without touching the Mac (use the console's controller only).

Pass when: the screen never dims. `pmset -g assertions` lists plugview while it plays.

**M18 No Reactions (K33).** Needs: a webcam pointed at your hand through the card, or a game showing a thumbs-up.
1. Make a thumbs-up and a peace sign in the picture.

Pass when: no confetti, balloons or fireworks appear.

## Windows

**W1 Latency (K09).** M1 on Windows, against OBS's preview and the Windows Camera app. Also run PresentMon: the
present mode is Hardware Independent Flip in full screen.

**W2 Cards, formats and CPU (K06, K07, K08, K19).** M2 on Windows. Also note CPU use with the MS2109 (MJPEG) and the
decoder used (diagnostics), and with a Cam Link 4K if available: change the console's resolution twice.

**W3 Right device, two cards, replug (K14, K15, K17).** On a laptop with a Windows Hello camera: open plugview; the
card plays, never the laptop or IR camera. With two identical cards: each window plays its own card's sound.
Replug twenty times and into another port.

**W4 Mode changes and sleep (K02, K03).** M4 on Windows, plus a PC sleep and resume while playing.

**W5 PlayStation HDCP (K01).** M5 on Windows; the help line names the PS5 setting after 3 seconds.

**W6 Colors and HDR desktop (K04, K05).** M6 on Windows, once with Windows HDR on and once off.

**W7 Smooth motion (K10).** M7 on 60, 120 and 144 Hz displays, and on a VRR display in full screen.

**W8 Full-screen path (K09).** PresentMon in a window and in full screen: Composed Flip in a window, Hardware
Independent Flip in full screen. Record the latency of each.

**W9 Card in use (K16).** M9 with OBS, then with the Windows Camera app.

**W10 Sound plays and follows the output (K22).** M10 on Windows, also changing the default device in Sound
settings while playing.

**W11 Two-hour sync and sample rates (K23, K24).** M11 on Windows; then set the output device to 44.1 kHz in Sound
settings and listen for crackles.

**W12 MS2109 sound channels (K25).** M12 on Windows: "left" only in the left ear, at normal pitch.

**W13 Silence from the console (K26).** M13 on Windows; the sound help appears about 20 seconds after the start.

**W14 Bluetooth (K27).** M14 on Windows.

**W15 Display stays awake (K32).** M17 on Windows.

**W16 Privacy switches (K30).** In Settings › Privacy & security › Camera, turn off camera access for plugview, then
for all desktop apps, then the global switch; do the same for the microphone. Each gives its own screen and the
button opens the right page. With only the microphone off, video still plays and says sound is blocked.

**W17 Antivirus (K31).** On a PC with ESET or Kaspersky webcam protection on: the help names the antivirus.

**W18 Windows on ARM (K39).** On a Snapdragon laptop with the MS2109 and the UGREEN: both play; record CPU and
decode time per frame.

## Linux

**L1 Enumeration, offload, latency (K09, K13, K20, K36).** With the MS2109 plugged in: `plugview --list` shows one
video device. Run with `GDK_DEBUG=offload` on GNOME Wayland: the log shows the video offloaded. Then M1 against OBS
and `mpv --profile=low-latency --untimed`.

**L2 Cards, formats and decoders (K06, K07, K08, K19).** M2 on Linux, on Intel, AMD and NVIDIA if available; record
the decoder (VA or software) and CPU use.

**L3 Right device, two cards, replug inside the Flatpak (K14, K15, K17).** W3 on Linux, run as the Flatpak.

**L4 Mode changes and suspend (K02, K03).** W4 on Linux.

**L5 PlayStation HDCP (K01).** W5 on Linux.

**L6 Colors (K05).** M6 on Linux.

**L7 Smooth motion and scaling (K10).** M7 on Linux, plus GNOME at 125 % scaling.

**L8 X11 and NVIDIA.** Play under an X11 session and on an NVIDIA card with the proprietary driver; record the
video path diagnostics show and the latency.

**L9 Busy card and USB bandwidth (K16, K21).** M9 with OBS; then a webcam and the card on the same hub: plugview
explains the bandwidth problem and plays a lighter mode.

**L10 Sound plays and follows the output (K22).** M10 on Linux.

**L11 Two-hour sync (K23).** M11 on Linux.

**L12 MS2109 sound (K25).** M12 on Linux; on a kernel older than 5.9 if one is at hand, plugview warns.

**L13 Tray and autostart (K34).** On GNOME without the AppIndicator extension: no tray, closing quits. On KDE: tray
shown. Turn on launch at login, log out and in: plugview starts.

**L14 Flatpak (K35).** `flatpak-builder-lint` passes on the manifest; with `flatpak run --nodevice=all` plugview
explains it needs device access.

## Steam Deck

**D1 Install.** From Discover in desktop mode, install plugview, plug the card into a powered dock, plug in a Switch.
It plays with sound.

**D2 Game Mode (K37).** Add plugview to Steam as a non-Steam game, start it in Game Mode: it opens full screen, the
controller's buttons mapped to keys toggle mute (M) and quit (Q), the Steam button and back works.

**D3 Sleep and dock (K03, K38).** While playing, press the power button to sleep, wait 10 seconds, wake; ten times.
Then unplug and replug the dock. The picture comes back by itself each time.
