# Changelog

## 0.2.0 (2026-08-25)

- Tracker button: installs and opens the community KH2 item tracker
  (Dee-Ayy/KH2Tracker) inside the game's bottle, with auto-tracking. The first
  install also puts .NET Framework 4.8 into the bottle (one time, 15 to 30
  minutes)
- FPS HUD toggle: per-bottle Metal Performance HUD on or off

## 0.1.0 (2026-08-24)

First release. Verified end to end on one machine (Apple Silicon, Steam version,
CrossOver): setup, game data extraction, GoA ROM Edition, generated seed, playing
in-game.

- KH2 Rando Manager (macOS app): guided setup, game data extraction, one-click GoA
  install, mod install from GitHub, zip, .kh2pcpatch, or standalone .lua (file picker,
  window drag and drop, or dock-icon drop), load-order management, mod updates, build,
  launch via CrossOver
- kh2rando CLI with the same features
- Automatic CrossOver integration: bottle discovery (including CrossOver Preview and
  relocated bottle directories), mac to Windows path translation, DLL-override
  configuration with registry backup, running-bottle detection
- Sikarugir wrapper support: wrappers are discovered and configured the same way
  (untested so far)
- Panacea and LuaBackend installed from official upstream releases at setup time
- Movies toggle: reversible cutscene skip (movies crash the game under CrossOver)
- Reset: returns the game and bottle to vanilla, keeping mods and extracted data
- Stale-extraction detection when a game update lands
- Seed Generator button installs the official generator to run natively
  (tools/setup-seed-generator.sh does the same from the command line)
- 42 automated tests; CI builds and tests every push
- Releases are signed and notarized (packaging/release.sh)
