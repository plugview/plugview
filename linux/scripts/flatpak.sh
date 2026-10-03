#!/usr/bin/env bash
# Builds the Flatpak bundle in a Fedora 42 container (privileged: flatpak-builder sandboxes with bubblewrap) into
# /out/plugview-linux-<arch>.flatpak, for the container's own architecture or FLATPAK_ARCH. Run the container natively:
# bubblewrap cannot create its namespaces under emulation, so x86_64 is built from an arm64 container with
# FLATPAK_ARCH=x86_64 (scripts/build-previews.sh does that; the build tools then run through binfmt). For arm64:
#   docker run --rm --privileged --platform linux/arm64 -v "$PWD":/src -v plugview-flatpak:/var/lib/flatpak \
#     -v "$PWD/linux/target/flatpak":/out fedora:42 bash /src/linux/scripts/flatpak.sh
set -euo pipefail
dnf install -y -q flatpak flatpak-builder appstream desktop-file-utils git >/dev/null 2>&1
flatpak remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
appstreamcli validate --no-net --explain /src/linux/data/app.plugview.plugview.metainfo.xml
desktop-file-validate /src/linux/data/app.plugview.plugview.desktop
arch="${FLATPAK_ARCH:-$(flatpak --default-arch)}"
work=/var/lib/flatpak/plugview-build
mkdir -p "$work" /out
cd "$work"
flatpak-builder --arch="$arch" --install-deps-from=flathub --force-clean --disable-rofiles-fuse --state-dir=state --repo=repo build \
  /src/linux/flatpak/app.plugview.plugview.yml
bundle="/out/plugview-linux-${arch/aarch64/arm64}.flatpak"
flatpak build-bundle --arch="$arch" --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo repo "$bundle" app.plugview.plugview
# The version the bundle's command reports, which scripts/release.sh checks before publishing it.
flatpak build build plugview --version >"$bundle.version"
echo "$bundle $(cat "$bundle.version")"
