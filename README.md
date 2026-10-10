# Zero Company Save Editor

[![tests](https://github.com/starmast/zero-company-save-editor/actions/workflows/tests.yml/badge.svg)](https://github.com/starmast/zero-company-save-editor/actions/workflows/tests.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Edit your **Star Wars: Zero Company** save: credits, focus points, bonds, upgrades, the roster and more, in an app that
looks like the game. Every change is backed up and checked before it is written.

> **Unofficial fan project**, not affiliated with Disney, Lucasfilm, Electronic Arts or Bit Reactor. No game content is
> included. Editing saves is at your own risk: please read the [disclaimer](DISCLAIMER.md) and keep your own backups.
> For offline, single-player use.

<p align="center">
  <img src="docs/screenshots/personnel-bonds.png" alt="Personnel > Bonds screen" width="900">
</p>

## Get started
1. **Download** the file for your system from the [Releases](https://github.com/starmast/zero-company-save-editor/releases)
   page, unpack it and run `ZeroCompanyEditor`. Nothing needs installing.
2. **Close the game**, then pick a save. On Windows your saves are found automatically; otherwise use **Add folder**.
3. **Make your changes** and press **Apply to save**. Edits stay pending until then, and a backup is made first.

Tip: copy a save somewhere first and try your edits on the copy. Load it in the game, and only then edit the real one.

Optional but nice: open the **Game data** tab once to show real item names and upgrade costs instead of internal names
([how](docs/game-data.md)).

## What you can do
- **Command** - turn, level, XP, credits and other resources; restore any backup.
- **Personnel** - focus points, bonds, ability levels, roster order, bring back fallen operators.
- **Upgrades** - start base upgrades and optionally finish them at the next turn change.
- **Armory**, **Galaxy** (influence, Coil upgrades), **Medbay** (heal injuries) and **Advanced** (every raw value).

See the [user guide](docs/user-guide.md) for each screen in detail.

## Staying safe
Before anything is written the editor keeps an untouched copy of your save, snapshots the current file, and checks the
result. It won't write while the game is running or if the save changed since you opened it. "Save as copy" never touches
the original. Details are in [docs/safety.md](docs/safety.md).

## Windows, Linux and macOS
Windows is the main platform. Linux and macOS builds exist but are less tested, and macOS can't extract game data itself.
See [docs/platforms.md](docs/platforms.md).

## More
| | |
|---|---|
| [User guide](docs/user-guide.md) | every screen and feature |
| [Game data](docs/game-data.md) | extracting names and costs from your own install |
| [Safety and how it works](docs/safety.md) | backups, verification, what gets edited |
| [Platforms](docs/platforms.md) | Windows, Linux, macOS notes and data folders |
| [Limitations](docs/limitations.md) | what it can't do yet |
| [Development](docs/development.md) | building, testing, project layout, credits |
| [Changelog](CHANGELOG.md) | what changed in each version |

Found a problem or have an idea? [Open an issue](https://github.com/starmast/zero-company-save-editor/issues/new/choose).
Please do **not** attach save files, game files or extracted game data.

## License
Source code: [MIT](LICENSE). The license covers this repository's code only; see [DISCLAIMER.md](DISCLAIMER.md) for
everything else.
