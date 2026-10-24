# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.0] - 2026-10-24

### Added
- **Claimed zone rewards.** Galaxy > Influence shows each zone by its Holotable name (Zone Jenth, Zone Aurek ...) with a **Rewards claimed**
  checkbox per influence tier; `zc edit do <save> zone-claimed <zone> <tier> <true|false>` does the same. The ticks on the Holotable come
  from the zone's list of claimed tiers, which the editor could not see before, so lowering a zone's influence never unticked anything.
  Untick a tier and the game offers its reward again, as long as the zone's influence still reaches it. This is how to earn Zone Jenth's
  Hutt Roulette Prize again (the "There Will Be No Bargain" achievement wants it three times; the game counts it in its profile file,
  `SharedGameSettings.sav`, not in campaign saves, so a reloaded save still adds to the count).
- Game data: the extractor reads the 12 zones with each tier's influence and reward, so the checkboxes say "Tier 3 · 100 influence,
  Hutt Roulette Prize" after extracting again. The zone names are built in, so they show without game data too.
- **Profile.** The game's profile file, `SharedGameSettings.sav` (one per player, shared by every campaign), now opens in the editor
  instead of being listed as "not editable here". Its Profile tab lists every achievement the game counts (from the game's own list:
  the counter it watches, the target and whether it is reached) with the count editable, the best reward tier reached in each zone, and
  the profile's unlocks (story outfits, campaign complete ...); the Advanced tab shows every raw value, including the language and the
  first-run flags. The campaign tabs are hidden for it, and writes get the same backup, refusal and verification as saves.
  `zc edit` reads and writes it with `stat.<Counter>` and `zone_best.<Zone>` paths.
- **Start a missing achievement counter.** The game only adds a counter to the profile the first time it counts something, so an
  achievement you have never progressed shows "not counted yet". Its row now has a **Start at** box: type a count and apply, and the
  counter is added with that count (a copy of an existing counter's entry, renamed). `zc edit do SharedGameSettings.sav add-stat
  <counter> <count>` does the same, e.g. `add-stat AngryBeeps 1`. Only the counters of the game's own achievements can be started.
  Checked in the game: Angry Beeps started at 1 was read, kept when the game rewrote the profile, and still 1 after loading two saves.
  `zc` also finds the profile by its file name now (`SharedGameSettings`), not only by its full path.

### Changed
- The zone field that used to be called "Reward tier claimed" is now "Reward index" (the highest tier the zone has reached); the claimed
  tiers are the new checkboxes.

### Checked in the game
- A Den copy with Zone Jenth's tier 3 unticked (influence 150): the game reported the Hutt Roulette Prize claimable again (and only that
  tier); claiming it with the game's own claim call added an Exotic weapon mod (Stabilizer Bracing T4), ticked the tier again in the save
  the game wrote next, and raised the profile's There Will Be No Bargain count from 1 to 2.
- A profile counter lowered with the editor (There Will Be No Bargain, 2 to 0) stayed at 0 after the game started and after a save was
  loaded, with the game rewriting the file each time: the game counts from the file, it does not restore counts from EA at start-up.

## [1.1.0] - 2026-10-24

### Added
- **Give a bond trait.** Personnel > Overview has a "Give a bond trait" box for the class traits operators normally learn only from a bond
  partner of another class through cross training, such as a Scout's Difficult Target, a Soldier's Grenadier or a Warrior's Hazard Training.
  All 32 traits the game defines are offered, not only ones someone in the save already has; `zc edit do <save> give-trait <operator> <trait>`
  does the same. A trait is one record in the operator's saved gameplay effects, so the editor copies a trait record already in the save (the
  operator's own when they have one) and switches it to the trait chosen. In a campaign where nobody has learned a trait yet, the operator's
  own health-progression record is used instead, with its stat values removed (a trait has none). Traits an operator already has are now
  named on the Overview ("Pack Tactics (bond trait, Scout)").
- Game data: the extractor also reads the bond traits, so after extracting again each one shows the game's own name and description.

### Checked in the game
- A fight copy with three traits given (Grenadier, which nobody in the save had; Pack Tactics, copied from Hawks; Hazard Training): after
  loading, each hero carried the trait and held the ability it grants (`GA_Grenadier_T1`, `GA_PackTactics_T1`, `GA_HazardTraining_T1`).
- An early Den save in which nobody had learned a trait (so the trait was made from a progression record): after loading, Hawks carried
  Grenadier and the effect its ability applies, and a save the game wrote afterwards still held the trait.

## [1.0.0] - 2026-10-23

The final planned release. Checked against game 1.2 (build 199049) with every save the game wrote in a full campaign, from the first
Den visit to the epilogue: all of them open, rebuild byte for byte and pass `zc save check`.

### Added
- **Campaign options.** Game 1.2 lets you pick additional campaign options when a campaign starts (permadeath, operator injuries, risk of
  death, Advantage rules, assist shots, enemy critical hits, perfect shots, operator health gain, cross training from low bonds and the
  specialization and recruitment locks) and never lets you change them afterwards. Command > Campaign options shows them as drop-downs,
  and `zc edit do <save> set-option <option> <choice>` does the same. A save keeps each choice in four places (the campaign's options, its
  permanent facts and both header entries), and the editor changes and verifies all four. Campaigns started before 1.2 have no options.

### Checked in the game
- **Campaign options**: after loading edited copies (a fight and a Den save), the running game held exactly the edited options and not the
  old ones.
- **Galaxy**: a Den mission's and an operation's time on offer work both ways: after one cycle a mission moved earlier and an operation aged
  by one were gone, and a mission given full time and an operation set back to 0 were still offered.
- **Fire zones**: a fire set from 2 turns left to 1 went out after the next round. The fire-zone note in the app says so.

### Changed
- One version number for the editor, `zc` and `zcdebug` (`zc` and `zcdebug` used to report 1.0.0 while the editor said 0.8.0).
- Combat: switching a unit's team is no longer labelled experimental (every direction was confirmed in the game). The stat notes say what
  the game does with each value instead of describing how it was tried.
- Dependabot no longer opens update pull requests.
- `zcdebug`: the mod (0.19) takes requests again if the game never runs one, and its heartbeat says what its request loop is doing. The
  developer notes explain why it hears nothing when `zcdebug` runs inside a packaged Windows app.

### Fixed
- Permadeath was never shown for saves made with game 1.2, which replaced the save's permadeath flag with the campaign option; the Command
  tab, `zc save profile` and the debug report read it again (and still read the old flag in older saves).
- A second specialization's copy of an ability (21 abilities, for example `br.AbilityID.Class.Ambush.2nd`) was named just "2nd" on the
  Focus Tree, in Give an ability and in `zc`; it is now "Ambush (2nd)", and `zc edit do grant-ability` accepts `Ambush.2nd`.
- The campaign finale's fight: units on no team (story holograms) can no longer be given a team (the editor shows it read-only and
  refuses the change), timers that start other objectives or waves when they run out say so, timers that count at the start of a turn
  say that, and the waves and definitions of a mission with several encounters are numbered per encounter.
- `zc save profile` printed "turn structure ?" for every standard mission, and counted destroyed props by health instead of by the mark
  the Combat tab uses.
- `zc save check` failed on the game's settings and character-pool files (and so exited 1 for `zc save check *.sav`); it now passes them.
- A save with a recipe, effect or bond record missing a field, or with fact tags in an unexpected layout, no longer fails to open.
- Opening a save with a large roster is faster (the focus-tree check no longer rebuilds its donor list once per operator).
- Docs: the user guide covers the Command tab's roster capacity, Den numbers, gear slots, Black Market and campaign options, Give an
  ability, the upgrade queue buttons and the Combat *Other* tab; the issue template gives the right backups folder; the disclaimer
  separates the editor from the developer-only `zcdebug`; `zc edit --help` examples use real names.

## [0.8.0] - 2026-10-17

### Added
- **Give an ability.** Personnel > Focus Tree has a "Give an ability" box that copies an ability another operator in the save holds (for example Cly's Combat
  Jump jetpack) onto the selected operator; `zc edit do <save> grant-ability <operator> <ability> [level]` does the same. The ability's tier records and its
  focus entry are copied from the operator who has it, so the save must contain someone who holds the ability. Confirmed in the game: Hawks used the jetpack.
- **Gear slots.** Command > Gear slots gives every operator more utility slots (up to 12) or fills the weapon's four mod slots; `zc edit do <save> set-slots
  <mod|utility> <total>` does the same. The game reads two things: each operator's list of slots (what the Armory draws) and the bonus slot count (what lets an
  item be equipped), so the editor raises both. Tried in the game: the new utility slots can be equipped. Weapon mods stop at four because a weapon takes one mod
  of each of its four kinds. The slot limit values in the save (the maximums) and the bonus alone do not unlock slots.
- **Den numbers** on the Command tab: field tasks per turn (most and now), the Black Market's refresh cost, and its bonus items (`zc edit fields <save> progression`). Tried in the
  game: a third operation could be launched with the limit at 6, the refresh cost followed the saved number (and the discount and increment worked from it), and the
  Black Market stocked 12 items instead of 7 after a refresh. The shop price number in the same part of the save is not exposed because its effect was not confirmed.
- **Black Market quality.** Command > Black Market restock sets how many items a refresh rolls from each quality table (Basic, Superior, Exquisite, for weapon mods
  and for utilities); `zc edit do <save> set-market <mods|utility> <tier 1-3> <count>` does the same. A table the save does not list yet is added. Tried in the game:
  Superior and Exquisite at 0 and Basic at 3 stocked only Basic items, and the reverse stocked only Exquisite ones. The bonus items still come from the Exquisite mods
  table. The game has no table above Exquisite, so Exotic items and the unique ones come from Armory > Add items.
- **Roster capacity** on the Command tab (`progression.roster_cap` in `zc edit`): the number of operators the roster holds, which roster upgrades normally raise. It is also what the Recruitment screen checks (tried in the game: at 18 of 18 recruiting is blocked with "No open roster slots", at 18 of 19 it is allowed), so raising it lets you recruit past the Den's limit. Tried in the game: a capacity above what the Den allows works, and a roster over its capacity loads untouched, though the roster screens look odd.
- **Debug report.** An error banner now has **Copy debug report** and **Save debug bundle…** (also under Game data > Report a problem). The
  report is text to paste into a bug report: the editor and game versions, the error with its details, the pending changes, counts from the
  open save (no names) and the setup checks. The bundle zips it with the save file and the crash log. `zc doctor --report [--save S]
  [--bundle FILE.zip]` does the same from the command line. The issue templates ask for the report and now accept the bundle.

### Fixed
- Armory, **Add items**: adding an item failed with "the save has no utility item to copy; add one in the game first" when every utility
  item in the save was equipped, or when the save had no weapon mod yet (common early in a campaign). The new entry is now copied from any
  inventory entry, a resource if need be, and always starts with a count of 1 and nobody equipping it.

## [0.7.0] - 2026-10-16

### Added
- **`zc`, a command-line tool**, and a shared **Zero Company Save SDK** (`src/ZeroCompany.Sdk`) that the editor, `zc` and `zcdebug` now all use for finding the
  game and the saves, so they agree. `zc edit` changes a save without the editor, with no UI needed (so scripts and agents can do it): `fields`, `get`, `set`,
  `do` (heal, add-item, unit-dead ...), `apply` (a JSON batch), `diff`, `backups`, `restore`. It uses the editor's own checked write (backup, refused while the
  game runs, refused if the file changed, verified rebuild) and writes nothing without `--write`. Also `zc doctor` (what is set up and what is missing),
  `zc save check` (consistency checks), and `zc data extract` / `zc data items|upgrades|...` (the game database, and the names edits accept). `--json` and
  `--data-dir` work everywhere. See [docs/development.md](docs/development.md).
- Save as copy: a "Name for the copy" box next to the button. The game lists a save by a name stored in two places in the file, and a plain copy
  keeps the original's, so two saves looked identical in the load menu; with a name set, both are rewritten and checked before the file is written.

- Combat, **Roster** tab: bring more of your roster into a combat save. The game builds a fight's heroes from the mission squad list, so adding an
  operator's roster GUID there makes the game spawn them when the save loads (tried with the whole roster of 19: all spawned, and a round played);
  operators arrive with the health their roster entry records, so the tab can also queue the Medbay heal for injured ones as they join, and `zc edit
  do add-squad <operator>|all [heal]` does the same from the command line. The game places the new operators itself around the map's start point,
  and some land outside the playable area, so the tab explains the second step (save in the game, then set their position here).
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
- Galaxy: split into Influence, Operations, Missions and Coil upgrades tabs. The Missions tab shows each mission's in-game name, place,
  difficulty, the faction it normally fights and how long it lasts. These come from the game's mission assets, so re-extract the game data (it adds a `Missions` list; without it the list
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

[Unreleased]: https://github.com/starmast/zero-company-save-editor/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/starmast/zero-company-save-editor/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/starmast/zero-company-save-editor/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.8.0...v1.0.0
[0.8.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.7.0...v0.8.0
[0.7.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.6.0...v0.7.0
[0.6.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/starmast/zero-company-save-editor/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/starmast/zero-company-save-editor/releases/tag/v0.1.0
