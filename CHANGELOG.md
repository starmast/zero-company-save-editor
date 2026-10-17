# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **`zc`, a command-line tool**, and a shared **Zero Company Save SDK** (`src/ZeroCompany.Sdk`) that the editor, `zc` and `zcdebug` now all use for finding the
  game and the saves, so they agree. `zc edit` changes a save without the editor, with no UI needed (so scripts and agents can do it): `fields`, `get`, `set`,
  `do` (heal, add-item, unit-dead ...), `apply` (a JSON batch), `diff`, `backups`, `restore`. It uses the editor's own checked write (backup, refused while the
  game runs, refused if the file changed, verified rebuild) and writes nothing without `--write`. Also `zc doctor` (what is set up and what is missing),
  `zc save check` (consistency checks), and `zc data extract` / `zc data items|upgrades|...` (the game database, and the names edits accept). `--json` and
  `--data-dir` work everywhere. See [docs/development.md](docs/development.md).
- Save as copy: a "Name for the copy" box next to the button. The game lists a save by a name stored in two places in the file, and a plain copy
  keeps the original's, so two saves looked identical in the load menu; with a name set, both are rewritten and checked before the file is written.

- Combat, Reinforcements and Objectives, from trying the edits in the game: a wave definition's **Delay** field is gone from the screen (changing it did not change when a wave arrived, whether a trigger or the game's own condition started it; it is still available through `zc edit`), the **timers** now let you change the total turns as well as the turns left (both took effect on loading, and a timer set to 1 failed its objective at the next round), the automatic-wave line says the game's condition decides when it starts, and the objective notes say plainly that marking an objective Succeeded does not start what follows it.

- Combat, Reinforcements: a **Current round** number. The game saves the round the fight is on (an earlier note said it did not), and loading
  a copy with the round set to 8 gave round 8 in the game. The editor shows it and changes it as one number in place. Tried in the game since: a queued wave's round and group, a definition's group and its times triggered took effect; a definition's delay did not
  change when a wave arrived.

- Developer notes on getting `zcdebug` running again after a game update (the UE4SS signature pack needs the new build listed); checked on game 1.2
  (build 199049). See [docs/development.md](docs/development.md).

- `zcdebug load` now waits until the loaded level is up and says so (`--no-wait` returns at once; new `zcdebug loadstatus`).
- `zcdebug` can investigate the running game without anyone at its window: `reload` swaps the mod's script without a restart (syntax-checked first, rolled
  back if the new one fails to start), `trace` logs every call of a game function with its parameters, `funcs` lists a live object's functions and their
  parameter types, `call` calls one, `shot` saves a screenshot with the HUD and prints its path, and `api` searches the class and function declarations
  UE4SS dumped. The mod also no longer re-runs a stale request from an earlier run when it starts.
- `zcdebug fix [--elevate]`: checks the game folders the mod and UE4SS write to and fixes the write errors under Program Files, such as UE4SS
  logging "Access is denied" for the missing `Content\Paks\LogicMods` folder. It creates what it can as your user and, with `--elevate`, asks
  Windows (through the normal UAC prompt) to run only the one command that is needed; without it, it prints the commands for an Administrator terminal.
  It also handles a game update that makes UE4SS's signature pack reject the new build (UE4SS stops at start-up and no mod runs): it reads the new
  exe's identity from its PE header and lists it in the pack's scripts, after backing them up (`--undo-signatures` restores them, `--no-signatures` skips it).
- `zcdebug log on|off|clear|snap|show`: a combat log built only from the game's own events, with no polling (so it costs nothing between events): ABILITY
  (which unit used which ability), MOVE (where), SHOT (who fired at whom), HIT and KILLED (target, attacker, the exact damage, CRIT and the ability, for
  example GA_StandardShot_Longarm_C or a teamwork assist shot), DOWNED, HEAL, each team's turn and each round. One snapshot of every unit's health, armor,
  shields, action points and position is written at the start of a round (and on `log snap`). A shot with no HIT after it missed. It also hooks the game's
  other event broadcasters, each costing nothing until its event happens: game sub-phase changes (PHASE), a player starting to aim (AIM), cancelled shots,
  melee, moves and grenades, and EVENT lines for objectives activated, progressing, succeeded or failed, bonds, destroyed props, reinforcements, toasts and
  combat start and end (bond progress, combat start and fatal damage were seen firing in play; the objective, destroyed-prop, reinforcement and toast hooks never fired, even while an objective was completed and reinforcements arrived).

- Combat, All stats: stats with no lasting effect are hidden until a "Show stats with no lasting effect" box is ticked (the header says how many are
  hidden). Besides the ones the game puts back or rebuilds (below), Accuracy, Aim, Attack and Defense are hidden because changing them did not change the
  hit chance or damage the game shows for a standard shot. Max accuracy (a cap on hit chance), Accuracy reduction (5 points of hit chance per point),
  Deflection chance (hit chance x (1 - value)), Damage multiplier, Bonus damage (added before the multiplier), Flat damage reduction, Incoming damage
  multiplier, Chance to ignore damage, Armor, Shields (a pool that takes damage before health), Defense penetration (cancels armor), grenade Critical
  chance / damage, Max damage per hit (caps each hit), Class tactic points per turn and Movement per action point were each loaded in the game and changed
  what it showed or did. An enemy's weapon Damage works and stays after it attacks (a Striker with melee damage 60 hit a hero for 57 after armor, an Enforcer with ranged damage 50 hit a hero for 47).
  Action points, movement points and special points per turn did not change a hero's next turn, so those three are hidden for heroes. Grenade bonus damage and the Burn and
  Shock chance bonuses changed nothing a grenade throw or rifle shot showed, so they are hidden too. Checked in the game by changing all 616 attributes of the 8 living units in one save, loading it, shooting, and ending the turn: the game
  puts the Advantage set back on load, rebuilds weapon and grenade stats from the equipped gear for player heroes (on load if the unit had already
  fired, otherwise when it first acts), and the scratch values do nothing. Action points, class tactic points and special action points are set back
  at the start of the unit's turn. Everything else (accuracy, defense, armor, shields, health, multipliers ...) was still there after both.

### Changed
- Combat: a unit's action points are limited to 0-3. The game caps them at 3 when it loads a save, so a higher number (9 was tried) used to come back as 3.
- `zcsave` and `zcassets` are now one tool, `zc` (`zc save ...`, `zc assets ...`); the commands and their output are the same. `zcdebug saves` now also
  lists the Wine-prefix and added save folders, and `zcdebug install` finds the game folder the same way the editor does.
- Combat: a unit's "All stats" list is grouped (Health, Actions, Offense, Defense, weapons, grenade ...) with plain names and hover notes
  taken from testing in the game. For example, weapon damage, crit and range are rebuilt from the equipped weapon when the unit fires, so editing
  them does not change attacks; the damage multiplier and attack did not change a standard shot either.

### Fixed
- Combat, Reinforcements: changing a wave definition's spawn group did nothing in the game. A test loaded the old group back, because the
  game restores each definition's group from a second list kept on the encounter (SaveLoadReinforcementWaveSelectedSpawnGroups) when it loads.
  The editor now changes that entry together with the definition and checks both before writing. Confirmed in the game: after the change the
  definition kept the new group, and the wave that arrived was the new one (3 Enforcers and 2 Strikers instead of the original Fodder Strikers).

## [0.6.0] - 2026-10-13

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
  or Damaged separately from its health, so Restore and Destroy rewrite that mark as well as the health. Restore and Destroy are both confirmed working in the game (destroyed barricades vanish: hidden, with collision off).
- Combat: the mission's enemy threat tier (for example Coil threat 10) can be swapped for another known tier on the Reinforcements
  tab (other factions' tiers are offered too, marked experimental). It does nothing for the encounter already started, because the
  enemies and waves in the save are fixed by then (tried on a hero mission); the editor counts the later encounters and says so beside
  the drop-down, so the setting can only matter for a mission with an encounter still to come.
- Combat: in a three-way battle the Reinforcements tab can switch the turn structure to standard and back. Trying to add the
  three-way structure to an ordinary mission did not work (the game loaded it as empty), so it is not offered there.

### Fixed
- Total focus could be written out of step with focus spent + unspent: raising unspent focus on the Overview and then levelling an
  ability on the Focus tab (spending it, or granting more) dropped or overwrote the change to the total. The total now follows at once,
  and the pending list shows it.
- Bringing an operator back from the Memorial and reordering the roster in the same apply was always refused ("the roster changed").
  A queued return now appears at the end of the roster strip and can be dragged into place.
- A save locked by the game (for example mid-autosave), a read-only folder or an unexpected save layout closed the editor. These are
  now error messages; anything else unexpected is shown in the banner and written to `crash.log` in the editor's data folder.
- Screens could still be edited while a write was running, and those edits were silently lost. Everything but the progress is now
  locked until the write ends.
- Reloading the game data discarded pending edits without saying so; they are now kept unless the save changed on disk.
- "Save as copy" dropped your pending edits; they now stay, since the open save is unchanged. Two copies made in the same second no
  longer overwrite each other.
- Linux/macOS: a save folder inside a Proton prefix was numbered by its position among all prefixes, so installing another Proton game
  could hide its backup history. Ids no longer depend on other games; older history is moved over the first time it is needed.
- Backups made in the same second were listed (and pruned) in the wrong order.
- A turn-structure change no longer lets the verifier accept unrelated changes elsewhere in the mission actor.

### Security
- The Oodle library that the game-data extractor downloads is checked against a pinned SHA-256 before it is loaded; anything else
  is deleted and refused. A copy you place yourself is not checked.
- Portraits in saves (which may be someone else's) are decoded only as OpenEXR, never by format detection, with a size cap, and
  oversized entries in the portrait archive are skipped.
- Magick.NET updated to 15.0.0.
- A ZIP entry in a save that unpacks to more than 64 MB is refused instead of being read into memory.
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
