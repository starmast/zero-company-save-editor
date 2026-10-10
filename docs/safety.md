# Safety and how it works

## What protects your save
- **An original is kept.** The first time a save is opened, an untouched copy is stored as `*.orig.sav` in the editor's data
  folder (`backups/<folder>/<save>/`).
- **Every write is snapshotted.** Before the save is changed, the current file is copied; the last 20 snapshots are kept.
  Restore any of them from **Command**.
- **Writes are verified.** The new file is built in a temp file and re-read. It is only used if the file opens cleanly, only
  the values you intended differ, the internal sizes and the save's metadata are consistent, and the edited values read back
  correctly. Then it replaces the save in one atomic step. If anything is off, nothing is written.
- **The game must be closed.** The editor refuses to write while the game appears to be running, because the game may
  overwrite your edit. A checkbox lets you override this if the check is wrong. It also refuses if the file changed since
  you opened it (for example after an autosave): reopen it first.
- **Save as copy** writes `<name>.edited-HHMMSS.sav` and never touches the original.
- **No network use** except the Oodle download during the first game-data extraction.

## How it works
A save is a ZIP containing an Unreal Engine 5 `GVAS` file whose data sits in nested byte blobs. The editor indexes the
property tree by byte offset and overwrites fixed-size numbers in place, so everything it doesn't understand is preserved
byte for byte.

A few actions change the file's size: starting an upgrade, healing, bringing someone back, completing a focus tree, and Coil
changes. For these the editor splices bytes and fixes up every enclosing size field and the sizes recorded in the save's
metadata. It then re-parses the result and refuses the write unless nothing else changed. Adding or removing characters,
recipes or items is not supported.

Upgrade behaviour was checked against saves written by the game itself: starting an upgrade reproduces the game's own recipe
entry byte for byte, and the game then completes it (applying its real effects) on the next turn change.

## How it was checked
The C# editor was compared against the earlier Python editor on the same save: identical fields and screens, and
byte-identical files for every scripted edit. See [development.md](development.md).
