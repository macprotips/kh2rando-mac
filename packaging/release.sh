#!/bin/bash
# Builds, signs, notarizes, and uploads a full release (both architectures).
# Usage: packaging/release.sh vX.Y.Z
#
# Prereqs (one-time, documented in docs/DEVELOPMENT.md):
#   - arm64 .NET 8 SDK at ~/.dotnet, x64 SDK at ~/.dotnet-x64 (via dotnet-install.sh
#     --architecture x64; requires Rosetta, GitHub retired Intel macOS runners, so
#     Intel is built locally through Rosetta)
#   - "Developer ID Application" certificate in the keychain
#   - notarytool keychain profile "kh2rando-notary" (xcrun notarytool store-credentials)
#   - gh CLI authenticated to the repo
set -euo pipefail

VERSION="${1:?usage: release.sh vX.Y.Z}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

SIGN_IDENTITY="${SIGN_IDENTITY:-$(security find-identity -v -p codesigning | grep -o '"Developer ID Application:[^"]*"' | head -1 | tr -d '"')}"
[ -n "$SIGN_IDENTITY" ] || { echo "No Developer ID Application certificate found."; exit 1; }
echo "Signing as: $SIGN_IDENTITY"

# notarytool can crash mid-upload; the submission usually lands anyway. Retry and
# recover by waiting on the newest submission in the history.
notarize_zip() {
  local zip="$1"
  for attempt in 1 2 3; do
    set +e
    local out; out="$(xcrun notarytool submit "$zip" --keychain-profile kh2rando-notary --wait 2>&1)"
    set -e
    echo "$out" | tail -3
    echo "$out" | grep -q "status: Accepted" && return 0
    echo "$out" | grep -q "status: Invalid" && return 1
    local rec; rec="$(xcrun notarytool history --keychain-profile kh2rando-notary 2>/dev/null | grep -m1 'id:' | awk '{print $2}')"
    if [ -n "$rec" ]; then
      set +e
      local waited; waited="$(xcrun notarytool wait "$rec" --keychain-profile kh2rando-notary 2>&1)"
      set -e
      echo "$waited" | tail -2
      echo "$waited" | grep -q "status: Accepted" && return 0
    fi
    sleep 10
  done
  return 1
}

upload_with_retry() {
  until gh release upload "$VERSION" "$@" --clobber 2>/dev/null; do
    echo "upload failed, retrying in 15s..."
    sleep 15
  done
}

OUT="dist/release-$VERSION"
rm -rf "$OUT" && mkdir -p "$OUT"

build_arch() {
  local rid="$1" dotnet="$2"
  echo "==> Building $rid..."
  find OpenKh src -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true
  DOTNET_ROOT="$(dirname "$dotnet")" "$dotnet" test src/Kh2RandoMac.Tests -c Release
  DOTNET_ROOT="$(dirname "$dotnet")" "$dotnet" publish src/Kh2RandoMac.Cli -c Release -r "$rid" \
    --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUT/$rid/cli"
  DOTNET_ROOT="$(dirname "$dotnet")" "$dotnet" publish src/Kh2RandoMac.Gui -c Release -r "$rid" \
    --self-contained -o "$OUT/$rid/gui"

  SIGN_IDENTITY="$SIGN_IDENTITY" bash packaging/make-app.sh "$OUT/$rid/gui" "$OUT/$rid"
  codesign --force --options runtime --timestamp \
    --entitlements packaging/entitlements.plist -s "$SIGN_IDENTITY" "$OUT/$rid/cli/kh2rando"

  bash packaging/notarize.sh "$OUT/$rid/KH2 Rando Manager.app"
  ditto -c -k --keepParent "$OUT/$rid/KH2 Rando Manager.app" "$OUT/KH2-Rando-Manager-$VERSION-$rid.zip"

  ditto -c -k "$OUT/$rid/cli" "$OUT/kh2rando-cli-$VERSION-$rid.zip"
  echo "==> Notarizing $rid CLI..."
  notarize_zip "$OUT/kh2rando-cli-$VERSION-$rid.zip"
}

build_arch osx-arm64 "$HOME/.dotnet/dotnet"
if [ -x "$HOME/.dotnet-x64/dotnet" ]; then
  build_arch osx-x64 "$HOME/.dotnet-x64/dotnet"
else
  echo "WARNING: ~/.dotnet-x64 not found, skipping Intel build."
fi

echo "==> Uploading to GitHub release $VERSION..."
if gh release view "$VERSION" >/dev/null 2>&1; then
  upload_with_retry "$OUT"/*.zip
else
  gh release create "$VERSION" "$OUT"/*.zip --title "$VERSION" --draft
fi

echo "==> Done. Review and publish the release:"
echo "    gh release edit $VERSION --draft=false"
