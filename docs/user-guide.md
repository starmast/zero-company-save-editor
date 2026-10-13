# User guide

How each screen works. Everything you change stays **pending** (shown in the bar at the bottom, with a list of what will
change) until you press **Apply to save**. **Discard** drops it all; **Save as copy** writes a separate file and leaves your
save alone.

![Command screen](screenshots/command.png)

## Opening a save
The **Saves** page lists the folders the editor knows about, each save with its in-game screenshot, name and turn. Use
**Add folder...** to point it at any other folder of `.sav` files. Settings and databank files can't be edited and are
listed as such.

The bar at the top shows your credits, intel, other resources, level and turn. Click any of them to type a new value.

## Command
Save details, campaign values (strategy turn, roster level and XP, base focus points), the stockpile, and your **backups**.
Press **Restore** on a backup to put it back; the current file is saved as a new backup first, so nothing is lost.

## Personnel
Pick an operator from the strip at the bottom. Hold and drag an operator's icon sideways (or use **Earlier / Later**) to change
the roster order; the game's own strip and mission select follow it.

- **Overview** - unspent focus points (the total moves with it so points spent stay the same) and training and stat effects.
- **Bonds** - partners placed on the game's 0-8 scale. Click a face to change that bond's level, progress or cross training.
- **Focus Tree** - set ability levels using the game's own focus costs. Choose **Spend unspent focus** (levelling costs
  what you have) or **Grant the focus** (levelling is free; the total rises to match).
- **Complete tree** - shown when an operator (such as the tutorial operator) is missing the higher tiers of some abilities.
  It copies them from another operator who has the same abilities.
- **Memorial** - a fallen operator's page has **Bring back**, which returns them to the roster ([limits](limitations.md)).

## Upgrades
Facilities, Crew and Weapons rows on a Den Level 1-10 timeline. Click an upgrade to see its cost, duration and requirements,
then **Queue: start**. Starting works like the game's Build button; the game finishes the upgrade at the next turn change.
Tick **Also expedite upgrades I start** to have it finish at the very next one. No cost is charged. **Queue all startable**
queues everything that can be started.

## Armory
Utility items and weapon mods, with how many you own. Use the search box to find one.

## Galaxy
Influence, contacts and reward tier for each region. Below, the **Coil upgrades** the enemy has gained from failed Crisis
missions: **Remove** puts the crisis back to available (it can be failed again), **Prevent** marks it as won. Both work on one
upgrade or all at once.

## Medbay
Beds, the bacta tank, their costs, who is injured and who is in treatment. **Heal** removes an operator's injuries the way the
bacta tank does, for free and instantly.

## Combat
Open a save made **during a fight** (a combat autosave such as `Autosave_EncounterStart_...`, or a manual save you made in one).
On any other save this tab just says so. A header shows the save's name, and the buttons on the right of the sub-tabs are quick
actions for that tab.

![Combat screen](screenshots/combat.png)

- **Allies, Enemies, Friendly AI** - one card per unit. Edit its **Health**, base max health, **action points**, movement points,
  **Advantage** and **position** (X Y Z in world units). Open **All stats** for every other base value (aim, attack, critical
  chance and so on) and **Effects** to remove an effect, change its stacks, or copy an effect from another unit. **Mark dead**
  sets the dead flag. The **Team** dropdown moves a unit to another side: *Ally* (you control it), *Friendly AI* (friendly, but the
  game moves it, like a rescued VIP or an allied fighter) or *Enemy*. In a **three-way battle** (a mission with two hostile sides
  fighting each other as well as you, like Belly of the Beast) there is also *Enemy2*, the second hostile side, and units can be
  moved between the two ([limits](limitations.md#combat-tab)). Quick actions: **Heal allies to full** and **Enemies to 1 HP**
  (both enemy sides).
- **Objectives** - each mission objective with its status; **Mark succeeded** or **Set back to active**. Counters such as
  occupy-region points and interactions are editable too. Above them, **Timers** show the turns left on a countdown, what it
  fails when it runs out, and let you raise it.
- **Reinforcements** - the waves already on their way (the round each arrives, its spawn group, **Cancel wave**), the encounter's
  wave definitions (delay, how many times each may spawn, group) and the triggers that start waves (switch off). **Mission
  settings** above them has the **enemy threat tier** the mission draws its units from (for example *Coil threat 10*; tiers of
  other factions are offered too, marked experimental) and, in a three-way battle, the **turn structure**. The threat tier is only
  saved in fresh fights; it has been missing from saves made after loading a save and playing on (the game seems to drop it), and
  then there is nothing to edit. For later waves use the wave definitions instead.
- **Scenery** - the fire zones (turns left; they start near 9999, so they effectively never burn out) and every destructible prop
  with its health. **Restore scenery** sets every damaged or destroyed prop back to full health.

The game caps Health at each unit's real maximum when it loads the save, so typing a high value is a full heal. Changes are
pending like everywhere else: **Apply to save**, or **Save as copy** to try them first.

## Advanced
Every editable value as a flat list, grouped the way the save stores it, with a filter, plus a raw property tree that lets you
edit numbers in place. Use with care.

## Game data
Real names, descriptions and costs; see [game-data.md](game-data.md).
