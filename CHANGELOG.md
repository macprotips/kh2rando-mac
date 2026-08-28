# Changelog

## Unreleased

- A Bottle picker in the status header, shown when the machine has more than one.
  Switching sets the mod loader and runtimes up in the chosen bottle as part of the
  switch, so there is no second step. When the bottle being left has already been
  set up, the confirmation says so and points out that Reset only ever acts on the
  bottle in use, so returning the old one to stock has to happen before switching.
  Bottles are re-read on every refresh, so one made or deleted in CrossOver while
  the app is open appears or goes away without a restart. Switching refuses up
  front if something is using the target bottle, and says so if that bottle does
  not have the game in its library and therefore could not launch it
- Setup no longer collapses one game folder seen from several bottles into a
  single choice. A game on an external drive is commonly visible from all of
  them, and picking one silently decided which bottle got modded
- Reordering a mod that is switched off now sticks. The move buttons acted on any
  row but only the enabled mods had their places recorded, so the list snapped back
  on the next refresh. A companion file records where every mod sits, leaving
  OpenKH's mods-KH2.txt exactly as it was, and export and import carry it
- Switching a mod off and on again returns it to where it was instead of jumping it
  to the top. A mod the order has never seen still goes to the top
- Build no longer destroys the previous build before it starts. It builds alongside
  and swaps at the end, so a build that fails part way leaves the working mods in
  place instead of nothing at all
- Switching bottles takes the store from the bottle being switched to, rather than
  carrying the old setting over and calling an Epic copy Steam
- A progress strip above the log shows what Setup is doing and how far along it
  is. Downloads report real percentages; steps that cannot say, such as installers
  running inside the bottle, show an indeterminate bar rather than a made-up
  number. Extraction drives it too
- Fixed settings being lost when two copies of the app saved at the same moment.
  Both wrote to one staging file, so one could truncate the other's half-written
  text and move the wreckage into place; the config then failed to load and was
  set aside for defaults

- Setup also installs the .NET 8 Desktop Runtime Re:Fined needs, so a Build with
  Re:Fined enabled never stops to install anything
- Dropped the Reset note from the README and setup guide

## 0.3.2 (2026-08-28)

- Setup now installs the .NET Framework the item tracker needs, so the Tracker
  button works straight away instead of meeting a multi-minute install the first
  time it is pressed. Setup already requires a quiet bottle, which that install
  also needs, so it costs no new restriction. A failure there is a warning only;
  modding is unaffected and Tracker still installs on demand
- A Change Folder button on the Game row points the app at a different copy of
  the game, which previously took deleting the config by hand
- Setup asks which copy to use when it finds more than one, instead of taking
  the first and mentioning the others in the log
- A hand-picked folder now works out for itself whether it is a Steam or Epic
  copy, rather than always being recorded as Steam
- Build & Run now goes straight into KH2 instead of leaving you on the
  collection's game-select menu, using the mod loader's own quick-launch rather
  than starting the game's exe behind Steam's back
- The seed generator gets a Retina icon; it was built at single resolution and
  looked soft next to other apps

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
