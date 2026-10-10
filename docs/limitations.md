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
