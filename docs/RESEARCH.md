# How the mod stack works

Notes on the KH2 Randomizer mod stack and what this port changes. Sources: the OpenKH
source (`OpenKh/` in this repo), the
[official setup guide](https://tommadness.github.io/KH2Randomizer/setup/Panacea-ModLoader/),
and the community [Linux guides](https://codeberg.org/KHOmega/KH-Mods-Setup).

## The four components

1. Seed generator (tommadness/KH2Randomizer). Python/Qt app. Produces a zip that is
   just an OpenKH mod (mod.yml plus assets). Runs natively on macOS from source, which
   is how our installer script sets it up.

2. OpenKH Mods Manager. A .NET 8 WPF app (`net8.0-windows`), the only genuinely
   Windows-locked piece of the stack. Its engine is plain `net8.0` and runs unmodified
   on macOS:
   - `OpenKh.Patcher` merges enabled mods over extracted game data (the "Build" step)
   - `OpenKh.Egs` decrypts and extracts the PC release `.hed`/`.pkg` archives
   - LibGit2Sharp for installing mods from GitHub

   This port reimplements only the orchestration and UI around those libraries.

3. Panacea (`OpenKh.Research.Panacea`, C++). The actual runtime mod loader. A Windows
   DLL installed into the game folder under a hijack name (`DBGHELP.dll` on real
   Windows, `version.dll` under Wine/CrossOver) plus a `dependencies/` folder of 13
   audio DLLs and `panacea_settings.txt` (`mod_path=...`). It intercepts the game's
   file loads and redirects them into the built `mod/` folder. Because it runs inside
   the game process, it works under CrossOver exactly as well as the game does.
   Prebuilt in the official `openkh.zip` release asset.

4. LuaBackend (Sirius902/LuaBackend). Lua scripting host the randomizer needs. The
   release zip contains `DBGHELP.dll` (renamed to `LuaBackend.dll`; Panacea
   chain-loads it) and `LuaBackend.toml`, which needs the built mod `scripts/` path
   added to its `[kh2]` scripts list and, for Steam installs, the `game_docs` line
   swapped to the `My Games/` variant.

## Why the stock setup fails on Mac

The Mods Manager needs the .NET Desktop Runtime (WPF) inside the bottle, and WPF under
Wine is unreliable. The community "refined-mac-setup" guide attempts exactly that and
is flagged untested and unsupported by its own authors. Everything the Mods Manager
actually does is either native file manipulation or one-time bottle configuration,
which is what made this port practical.

## Wine/CrossOver specifics

- The game only loads the hijack DLLs with the overrides
  `version, dinput8, LuaBackend = native,builtin`. On Linux this is done per launch
  with `WINEDLLOVERRIDES=... %command%` in Steam launch options. On CrossOver we write
  it once into the bottle registry (`user.reg`, key `[Software\\Wine\\DllOverrides]`),
  so any launch method works. The file must be edited while the bottle is stopped
  because wineserver rewrites it on shutdown.
- Paths crossing into the bottle (in `panacea_settings.txt` and `LuaBackend.toml`)
  must be Windows paths as the bottle sees them. CrossOver bottles map `Y:` to `$HOME`
  and `Z:` to `/` (see the `dosdevices/` symlinks), so `~/KH2 Rando/mod` becomes
  `Y:\KH2 Rando\mod`. The port resolves the mapping per bottle from the symlinks,
  preferring the most specific drive.
- Steam library folders (including ones on other drives) are found by parsing
  `steamapps/libraryfolders.vdf` and translating drive letters back to mac paths.
- A running bottle is detected through the wineserver socket
  (`/tmp/.wine-{uid}/server-{dev}-{inode}/socket`, keyed by the bottle directory).
- Launch: `cxstart --bottle <name> steam://rungameid/2552430`.

## Verification status

The full chain (setup, extraction, GoA build, generated seed, in-game play) has been
verified on one machine: Apple Silicon, Steam version, CrossOver. Not yet verified:
Epic installs, Intel Macs, other people's machines. See the roadmap.
