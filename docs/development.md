# Development

You need the .NET 10 SDK.

```
dotnet build
dotnet test
dotnet run --project src/ZeroCompany.App
```

Publish a self-contained single file for one platform:

```
dotnet publish src/ZeroCompany.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/win-x64
```

(`linux-x64`, `osx-arm64` and `osx-x64` work the same way.) Pushing a `v*` tag runs `.github/workflows/release.yml`, which
builds and uploads all four.

## Layout
```
src/ZeroCompany.Core/      GVAS reader/patcher, save container, editable model, actions (Actions/Combat/ for combat saves), apply/verify/backup service, view models, ZcWorkspace
src/ZeroCompany.GameData/  game-data extractor (CUE4Parse) and the typed game database
src/ZeroCompany.Sdk/       the Zero Company Save SDK: what the editor and the command-line tools share (see below)
src/ZeroCompany.App/       Avalonia desktop UI (MVVM): screens, theme, portraits
src/ZeroCompany.Cli/       zc: the command-line tool (save, edit, assets, data, doctor)
src/ZeroCompany.DebugCli/  zcdebug: the live-game debugger (dev only)
tests/ZeroCompany.Tests/       core, SDK, game-data and service tests
tests/ZeroCompany.App.Tests/   headless UI tests that also render screenshots
tools/golden/              scripts that produce the comparison files described below
```

## Tests
Tests that need a real save, extracted game data or the game install skip themselves when those are absent (they are never
committed), so a fresh clone runs the synthetic tests and the UI smoke tests only. To run everything:

- put a save in `saves/` (tests only ever write to temporary copies);
- extract game data once, or keep the `gamedata/*.json` dumps there;
- for the extractor test, set `ZC_GAME_DIR`, `ZC_USMAP` and `ZC_OODLE_DIR` (or leave a `.usmap` and `oodle-data-shared.dll` in
  `gamedata/`) so the test never downloads anything.

**Combat saves.** The combat tests read real combat saves from `saves/` (also git-ignored) and skip without them. Copy these
from the game's save folder, named exactly: `Combat_EncounterStart.sav` (any combat autosave with enemies, allies and civilians),
and from one rescue mission: `Combat_Rescue_before.sav` (before the civilian is rescued, with the countdown timer running),
`Combat_Rescue_after.sav` (right after the rescue) and `Combat_Rescue_late.sav` (later, with the occupy-region points counting).
Two more fixtures cover the newer features: `Combat_ThreeWay.sav` (a three-way battle, such as Belly of the Beast, with Coil on
Enemy and Imperials on Enemy2, fire zones and destructible props, saved mid-fight) and `Combat_Imperial.sav`
(an ordinary two-sided mission whose enemy threat tier is still saved, such as a Brentaal skirmish at the start of the fight). `Combat_Scenery.sav`
is a game-written save in which some destructible props are already destroyed (it has four), for the scenery restore tests.
Some assertions rely on what is in those saves (unit counts, three queued waves), so a different save needs the numbers adjusted.

**Parity with the original Python editor.** The suite compares the C# code with the v0.3.0 Python editor on your sample save:
identical fields and screen views, and byte-identical files for scripted edits. The comparison files are produced from the
`v0.3.0` tag; see [`tools/golden`](../tools/golden/README.md). They contain game text and save contents, so they are git-ignored.

**UI screenshots.** The UI tests render screens to `artifacts/shots/`. Set `ZC_UPDATE_DOCS=1` and run the
`Regenerate_readme_screenshots` test to refresh `docs/screenshots/` (portraits are switched off so no game artwork is shown).
Set `ZC_PORTRAITS=off` to run the app itself with initials instead of portraits.

## Dependencies and supply chain
Dependabot (`.github/dependabot.yml`) opens weekly pull requests for NuGet packages and for the GitHub Actions, which the
workflows pin to exact commits (the version is in a comment beside each). `dotnet list package --vulnerable --include-transitive`
should stay clean.

The extractor's Oodle download is pinned in `src/ZeroCompany.GameData/Extractor/OodleCheck.cs`. When CUE4Parse is updated to a
newer Oodle build, `The_pins_are_for_the_oodle_release_the_referenced_cue4parse_downloads` fails on purpose: download the release
assets named in CUE4Parse (`clang-cl-x64-release.zip` for Windows, `gcc-x64-release.zip` for Linux), take the SHA-256 of
`oodle-data-shared.dll` / `liboodle-data-shared.so` inside them, and update the pins and the release tag in that test.

## Third-party software
[Avalonia](https://avaloniaui.net/) (MIT), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT),
[Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache-2.0, decodes the OpenEXR portraits in saves),
[CUE4Parse](https://github.com/FabianFG/CUE4Parse) (Apache-2.0, used by the extractor), which downloads Oodle (proprietary,
Epic Games Tools / RAD) at run time, and a community-made `.usmap` mappings file you obtain yourself. Tests: xUnit
(Apache-2.0). The UI uses the Barlow Condensed font family (SIL OFL) when it is installed.

## Acknowledgements
The earlier [Zero_Company_Save_Editor](https://github.com/jpatrickp512/Zero_Company_Save_Editor) by jpatrickp512 was a useful
reference for which save properties matter; no code was copied from it. The earlier Python/web implementation (v0.1.0 to
v0.3.0) is kept in git history under those tags.

## The SDK and `zc`
`src/ZeroCompany.Sdk` is the one place that knows where things are and how to read and change a save, so the editor, `zc` and `zcdebug`
always agree. The layering is `GameData <- Core <- Sdk <- App, zc, zcdebug`; anything the editor itself needs lives in Core and the SDK adds the rest.

| Piece | What it does |
|---|---|
| `ZcWorkspace` (Core) | This user's setup: the data folder (`--data-dir`, `ZC_DATA_DIR`), saved settings, save folders, the game install, the `.usmap`, the Oodle folder |
| `SaveCatalog` | Lists saves from their small info entry and finds one by path, name, fragment, save name or `latest` |
| `SaveDocument` | A save opened read-only: the property tree, search, diff, dump |
| `SaveEditor`, `FieldCatalog`, `EditScript` | Edits a save headlessly with stable names like `resource.Intelligence`, through the same pending-changes and checked write the editor uses |
| `FieldDiff`, `SaveCheck` | What changed between two saves; round-trip and consistency checks that write nothing |
| `GameAssets`, `GameDataBuilder`, `GameDataLookup` | The game's packages read-only; building the game database; the names edits accept |
| `ZcDoctor` | What is set up and what is missing |
| `Sdk.Cli` | Shared argument parsing, error handling and exit codes for the tools |

`src/ZeroCompany.Cli` builds `zc`. Every group has `--help`; `--json` prints machine-readable output; `--data-dir DIR` (or `ZC_DATA_DIR`) points it
at another settings/backups/game-data folder, which is how to try things without touching your real one.

```
zc doctor                                   what is set up and what is missing, with the fix for each (exit 1 on a failure)
zc doctor --report [--save S] [--bundle Z]  the debug report users paste into a bug report; --bundle zips it with the save and crash.log
zc save list | profile <save> | diff <a> <b> | find <save> <text> | dump <save> [path] | check <save>...
zc edit fields|get|set|do|actions|apply|diff|backups|restore ...
zc assets find|dump|scan|missions|operations|rewards ...
zc data extract | items|upgrades|effects|coil|spawn-sets [filter]
```

A save is a path, a file-name fragment, a save name (the comment the game lists it by), or `latest`. `zc save` and `zc assets` never write.

### zc save
```
dotnet run --project src/ZeroCompany.Cli -- save list
dotnet run --project src/ZeroCompany.Cli -- save profile "COMBAT BASE 4.0"
dotnet run --project src/ZeroCompany.Cli -- save diff "COMBAT BASE 4.0" latest
dotnet run --project src/ZeroCompany.Cli -- save find latest Enemy2
dotnet run --project src/ZeroCompany.Cli -- save dump latest TacticalSaveGameWrapper/ArchiveBytes/CharacterInfos/[6] --depth 3
```
`profile` shows the teams, units, objectives, waves and scenery, `diff` shows what changed between two saves (units first), `find` locates a
name or value, `dump` prints a property tree, and `check` runs round-trip and consistency checks (the container rebuilds unchanged, the
sizes in the metadata match the data, the name is stored consistently, the editor can read it).

### zc edit: editing a save without the editor
It goes through the editor's own pipeline (pending changes, then the checked write: backup, refusal while the game runs, refusal if the file
changed since it was read, verified rebuild), so a script or an agent gets the same safety as the UI. **Nothing is written without `--write`**:
without it you get the list of changes and exit code 0.

```
zc edit fields latest resource                     editable values: path, current value, range
zc edit set latest resource.Intelligence=300 operator.hawks.focus=9          preview
zc edit set latest resource.Intelligence=300 --write                         write (a backup is kept)
zc edit set latest resource.Credits=50000 --copy --as "RICH COPY" --write    write a copy with its own in-game name instead
zc edit do latest heal Neiya --write                                         a structural edit; `zc edit actions <save>` lists them
zc edit do latest add-item Medpac 3 --write                                  names as `zc data items` lists them
zc edit apply script.json --write                                            a batch in one write
zc edit diff original.sav edited.sav                                         what changed, as field paths
zc edit backups latest / zc edit restore latest 20261015-181624.sav --write
```
Paths are dotted names: `resource.<key>`, `level`, `xp`, `turn`, `operator.<name>.focus|total_focus|ability.<tag>.level|spent|effect.<asset>.stacks|bond.<partner>.level`,
`item.<asset>`, `region.<tag>.influence`, and for combat saves `unit.<n>.attr.<Stat>`, `unit.<n>.x`, `wave.<logic>.<n>.round`, `objective.<n>.counter.<label>`,
`timer.<n>.remaining`, `hazard.<n>.turns`, `prop.<n>.health`. They ignore case and punctuation; a raw field id such as `o2897` always works.
A script is `{ "save": "latest", "set": { "resource.Intelligence": 300 }, "actions": [ { "do": "heal", "args": ["Neiya"] } ] }`.
Exit codes: 0 done or previewed, 1 error (nothing written), 2 refused (the game is running, or the file changed on disk). With `--json` an error is
`{ "error": ..., "exitCode": ... }` on stdout. Whether the game loads a file written this way is the same open question as for the UI's edits.

### zc assets: browsing the game's own assets
`zc assets` is a read-only browser for the game's packages. It uses the same install folder,
`.usmap` and Oodle library the editor already knows about (extract game data once, in the editor or with `zc data extract`, first), never downloads anything
and never writes to the game. Mounting about 180,000 files takes a few seconds.

```
zc assets find TurnStructure                      asset package paths containing a word
zc assets dump BP_TurnStructure_ThreeWayBattle     one asset's objects as JSON (--merged adds inherited properties)
zc assets dump Enemy2Team --grep FriendlyTeamTags  only the properties that mention a word
zc assets scan Blueprints/Objects Enemy2          search inside every asset whose path contains a word
```

It is how the combat rules were checked against the game's own data rather than guessed from saves. For example, each team asset
(`GameData/Teams/*Team`) lists the teams it counts as friends in `FriendlyTeamTags`: Player and Civilian list each other, Enemy
lists only Enemy, and Enemy2 lists only Enemy2, so Enemy and Enemy2 are hostile to each other and to the squad. A team only acts
if the mission's turn structure has a turn group for it, and `BP_Enemy2TurnGroup` exists only in `BP_TurnStructure_ThreeWayBattle`.

The output is the game's own content: use it to read, and never publish or commit dumps (see the [disclaimer](../DISCLAIMER.md)).
On Git Bash, set `MSYS_NO_PATHCONV=1` before passing a path that starts with `/`, or the shell rewrites it.

**What it found about combat** (read from the game's data, not guessed from saves):
- *Threat tiers.* The game ships these enemy spawn sets: Coil 1-10 (plus a separate crisis-elite set), Separatist 1-10, Outlaw 1-5,
  Imperial 1 and 9, and SlugRats 1. A set is a pool of spawn groups, and the pool grows with the tier: Coil 2 holds eight basic
  non-infected Enforcer/Striker groups (T1), while Coil 10 holds about 89 groups from T4 up through Droidekas, Guardians, Captains,
  Marauders and Scourge types. A lower tier means a weaker pool, not fewer units.
- *Three-way battles.* Of all the levels (179 `_Gameplay` maps), only Belly of the Beast sets `TurnStructure` to the three-way
  structure, and only it has spawners with `OwningTeam = Enemy2Team`. `BP_MissionActor`'s default is the standard structure. Other
  levels never load the three-way class, which is why a reference to it saved into another mission came back empty.
- *Where the rules live.* The turn system itself is native code, so why the game puts Enemy2 units back on Enemy in a standard
  mission is not in any asset; only the data around it is (teams, turn groups, structures).

### zc doctor and zc data
`zc doctor` lists the save folders, game folder, mappings file, Oodle library, game database (and whether the game files are newer than it), backups
and whether the game is running, with a fix for each problem. `zc doctor --report` prints the debug report (`DebugReport` in the SDK, the same text as the editor's *Copy debug
report*): tool and OS, the last error with its stack trace (editor only), pending changes, the save as counts (game build, mode, inventory per
kind with how many are equipped), the save checks and the doctor checks, with the home folder shown as `~`. `--bundle FILE.zip` adds the save
file and the end of `crash.log`. `zc data extract` builds the game database the way the editor's Game data screen does
(same defaults, same file); `zc data items|upgrades|effects|coil|spawn-sets` list the names `zc edit do` accepts.

The published tool is a self-contained single file (`dotnet publish src/ZeroCompany.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/zc-win-x64`);
on machines with Application Control a loose `zc.dll` may be refused where the single-file exe is not.

## zcdebug: talking to the running game
`src/ZeroCompany.DebugCli` builds `zcdebug`, a command-line debugger for the live game. It does not touch the game process
itself. Instead it talks to a small Lua mod, **ZcDebug**, that runs inside the game through [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)
(a third-party Unreal mod loader that you install yourself; `zcdebug` never downloads or patches anything).

```
zcdebug install            copy the ZcDebug mod into UE4SS's Mods folder (zcdebug uninstall removes it)
zcdebug fix [--elevate]    check the game folders the mod and UE4SS write to (Mods, LogicMods) and fix what is not writable (see below)
zcdebug status             is the game running, is the mod answering
zcdebug find Pawn          live instances of an Unreal class
zcdebug props <object>     a class's properties and types
zcdebug get <object> Path  read a property; Path can walk in (SpawnedAttributes.1, Health.CurrentValue)
zcdebug dump <object>      every simple property with its value (attribute structs show base and current)
zcdebug effects <unit>     the unit's active gameplay effects (Burning, Spotted ...)
zcdebug set ... --write    change a property in the running game   (also: exec <console command>, eval <lua>)
zcdebug attr <unit> <Attribute> <n> --write   set a stat (Health, ActionPoints ...) in the game and on the HUD
zcdebug fx <unit> add|remove <Name> [n] --write   add (n stacks) or remove a status effect (Burning, Spotted ...)
zcdebug saves [filter] [--all]   the save folder: file names as load wants them, in-game names, and which the game lists (no game needed)
zcdebug load <save name> --write   load a save by file name, without .sav (the game must already list it, see below); waits until the level is up
zcdebug loadstatus         whether the level of the last load is up
zcdebug funcs <obj> [text] the functions a live object's class has (and its parents'), with parameter types
zcdebug call <obj> <Fn> [args] --write   call one: numbers, true/false, @/object/path, n:Name, anything else is text
zcdebug trace <Class:Fn> [on|off|list] [max] --write   log each call of a function with its parameters to combat.log (first 300 calls)
zcdebug reload [--from FILE] --write   run the mod's script again without a restart; --from copies FILE over the installed script first
zcdebug shot [--out FILE]  have the game save a screenshot with the HUD (Shot showui) and print its path
zcdebug api <text> [--limit N]   search the class, struct and function declarations UE4SS dumped (Dumpers > "Dump CXX Headers", once)
zcdebug log on|off|clear|snap   record the game's own combat events without polling: ABILITY (which unit used which ability), MOVE, SHOT, HIT/KILLED (target,
                           attacker, exact damage, CRIT, ability), DOWNED, HEAL, each team's turn and each round, plus one snapshot of every unit per round
zcdebug log show [n]       the last n lines of that log (combat.log in the channel folder; works without the game)
zcdebug repl [--write]     the same, interactively
```

Reads need no flag; anything that changes the running game (`set`, `attr`, `fx`, `load`, `exec`, `eval`) is refused without `--write`.
`<object>` is a full object path or a short class name (its first live instance). A path after the object is not walked for
you: `get <object> A.B.C` walks `A.B.C` inside the object, but the object itself must be a real object path (for example
`...PersistentLevel.Char_Hero_HAWKS_Control_C_0.BitReactorHealthSet_0`). After `zcdebug install`, reload the mods in the game
(Ctrl+R in the UE4SS window) so the game runs the new Lua.

**Investigating without the game window.** After one Ctrl+R that loads a mod with `reload`, later changes to `mod/ZcDebug/Scripts/main.lua` go
live with `zcdebug reload --from <that file> --write`: the mod syntax-checks the new script first, switches to it without a restart (the old
generation's hooks are removed and its loops stop), and `zcdebug` waits until it answers as the new version; a script that fails to start leaves
the running one in place. `zcdebug api` finds what to look at in the dumped headers, `funcs` lists a live object's functions, `trace` shows which
of them are called (and with what) while something happens, `call` calls one, and `shot` saves a screenshot with the HUD. Only functions the
game calls through Unreal's reflection system can be traced (Blueprint events and native events the engine raises); Blueprint custom events
that are called inside their own graph, such as `OnUnitDamaged`, are not seen. The mod no longer re-runs a request left in the channel file by an
earlier run when it starts.

**When Windows blocks a freshly built `zcdebug.exe`.** Application Control / Smart App Control sometimes refuses new unsigned builds (and the
test assemblies). The CLI also runs under WSL, where that policy does not apply, and reaches the Windows game through `/mnt/c`: set
`ZCDEBUG_DIR=/mnt/c/Users/<you>/AppData/Local/ZeroCompanyEditor/debug` (the channel folder) and `ZCDEBUG_SHOTS=/mnt/c/Users/<you>/AppData/Local/SWZeroCompany/Saved/Screenshots/Windows`
(where `shot` looks), and pass `--game "/mnt/c/Program Files/EA Games/Star Wars Zero Company"` to commands that touch the game folder (`reload --from`, `api`).
`zcdebug status` cannot see the Windows game process from WSL, but every command that talks to the mod works.

**Testing what a stat does.** `attr` and `set` write the attribute directly, which is right for health, action points and the HUD, but the
game's hit-chance and damage calculations read stats through an aggregator that only its own setter updates, so a stat changed that way
shows no effect there (Accuracy 20 and enemy Defense 10 changed nothing in the aim preview until the same values were loaded from a
save). To see what a stat does, edit a copy of a combat save (`zc edit set ... --copy`), load it with `zcdebug load`, and look at the
result: the aim preview (hit chance and damage), the health numbers after a shot, or `zcdebug log` over a turn. `zcdebug exec "Shot
showui"` makes the game write a screenshot with the HUD to `%LOCALAPPDATA%\SWZeroCompany\Saved\Screenshots\Windows` (plain `HighResShot`
leaves the HUD out), which is how the preview can be read without being at the screen.

The `--write` check is in `zcdebug` only. The mod itself runs whatever request it finds, `eval` included (arbitrary Lua inside the
game), so while it is installed any program running as your Windows user can drive the game through the request file. That gives
nothing such a program could not already do, but uninstall the mod (`zcdebug uninstall`) when you are not using it.

**How it talks to the game.** Lua has no sockets or JSON library, so both sides use three small files in
`%LOCALAPPDATA%\ZeroCompanyEditor\debug`: `request.txt` (written by `zcdebug`), `response.txt` (written by the mod) and
`heartbeat.txt` (the mod, about once a second, which is how `status` knows the game is up). Each is one tab-separated line ending
in a newline; see `Protocol.cs`. The mod polls every 200 ms and runs each command on the game thread.

**State of it.** The tool, the protocol and the installer are covered by tests against a fake mod. The Lua mod itself
(`src/ZeroCompany.DebugCli/mod/ZcDebug/Scripts/main.lua`) was **run against the real game on 2026-10-14** (EA App build 197649,
Unreal 5.6.1) in a Brentaal skirmish: `find`, `props`, `get`, `dump`, `set`, `attr` and `exec` worked, and so did `eval`; `effects` and
`fx` were tried as plain `eval` calls. What was seen:

- Units are plain actors (`Char_Hero_<Name>_<Class>_C`, `Char_Enemy_Coil_<Type>_NotInfected_C`). Their stats are Gameplay Ability System
  attribute sets on `<unit>.AbilitySystemComponent.SpawnedAttributes` (`BitReactorHealthSet`, `CombatSet`, `AdvantageSet` ...); each
  attribute has a `BaseValue` and a `CurrentValue`. Damage changes both. Writing both changes the game's logic, for example extra
  action points let a hero act more times and later damage subtracts from the written health.
- "Spotted" and "Burning" are gameplay effects (`ActiveGameplayEffects`), not attributes.
- The HUD does not follow a direct write: the UI reads `BrunoGameplayAttributeViewModel` objects. `attr` also sets those and
  broadcasts the change (`K2_BroadcastFieldValueChanged`), which made the AP pips and health bars update.
- In the save a hero's `hp` shows as `151/28`: Health 151 against the *base* MaxHealth 28; the live current MaxHealth is 151.

**Loading an edited save without restarting the game.** The load menu and `BrunoSaveGameSubsystem.LoadFile` use a list of saves
(`SavePreviews`) that the game builds once, at start-up. A file written while the game runs is not in it: the menu does not show it
(re-opening the menu rebuilds the list from the same cache), and `zcdebug load` is refused with "the requested save does not exist or is
invalid". The game does rescan the folder when asked (`GetAllSaves` returned the new file), but nothing exposed refreshes the cache.
What works is a **slot file**: pick a save the game already lists (for example a throwaway copy you made earlier and restarted for),
overwrite its contents on disk with the new save, then `zcdebug load <that file's name>`. Checked with two test copies: the game loaded
the overwritten file's new data (an Enforcer at 60 instead of 61). The menu entry keeps the old name and thumbnail. Only overwrite
files you made for testing, never a save you care about. Send one `load` and wait for the level to finish loading: a second `load` during a load (even a refused one) left the level with no enemies and heroes that could not move, so the mod now refuses a second request for 30 seconds. `zcdebug saves` shows which files the game should accept: a file created after the game started is marked NEW (a heuristic from file creation times; an overwritten file keeps its creation time, so it stays "listed"). The editor does not depend on any of this: it only reads and writes saves.

**Some properties must not be read.** Reading arrays of soft object paths crashed the game twice (`SaveLoadReinforcementWaveSelectedSpawnGroups` on the encounter actor, once through `get` and once through `dump`). `get` and `dump` now look up each property's declared type before reading it: `get` refuses arrays whose element type is not an object, struct, number or bool, plus maps, sets and soft or lazy references, and `dump` reads only plain values (numbers, bools, names, strings, enums) and attribute structs. The mod's own `eval` has no such guard. A request that times out is a warning: stop and check the game before sending anything else, and never run a wide read on an actor that has just crashed the game.

**Never call a method on a null object.** A function that returns an object can return a null one (UE4SS prints it as `<userdata>`), and UE4SS
dereferences it when you call anything on it: the game dies with `EXCEPTION_ACCESS_VIOLATION reading address 0x10` (the class pointer's
offset) and a stack made of UE4SS frames only. Always ask `obj:IsValid()` first. `call`, `funcs` and `trace` do (a null result prints as
`(null object)`); `eval` and anything written by hand must. For example an ability's default object (CDO) has no to-hit calculation, only live
ability instances do.

**Do not bulk-scan from Lua.** An `eval` that looped over every character walking each one's `ActiveGameplayEffects` crashed the game
(`EXCEPTION_ACCESS_VIOLATION` reading address 0x18). Read one named unit at a time and stop after the first timeout. Unreal's
crash reports are in `%LOCALAPPDATA%\SWZeroCompany\Saved\Crashes`. Errors from the mod come back as `error: ...` and also go to
UE4SS's log. Mods change the game's behaviour: use it for exploring, not in a save you care about, and expect that updating or
repairing the game (the EA app may repair modified files) can undo the install.

**UE4SS.** An earlier try with the UE4SS 1.1.2 pack made this build start as a process and show nothing (no window, no log), and
renaming `dwmapi.dll` back out of the way fixed it. A later install loaded and ran mods fine on the same build, so it is not a
permanent problem, but which UE4SS build works is not recorded here. The game folder is under `Program Files`, so `zcdebug install`
needs an Administrator terminal there, or `zcdebug install --out <folder>` and a manual copy.

**Write errors under Program Files.** `zcdebug fix` checks the two folders that matter and says what it finds. UE4SS's own `ue4ss` folder
(where `zcdebug install` writes the mod and UE4SS writes its log) is often writable by your user already; `Content\Paks\LogicMods`
usually is not, and UE4SS's BPModLoaderMod (shipped with the pack) logs `CreateLogicModsDirectory: Error creating directory: Access is
denied` at every start because of it. The error is harmless (no Blueprint mods are used) but `zcdebug fix --elevate` removes it: it
creates what it can as your user, and for the rest asks Windows to run just that command (a `mkdir`, or an `icacls` that grants your
user write access to the `ue4ss` folder) through the normal UAC prompt. Without `--elevate` it only prints the commands, to run from
an Administrator terminal. It never touches the game's own files.

**After a game update.** The "SWZC" signature pack for UE4SS (`ue4ss/UE4SS_Signatures/*.lua` in the game folder) only accepts game builds it lists
by exe length, PE timestamp, image size and CodeView GUID. A new build makes UE4SS stop at start-up with `loaded fallback has no verified build
profile; signature rejected` in `UE4SS.log`, and ZcDebug never starts. The game's code is packed on disk, so the patterns can only be checked
against the running game. When the pack's author has not yet shipped the new build, the one thing that stopped build 199049 (game 1.2) was the
`builds` table at the top of each of the four scripts (FName_Constructor, GUObjectArray, ProcessLocalScriptFunction, StaticConstructObject): the
byte patterns still matched, and adding one table entry for the new exe, with the values read from its PE header, made UE4SS load and every
`zcdebug` command work. This is the pack's own safety check being extended by hand, not something the pack's author tested. `zcdebug fix` does
this for you: it reads the exe's identity (length, PE timestamp, image size, CodeView GUID) from its PE header, adds an entry to the `builds`
table of every script in the pack's format that does not list it yet, and leaves everything else alone (scripts it does not recognise are not
touched). The originals are copied first to `ue4ss\zcdebug-signature-backup\<time>` and `zcdebug fix --undo-signatures` puts the newest set
back; `--no-signatures` skips the step. Try it on a save you can lose, and prefer the author's own pack as soon as there is one. If a script
then reports `no validated loaded function entry`, the function itself changed and needs a new pattern.
Like `zc`, publish it as a single-file build (`dotnet publish src/ZeroCompany.DebugCli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/zcdebug-win-x64`).

**Driving a fight without anyone at the keyboard.** With the mod loaded the combat log shows what happened, and these calls make it happen:
`call BP_BrunoGameplayMaster_C RequestEndTurn --write` ends the player turn and `zcdebug loadstatus` / the log's `== round N` lines say when the
next one starts. A hero's action is started the way a radial-menu click does it, by calling `TryActivatingAbilityOfClass(<ability class>)` on the
`B_AbilitySelectionVM` whose `RepresentedCharacter` is that hero, which opens aiming; `ConfirmTargeting` on the `BRG_PlayerTargetingViewModel`
then fires it at the suggested target (the log shows `ABILITY`, `SHOT` and `HIT` for it; `AddSelectedTarget` picks another). Check each object with `IsValid()`
before calling a method on it: a method call on a null object crashes the game.
The game's own manual save can be called from the mod, which gives a save of any live state (for example right after a wave was announced):
`FindFirstOf('BrunoSaveGameSubsystem'):SaveGame(subsystem, '<name>', 1, 0, 0, 0, 0)`, where 1 is ManualSave; the file appears in the save folder
within a couple of seconds. Edit that save with `zc edit apply --copy`, put it in the slot the game is loaded from and `zcdebug load` it to see what the
edit did. Loading only works within the level the game is already in: loading a save from another mission leaves the current level and mixes
the two, so test an edit on a save from the same map, put in a slot file whose name belongs to that map (the game takes the level from the file name, and only loads names it listed at start-up, so overwrite an existing save of that map and keep a backup).
