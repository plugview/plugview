# plugview for Windows

Work in progress (phase 6 in `plugview-product/PHASES.md`). What exists:

| Project | What it is |
| --- | --- |
| `src/Plugview.Core` | The shared decisions (device and mode choice, options, release feed, signal watch, audio drift and card quirks), tested against `spec/fixtures` on any OS |
| `src/Plugview.Windows` | Capture, display and audio (Media Foundation source reader → Direct3D 11 video processor → flip-model swap chain; WASAPI card audio with drift correction), and the app: window, launcher, controls, signal watch, tray, Narrator |
| `src/Plugview.App` | `plugview-app.exe`: the Start menu entry and launch at login (one instance, tray) |
| `src/Plugview.Cli` | `plugview.exe`: the command, `--list`, `--measure`, and the same window as the app (closing it quits) |

The window is plain Win32 with no UI framework: the video keeps its flip-model swap chain (the zero-copy path), and
everything else (launcher, brand art, controls, toasts, notices, window buttons) is drawn with Direct2D and DirectWrite
into a DirectComposition visual above it. With the controls hidden that layer is empty, so in full screen Windows can
flip the video straight to the display. The brand comes from `spec/fixtures/brand.json` (embedded), the art from the
same keyframes as the Mac (`Plugview.Core/Brand/ArtScene.cs`, tested), Geist from the Mac's font files. Popup menus
are Windows' own. Narrator reads the window through a UI Automation provider built from what each frame draws.

Everything builds on macOS and Linux too (`EnableWindowsTargeting`), but only runs on Windows 10 2004 (19041) or later.

## Checks (any OS)

```sh
dotnet format Plugview.slnx --verify-no-changes
dotnet test --solution Plugview.slnx
```

## Run it on a Windows PC

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), plug in the card, then from this folder:

```powershell
dotnet run --project src/Plugview.Cli -c Release -- --list
dotnet run --project src/Plugview.Cli -c Release -- --measure
dotnet run --project src/Plugview.Cli -c Release
```

The last one opens the window: the launcher until a card plays, then the picture with sound and the controls bar. Keys:
`F`, `F11` or `Alt+Enter` full screen, `Esc` leaves it, `M` mutes, `↑` `↓` volume, `S` screenshot (Pictures\plugview),
`Ctrl+0` `Ctrl+=` `Ctrl+-` size, `Ctrl+Shift+T` keep on top, `Tab` moves between controls, `Ctrl+W` closes, `Q` quits.
`dotnet run --project src/Plugview.App -c Release` runs the app (tray icon, remembered choices, one instance). To build
native executables (no .NET needed to run them): `dotnet publish src/Plugview.App -c Release -r win-x64` and the same
for `src/Plugview.Cli` (or `win-arm64`). `scripts/icons.sh` regenerates the app and tray icons.

What to check first (phase 6.2, and W1, W2 in `spec/hardware-test-plan.md`):

1. `--list` shows the card once, marked `[usb]`, with its modes, and its audio input with the same `{…}` container.
2. `--measure` reports the full frame rate (60 fps at 1080p60) and a capture → app time; keep the whole output.
3. The window plays with sound; full screen with `F`.
4. Glass to glass against OBS's preview and the Windows Camera app on the same PC (M1 in the test plan).
