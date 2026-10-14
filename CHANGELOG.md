# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Galaxy, Missions tab: each available mission also has Shorter / Longer / Full time for how long it stays on offer (it is counted from
  the cycle it was added on), and a crisis mission has a drop-down for which Coil upgrade it is about. Neither is tried in the game yet.
  The mission and operation lists no longer show the game's description, summary, objective or options text (the game shows those; the
  names are there to identify each entry).
- Galaxy: the Operations tab lists the Holotable's operations by name, with their description, options and how many cycles each has
  left before it expires. Shorter, Longer and Full time change that (the save counts the cycles an operation has been on offer; the
  game removes it at its limit). Not yet tried in the game. Re-extract the game data for names and limits; `zcassets operations` prints them.
- Galaxy: split into Influence, Operations, Missions and Coil upgrades tabs (Operations only explains that they are not editable yet). The
  Missions tab now shows each mission's in-game name, place, difficulty, the faction it normally fights, how long it lasts, its summary
  and objective. These come from the game's mission assets, so re-extract the game data (it adds a `Missions` list; without it the list
  falls back to name strings and readable asset names). `zcassets missions [--json]` prints them.
- Galaxy: a "Missions in the Den" list for saves taken at the Den. Each available mission has a drop-down for the enemy faction and threat
  tier it will spawn (its SelectedSpawnSet), which is the one place that choice can be made before a mission starts; the threat tier inside
  a running encounter does nothing. Confirmed in the game: Brentaal 035, changed from Imperial to Coil, launched with Coil units and Coil waves.
- Armory: an Add items tab for utility items and weapon mods you never had (for example the meditation rewards you did not pick). It
  lists what the game defines (with filters for type, mod slot, tier, rarity and mission choice rewards, a sort, and a never-owned / queued-only
  toggle; the game data now records which mission and choice gives each item, so re-extract it) and adds
  each copy as its own inventory entry. Confirmed working in the game.
- Combat: a second hostile team, Enemy2 (for example Imperials fighting beside the Coil), offered only in three-way battles such as
  Belly of the Beast. It is listed under Enemies, can be picked in a unit's team dropdown, and is included by "Enemies to 1 HP".
  Swapping units between Enemy, Enemy2 and Friendly AI is confirmed working in the game. In ordinary missions the game puts Enemy2
  units back on Enemy when it loads the save, so the option is hidden there and the editor refuses to set it.
- Combat: a Scenery tab with the fire zones' turns left and every destructible prop the save tracks (not just the ones already hit). It has
  a search, a condition filter, and Destroy / Restore buttons per prop or for everything the filters show. The save marks a prop Destroyed
  or Damaged separately from its health, so Restore and Destroy rewrite that mark as well as the health. Restore is confirmed working in the game; Destroy is not yet tried.
- Combat: the mission's enemy threat tier (for example Coil threat 10) can be swapped for another known tier on the Reinforcements
  tab (other factions' tiers are offered too, marked experimental). It does nothing for the encounter already started, because the
  enemies and waves in the save are fixed by then (tried on a hero mission); the editor counts the later encounters and says so beside
  the drop-down, so the setting can only matter for a mission with an encounter still to come.
- Combat: in a three-way battle the Reinforcements tab can switch the turn structure to standard and back. Trying to add the
  three-way structure to an ordinary mission did not work (the game loaded it as empty), so it is not offered there.

### Security
- The Oodle library that the game-data extractor downloads is checked against a pinned SHA-256 before it is loaded; anything else
  is deleted and refused. A copy you place yourself is not checked.
- Portraits in saves (which may be someone else's) are decoded only as OpenEXR, never by format detection, with a size cap, and
  oversized entries in the portrait archive are skipped.
- Magick.NET updated to 15.0.0.
- The GitHub Actions are pinned to exact commits, and Dependabot now proposes weekly updates for NuGet packages and Actions.

## [0.5.0] - 2026-10-11

### Added
- **Combat tab.** Open a save made during a fight (a combat autosave such as `Autosave_EncounterStart_*.sav`, or a manual save
  made in one) and edit it in sub-tabs. Edits go through the same verified, backed-up apply as the rest of the editor.
  - **Allies, Enemies, Friendly AI:** per unit, health, action points, advantage, position, every other base stat, the dead
    flag, and active effects (remove, change stacks, or copy one from another unit). Quick actions: *Heal allies to full* and
    *Enemies to 1 HP*.
  - **Team** (experimental): a per-unit dropdown (Ally you control, Friendly AI the game controls, Enemy) that switches a unit
    the way the game does when a rescued civilian joins your squad: team tag, friendly flag, the tag inside the unit, its
    player / enemy behaviour, and the game's second copy of that behaviour in the save's spawned-actors file. A unit turned Enemy
    also gets the AI area / faction tags and action-point refresh values of the nearest enemy. Heroes turned Enemy or Friendly AI
    skip their turn, and the editor warns about it.
  - **Objectives:** mark succeeded or set back to active; editable counters (occupy-region points, interactions); countdown
    timers (turns left before an objective fails).
  - **Reinforcements:** queued waves (round, spawn group, *Cancel wave*), the encounter's wave definitions (delay, how often
    they may spawn, group) and triggers (switch off).
  - Not all of it has been tried in the game yet; see [limitations](docs/limitations.md#combat-tab).
- A Combat screenshot in the user guide, and updated screenshots (the navigation bar has a Combat tab).

### Changed
- Inside a combat save a unit's `Transform` is read as a property list, so its position can be edited; elsewhere it is still read
  as before.
- Edits that change a save's size no longer make an extra full copy of the payload, which makes them faster on large saves.

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
