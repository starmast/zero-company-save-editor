# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.4.0] - 2026-10-10

### Changed
- **One native desktop application.** The Python/Flask web editor and the separate console extractor are replaced by a
  single C# (.NET 10) application with an [Avalonia](https://avaloniaui.net/) interface: no Python, virtual environment,
  browser or CDN access is needed any more, and it ships as one self-contained file per platform.
- **Runs on Windows, Linux and macOS.** Save folders are discovered per OS (including Steam/Heroic/Lutris Wine prefixes on
  Linux) and any folder can be added from the app. Data (backups, game data, settings) lives in the per-user data folder
  instead of next to the source.
- **The extractor is built in.** The new **Game data** tab extracts your own game install straight into a small typed
  database (items, effects, upgrade recipes, focus costs, Coil text and names) instead of large raw JSON dumps, shows the
  progress and log, and can import a database produced on another machine (needed on macOS).
- Operator portraits are decoded in the app (OpenEXR, DWAA-compressed) and cached.
- Same screens and behaviour as 0.3.0 (Command, Personnel, Armory, Upgrades, Medbay, Galaxy, Advanced), with the same
  edit pipeline: pending changes, backups, atomic writes and post-write verification.

### Verified
- The C# core was checked against the Python editor on the same save: identical fields and screen views, and
  byte-identical files for every scripted edit (scalar edits, linked focus, start/expedite upgrades, Bring back, focus
  tree completion, roster reorder and Coil removal).

### Fixed
- The Advanced raw tree could address the wrong node when a property and its first child started at the same byte.

### Removed
- The local web server, the browser UI and the Python code. They remain in git history under the v0.1.0 to v0.3.0 tags.

## [0.3.0] - 2026-10-07

### Added
- **Complete focus tree**: an operator whose abilities only have their first tier saved (the tutorial operator, Aurelio)
  gets the missing tier records copied from another operator with the same ability (the lists are identical for
  everyone), so the game can show and level them. Offered on the Focus Tree tab, and done automatically by Bring back.
- **Bring back** a fallen operator from the Personnel screen: removes them from the fallen list and the
  death records, appends them to the roster with the current turn as their recruited turn, and removes their death and
  injury effects. Bonds with operators recruited after their death are not created and the permanent health
  progression is not added. Tested in the game with the tutorial operator: they appear on the roster, in the Personnel
  screens and in the Den, and can be taken on missions.
- **Roster order**: hold and drag an operator's icon on the Personnel strip (or use Earlier / Later) to move them. The roster is a list of operator ids that
  the game's Personnel strip and mission select follow (confirmed in the game); the edit swaps those fixed-size entries in
  place, so nothing else in the save changes, and it is verified to be exactly the requested order.

## [0.2.0] - 2026-10-06

### Added
- **Medbay** screen: beds, bacta tank, costs and injured operators, read from the save. Injured operators get a marker on
  the Personnel roster strip.
- **Heal** injured operators from the Medbay. It removes the operator's `GE_Injured` effect exactly as the game does when
  it uses the bacta tank (verified byte-for-byte against a before/after pair of saves written by the game), and keeps
  the character sizes in the save's metadata consistent. It is instant and free; it does not charge credits or the
  tank charge, and does not add the game's treatment history entry.
- **Coil upgrades** on the Galaxy screen: lists the permanent enemy upgrades (name, unit, description as in the game's
  Active Coil Upgrades panel) and takes any or all of them away, either as **Remove** (the crisis's `.Selected` fact tag
  goes back to `.Available`, so it can be failed again) or **Prevent** (`.Prevented`, as if the crisis had been won).
  Both are renames the game itself writes; no other tag changes.

### Fixed
- Restoring a backup while the game appears to be running no longer dead-ends: the editor asks for confirmation.

## [0.1.0] - 2026-10-05

First public release. Tested with saves from game build `++ProjectBruno+Stable` changelist 197649 (Unreal Engine 5.6.1).
The test suite runs in CI on Python 3.10-3.14. A game update may change the save format.

### Added
- Local Flask web app that opens Star Wars: Zero Company saves and edits them in place, laid out like the game:
  **Command**, **Personnel** (Overview, Bonds, Focus Tree), **Upgrades**, **Armory**, **Galaxy**, plus **Advanced**
  (flat field list and raw property tree).
- Safety: untouched original kept per save, snapshot before every write (last 20), atomic verified writes, restore
  from the UI, refusal to write while the game runs or if the file changed on disk, "Save as copy".
- Offset-preserving GVAS reader/patcher: unknown data is preserved byte-for-byte.
- Base upgrades: start (matches the game's own save output byte-for-byte) and expedite, with the metadata sizes
  kept consistent.
- Focus Tree levelling that follows the game's own cost table, either spending unspent focus or granting it.
- Real operator portraits and save screenshots read from the saves themselves.
- Optional .NET extractor (`tools/extract`) that reads the player's own installation for names, descriptions, upgrade
  costs and Den Level requirements. Nothing from the game is included in this repository.
- MIT license, disclaimer, CI (Python 3.10-3.14 and an extractor build) and issue templates.

### Known limitations
- Windows only. No Medbay/injury editing, no adding items or abilities, no computed combat stats.
