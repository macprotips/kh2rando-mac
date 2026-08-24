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

say() { printf '\n==> %s\n' "$*"; }

if [ -e "$DEST/localUI.py" ]; then
  say "Generator already installed at $DEST, updating launcher only."
else
  say "Downloading seed generator $GENERATOR_VERSION source..."
  git clone -q --depth 1 --branch "$GENERATOR_VERSION" \
    https://github.com/tommadness/KH2Randomizer "$DEST"

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
  curl -sL -o "$WORK/pyinstxtractor.py" \
    "https://raw.githubusercontent.com/extremecoders-re/pyinstxtractor/master/pyinstxtractor.py"
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
  rm -rf macicon.iconset && mkdir -p macicon.iconset
  for s in 16 32 64 128 256 512; do
    sips -z $s $s Module/icon.png --out "macicon.iconset/icon_${s}x${s}.png" >/dev/null
  done
  iconutil -c icns macicon.iconset -o macicon.icns
  .venv/bin/pyinstaller --noconfirm "KH2 Randomizer macOS.spec" >/dev/null
)

rm -rf "$HOME/Desktop/KH2 Seed Generator.app"
cp -R "$DEST/dist/KH2 Seed Generator.app" "$HOME/Desktop/"

say "Done. 'KH2 Seed Generator' is on your Desktop, double-click to open."
say "First launch may take a few seconds while it unpacks its data."
