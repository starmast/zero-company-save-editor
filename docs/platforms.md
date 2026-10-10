# Platforms

The editor is a single self-contained file: no .NET, Python or browser is needed. Download the archive for your system from the
[Releases](https://github.com/starmast/zero-company-save-editor/releases) page.

| System | Status | Notes |
|---|---|---|
| Windows 10/11 | Main platform | Saves are found automatically in `%LOCALAPPDATA%\SWZeroCompany\Saved\SaveGames`. |
| Linux | Runs natively, less tested | For a Proton/Wine install the editor looks inside Steam, Heroic and Lutris prefixes; otherwise use **Add folder**. Game-data extraction works if you point it at the game's files. |
| macOS | Runs natively, less tested | Builds are unsigned: right-click > Open, or run `xattr -dr com.apple.quarantine <folder>`. Copy saves over and use **Add folder**. The extractor's decompression library isn't available for macOS, so extract on another machine and use **Import database** ([game-data.md](game-data.md)). |

Linux and macOS builds are built and tested by CI, but the maintainers can only try them by hand on Windows, so reports are
welcome.

## Data folder
Settings, backups, the game database and the portrait cache live here:

| System | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\ZeroCompanyEditor` |
| Linux | `~/.local/share/ZeroCompanyEditor` |
| macOS | `~/Library/Application Support/ZeroCompanyEditor` |

Deleting it resets the editor (your saves are elsewhere and are not affected, but you lose the backups kept there).
