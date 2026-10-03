# Platform parity

Everything plugview is on the Mac, and where it stands on Windows and Linux. A platform ships as "1.0" only when every
row is ✓ or marked as a deliberate platform difference. Update this file in the same commit that changes a row.

✓ done · ◐ partial (preview) · ✗ not yet, with the phase that brings it · — not applicable

## Brand

The source is the brand kit (`plugview-design/`); the numbers every platform draws with are in
`spec/fixtures/brand.json`, and each platform's tests compare its own constants to it.

| | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Palette (cathode, bone, signal, tally, bars…) from `brand.json` | ✓ tested | ✓ `brand.json` embedded, tested | ✓ read from `brand.json` |
| Geist and Geist Mono bundled (SIL OFL), system font only as a fallback | ✓ | ◐ embedded, DirectWrite from memory (not run) | ✓ embedded, checked on Fedora 42 |
| Dark CRT window in light mode too, title bar included | ✓ | ◐ no title bar, own window buttons with Snap Layouts, dark frame (not run) | ✓ no header bar, dark forced |
| The mark, the waiting loop, the plug-in intro, the live loop (64-unit geometry, keyframes and easing from `brand.json`) | ✓ tested | ◐ scenes tested, drawn with Direct2D (not run) | ✓ cairo, from `brand.json` |
| Reduce Motion freezes every animation on its resting frame | ✓ | ◐ Animation effects off (not run) | ✓ `gtk-enable-animations` |
| App icon from `plugview-icon.svg` | ✓ | ✗ 6.6 (.ico, MSIX tiles) | ✗ 6.10 (hicolor SVG and symbolic) |
| Menu bar / tray icon: empty, signal, playing | ✓ | ◐ drawn from `brand.json`, light and dark taskbar (not run) | ✓ drawn from `brand.json` (bone on every panel) |
| Lowercase `plugview`, no emojis, words or line icons only | ✓ | ✓ (text so far) | ✓ (text so far) |
| Control icons from the platform's own set | ✓ SF Symbols | ◐ Segoe Fluent Icons, MDL2 on Windows 10 (not run) | ✓ Adwaita symbolic |
| English and Spanish | ✗ 5.3 | ✗ 5.3 | ✗ 5.3 |

## Playing

| | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Picks the card, never a webcam, phone or virtual camera (spec rules) | ✓ | ◐ 6.2 | ◐ 6.7 |
| Pairs the card's own audio (model ID first) | ✓ | ◐ ContainerId | ◐ USB parent |
| Smoothest mode by default; any mode by choice, remembered per card | ✓ | ◐ chosen by `--mode`, not remembered | ◐ remembered (not run on Linux yet) |
| Zero-copy display path | ✓ preview layer | ◐ flip-model swap chain, one frame (not run yet) | ◐ dmabuf + graphics offload (not run on Linux yet) |
| Card audio plays at once, follows the output, delay held | ✓ | ◐ WASAPI + drift controller | ◐ GStreamer on the card's clock |
| Card quirks (MS2109 audio) | ? test M12 | ◐ | ✓ kernel 5.9+, warning before |
| Keeps the display awake while playing | ✓ | ◐ | ◐ |
| Replug: plays again by itself | ✓ | ◐ device watcher (not run) | ◐ (not run on Linux yet) |
| Sleep and wake recovery | ✓ | ◐ releases before sleep, plays after wake (not run) | ◐ signal watch reopens (not run on Linux yet) |
| Signal watch: No signal, reconnect, HDCP and sound help | ✓ | ◐ wired, samples a 64×36 frame twice a second (not run) | ◐ checked on macOS, loss simulated |
| Never a stretched picture | ✓ | ✓ letterbox | ✓ content fit |

## Window and controls

| | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Launcher: onboarding, permission denied, waiting, ready, pick a device, failed | ✓ | ◐ every screen; onboarding only in the Store package (not run) | ◐ no onboarding (no permission prompt on Linux) |
| No title bar over the video; window buttons and controls fade in on mouse move | ✓ | ◐ (not run) | ◐ (not run on GNOME yet) |
| Controls bar: mute, volume (click, drag, scroll), screenshot, resolution, full screen, device menu | ✓ | ◐ menus are Windows' own (not run) | ◐ (not run on GNOME yet) |
| Resolution panel with Full-range colors per card | ✓ | ◐ menu; video processor contrast and brightness where the driver has them (not run) | ◐ `videobalance`, leaves zero copy while on |
| Toasts (intro, screenshot saved, warnings) | ✓ | ◐ no intro toast (not run) | ◐ no intro toast |
| Screenshots (`S`, `<Pictures>/plugview`, same file name) | ✓ | ◐ PNG at the card's size (not run) | ✓ |
| Keys from the spec's table | ✓ | ◐ all, Ctrl+, opens the tray menu (not run) | ◐ all but Ctrl+Shift+T and Ctrl+, |
| Actual size, zoom, keep on top | ✓ | ◐ (not run) | ◐ no keep on top: GTK 4 and Wayland leave it to the compositor |
| Remembers window size and position | ✓ | ◐ (not run) | ◐ size only: Wayland does not let apps place windows |
| Drag the video to move the window | ✓ | ◐ (not run) | ✓ |
| Accessibility: screen reader, keyboard only | ◐ 5.2 | ◐ UI Automation provider, Tab and Enter (not run with Narrator) | ◐ labeled controls, Orca not checked |

## System

| | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Menu bar / tray panel: status, device, resolution, settings | ✓ | ◐ tray menu with status and settings, no panel (not run) | ◐ tray menu with the settings, no panel (KDE, Steam Deck; GNOME needs AppIndicator) |
| Open when a card is plugged in | ✓ | ◐ (not run) | ◐ (not run on Linux yet) |
| Closing releases the card; stays in the menu bar / tray | ✓ | ◐ (not run) | ✓ checked under Xvfb; quits without a tray |
| Launch at login | ✓ | ◐ Run key; StartupTask in the Store package (not run) | ✓ XDG autostart; Background portal in Flatpak (6.10) |
| Permissions explained before the system asks | ✓ | ◐ Store package: onboarding, then Windows' prompt; denied screen opens Settings (not run) | — device access, `video` group |
| Update check from the feed | ✓ | — Store updates (6.6) | — Flathub updates (6.10) |
| Signed and trusted install | ✗ 3.1 Developer ID | ✗ 6.6 Microsoft Store | ✗ 6.10 Flathub |

## Command

| | macOS | Windows | Linux |
| --- | --- | --- | --- |
| Same options, messages and exit codes (spec fixtures) | ✓ | ✓ | ✓ |
| `--list` in the spec's layout | ✓ | ◐ (not run yet) | ✓ checked on macOS |
| `--measure` with the same report and advice for the platform | ✓ | ◐ (not run yet) | ✓ checked on macOS |
| `plugview` on the PATH | ✓ | ✗ 6.5 MSIX alias | ✗ 6.10 |
