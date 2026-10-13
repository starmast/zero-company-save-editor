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
