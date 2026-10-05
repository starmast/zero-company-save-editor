# Game data extractor (optional, local only)

Reads **your own** installation of the game (read-only) and writes JSON into `../../gamedata/`. That folder is
git-ignored on purpose: it is the game's content, so keep it on your machine and never publish it
(see [DISCLAIMER](../../DISCLAIMER.md)). The editor works fine without it.

## Requirements
- [.NET SDK](https://dotnet.microsoft.com/download) 10 (the project targets `net10.0`)
- The game installed (default `C:\Program Files\EA Games\Star Wars Zero Company`, or pass `--game <dir>`)
- Internet access on the first run: NuGet restores [CUE4Parse](https://github.com/FabianFG/CUE4Parse) (Apache-2.0) and it
  downloads the Oodle decompression library (proprietary; fetched at run time, never committed)
- For anything beyond plain text: a community-made **`.usmap`** mappings file matching your game version (for example
  "Star Wars Zero Company Unreal Mappings" on Nexus Mods). Download it yourself and put it in this folder (it is
  git-ignored). Without it only `strings` works.

## Commands
```
cd tools/extract
dotnet build
dotnet run --no-build -- strings                                   # English text -> gamedata/strings_en.json
dotnet run --no-build -- upgrades --usmap <file>.usmap             # gamedata/upgrades.json
dotnet run --no-build -- dump items GameData/ItemData/ --usmap <file>.usmap
dotnet run --no-build -- dump effects RosterUpgradeEffects/ CrossTrainingStatRewards/ GameData/Progression/ --usmap <file>.usmap
dotnet run --no-build -- dump focus GameData/FocusPointData/ --usmap <file>.usmap
dotnet run --no-build -- probe <AssetName> --usmap <file>.usmap     # print one asset's properties to a temp file
dotnet run --no-build -- file Config/DefaultGame.ini               # print a packaged text file
```
Options: `--game <install dir>`, `--out <dir>`.

Notes
- Pass `dump` path filters **without a leading `/`** (Git Bash rewrites those into Windows paths).
- The game's archives are Unreal Engine 5.6 IoStore containers that are not encrypted; data assets use unversioned
  properties, which is why a mappings file is required.
- The Python app expects these files in `gamedata/`: `strings_en.json`, `upgrades.json`, `raw_items.json`,
  `raw_effects.json`, `raw_focus.json`.
