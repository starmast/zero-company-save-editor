# Limitations

- Checked against the game up to version 1.2 (build 199049), with every save the game wrote from a campaign's start to its
  epilogue. A later game update can change the save format: run `zc save check` on a new save, and keep the backups.
- **Campaign options** (game 1.2) exist only in campaigns started in 1.2 or later; a campaign started earlier has none, even when it
  is saved again in 1.2, and the editor cannot add them. Changing one rewrites it everywhere the save keeps it (the campaign's options,
  its permanent facts and both header entries). Tried in the game: after loading edited copies, the game held exactly the edited options
  (permadeath off, two strikes, locked talents and weapon respec, enemy crits, no perfect shots, assist shots can miss) and not the old ones.
  Whether every option changes play the way it does when picked at the start was not played through.
- You can add utility items and weapon mods you never had (Armory > Add items; confirmed working in the game), but not other items
  or gear, and combat stats (health, damage) aren't computed. Those need data this project hasn't been
  able to verify.
- **Roster capacity** can be set higher than the Den's upgrades allow, and a roster above its capacity loads without trimming (tried in the game), but the roster screens
  look odd when the numbers do not fit, for example 18/15.
- **Weapons and gear** (Personnel > Loadout): the editor reads every operator's weapon and gear slots. It can replace a weapon and its gear kit (damaged or not) by copying
  an undamaged vanilla weapon of the same class from another operator or, when there is none, one of four generic weapons it ships (rifle, pistol,
  longarm, repeater, captured from game build 199049 and untried on other builds). It cannot pick any other weapon type, restore a modded weapon,
  or fix a weapon of a class with no generic weapon unless you pick a generic weapon of another class yourself. Gear slots take any owned item that fits
  (utility item or weapon mod), taking it from its holder. Neither is confirmed
  in the game. Operators who are not on the roster (a VIP, say) are not listed.
- **Gear slots** raise every operator at once, never lower them, and need the save to have a bonus slot record (any save that has had a slot upgrade does).
- **Recruiting** cannot be steered from a save. The six candidates the Recruitment screen offers are generated each time the Den loads (the save keeps no list and
  no seed for them: a candidate kept her name and talent but came back with a different specialization and weapon after a reload), so you cannot choose who
  appears. What the editor can change is the room to recruit (Roster capacity) and, afterwards, the new operator: abilities (Personnel > Focus Tree > Give an
  ability) and gear slots.
- **Profile** (`SharedGameSettings.sav`): achievement counts can be changed, and the game keeps an edited count (tried: a count lowered to
  0 stayed 0 after the game started and loaded a save), so the next thing the game counts adds to it. Whether a count raised to its target
  this way makes EA or Steam show the achievement was not tried. A counter the profile has not started can be added (tried: Angry Beeps
  started at 1, below its target; the game read it, kept it when it rewrote the profile and through loading two saves). The bond
  achievements' counters (Heroes On Both Sides, Lost Causes ...) probably hold a bond level the game sets itself, so the game may
  overwrite a count started or changed by hand. The unlocks cannot be added here.
- **Zone rewards:** unticking a claimed tier was tried in the game on Zone Jenth: the Hutt Roulette Prize became claimable again, claiming it
  added an Exotic item and the game counted it towards There Will Be No Bargain. Achievement counts live in the game's profile file
  (`SharedGameSettings.sav`, see Profile above); whether a platform (EA, Steam) then shows the achievement is up to it.
- **Galaxy:** a Den mission's and an operation's time on offer were tried in the game: after one cycle, a mission moved earlier and an
  operation aged by one were gone, while a mission given full time and an operation set back to 0 were still on offer. The crisis
  mission's **Coil upgrade** drop-down has not been tried (no save so far had a crisis mission on offer).
- **Give an ability** can only copy an ability from an operator who has it in the same save, and it arrives at level 1.
- **Give a bond trait** offers all 32 traits. A trait is one saved gameplay effect, so it is made from a trait record already in the save
  or, in a campaign where nobody has learned one, from the operator's own health-progression record with its stat values removed. Tried in
  the game both ways: the heroes carried the traits and their abilities after loading, and a save the game wrote afterwards kept them.
  The game normally limits which traits an operator can learn (from a partner of another class, at certain bond levels); the editor does
  not, so an operator can be given their own class's traits too. Not every trait was played through, and traits cannot be taken away here.
- **Give a stat upgrade** copies the same stat's record from another operator in the save when there is one. Otherwise it copies another
  record and, with the game data extracted, fills in the stat's modifier entries and values per stack from it (the saved layout matches the
  game data for the stat checked); without the game data the stat arrives with no values. Tried in the game: the stats were carried after
  loading, but not every stat was played through, so check an operator's stats after loading. A modifier the game works out from other attributes (Reduce range penalties) is written as 0.
- **Heal** removes injuries but doesn't add the game's own treatment history entry.
- **Bring back** works in the Personnel screens, the Den and on missions (tested in the game with the tutorial operator), but it
  was built by comparing a fallen operator with the living roster, not from a save the game wrote itself. A returning operator
  has no bonds with anyone recruited after their death and none of the roster's permanent health progression, so their Health
  can read lower than the others'.
- Linux and macOS are less tested than Windows ([platforms.md](platforms.md)).

## Combat tab
Combat saves are newer territory than the rest of the editor. **Try your edits on a copy first** ("Save as copy"), load it in the
game, and keep the automatic backup.

Most of what follows was loaded and played in the game: health, the action-point limit, team switches (Enemy, Enemy2 and Friendly AI,
both ways), destroying and restoring scenery, objective status and counters, timers, the round, reinforcement waves and definitions,
bringing more of the roster in, and which unit stats the game keeps. Each item says what happened, including what did not work.
Everything was also checked against saves the game wrote and re-read after editing.

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
    save that has it. This matches the game's own data (read with `zc assets`, see [development.md](development.md)): the team
    assets list only themselves as friends for Enemy and Enemy2, so they are hostile to each other, and the Enemy2 turn group
    exists only in the three-way turn structure.
- **Mission enemy set** (Galaxy screen, *Missions in the Den*): changes which faction and tier an available mission spawns when you
  launch it. This is the intended way to change who you fight; confirmed in the game (an Imperial mission switched to Coil spawned Coil units and waves). The crisis-elite set that
  missions also carry is not offered.
- **Enemy threat tier** (Reinforcements tab, *Mission settings*): swaps the mission's enemy spawn set, for example Coil threat 10
  for 5. **It does nothing for the encounter you are in.** The tier is only read when an encounter starts; after that the enemies on the
  map and the reinforcement waves already have their groups fixed in the save. This was tried on a hero mission: with the set changed
  to Separatists the enemies and both queued waves stayed Coil, because the save already held those groups (hero missions also use
  hand-made groups of their own). Real missions in the saves so far have one encounter (or are in their last), so the setting only has
  anything to act on if a *later* encounter exists; the editor counts them and says so next to the drop-down. To change who turns up,
  edit the units or the groups on the Reinforcements tab. The setting is also missing from saves made after loading a save and playing
  on (the game seems to drop it). Every tier the game ships is offered (read from its own assets), and other factions' tiers would
  bring that faction's units into a later encounter; what the game does with that has not been tried.
- **Scenery:** a fire zone's turns left and a prop's health are plain numbers and are edited in place. A fire goes out when its turns left
  reach 0: tried on a fire set from 2 to 1, whose flames were out after the next round (the fire's actor stays in the level, empty). A prop that is already **destroyed** does not come back by restoring its health: the first attempt (health
  only) left the wreck in the game, and the game then dropped the prop's health entry on loading. What keeps a prop wrecked is a state
  tag in its actor record (Pristine, Damaged or Destroyed), so **Restore** rewrites that tag as well as the health. That was built
  from the game's own before/after saves and passes the verifier, and it works: the destroyed props came back in the game.
  **Destroy** is the mirror image (an intact prop's record becomes Destroyed, flags (0,0,1) as the game wrote them for props it saw wrecked,
  and no health entry is added since the game drops those on load); it works too: four barricades destroyed this way vanished in the game
  (the game hides them and turns their collision off, so units can walk through the gap).
- **Objectives** only change the saved status (Active / Succeeded). A normal completion also fires follow-on steps (cutscenes,
  the next objective, reinforcements); flipping the status does not. Tried in the game: marking an interact objective Succeeded greyed it
  out in the objective list, but its counter stayed at 0 of 1 and the objective that normally follows did not start. A real completion
  also records the interaction count, removes the objective's waypoint and starts the next one with about two dozen runtime fields, so
  objectives that have not started cannot be edited.
  Played through in the game on Brentaal 035: completing the interact objective the game's own way started the next one (hold the zone for 3
  points), finishing that started the evacuation, and getting all four operators into the evacuation region ended the mission.
- **Countdown timers** can be raised or lowered while the game still saves them. Once a timer has finished it is no longer saved. Lowering
  one from 6 to 2 was confirmed in the game (the countdown showed 2 on loading).
  Tried on Brentaal 030 (rescue with a 6-turn timer): with 1 turn left the timer ran out at the next round and failed the rescue objective; with 10 turns left it counted
  down to 8 over two rounds without failing; the total can be changed too and takes effect on loading.
- **Reinforcements:** the game does save which round the fight is on (one number in the tactical game state, shown and editable as **Current
  round**; a copy set to round 8 loaded as round 8, and the next round after loading a copy set to 12 was 13), so a wave's round can be
  compared with it. Tried in the game on Brentaal 035: a queued wave's **round** and **group** and a wave definition's **group** and
  **times triggered** all took effect (a wave moved from round 7 to 9 arrived two rounds later; a definition whose times triggered equalled its
  times to spawn did not spawn when its trigger fired). A definition's **delay** did not change when a wave arrived, for a wave started by
  its trigger or by the strength condition below (0 and 3 gave the same round), so treat that field as unproven. Cancel
  just pushes a wave to round 999. A wave's group can only be swapped for one the encounter already uses. A trigger can be
  switched off only once it has fired. An automatic wave may also be held back by a condition that is not in the save: in Brentaal 035 the
  wave only queued once the enemy force's combined strength (each unit counts 100) was below a threshold of 150 (raising that threshold in
  the running game queued the wave at once). The save keeps only a reference to that condition, so its value cannot be edited here, and
  a short delay alone will not bring reinforcements while the enemy is at full strength. Other missions may use other conditions.
- **Bringing more of the roster into a fight** (Combat, *Roster* tab): the editor adds the operator's roster GUID to the mission squad
  (`MissionSquadCharacterIDs`, and the in-progress mission's own squad list), and the game spawns them with the squad when the save loads.
  Tried on Brentaal 030 with the whole roster: all 19 operators spawned and the game's portrait bar showed them, and a round played. They arrive
  with the health their roster entry records (injured operators arrive hurt, so the tab can heal them as they join). The game places them
  itself around the map's single start point: the first slots are outside the playable area, so an operator can arrive where they cannot move, and
  the rest share one spot. A position written into the save for an operator the game has not saved yet is ignored, so placing them takes a second
  step: load the save in the game, save it there, open that save in the editor and set the new operators' Position (Allies tab). Tried: a position
  set that way was kept on the next load. Whether the mission counts the extra operators when it ends (experience, bonds) is not tried.
- **Restarting a fight:** the save's `SaveGameRestartDataCache` entry (used when a fight restarts) is left as it is. It holds no
  unit health, so a restart reloads the original encounter.
