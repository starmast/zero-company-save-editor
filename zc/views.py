"""Screen-shaped view models for the game-style UI.

The flat field list (``SaveModel.fields``) mirrors how the save stores things; these builders
regroup the same fields the way the game presents them (header resource bar, Personnel,
Command ...).  Every editable value is a *ref*: ``{id, value, min, max, kind}`` where ``id`` is the
existing field id, so edits keep flowing through the unchanged /api/apply pipeline.
"""
from __future__ import annotations

from typing import Optional

import re

from . import coil, gamedata
from .domain import Field, SaveModel, guid_hex

BOND_OFFSET = 4          # in-game bond scale 0-8 = save level (-4..4) + 4
LEVEL_OFFSET = 1         # in-game "LV n" = save RosterLevel + 1

BOND_WORDS = ["Very low", "Low", "Low", "Poor", "Neutral", "Good", "High", "High", "Very high"]

# Header order and display names (the game's own wording).
RESOURCE_ORDER = [
    ("Resource_Credits", "Credits"),
    ("Resource_Intelligence", "Intel"),
    ("Resource_UpgradeFacilityResource", "Capacitors"),
    ("Resource_Contacts", "Contacts"),
]


def ref(f: Optional[Field]) -> Optional[dict]:
    if f is None:
        return None
    return {"id": f.id, "value": f.value, "min": f.min, "max": f.max, "kind": f.kind}


def header(m: SaveModel) -> dict:
    res = []
    for asset, default in RESOURCE_ORDER:
        r = m.resources.get(asset)
        if r and r["field"] is not None:
            res.append({"key": asset.replace("Resource_", ""), "label": r["label"] or default,
                        "description": r["description"], **ref(r["field"])})
    lvl, xp, turn = (m.prog.get(k) for k in ("roster_level", "roster_xp", "turn"))
    return {
        "resources": res,
        "level": {**ref(lvl), "offset": LEVEL_OFFSET} if lvl else None,
        "xp": ref(xp),
        "turn": ref(turn),
    }


def _operator(m: SaveModel, op: dict, portraits: set[str]) -> dict:
    g = op["guid"]
    bonds = []
    for b in m.bond_rows:
        if g not in (b["a"], b["b"]) or b.get("level") is None:
            continue
        other = b["b"] if b["a"] == g else b["a"]
        bonds.append({
            "partner": other,
            "partner_name": m.name_of(other),
            "partner_in_roster": other in m.roster or other in m.dead,
            "level": ref(b["level"]), "progress": ref(b["progress"]), "cross": ref(b["cross"]),
            "highest": ref(b.get("highest")),
            "game_level": b["level"].value + BOND_OFFSET,
        })
    bonds.sort(key=lambda x: (-x["game_level"], x["partner_name"]))
    return {
        "guid": g,
        "name": op["name"],
        "role": op["role"],
        "dead": op["dead"],
        "injuries": op["injuries"],
        "portrait": f"/api/portrait/{g}.png" if g in portraits else None,
        "focus": ({**ref(op["focus"]), "total": op["total_focus"], "total_ref": ref(op["total_field"])}
                  if op["focus"] else None),
        "abilities": [{"tag": a["tag"], "name": a["name"], "kind": a["kind"],
                       "level": ref(a["level"]), "spent": ref(a["spent"]),
                       "current_level": a["base_level"], "thresholds": a["thresholds"]} for a in op["abilities"]],
        "effects": [{"asset": e["asset"], "title": e["title"], "description": e["description"],
                     "stacks": ref(e["stacks"]), "magnitudes": [ref(x) for x in e["magnitudes"]]}
                    for e in op["effects"]],
        "bonds": bonds,
    }


def personnel(m: SaveModel, portraits: set[str]) -> dict:
    order = [g for g in m.roster if g in m.operators and g not in m.dead]
    ops = [_operator(m, m.operators[g], portraits) for g in order]
    memorial = [_operator(m, m.operators[g], portraits)
                for g in m.operators if g in m.dead]
    cross_total = sum(b["cross"].value for b in m.bond_rows if b.get("cross") is not None)
    return {"roster": ops, "memorial": memorial, "cross_training_total": cross_total,
            "bond_words": BOND_WORDS}


def command(m: SaveModel, info: Optional[dict]) -> dict:
    info = info or {}
    return {
        "save": {"title": info.get("comment"), "world": info.get("worldName"),
                 "created": info.get("creationTime"), "mode": info.get("gameMode"),
                 "type": info.get("saveGameType"), "autosave": info.get("autoSaveType"),
                 "difficulty": info.get("currentDifficultyLevel"), "permadeath": info.get("bPermaDeath")},
        "progression": {k: ref(v) for k, v in m.prog.items()},
    }


# -------------------------------------------------------------------- medbay
MEDBAY_SLOTS = {"BedOne": "Bed 1", "BedTwo": "Bed 2", "BedThree": "Bed 3", "BedFour": "Bed 4",
                "BactaTank": "Bacta tank"}


def medbay(m: SaveModel, portraits: set[str]) -> dict:
    """Beds, the bacta tank, costs, who is injured and who is currently being treated.

    Values are the game's own: `Facts.Values.Medbay.*` (slot counts) and `MedbayCost.*` (credits),
    which match the in-game Medbay screen.  Read-only for now (see CHANGELOG).
    """
    g = m.g
    f = lambda name, d=0: int(round(m.fact(name, d) or 0))
    injured = []
    for op in m.operators.values():
        if op["injuries"] and not op["dead"]:
            injured.append({"guid": op["guid"], "name": op["name"], "injuries": op["injuries"],
                            "portrait": f"/api/portrait/{op['guid']}.png" if op["guid"] in portraits else None})
    injured.sort(key=lambda x: x["name"])

    treating = []
    ar = g.child(m.sd, "ActiveRecipes")
    for e in (ar.children or []) if ar else []:
        val = e.children[1]
        cls = g.get(g.child(val, "SoftRecipeClass")) if g.child(val, "SoftRecipeClass") else ""
        name = cls.rsplit("/", 1)[-1]
        if "InjuryRecover" not in name or g.get(g.child(val, "Status")).endswith("Available"):
            continue
        ctx = g.child(val, "InProgressRecipeContext")
        ids = g.child(ctx, "AssignedCharacterIDs") if ctx else None
        who = [m.name_of(guid_hex(g, x)) for x in ((ids.children or []) if ids else [])]
        slot = next((label for key, label in MEDBAY_SLOTS.items() if f"_{key}_" in name), name)
        treating.append({"slot": slot, "operators": who,
                         "started": g.get(g.child(val, "TurnStarted")) if g.child(val, "TurnStarted") else None})
    return {
        "beds": f("Facts.Values.Medbay.TotalBeds"), "tanks": f("Facts.Values.Medbay.TotalTanks"),
        "cost": {"bed": f("Facts.Values.MedbayCost.BedSingle"), "tank": f("Facts.Values.MedbayCost.TankSingle"),
                 "bed_double": f("Facts.Values.MedbayCost.BedDouble"), "tank_double": f("Facts.Values.MedbayCost.TankDouble")},
        "injured": injured, "treating": treating,
    }


# --------------------------------------------------------------- armory / galaxy
KIND_ORDER = {"Utility": 0, "Modification": 1, "Other": 2}


def armory(m: SaveModel) -> dict:
    items = []
    for it in m.inventory:
        if it["field"] is None:
            continue
        kind = "Utility" if it["section"] == "Utility" else "Modification" if it["section"] == "Modifications" else "Other"
        items.append({"asset": it["asset"], "name": it["label"], "kind": kind, "tier": it["tier"],
                      "rarity": it["rarity"], "description": it["description"], "count": ref(it["field"])})
    items.sort(key=lambda x: (KIND_ORDER[x["kind"]], x["name"]))
    seen: dict[str, int] = {}
    totals: dict[str, int] = {}
    for it in items:
        totals[it["asset"]] = totals.get(it["asset"], 0) + 1
    for it in items:                                   # the save keeps each item instance separately
        if totals[it["asset"]] > 1:
            seen[it["asset"]] = seen.get(it["asset"], 0) + 1
            it["name"] = f"{it['name']} #{seen[it['asset']]}"
    return {"items": items}


def galaxy(m: SaveModel) -> dict:
    regions = [{"tag": r["tag"], "name": r["name"], "influence": ref(r["influence"]),
                "contacts": ref(r["contacts"]), "reward": ref(r["reward"])} for r in m.regions]
    return {"regions": regions}


def coil_upgrades(m: SaveModel) -> dict:
    """Permanent Coil enemy upgrades (gained by failing Crisis missions); each can be removed."""
    rows = []
    for u in coil.active(m.g):
        fx = gamedata.crisis_effect(u["id"]) or {}
        rows.append({"id": u["id"], "tier": u["tier"], "unit": gamedata.crisis_unit(u["unit"]) or "Coil " + u["unit"],
                     "name": fx.get("title") or u["id"].replace(".", " ").replace("_", " "),
                     "description": fx.get("description", "")})
    rows.sort(key=lambda r: (r["unit"], r["tier"] != "Major", r["name"]))
    return {"active": rows}


# ------------------------------------------------------------------ upgrades
TAG_PREFIX = "BitReactor.Strategy.Facilities.Upgrade."
TABS = ["Facilities", "Crew", "Weapons"]
# Row labels as the game's upgrade screen shows them (key = tag after the tab name).
ROW_LABELS = {
    "Crew.Cantina": "Teamwork", "Crew.Combat": "Utility Items", "Crew.CombatReadyness": "Combat Readiness",
    "Crew.CrewQuarters": "Crew Quarters", "Crew.FocusPoint": "Crew Focus", "Crew.Mods": "Weapon Mods",
    "Facilities.Shop": "Black Market", "Facilities.ShopQuality": "Black Market Quality",
    "Facilities.DroidRepair": "Droid Repair Bay", "Facilities.Medbay": "Medbay",
    "Facilities.Networking": "Networking", "Facilities.RegionInfluenceCore": "Core Zone Influence",
    "Facilities.RegionInfluenceMid": "Mid Zone Influence", "Facilities.RegionInfluenceOuter": "Outer Zone Influence",
}
ROW_ORDER = {
    "Facilities": ["Shop", "ShopQuality", "DroidRepair", "Medbay", "Networking",
                   "RegionInfluenceCore", "RegionInfluenceMid", "RegionInfluenceOuter"],
    "Crew": ["Cantina", "Combat", "CombatReadyness", "CrewQuarters", "FocusPoint", "Mods"],
}
WEAPON_ORDER = ["Rifle", "Pistol", "Longarm", "Repeater"]
STAT_ORDER = ["Damage", "CritChance", "Range", "MovementRange"]
STAT_LABELS = {"CritChance": "Critical Chance", "MovementRange": "Movement", "Range": "Range & Overwatch"}


def _pretty(s: str) -> str:
    return re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", s).strip()


def _row_of(rec: Optional[dict], row: dict) -> tuple[str, str, str]:
    """(tab, row key, row label) for an upgrade, from the game's recipe tags when known."""
    tags = [t for t in (rec or {}).get("recipeTags", []) if t.startswith(TAG_PREFIX) and "UpgradeMajor" not in t]
    if tags:
        rest = tags[0][len(TAG_PREFIX):]                      # e.g. Crew.FocusPoint / Weapons.Rifle.Damage
        tab = rest.split(".", 1)[0]
        key = rest.split(".", 1)[1] if "." in rest else rest
        if tab == "Weapons":
            weapon, _, stat = key.partition(".")
            return tab, key, f"{weapon} {STAT_LABELS.get(stat, _pretty(stat))}".strip()
        return tab, key, ROW_LABELS.get(rest, _pretty(key))
    cat = row.get("category") or "Facilities"
    tab = cat if cat in TABS else "Facilities"
    return tab, row.get("group") or cat, row.get("group") or cat


def _row_sort(tab: str, key: str):
    if tab == "Weapons":
        weapon, _, stat = key.partition(".")
        return (WEAPON_ORDER.index(weapon) if weapon in WEAPON_ORDER else 99,
                STAT_ORDER.index(stat) if stat in STAT_ORDER else 99, key)
    order = ROW_ORDER.get(tab, [])
    return (order.index(key) if key in order else 99, 0, key)


def upgrades_screen(info: dict) -> dict:
    gd = gamedata.upgrades()
    tabs: dict[str, dict[str, dict]] = {t: {} for t in TABS}
    slots = []
    for r in info["items"]:
        rec = gd.get(r["name"]) if gd else None
        tab, key, label = _row_of(rec, r)
        node = {
            "name": r["name"], "id": r["id"], "title": r["title"],
            "description": r.get("description", ""), "status": r["status"],
            "den": r.get("den_level"), "den_met": r.get("den_met", True),
            "major": bool(rec and any("UpgradeMajor" in t for t in rec.get("recipeTags", []))),
            "duration": r.get("duration"), "cost": r.get("cost", {}),
            "can_start": r["can_start"], "can_expedite": r["can_expedite"], "reason": r["reason"],
            "started": r["started"], "progress": r["progress"],
            "remaining": (r["duration"] - r["progress"]) if r["status"] == "InProgress" and r.get("duration") else None,
        }
        row = tabs.setdefault(tab, {}).setdefault(key, {"key": key, "label": label, "nodes": []})
        row["nodes"].append(node)
        if r["status"] == "InProgress":
            slots.append(node)
    out_tabs = []
    for t in TABS:
        rows = sorted(tabs.get(t, {}).values(), key=lambda x: _row_sort(t, x["key"]))
        for row in rows:
            row["nodes"].sort(key=lambda n: (n["den"] or 0, n["title"] or ""))
        out_tabs.append({"key": t, "rows": rows})
    return {"turn": info["turn"], "in_progress": info["in_progress"], "level": info.get("roster_level", 0) + 1,
            "gamedata": info.get("gamedata", False), "tabs": out_tabs, "slots": slots}


def build(m: SaveModel, info: Optional[dict], portrait_guids, upgrades_info: Optional[dict] = None) -> dict:
    portraits = {g.upper() for g in portrait_guids}
    out = {
        "header": header(m),
        "command": command(m, info),
        "personnel": personnel(m, portraits),
        "armory": armory(m),
        "medbay": medbay(m, portraits),
        "galaxy": galaxy(m),
        "coil": coil_upgrades(m),
    }
    if upgrades_info is not None:
        out["upgrades"] = upgrades_screen(upgrades_info)
    return out
