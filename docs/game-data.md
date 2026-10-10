# Game data (optional)

Out of the box the editor works from the save alone and shows internal names. With **game data** it shows real item names and
descriptions, upgrade costs and durations, Den Level requirements, focus costs and Coil upgrade text.

The editor reads **your own game install** for this, read-only. Nothing is written to the game, and the result stays on your
computer. It is the game's own content, so never publish it (see the [disclaimer](../DISCLAIMER.md)).

## Extract it
Open the **Game data** tab and press **Extract game data**. You need:

1. **The game installed**, or its `Paks` folder copied somewhere. The tab fills in the usual install location when it finds it.
   Otherwise press **Browse...** and pick the folder that contains `SWZeroCompany`.
2. **A `.usmap` mappings file** for your game version. This is a community-made description of the game's data layout (search
   for "Star Wars Zero Company Unreal Mappings" on Nexus Mods). Download it yourself; it is not included. Press **Browse...**
   and pick it.
3. **Internet access the first time.** The extractor ([CUE4Parse](https://github.com/FabianFG/CUE4Parse)) downloads the Oodle
   decompression library, which is proprietary and never part of this project.

It takes a few seconds, shows a log, and then every screen picks up the real names. If the game updates, extract again.

## Where it is kept
One small file called `gamedata.db.json` in the editor's data folder (see [platforms](platforms.md)). Delete it to go back to
internal names. **Where is my database?** copies its path.

## Import (for macOS, or to share between your own machines)
The extractor can't run on macOS. Extract on a Windows or Linux machine, copy `gamedata.db.json` over, and use
**Import database...**. Only do this with a file you made yourself from your own install.

## If it fails
- *Could not find the game's Paks folder* - pick the folder that contains `SWZeroCompany` (or `SWZeroCompany/Content/Paks`).
- *A .usmap mappings file is required* - the game's data assets can't be read without one that matches your game version.
- Failures during the Oodle download usually mean no internet or a blocked connection; try again, or copy the library next
  to the editor's data folder.
