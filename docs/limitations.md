# Limitations

- Built against the 2026 releases of the game. A game update can change the save format.
- You can add utility items and weapon mods you never had (Armory -> Add items; confirmed working in the game), but not other items
  or abilities, and combat stats (health, damage) aren't computed. Those need data this project hasn't been
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

What has been tried in the game: setting health (enemies to 1, allies to a high value), and switching a unit's team (Enemy,
Enemy2 and Friendly AI, both ways). Everything else below was checked against saves the game wrote and re-read after editing, but
not played through. The scenery and the enemy threat tier in particular have not been tried in the game.

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
  - **Enemy2** is the second hostile side of a **three-way battle** (Belly of the Beast is one). It is offered only in saves
    that already are one, and the editor refuses it elsewhere. In an ordinary mission the game puts Enemy2 units back on Enemy
    when it loads the save, so they never fight the other enemies. Giving such a mission the three-way turn structure was
    tried and does not work: the game loaded it as empty. The turn structure can be switched to standard and back, but only in a
    save that has it. This matches the game's own data (read with `zcassets`, see [development.md](development.md)): the team
    assets list only themselves as friends for Enemy and Enemy2, so they are hostile to each other, and the Enemy2 turn group
    exists only in the three-way turn structure.
- **Enemy threat tier** (Reinforcements tab, *Mission settings*): swaps the mission's enemy spawn set, for example Coil threat 10
  for 5. It is saved in fresh fights, but has been missing from saves made after loading a save and playing on (the game seems to
  drop it when it loads), and then there is nothing to edit. It doesn't
  change units already on the map. Every tier the game ships is offered (read from its own assets). Tiers of another faction are offered too and would
  bring that faction's units into the mission; nobody knows yet what the game does with that. For later waves, edit the wave
  definitions instead (delay, how often each may spawn, group).
- **Scenery:** a fire zone's turns left and a prop's health are plain numbers and are edited in place; whether a low turns-left value puts a
  fire out is not known. A prop that is already **destroyed** does not come back by restoring its health: the first attempt (health
  only) left the wreck in the game, and the game then dropped the prop's health entry on loading. What keeps a prop wrecked is a state
  tag in its actor record (Pristine, Damaged or Destroyed), so **Restore** rewrites that tag as well as the health. That was built
  from the game's own before/after saves and passes the verifier, but whether the game then rebuilds the prop has not been tried yet.
- **Objectives** only change the saved status (Active / Succeeded). A normal completion also fires follow-on steps (cutscenes,
  the next objective, reinforcements); flipping the status does not, so check the result in the game.
- **Countdown timers** can be raised or lowered while the game still saves them. Once a timer has finished it is no longer saved.
- **Reinforcements:** the game does not save which round the fight is on, so a wave's round can't be compared with "now". Cancel
  just pushes a wave to round 999. A wave's group can only be swapped for one the encounter already uses. A trigger can be
  switched off only once it has fired.
- **Restarting a fight:** the save's `SaveGameRestartDataCache` entry (used when a fight restarts) is left as it is. It holds no
  unit health, so a restart reloads the original encounter.
