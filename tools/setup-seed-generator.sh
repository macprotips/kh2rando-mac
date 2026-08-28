#!/bin/bash
# Installs the official KH2 Randomizer seed generator (tommadness/KH2Randomizer)
# to run natively on macOS, and creates a double-clickable launcher.
#
# The generator is a Python/Qt app. Its releases ship as a Windows .exe and Linux
# AppImage only, and the source repo doesn't include the required extracted_data.zip
# (it's bundled inside the release executables). This script runs the generator from
# source and recovers extracted_data.zip from the official Windows release using
# pyinstxtractor (https://github.com/extremecoders-re/pyinstxtractor), credit to
# extremecoders-re for that tool.
set -euo pipefail

GENERATOR_VERSION="v3.3.1"
DEST="${1:-$HOME/KH2SeedGenerator}"
# Self-contained Python (no Homebrew, no admin password), from
# https://github.com/astral-sh/python-build-standalone
PYTHON_RELEASE="20260814"
PYTHON_BUILD="cpython-3.12.14"
# pyinstxtractor commit to run (see the note at its download below)
PYINSTXTRACTOR_COMMIT="815d31cf26bc71e62f851b2e549452e7b7c9dd98"

say() { printf '\n==> %s\n' "$*"; }

if [ -e "$DEST/localUI.py" ]; then
  say "Generator already installed at $DEST, updating launcher only."
else
  say "Downloading seed generator $GENERATOR_VERSION source..."
  # curl + tar instead of git: /usr/bin/git on a fresh Mac is a stub that pops
  # Apple's "Install Command Line Tools?" dialog, and this script must run on a
  # Mac with no developer tools at all.
  mkdir -p "$DEST"
  curl -fsSL "https://github.com/tommadness/KH2Randomizer/archive/refs/tags/$GENERATOR_VERSION.tar.gz" \
    | tar -xz -C "$DEST" --strip-components 1

  PYBIN="$DEST/python/bin/python3.12"
  if [ ! -x "$PYBIN" ]; then
    case "$(uname -m)" in
      arm64) PYARCH="aarch64-apple-darwin" ;;
      *)     PYARCH="x86_64-apple-darwin" ;;
    esac
    say "Downloading a private copy of Python (24 MB, used only by the generator)..."
    curl -sL -o "$DEST/python.tar.gz" \
      "https://github.com/astral-sh/python-build-standalone/releases/download/$PYTHON_RELEASE/$PYTHON_BUILD+$PYTHON_RELEASE-$PYARCH-install_only.tar.gz"
    tar -xzf "$DEST/python.tar.gz" -C "$DEST"
    rm -f "$DEST/python.tar.gz"
  fi

  say "Installing Python dependencies (a few minutes)..."
  "$PYBIN" -m venv "$DEST/.venv"
  "$DEST/.venv/bin/pip" install -q -r "$DEST/requirements.txt"

  say "Recovering extracted_data.zip from the official $GENERATOR_VERSION release..."
  WORK="$(mktemp -d)"
  trap 'rm -rf "$WORK"' EXIT
  curl -sL -o "$WORK/KH2Randomizer.exe" \
    "https://github.com/tommadness/KH2Randomizer/releases/download/$GENERATOR_VERSION/KH2.Randomizer.exe"
  # Pinned to a commit, not a branch: this script runs natively on the user's Mac,
  # so "whatever is on master today" is not an acceptable input.
  curl -sL -o "$WORK/pyinstxtractor.py" \
    "https://raw.githubusercontent.com/extremecoders-re/pyinstxtractor/$PYINSTXTRACTOR_COMMIT/pyinstxtractor.py"
  (cd "$WORK" && "$PYBIN" pyinstxtractor.py KH2Randomizer.exe >/dev/null 2>&1)
  if [ ! -f "$WORK/KH2Randomizer.exe_extracted/extracted_data.zip" ]; then
    echo "Could not recover extracted_data.zip, please report this."
    exit 1
  fi
  cp "$WORK/KH2Randomizer.exe_extracted/extracted_data.zip" "$DEST/"
fi

say "Building the KH2 Seed Generator app (a couple of minutes)..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cp "$SCRIPT_DIR/seedgen/KH2 Randomizer macOS.spec" "$DEST/"
cp "$SCRIPT_DIR/seedgen/pyi_rth_maccwd.py" "$DEST/"

# App icon from the generator's own icon asset.
(
  cd "$DEST"
  # macOS wants a 1x and a 2x image per slot, and only accepts these exact names;
  # anything else (a 64x64, say) is silently dropped. Without the 2x images the
  # system upscales at render time and the icon looks soft beside other apps.
  rm -rf macicon.iconset && mkdir -p macicon.iconset
  for s in 16 32 128 256 512; do
    sips -z "$s" "$s" Module/icon.png --out "macicon.iconset/icon_${s}x${s}.png" >/dev/null
    sips -z "$((s * 2))" "$((s * 2))" Module/icon.png --out "macicon.iconset/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns macicon.iconset -o macicon.icns
  .venv/bin/pyinstaller --noconfirm "KH2 Randomizer macOS.spec" >/dev/null
)

rm -rf "$HOME/Desktop/KH2 Seed Generator.app"
cp -R "$DEST/dist/KH2 Seed Generator.app" "$HOME/Desktop/"

say "Done. 'KH2 Seed Generator' is on your Desktop, double-click to open."
say "First launch may take a few seconds while it unpacks its data."
