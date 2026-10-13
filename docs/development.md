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
(an ordinary two-sided mission whose enemy threat tier is still saved, such as a Brentaal skirmish at the start of the fight).
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
zcdebug get <object> Prop  read a property
zcdebug set ... --write    change a property in the running game   (also: exec <console command>, eval <lua>)
zcdebug repl [--write]     the same, interactively
```

Reads need no flag; anything that changes the running game (`set`, `exec`, `eval`) is refused without `--write`. `<object>` is a
full object path or a short class name (its first live instance).

**How it talks to the game.** Lua has no sockets or JSON library, so both sides use three small files in
`%LOCALAPPDATA%\ZeroCompanyEditor\debug`: `request.txt` (written by `zcdebug`), `response.txt` (written by the mod) and
`heartbeat.txt` (the mod, about once a second, which is how `status` knows the game is up). Each is one tab-separated line ending
in a newline; see `Protocol.cs`. The mod polls every 200 ms and runs each command on the game thread.

**State of it.** The tool, the protocol and the installer are covered by tests against a fake mod. The Lua mod itself
(`src/ZeroCompany.DebugCli/mod/ZcDebug/Scripts/main.lua`) has **not been run against the game yet**: it uses the UE4SS Lua API
from memory (`FindAllOf`, `StaticFindObject`, `ForEachProperty`, `ExecuteInGameThread`), and it is not known whether a UE4SS build
that supports this game's Unreal version works with it. Errors from the mod come back as `error: ...` and also go to UE4SS's log.
Mods change the game's behaviour: use it for exploring, not in a save you care about, and expect that updating or repairing the
game (the EA app may repair modified files) can undo the install.


**Known problem.** On the EA App build 197649, installing the UE4SS 1.1.2 pack made the game start as a process and then show
nothing (no window, no `UE4SS.log`, no game log). Renaming `dwmapi.dll` (UE4SS's loader) back out of the way let the game start
normally, so UE4SS itself hangs this build and `zcdebug` can't be used with it until a UE4SS build is confirmed to work. The pack's
own diagnostic report lists this EA build as "runtime feedback pending". The game folder is under `Program Files`, so
`zcdebug install` needs an Administrator terminal there, or `zcdebug install --out <folder>` and a manual copy.
Like `zcsave`, publish it as a single-file build (`dotnet publish src/ZeroCompany.DebugCli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/publish/zcdebug-win-x64`).
