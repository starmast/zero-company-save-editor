# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Medbay** screen: beds, bacta tank, costs and injured operators, read from the save (view-only). Injured operators
  get a marker on the Personnel roster strip.

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
