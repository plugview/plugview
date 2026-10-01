# Contributing to plugview

Thanks for helping. plugview stays small on purpose: one job (show a capture device with the least delay),
Apple frameworks only, no third-party dependencies. Changes that keep it that way are the easiest to merge.

## Setup

You need macOS 14 or later and Xcode 16.4 or later (or its Command Line Tools). CI tests the oldest and newest toolchains.

```sh
git clone https://github.com/MauricioLeon54/plugview.git && cd plugview
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
  plugview/               The macOS shell: AVFoundation, Core Audio, AppKit
    main.swift              Parses the command and dispatches it
    App/                    App delegate (hot-plug, permissions, commands), menu, console output
    Capture/                Capture session, device discovery, format locking, Core Audio buffers, --measure
    Window/                 The player window and key handling
    Resources/Info.plist    Embedded in the binary; holds the camera/microphone prompt texts
Tests/PlugviewCoreTests/  Swift Testing suites for PlugviewCore
docs/                     Performance notes and measurements
```

**Rule of thumb:** a decision that can be tested without a capture card belongs in `PlugviewCore`. The
`plugview` target turns framework objects into `PlugviewCore` values, asks it what to do, and does it.

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
3. Commit, then tag and push:

   ```sh
   git tag -a vX.Y.Z -m "plugview X.Y.Z" && git push origin main vX.Y.Z
   ```

The [release workflow](.github/workflows/release.yml) checks that the tag matches the version, runs
`make check`, builds the universal archive, and publishes a GitHub Release whose notes come from that
version's `CHANGELOG.md` section. The [Homebrew tap](https://github.com/MauricioLeon54/homebrew-tap)
picks up the new release automatically within a few hours, or right away if you run its
*Update formula* workflow.
