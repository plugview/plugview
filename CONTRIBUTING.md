# Contributing to plugview

Thanks for helping. plugview stays small on purpose: one job (show a capture device with the least delay),
Apple frameworks only, no third-party dependencies. Changes that keep it that way are the easiest to merge.

## Setup

You need macOS 14 or later and Xcode 16.4 or later (or its Command Line Tools). CI tests the oldest and newest toolchains.

```sh
git clone https://github.com/plugview/plugview.git && cd plugview
make check      # what CI runs: strict lint, warnings-as-errors build, tests
swift run plugview --list
```

| Command | What it does |
| --- | --- |
| `make build` | Release build into `.build/release/plugview` |
| `make test` | Unit tests |
| `make lint` | `swift format lint --strict` |
| `make format` | Rewrites files with `swift format` |
| `make check` | Lint + strict build + tests, same as CI |
| `make install` | Installs to `~/.local/bin` (`PREFIX=/usr/local` with `sudo` for all users) |
| `make dist` | Universal release archive and its SHA-256 in `.build/dist` |

## Project layout

```
Sources/
  PlugviewCore/           Pure Swift, no Apple media frameworks; unit-tested
    Constants.swift         Version and every tuning value, each with the measurement behind it
    CommandLine/            Argument parsing and --help text
    Selection/              Which video device, audio input, and format to use
    Measurement/            Percentiles and the advice --measure prints
  PlugviewCapture/        AVFoundation and Core Audio: capture session, device discovery, format locking,
                          audio buffers, permissions, --measure
  PlugviewUI/             SwiftUI launcher shown when plugview is opened as an app: states, onboarding
    Brand/                  Palette, fonts, and the mark and its animations drawn from the 64-unit brand geometry
    Fonts/                  Geist and Geist Mono (SIL OFL); a binary installed without them uses system fonts
  plugview/               The executable: AppKit shell that wires the modules together
    main.swift              Parses the command and dispatches it
    App/                    App delegate (hot-plug, permissions, commands) and menu
    Window/                 The player window and key handling
    Resources/Info.plist    Embedded in the binary; holds the camera/microphone prompt texts
Tests/PlugviewCoreTests/  Swift Testing suites for PlugviewCore
docs/                     Performance notes and measurements
```

**Rule of thumb:** a decision that can be tested without a capture card belongs in `PlugviewCore`. The
`PlugviewCapture` and `plugview` targets turn framework objects into `PlugviewCore` values, ask it what to do, and do it.

## Code rules

- **Swift 6 language mode** with complete concurrency checking, plus `ExistentialAny`. Warnings fail CI.
- **`swift format --strict`** with the rules in `.swift-format`: no force unwraps, no `try!`, no implicitly
  unwrapped optionals, documentation comments that start with a one-line summary.
- **No third-party dependencies.**
- **Typed errors** that tell the user what to do next. Never crash on missing hardware.
- **Performance claims need numbers.** If a change is about latency, include `plugview --measure` output
  from before and after, and update [docs/PERFORMANCE.md](docs/PERFORMANCE.md).
- New tuning values go in `Constants.swift`, with a comment on how they were measured.

## Pull requests

1. Branch from `main`.
2. Run `make check`.
3. Add a line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for anything a user would notice.
4. Describe how you tested it, and with which capture card if it touches capture.

## Releasing (maintainers)

1. Move the `Unreleased` entries in `CHANGELOG.md` under a new `## [x.y.z] - YYYY-MM-DD` heading and update the
   compare links at the bottom.
2. Set the version in `Sources/PlugviewCore/Constants.swift` and `Sources/plugview/Resources/Info.plist`
   (a test fails if they differ).
3. Commit and push `main`.
4. Run `scripts/release.sh` (a dry run: it builds, verifies, and stages the release in `.build/publish/x.y.z/`),
   then `scripts/release.sh --publish`.
5. Redeploy plugview-site (`./deploy.sh`) so its download links point at the new version.

Releases are hosted on plugview.app, not on GitHub. `--publish` uploads the versioned files to
`https://plugview.app/releases/x.y.z/` (never overwritten), copies them to `releases/latest/` under version-free
names, then points `releases/latest.json` at them, and finally tags `vx.y.z`. The app's update check reads
`latest.json`; its schema is `ReleaseFeed` in `Sources/PlugviewCore/Settings/ReleaseFeed.swift`. Each release
lists its downloads by platform, arch, and kind (`app` or `cli`), so builds for other platforms join the same
release as more entries. It needs the `plugview` AWS profile.
