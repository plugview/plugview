#!/usr/bin/env bash
# Builds, verifies and (with --publish) publishes a plugview release to https://plugview.app/releases/.
#
#   scripts/release.sh            dry run: build, verify, stage everything in .build/publish/<version>/, upload nothing
#   scripts/release.sh --publish  the same, then upload, point latest.json at it, and tag v<version>
#
# Layout on plugview.app (S3 bucket behind CloudFront, stack plugview-site):
#   releases/<version>/plugview-<version>-<platform>-<arch>[-app].<ext>   immutable, never overwritten
#   releases/<version>/SHA256SUMS, notes.md, release.json
#   releases/latest.json                                                  copy of the newest release.json
#   releases/latest/plugview-<platform>-<arch>[-app].<ext>                version-free copies, for links and curl
#
# release.json lists one artifact per platform/arch/kind, so Windows and Linux builds join the same release later.
# Its schema is ReleaseFeed (Sources/PlugviewCore/Settings/ReleaseFeed.swift); the app reads latest.json.
#
# Needs the AWS profile `plugview` (override with AWS_PROFILE) and runs on macOS bash 3.2.
set -euo pipefail

export AWS_PROFILE="${AWS_PROFILE:-plugview}"
REGION=us-east-1
STACK=plugview-site
SITE=https://plugview.app
TAG_REMOTE="${TAG_REMOTE:-aws}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

IS_PUBLISHING=false
case "${1:-}" in
  --publish) IS_PUBLISHING=true ;;
  "") ;;
  *) echo "usage: scripts/release.sh [--publish]" >&2; exit 2 ;;
esac

fail() { echo "release: $*" >&2; exit 1; }

# --- Version, changelog, and repository state ---

VERSION="$(make -s version)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "version '$VERSION' in Constants.swift is not x.y.z"
RELEASE_DATE="$(sed -n "s/^## \[$VERSION\] - \([0-9-]*\)$/\1/p" CHANGELOG.md)"
[[ -n "$RELEASE_DATE" ]] || fail "CHANGELOG.md has no '## [$VERSION] - YYYY-MM-DD' section"
MINIMUM_MACOS="$(/usr/libexec/PlistBuddy -c 'Print LSMinimumSystemVersion' Sources/plugview/Resources/Info.plist)"

if $IS_PUBLISHING; then
  [[ -z "$(git status --porcelain)" ]] || fail "the working tree has changes; commit them first"
  [[ "$(git rev-parse --abbrev-ref HEAD)" == main ]] || fail "release from main"
  ! git rev-parse -q --verify "refs/tags/v$VERSION" >/dev/null || fail "tag v$VERSION already exists"
fi

# --- Build and verify ---

make check
make dist

DIST=.build/dist
VERIFY="$(mktemp -d)"
trap 'rm -rf "$VERIFY"' EXIT

is_universal() { case "$(lipo -archs "$1")" in *arm64*x86_64* | *x86_64*arm64*) return 0 ;; *) return 1 ;; esac; }

tar -xzf "$DIST/plugview-macos-universal.tar.gz" -C "$VERIFY"
command_binary="$VERIFY/plugview-$VERSION/plugview"
is_universal "$command_binary" || fail "the command is not universal"
[[ "$("$command_binary" --version)" == "$VERSION" ]] || fail "the command reports another version"

ditto -x -k "$DIST/plugview-macos-app.zip" "$VERIFY/app"
app="$VERIFY/app/plugview.app"
plist() { /usr/libexec/PlistBuddy -c "Print $1" "$app/Contents/Info.plist"; }
codesign --verify --deep --strict "$app"
[[ "$(plist CFBundleIdentifier)" == app.plugview.plugview ]] || fail "unexpected bundle id"
[[ "$(plist CFBundleShortVersionString)" == "$VERSION" ]] || fail "Info.plist has another version"
[[ -f "$app/Contents/Resources/AppIcon.icns" && -d "$app/Contents/Resources/plugview_PlugviewUI.bundle" ]] || fail "app resources missing"
is_universal "$app/Contents/MacOS/plugview" || fail "the app is not universal"
[[ "$("$app/Contents/MacOS/plugview" --version)" == "$VERSION" ]] || fail "the app reports another version"

# --- Stage ---

STAGE=".build/publish/$VERSION"
BASE_URL="$SITE/releases/$VERSION"
rm -rf "$STAGE" && mkdir -p "$STAGE"
APP_FILE="plugview-$VERSION-macos-universal-app.zip"
COMMAND_FILE="plugview-$VERSION-macos-universal.tar.gz"
cp "$DIST/plugview-macos-app.zip" "$STAGE/$APP_FILE"
cp "$DIST/plugview-macos-universal.tar.gz" "$STAGE/$COMMAND_FILE"
(cd "$STAGE" && shasum -a 256 "$APP_FILE" "$COMMAND_FILE" >SHA256SUMS)

awk -v heading="## [$VERSION]" '
  index($0, heading) == 1 { found = 1; next }
  found && (/^## \[/ || /^\[[^]]+\]: /) { exit }
  found { print }
' CHANGELOG.md >"$STAGE/notes.md"

# One JSON object per artifact: platform, arch, kind, file.
artifact_json() {
  local file="$STAGE/$4"
  printf '    {"platform": "%s", "arch": "%s", "kind": "%s", "url": "%s", "sha256": "%s", "size": %s, "minimumOSVersion": "%s"}' \
    "$1" "$2" "$3" "$BASE_URL/$4" "$(shasum -a 256 "$file" | cut -d' ' -f1)" "$(stat -f %z "$file")" "$MINIMUM_MACOS"
}
{
  printf '{\n  "schemaVersion": 1,\n  "version": "%s",\n  "releasedAt": "%s",\n  "notesUrl": "%s",\n  "artifacts": [\n' \
    "$VERSION" "$RELEASE_DATE" "$BASE_URL/notes.md"
  artifact_json macos universal app "$APP_FILE"
  printf ',\n'
  artifact_json macos universal cli "$COMMAND_FILE"
  printf '\n  ]\n}\n'
} >"$STAGE/release.json"
python3 -m json.tool "$STAGE/release.json" >/dev/null || fail "release.json is not valid JSON"

echo
echo "Staged plugview $VERSION ($RELEASE_DATE) in $STAGE:"
ls -l "$STAGE" | tail -n +2
if ! $IS_PUBLISHING; then
  echo
  echo "Dry run: nothing uploaded. Publish with: scripts/release.sh --publish"
  exit 0
fi

# --- Publish ---

output() {
  aws cloudformation describe-stacks --region "$REGION" --stack-name "$STACK" \
    --query "Stacks[0].Outputs[?OutputKey=='$1'].OutputValue" --output text
}
BUCKET="$(output BucketName)"
DISTRIBUTION_ID="$(output DistributionId)"

# Published versions are immutable: users and caches may already hold these files. If an upload below failed
# halfway, delete releases/<version>/ by hand (latest.json still points at the previous release) and rerun.
existing="$(aws s3api list-objects-v2 --bucket "$BUCKET" --prefix "releases/$VERSION/" --max-keys 1 --query 'KeyCount' --output text)"
[[ "$existing" == 0 ]] || fail "releases/$VERSION/ already exists on $BUCKET; bump the version instead"

upload() { # file, key, content type, cache control
  aws s3 cp "$1" "s3://$BUCKET/$2" --content-type "$3" --cache-control "$4" --only-show-errors
}
IMMUTABLE='public, max-age=31536000, immutable'
upload "$STAGE/$APP_FILE" "releases/$VERSION/$APP_FILE" application/zip "$IMMUTABLE"
upload "$STAGE/$COMMAND_FILE" "releases/$VERSION/$COMMAND_FILE" application/gzip "$IMMUTABLE"
upload "$STAGE/SHA256SUMS" "releases/$VERSION/SHA256SUMS" 'text/plain; charset=utf-8' "$IMMUTABLE"
upload "$STAGE/notes.md" "releases/$VERSION/notes.md" 'text/markdown; charset=utf-8' "$IMMUTABLE"
upload "$STAGE/release.json" "releases/$VERSION/release.json" application/json "$IMMUTABLE"
LATEST='public, max-age=300'
upload "$STAGE/$APP_FILE" releases/latest/plugview-macos-universal-app.zip application/zip "$LATEST"
upload "$STAGE/$COMMAND_FILE" releases/latest/plugview-macos-universal.tar.gz application/gzip "$LATEST"
# Last, so the feed never points at files that are not there yet.
upload "$STAGE/release.json" releases/latest.json application/json "$LATEST"
aws cloudfront create-invalidation --distribution-id "$DISTRIBUTION_ID" --paths '/releases/latest.json' '/releases/latest/*' \
  --query 'Invalidation.Id' --output text >/dev/null

git tag -a "v$VERSION" -m "plugview $VERSION"
# The keychain may hold a stale CodeCommit password that would answer first; use the AWS helper only.
git -c credential.helper= -c 'credential.helper=!aws codecommit credential-helper $@' push "$TAG_REMOTE" "v$VERSION"

echo
echo "Published $SITE/releases/latest.json -> $VERSION"
echo "Next: redeploy the site (plugview-site ./deploy.sh) so its download links point at $VERSION."
