# Development

## Setup

```bash
git clone --recurse-submodules https://github.com/macprotips/kh2rando-mac
cd kh2rando-mac
dotnet test src/Kh2RandoMac.Tests          # no game or CrossOver needed
dotnet build src/Kh2RandoMac.Gui -c Release
```

Requires the .NET 8 SDK. If you cloned without submodules:
`git submodule update --init --recursive` (OpenKh has its own nested submodules).

## Architecture

```
src/Kh2RandoMac.Core   engine: everything the app does, UI-free
src/Kh2RandoMac.Cli    thin command-line front end (kh2rando)
src/Kh2RandoMac.Gui    Avalonia desktop app (KH2 Rando Manager)
src/Kh2RandoMac.Tests  xUnit tests for Core
OpenKh/                upstream OpenKH, pinned git submodule (Apache-2.0)
packaging/             app bundle assembly, signing, notarization, release scripts
```

Core reuses OpenKH's cross-platform libraries (`OpenKh.Patcher` for mod building,
`OpenKh.Egs` for game archive extraction) and adds the Mac/CrossOver layer:

- `Bottle`: CrossOver bottle discovery, drive-letter to mac path translation via
  `dosdevices` symlinks, and DLL-override editing in `user.reg`. **This is the only code
  that writes into a user's bottle configuration. Treat every change as dangerous,
  keep it covered by `UserRegTests`, and never edit while the bottle runs.**
- `CrossOverApp`: locates CrossOver/CrossOver Preview and the bottles root
- `GameLocator`: finds KH 1.5+2.5 through Steam library vdf parsing and EGS heuristics
- `PanaceaService` / `LuaBackendService`: download official release payloads and
  install the Windows DLLs + config files into the game folder
- `ExtractionService`: native port of the Mods Manager PC extraction (KH2 only)
- `PatchBuilder`: native equivalent of the Mods Manager "Build" step
- `Workspace` / `ModsService`: Mods-Manager-compatible on-disk layout
  (`mods/kh2/...`, `mod/kh2/`, `data/kh2/`, `mods-KH2.txt`)

## Conventions

- All paths that cross into the bottle must go through `Bottle.ToWindowsPath`;
  never hand-build a `Y:\...` string.
- Anything that could brick a setup gets a test before it ships.
- Mod names and zip contents are untrusted input (traversal-guarded); keep it that way.
- Diagnostics go through `FileLog` (`~/Library/Logs/kh2rando-mac.log`).

## Releasing

Releases are built, signed, and notarized locally (CI can't hold the signing
certificate, and GitHub retired Intel macOS runners):

```bash
git tag vX.Y.Z && git push origin vX.Y.Z
packaging/release.sh vX.Y.Z        # build both arches, sign, notarize, upload
gh release edit vX.Y.Z --draft=false
```

One-time machine setup for releasing: arm64 .NET 8 SDK at `~/.dotnet`, x64 SDK at
`~/.dotnet-x64` (dotnet-install.sh `--architecture x64`, needs Rosetta), a
"Developer ID Application" certificate in the keychain, and a notarytool keychain
profile named `kh2rando-notary`. CI (`ci.yml`) still builds and tests every push.
