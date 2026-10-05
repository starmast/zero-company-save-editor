"""Base (facility) upgrades.

An upgrade is a recipe under .../Recipes/Upgrade_Facility/.  We never fake a
"completed" state (that would skip the game's own effects).  Instead we do what
the game does when you press Start: mark the recipe InProgress, stamp the
current turn and set the facility tag.  The game then finishes it on the next
turn advance and applies the real effects itself.

Verified against a game-written save: starting Droid Repair changed exactly
Status, TurnStarted and InProgressRecipeContext.FacilityTag.
"""
from __future__ import annotations

import re
from typing import Optional

from . import gamedata
from .gvas import Gvas, GvasError, Node, Reader

STATUS_PREFIX = "ERecipeStatus::"
UPGRADE_FACILITY_TAG = "BitReactor.Strategy.Facilities.Upgrade"
EXPEDITE_TURNS = 10          # progress credited / how far TurnStarted is pushed back
UPGRADE_DIR = "/Recipes/Upgrade_Facility/"
TIER_RE = re.compile(r"^(?P<stem>.+_)(?P<n>\d+)$")


def strategy_data(g: Gvas) -> Node:
    for w in g.root:
        if w.name == "GameInstanceSaveGameWrapper":
            ab = g.child(w, "ArchiveBytes")
            sd = g.child(ab, "StrategyData") if ab else None
            if sd is not None:
                return sd
    raise GvasError("StrategyData not found")


def _status(g: Gvas, v: Node) -> str:
    return g.get(g.child(v, "Status")).removeprefix(STATUS_PREFIX)


def _recipe_key(g: Gvas, v: Node) -> str:
    return g.get(g.child(v, "SoftRecipeClass"))


def _is_upgrade(path: str) -> bool:
    return UPGRADE_DIR in path and "/Unused/" not in path


def _pretty_group(s: str) -> str:
    return re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", s.replace("_", " "))


def _int(g: Gvas, parent: Node, name: str, default: int = 0) -> int:
    """Integer property, or `default` when the save omits it (the game skips default values)."""
    n = g.child(parent, name)
    return g.get(n) if n is not None else default


def fact_tags(g: Gvas, sd: Node) -> set[str]:
    """The save's active fact tags (native container: int32 count, then FStrings)."""
    node = g.child(sd, "FactTags")
    if node is None or node.size < 4:
        return set()
    out: set[str] = set()
    try:
        r = Reader(g.data, node.value_offset)
        for _ in range(r.i32()):
            out.add(r.fstring())
    except Exception:                                # unexpected layout: behave as "no tags known"
        return set()
    return out


def list_upgrades(g: Gvas) -> dict:
    sd = strategy_data(g)
    turn = _int(g, sd, "StrategyTurn")
    gd = gamedata.upgrades()                          # optional extracted game data
    tags = fact_tags(g, sd) if gd else set()
    roster = _int(g, sd, "RosterLevel")
    items: list[dict] = []
    done_names: set[str] = set()                    # Completed recipe base names
    for e in g.child(sd, "CompletedRecipes").children or []:
        path = _recipe_key(g, e.children[1])
        if _is_upgrade(path):
            done_names.add(path.rsplit("/", 1)[-1])
            items.append(_row(g, e.children[1], None, "Completed", path, gd, roster))
    active = g.child(sd, "ActiveRecipes")
    rows = []
    for i, e in enumerate(active.children or []):
        v = e.children[1]
        path = _recipe_key(g, v)
        if _is_upgrade(path):
            rows.append((i, v, path))
    names = {p.rsplit("/", 1)[-1] for _, _, p in rows}
    for i, v, path in rows:
        st = _status(g, v)
        row = _row(g, v, i, st, path, gd, roster)
        if st == "Available":
            row["can_start"], row["reason"] = _eligibility(path, names, done_names, gd, tags)
        elif st == "InProgress":
            row["can_expedite"] = row["progress"] < EXPEDITE_TURNS
        items.append(row)
    in_progress = sum(1 for r in items if r["status"] == "InProgress")
    return {"turn": turn, "in_progress": in_progress, "items": items,
            "roster_level": roster, "gamedata": gd is not None}


def _row(g: Gvas, v: Node, index: Optional[int], status: str, path: str,
         gd: Optional["gamedata.Upgrades"] = None, roster: int = 0) -> dict:
    after = path.split(UPGRADE_DIR, 1)[1].split("/")
    row = {
        "id": f"r{index}" if index is not None else None,
        "name": path.rsplit("/", 1)[-1],
        "title": g.get(g.child(v, "TitleProperty")),
        "category": _pretty_group(after[0]),
        "group": _pretty_group(after[1]) if len(after) > 2 else "",
        "status": status,
        "can_start": False,
        "can_expedite": False,
        "reason": "",
        "started": g.get(g.child(v, "TurnStarted")),
        "progress": g.get(g.child(v, "InProgressTurns")),
    }
    rec = gd.get(row["name"]) if gd else None
    if rec:
        info = gd.summary(rec)
        if info.get("title"):
            row["title"] = info["title"]                # the game's own upgrade name
        row["duration"] = info["duration"]
        row["cost"] = info["cost"]                    # {"Credits": 4000, "UpgradeFacilityResource": 2}
        row["description"] = info["description"]
        row["den_level"] = info["den_level"]
        # In-game level = save RosterLevel + 1 (save 4 is shown as LV 5 on the upgrade screen).
        row["den_met"] = info["den_level"] is None or roster + 1 >= info["den_level"]
    return row


def _eligibility(path: str, active_names: set[str], done_names: set[str],
                 gd: Optional["gamedata.Upgrades"] = None, tags: Optional[set] = None) -> tuple[bool, str]:
    """Prerequisites: the game's own fact-tag requirements when game data is available,
    otherwise a conservative naming rule (higher tiers need the previous tier completed)."""
    name = path.rsplit("/", 1)[-1]
    rec = gd.get(name) if gd else None
    if rec is not None:
        missing = [t for t in gd.summary(rec)["require_tags"] if t not in (tags or set())]
        if missing:
            src = gd.by_tag.get(missing[0])
            return False, "Needs " + (src.get("title") if src and src.get("title") else missing[0].rsplit(".", 1)[-1]) + " first"
        return True, ""
    m = TIER_RE.match(name)
    if m and int(m["n"]) > 1:
        prev = f"{m['stem']}{int(m['n']) - 1}"
        if prev in done_names:
            return True, ""
        if prev in active_names:
            return False, "Complete the previous tier first"
    return True, ""


def start_upgrade(g: Gvas, active_index: int, expect_name: str) -> Gvas:
    """Return a new Gvas with the given Available upgrade set InProgress."""
    sd = strategy_data(g)
    entries = g.child(sd, "ActiveRecipes").children or []
    if not 0 <= active_index < len(entries):
        raise GvasError("upgrade not found")
    v = entries[active_index].children[1]
    path = _recipe_key(g, v)
    if path.rsplit("/", 1)[-1] != expect_name or not _is_upgrade(path):
        raise GvasError("upgrade list changed; reopen the save")
    if _status(g, v) != "Available":
        raise GvasError(f"{expect_name} is not Available")
    turn = _int(g, sd, "StrategyTurn")

    base = g.path_of(v)                              # re-find nodes after each resize
    def at(*names):
        n = g.find_path(base + names)
        if n is None:
            raise GvasError(f"missing {'/'.join(names)}")
        return n

    g = g.set_string(at("Status"), STATUS_PREFIX + "InProgress")
    g.set(at("TurnStarted"), turn)                   # fixed-size, in place
    tag = at("InProgressRecipeContext", "FacilityTag", "TagName")
    g = g.set_string(tag, UPGRADE_FACILITY_TAG)
    # The game-written in-progress entry also carries reward tier 0.
    tiers = at("FulfilledRewardTiers")
    if not tiers.count:
        g = g.array_append_int(tiers, 0)
    return g


def expedite_upgrade(g: Gvas, active_index: int, expect_name: str) -> Gvas:
    """Make an InProgress upgrade look finished to the game's next turn tick.

    Raises its progress counter and pushes TurnStarted back (in place, no size
    change).  The game still performs the actual completion, so all real effects
    are applied by it.  Verified in-game: the game computes remaining turns from
    the progress counter (shown as e.g. -8) and completes the upgrade at the next
    turn change.
    """
    sd = strategy_data(g)
    entries = g.child(sd, "ActiveRecipes").children or []
    if not 0 <= active_index < len(entries):
        raise GvasError("upgrade not found")
    v = entries[active_index].children[1]
    path = _recipe_key(g, v)
    if path.rsplit("/", 1)[-1] != expect_name or not _is_upgrade(path):
        raise GvasError("upgrade list changed; reopen the save")
    if _status(g, v) != "InProgress":
        raise GvasError(f"{expect_name} is not in progress")
    turn = _int(g, sd, "StrategyTurn")
    prog, started = g.child(v, "InProgressTurns"), g.child(v, "TurnStarted")
    g.set(prog, max(g.get(prog), EXPEDITE_TURNS))
    g.set(started, max(0, min(g.get(started), turn - EXPEDITE_TURNS)))
    return g
