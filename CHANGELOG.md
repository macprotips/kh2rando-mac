# Changelog

## 0.3.3 (2026-08-28)

Everything from 0.3.2, which was built but never published.

### Added

- **The app notices when the game is pointed at mods that have moved** — the "my
  mods stopped working after I moved my folders" trap. The Mod loader row says
  which recorded path is stale, Build warns again before you play, and Run Setup
  fixes it. Moving the files from inside the app re-points the game by itself.
- **Reset can also delete the extracted game data**, offered as a choice with the
  real size shown and a second confirmation. Mods and seeds are never touched.
- **Build & Run goes straight into KH2**, instead of leaving you on the collection's
  game-select menu.
- **Bottle picker.** Choose which CrossOver bottle the game is modded in. Switching
  sets that bottle up as part of the move, and says what the old one keeps.
- **Files row.** Shows where mods, extracted data and builds are kept, with a button
  to move them to another disk.
- **Extract asks where the game data should go**, showing free space, and refuses
  when there is not room for it.
- **Progress bar** for Setup, downloads and extraction.
- **Sizes** for each mod and for the files folder as a whole.
- **Change Folder** for the game itself, for pointing at a different copy. Setup also
  asks which to use when it finds more than one.
- Setup installs the .NET runtimes the item tracker and Re:Fined need, so neither
  interrupts you later.

### Fixed

- **Crash when launching the game.** The window was drawn through OpenGL, which
  segfaulted as the game took over the screen. It now draws on the CPU.
- **The item tracker becoming unresponsive** after a while. Its output was routed
  through this app and blocked once nothing was reading it.
- **Settings lost when two copies of the app ran together.** Only one copy runs now;
  a second says so and closes.
- **Re:Fined could not be reinstalled** after an interrupted download.
- **Reordering a mod that was switched off** did not stick, and switching a mod off
  and on again moved it to the top.
- **A failed build destroyed the previous one.** It now builds alongside and swaps at
  the end.
- Mods dropped on the window during another operation were discarded silently.
- The FPS HUD toggle was discarded silently when the bottle was in use, and Reset
  left the HUD switched on.
- The bottle registry, CrossOver's bottle config and this app's settings are written
  so an interrupted write cannot truncate them.
- Switching bottles kept the old store setting, so an Epic copy could be treated as
  Steam.

### Changed

- Mods with a known problem are flagged on their own row, and **Movies: On** is
  marked, since cutscenes crash the game under CrossOver.
- The seed generator installs alongside everything else in the files folder, and has
  a sharper icon.
- Status header laid out in rows, with the paths given full width. The
  change-folder buttons are small folder icons beside the paths they act on.
- The mod list and the log can be resized against each other, and the window remembers
  its size and that split between launches. The log previously held a fixed height, so
  shrinking the window came out of the mod list alone.

## 0.3.1 (2026-08-28)

- Fixed the app reporting a bottle as running when it was not, which left
  "Quit Steam and the game first" on screen no matter how thoroughly you had
  quit them, and blocked the tracker and Re:Fined installs
- Installs that need the bottle to themselves now say so before asking rather
  than after, so a refusal does not look like the button doing nothing
- Messages about a busy bottle name the item tracker when it is the tracker
  holding it open. It runs inside the bottle and this app starts it, so it was
  the likeliest cause of being told to quit things you had already quit
- Skipping or restoring movies refuses while the game is running, since it
  renames a folder the game has open, and Build says so if the game is running
  while it replaces the mods being read

## 0.3.0 (2026-08-27)

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
