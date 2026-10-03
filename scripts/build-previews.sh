#!/usr/bin/env bash
# Builds the Windows and Linux preview downloads into .build/dist/, where scripts/release.sh picks them up:
#
#   plugview-windows-x64.zip, plugview-windows-arm64.zip   self-contained plugview.exe (.NET 10 SDK; NativeAOT needs a
#                                                         Windows host, so these are single-file builds for now)
#   plugview-linux-x86_64.tar.gz                          built in a Fedora 41 container (Docker); runs where GTK 4.16+,
#                                                         libadwaita 1.6+ and GStreamer 1.24+ are installed
#
# Run it before scripts/release.sh. Runs on macOS bash 3.2.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
VERSION="$(make -s version)"
DIST=.build/dist
WORK=.build/preview
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
fail() { echo "build-previews: $*" >&2; exit 1; }

[[ -x "$DOTNET" ]] || fail "no .NET SDK at $DOTNET (set DOTNET=...)"
command -v docker >/dev/null || fail "Docker is needed for the Linux build"
rm -rf "$WORK" && mkdir -p "$WORK" "$DIST"

# --- Windows ---

for arch in x64 arm64; do
  out="$WORK/windows-$arch/plugview-$VERSION"
  DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$DOTNET" publish windows/src/Plugview.Cli -c Release -r "win-$arch" --self-contained \
    -p:PublishAot=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none -p:GenerateDocumentationFile=false -o "$out" >/dev/null
  rm -f "$out"/*.xml "$out"/*.pdb
  cp LICENSE "$out/LICENSE.txt"
  cp windows/README.md "$out/README.md"
  [[ -f "$out/plugview.exe" ]] || fail "windows-$arch: no plugview.exe"
  rm -f "$DIST/plugview-windows-$arch.zip"
  (cd "$WORK/windows-$arch" && zip -q -r "$ROOT/$DIST/plugview-windows-$arch.zip" "plugview-$VERSION")
  echo "$DIST/plugview-windows-$arch.zip"
done

# --- Linux ---

# Fedora 41 is the oldest current release with GTK 4.16; its glibc 2.40 keeps the binary running on newer systems.
# The named volumes keep Rust and the crate cache between builds.
docker run --rm --platform linux/amd64 -v "$ROOT":/src -w /src/linux \
  -v plugview-cargo:/root/.cargo -v plugview-rustup:/root/.rustup \
  -e CARGO_TARGET_DIR=/src/linux/target/linux-x86_64 fedora:41 bash -c '
    set -e
    dnf install -y -q gcc git gtk4-devel libadwaita-devel gstreamer1-devel gstreamer1-plugins-base-devel >/dev/null
    [ -x /root/.cargo/bin/cargo ] || curl -sSf https://sh.rustup.rs | sh -s -- -y -q --profile minimal >/dev/null
    /root/.cargo/bin/cargo build --release -q
  '
binary=linux/target/linux-x86_64/release/plugview
file "$binary" | grep -q "ELF 64-bit LSB.*x86-64" || fail "the Linux build is not an x86-64 ELF binary"
out="$WORK/linux-x86_64/plugview-$VERSION"
mkdir -p "$out"
cp "$binary" "$out/plugview"
cp LICENSE "$out/LICENSE"
cp linux/README.md "$out/README.md"
# No macOS extended attributes: GNU tar on Linux would warn about each one.
COPYFILE_DISABLE=1 tar --no-xattrs --no-mac-metadata -czf "$DIST/plugview-linux-x86_64.tar.gz" -C "$WORK/linux-x86_64" "plugview-$VERSION"
echo "$DIST/plugview-linux-x86_64.tar.gz"
