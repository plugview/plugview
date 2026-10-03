# plugview for Windows

Work in progress (phase 6 in `plugview-product/PHASES.md`). What exists:

| Project | What it is |
| --- | --- |
| `src/Plugview.Core` | The shared decisions (device and mode choice, options, release feed, signal watch, audio drift and card quirks), tested against `spec/fixtures` on any OS |
| `src/Plugview.Windows` | Capture, display and audio: Media Foundation source reader → Direct3D 11 video processor → flip-model swap chain; WASAPI card audio with drift correction |
| `src/Plugview.Cli` | `plugview.exe`: `--list`, `--measure`, and a plain window that plays the card (the phase 6.2 latency spike, later the `plugview` command) |

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

The last one opens a window with the card's picture and sound. Keys: `F`, `F11` or `Alt+Enter` full screen, `Esc`
leaves it, `M` mutes, `↑` `↓` change the volume, `Q` quits. To build a single native `plugview.exe` (no .NET needed to
run it): `dotnet publish src/Plugview.Cli -c Release -r win-x64` (or `win-arm64`).

What to check first (phase 6.2, and W1, W2 in `spec/hardware-test-plan.md`):

1. `--list` shows the card once, marked `[usb]`, with its modes, and its audio input with the same `{…}` container.
2. `--measure` reports the full frame rate (60 fps at 1080p60) and a capture → app time; keep the whole output.
3. The window plays with sound; full screen with `F`.
4. Glass to glass against OBS's preview and the Windows Camera app on the same PC (M1 in the test plan).
