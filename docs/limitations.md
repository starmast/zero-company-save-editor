# Limitations

- Built against the 2026 releases of the game. A game update can change the save format.
- You can't add items or abilities, and combat stats (health, damage) aren't computed. Those need data this project hasn't been
  able to verify.
- **Heal** removes injuries but doesn't add the game's own treatment history entry.
- **Bring back** works in the Personnel screens, the Den and on missions (tested in the game with the tutorial operator), but it
  was built by comparing a fallen operator with the living roster, not from a save the game wrote itself. A returning operator
  has no bonds with anyone recruited after their death and none of the roster's permanent health progression, so their Health
  can read lower than the others'.
- Linux and macOS are less tested than Windows ([platforms.md](platforms.md)).

## Combat tab
Combat saves are newer territory than the rest of the editor. **Try your edits on a copy first** ("Save as copy"), load it in the
game, and keep the automatic backup.

What has been tried in the game: setting health (enemies to 1, allies to a high value), and switching a unit's team. Everything
else below was checked against saves the game wrote and re-read after editing, but not played through.

- **Health** is the unit's saved base value. When the game loads the save it caps Health at the unit's real maximum, so a high
  value works as a full heal and does not stay at, say, 999. The editor can't show that maximum.
- **Dead** is only a flag. Set Health to 0 as well if you want the unit gone.
- **Effects** you copy from another unit are byte copies and keep that unit's context.
- **Team** changes the team tag, friendly flag, the tag inside the unit, and its player / enemy behaviour, in the main save and in
  the game's second copy of that behaviour (the save's spawned-actors file), the way the game does when a rescued civilian joins
  the squad. This was checked against a game-written before/after pair.
  - **Ally** (the player team) and **Friendly AI** (the civilian team: friendly, moved by the game, like the Umbaran fighters in
    the rescue mission) both work for **enemies**. Friendly AI is the safer choice, since those units already have the same stats.
  - A **hero** (a player-class unit) turned **Enemy** or **Friendly AI** loads but skips its turn: the AI has no abilities for it,
    and abilities come from the unit's class, not the save. The editor warns about this. Turning any unit into an Enemy also
    copies the AI area and faction tags and the action-point refresh values of the nearest enemy; that was not enough for heroes.
- **Objectives** only change the saved status (Active / Succeeded). A normal completion also fires follow-on steps (cutscenes,
  the next objective, reinforcements); flipping the status does not, so check the result in the game.
- **Countdown timers** can be raised or lowered while the game still saves them. Once a timer has finished it is no longer saved.
- **Reinforcements:** the game does not save which round the fight is on, so a wave's round can't be compared with "now". Cancel
  just pushes a wave to round 999. A wave's group can only be swapped for one the encounter already uses. A trigger can be
  switched off only once it has fired.
- **Restarting a fight:** the save's `SaveGameRestartDataCache` entry (used when a fight restarts) is left as it is. It holds no
  unit health, so a restart reloads the original encounter.
