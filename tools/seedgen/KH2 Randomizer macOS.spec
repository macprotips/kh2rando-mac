# -*- mode: python ; coding: utf-8 -*-

# macOS build: a windowed .app bundle. Mirrors the Linux spec's data collection,
# with a runtime hook that chdirs to ~/Library/Application Support/KH2SeedGenerator
# so the first-launch extracted_data unpack (which targets the working directory)
# lands somewhere writable instead of wherever Finder launched us.

import glob
import os

from PyInstaller.utils.hooks import collect_data_files


def build_datas_recursive(paths):
  datas = []
  for path in paths:
    for filename in glob.iglob(path, recursive=True):
      if os.path.isfile(filename):
        dest_dirname = os.path.dirname(filename)
        if dest_dirname == "":
          dest_dirname = "."
        datas.append((filename, dest_dirname))
  return datas


a = Analysis(
    ['localUI.py'],
    pathex=[],
    binaries=[],
    datas=build_datas_recursive([
        'UI/**/*.*',
        'UI/*.*',
        'static/**/*.*',
        'static/*.*',
        'presets/*.*',
        'Module/icon.png',
        'extracted_data.zip',
       ]) + collect_data_files('kh2fmbr', include_py_files=False),
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=['pyi_rth_maccwd.py'],
    excludes=[],
    noarchive=False,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='KH2 Seed Generator',
    debug=False,
    strip=False,
    upx=False,
    console=False,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=False,
    name='KH2 Seed Generator',
)

app = BUNDLE(
    coll,
    name='KH2 Seed Generator.app',
    icon='macicon.icns',
    bundle_identifier='dev.kh2rando.mac.seedgen',
    info_plist={
        'CFBundleShortVersionString': '3.3.1',
        'NSHighResolutionCapable': True,
    },
)
