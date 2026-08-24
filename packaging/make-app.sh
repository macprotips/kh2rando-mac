#!/bin/bash
# Assembles "KH2 Rando Manager.app" from a dotnet publish output directory.
# Usage: packaging/make-app.sh <publish-dir> <output-dir>
set -euo pipefail

PUBLISH_DIR="${1:?usage: make-app.sh <publish-dir> <output-dir>}"
OUT_DIR="${2:?usage: make-app.sh <publish-dir> <output-dir>}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$OUT_DIR/KH2 Rando Manager.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/seedgen-setup"
cp -R "$PUBLISH_DIR/." "$APP/Contents/MacOS/"
cp "$REPO_ROOT/packaging/Info.plist" "$APP/Contents/Info.plist"
cp "$REPO_ROOT/packaging/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
cp "$REPO_ROOT/tools/setup-seed-generator.sh" "$APP/Contents/Resources/seedgen-setup/"
cp -R "$REPO_ROOT/tools/seedgen" "$APP/Contents/Resources/seedgen-setup/seedgen"
echo "APPL????" > "$APP/Contents/PkgInfo"

# SIGN_IDENTITY: a "Developer ID Application" identity for real distribution
# (hardened runtime + timestamp, ready for notarization); unset = ad-hoc for dev builds.
if [ -n "${SIGN_IDENTITY:-}" ]; then
  codesign --force --deep --options runtime --timestamp \
    --entitlements "$REPO_ROOT/packaging/entitlements.plist" \
    -s "$SIGN_IDENTITY" "$APP"
else
  codesign --force --deep -s - "$APP"
fi
echo "Assembled: $APP"
