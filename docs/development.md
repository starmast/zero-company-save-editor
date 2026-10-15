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
src/ZeroCompany.Core/      GVAS reader/patcher, save container, editable model, actions (Actions/Combat/ for combat saves), apply/verify/backup service, view models
src/ZeroCompany.GameData/  game-data extractor (CUE4Parse) and the typed game database
src/ZeroCompany.App/       Avalonia desktop UI (MVVM): screens, theme, portraits
tests/ZeroCompany.Tests/       core, game-data and service tests
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

## zcsave: a read-only save profiler
`src/ZeroCompany.Cli` builds `zcsave`, a command-line tool for looking inside saves while developing. It never writes to a save.

```
dotnet run --project src/ZeroCompany.Cli -- list
dotnet run --project src/ZeroCompany.Cli -- profile "COMBAT BASE 4.0"
dotnet run --project src/ZeroCompany.Cli -- diff "COMBAT BASE 4.0" latest
dotnet run --project src/ZeroCompany.Cli -- find latest Enemy2
dotnet run --project src/ZeroCompany.Cli -- dump latest TacticalSaveGameWrapper/ArchiveBytes/CharacterInfos/[6] --depth 3
```

A save is a path, a file-name fragment, a save comment, or `latest`. `profile` shows the teams, units, objectives, waves and scenery,
`diff` shows what changed between two saves (units first), `find` locates a name or value, and `dump` prints a property tree.

The published tool is a self-contained single file (`dotnet publish src/ZeroCompany.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/zcsave-win-x64`);
on machines with Application Control a loose `zcsave.dll` may be refused where the single-file exe is not.

## zcdebug: talking to the running game
`src/ZeroCompany.DebugCli` builds `zcdebug`, a command-line debugger for the live game. It does not touch the game process
itself. Instead it talks to a small Lua mod, **ZcDebug**, that runs inside the game through [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)
(a third-party Unreal mod loader that you install yourself; `zcdebug` never downloads or patches anything).

```
zcdebug install            copy the ZcDebug mod into UE4SS's Mods folder (zcdebug uninstall removes it)
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
zcdebug load <save name> --write   load a save by file name, without .sav (the game must already list it, see below)
zcdebug repl [--write]     the same, interactively
```

Reads need no flag; anything that changes the running game (`set`, `attr`, `fx`, `load`, `exec`, `eval`) is refused without `--write`.
`<object>` is a full object path or a short class name (its first live instance). A path after the object is not walked for
you: `get <object> A.B.C` walks `A.B.C` inside the object, but the object itself must be a real object path (for example
`...PersistentLevel.Char_Hero_HAWKS_Control_C_0.BitReactorHealthSet_0`). After `zcdebug install`, reload the mods in the game
(Ctrl+R in the UE4SS window) so the game runs the new Lua.

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
files you made for testing, never a save you care about. `zcdebug saves` shows which files the game should accept: a file created after the game started is marked NEW (a heuristic from file creation times; an overwritten file keeps its creation time, so it stays "listed"). The editor does not depend on any of this: it only reads and writes saves.

**Do not bulk-scan from Lua.** An `eval` that looped over every character walking each one's `ActiveGameplayEffects` crashed the game
(`EXCEPTION_ACCESS_VIOLATION` reading address 0x18). Read one named unit at a time and stop after the first timeout. Unreal's
crash reports are in `%LOCALAPPDATA%\SWZeroCompany\Saved\Crashes`. Errors from the mod come back as `error: ...` and also go to
UE4SS's log. Mods change the game's behaviour: use it for exploring, not in a save you care about, and expect that updating or
repairing the game (the EA app may repair modified files) can undo the install.

**UE4SS.** An earlier try with the UE4SS 1.1.2 pack made this build start as a process and show nothing (no window, no log), and
renaming `dwmapi.dll` back out of the way fixed it. A later install loaded and ran mods fine on the same build, so it is not a
permanent problem, but which UE4SS build works is not recorded here. The game folder is under `Program Files`, so `zcdebug install`
needs an Administrator terminal there, or `zcdebug install --out <folder>` and a manual copy.
Like `zcsave`, publish it as a single-file build (`dotnet publish src/ZeroCompany.DebugCli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/zcdebug-win-x64`).

## zcassets: browsing the game's own assets
`src/ZeroCompany.AssetsCli` builds `zcassets`, a read-only browser for the game's packages. It uses the same install folder,
`.usmap` and Oodle library the editor already knows about (extract game data once in the editor first), never downloads anything
and never writes to the game. Mounting about 180,000 files takes a few seconds.

```
zcassets find TurnStructure                      asset package paths containing a word
zcassets dump BP_TurnStructure_ThreeWayBattle     one asset's objects as JSON (--merged adds inherited properties)
zcassets dump Enemy2Team --grep FriendlyTeamTags  only the properties that mention a word
zcassets scan Blueprints/Objects Enemy2          search inside every asset whose path contains a word
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
