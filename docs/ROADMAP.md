# Roadmap

## Phase 1: Prove it (done)

- [x] Pilot machine: KH 1.5+2.5 installed in CrossOver (Steam version)
- [x] Setup, extract, and build with GoA ROM Edition
- [x] Game boots into GoA's modded start room (Panacea confirmed working under CrossOver)
- [x] Full randomizer seed generated with the native seed generator, installed, built,
      and playing in-game
- [ ] Soft reset in-game (confirms LuaBackend)

## Phase 2: Beta

- [x] Publish to GitHub with CI
- [ ] Fix whatever the pilot machine shakes out
- [ ] Recruit a few Mac testers from the KH2 Rando community Discord
- [ ] Test matrix: Steam and EGS, internal and external drive, CrossOver and
      CrossOver Preview, Apple Silicon (Intel is not supported)
- [ ] Better in-app guidance when the bottle is running during setup

## Phase 3: Public release

- [x] Apple Developer ID signing and notarization (packaging/notarize.sh)
- [x] App icon
- [x] Seed generator installer built into the app
- [ ] Submit the setup guide to the community docs (KHOmega/KH-Mods-Setup and/or the
      official KH2Randomizer site) as the supported Mac path
- [ ] Contribute the macOS seed generator build recipe upstream to
      tommadness/KH2Randomizer
- [ ] Auto-update check (compare against the latest GitHub release tag)

## Non-goals for now

- Games other than KH2 (the libraries support them; the UI does not)
- PCSX2/emulator workflows; this is for the PC release only
- Windows/Linux builds; Windows has the Mods Manager and Linux has working guides
