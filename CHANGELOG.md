# Changelog

## 0.3.0 (unreleased)

- Re:Fined support: an Install Re:Fined button, and the .NET runtime it needs is
  installed into the bottle on the first build. Use it on its own for a normal
  playthrough or alongside a randomizer seed
- Export: copies every installed mod and the load order into one folder, for a
  backup or to hand someone your exact setup
- Import: drop an exported folder, or any folder holding a mod, onto the window
  or the dock icon. The load order comes with it, and the previous one is kept
- Move a mod straight to the top or bottom of the load order, and a live count
  of how many mods are installed and enabled
- Warns about mods confirmed to break current versions of the game, on install
  and again at build time
- Works with any CrossOver you have installed, wherever it lives, including
  older releases kept alongside the current one. Copies share bottles and a
  bottle can only be opened by one its own age or newer, so the app matches
  them up and remembers the choice; a picker appears when there is more than
  one to choose from
- The tracker recovers by itself after CrossOver changes version. CrossOver
  sets the bottle up again and reinstates its own .NET, which the tracker
  cannot use; clearing that now happens on the next click and takes seconds
- Log button, which reveals the log file in Finder for bug reports
- Fixed texture and model mods only partially applying: mod definitions written
  on Windows mix backslash and forward-slash paths, and the backslash entries
  were silently skipped on macOS
- The seed generator install no longer triggers the Xcode Command Line Tools
  prompt, so a Mac with no developer tools can use it
- Interface grouped by purpose, with destructive actions set apart; every
  tooltip and log message rewritten
- Corrected the tracker install estimate: the .NET step takes a few minutes,
  not the 15 to 30 previously claimed

## 0.2.1 (2026-08-25)

Tracker fixes proven on a second machine, a fix for texture and model mods,
and easier bug reporting.

- Fixed texture and model mods only partially applying: mod definitions written
  on Windows mix backslash and forward-slash paths, and the backslash entries
  were silently skipped on macOS. All asset paths are now normalized before
  building. (Found via the Roxas mod, where text changed but models did not.)

- Tracker install now works across CrossOver versions: the removal of Wine's
  .NET substitute handles output differences between versions, installed-state
  detection can no longer be fooled by substitute files, and the bottle is
  pinned to the real .NET Framework so the tracker keeps working after
  CrossOver updates
- Repair path: if the tracker crashes on startup, the next click offers a full
  clean reinstall of the bottle's .NET Framework
- Tracker button shows a launching state until the tracker window is actually
  on screen
- Log button: opens Finder with the app's log file highlighted for bug reports;
  a tracker startup crash does this automatically
- The app records its version and tracker diagnostics in the log, and the
  version is shown in the main window

## 0.2.0 (2026-08-25)

- Tracker button: installs and opens the community KH2 item tracker
  (Dee-Ayy/KH2Tracker) inside the game's bottle, with auto-tracking. The first
  install also puts .NET Framework 4.8 into the bottle (one time, a few
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
