"""Curated, editable view over a parsed Zero Company save.

Every editable field is addressed by the byte offset of its value ("o<offset>"),
which is unique within a save.  Fields may carry *linked* writes (e.g. changing a
character's available focus also shifts their total so points spent stay equal).
"""
from __future__ import annotations

import json
import re
import struct
from dataclasses import dataclass, field
from typing import Optional

from . import gamedata
from .gvas import Gvas, GvasError, Node, SCALARS

INT_MAX = 2**31 - 1
INTEL_CAP = 500          # in-game cap for Intel (matches the reference editor)

KNOWN_NAMES = {
    "JaeMordant": "Jae Mordant", "KabbUppercut": "Kabb Uppercut", "Trick": "Trick",
    "HAWKS": "Hawks", "Astromech_BR-1": "BR-1", "Aurelio": "Aurelio", "TelRea": "Tel-Rea",
    "ClyKullervo": "Cly Kullervo",
}


@dataclass
class Field:
    id: str
    group: str
    section: str
    label: str
    kind: str                       # "int" | "float"
    value: float | int
    min: float | int = 0
    max: float | int = INT_MAX
    note: str = ""
    node: Optional[Node] = None
    linked: list[Node] = field(default_factory=list)   # nodes moved by the same delta

    def public(self) -> dict:
        d = {"id": self.id, "group": self.group, "section": self.section,
             "label": self.label, "kind": self.kind, "value": self.value,
             "min": self.min, "max": self.max}
        if self.note:
            d["note"] = self.note
        return d


_FIRST = re.compile(rb"Name_First_([A-Za-z0-9]+)")
_LAST = re.compile(rb"Name_Last_([A-Za-z0-9]+)")


def real_name(g: Gvas, wrapper: Node) -> Optional[str]:
    """Custom operators store their name as localisation keys (Name_First_Neiya ...)."""
    raw = bytes(g.data[wrapper.start:wrapper.end])
    a, b = _FIRST.search(raw), _LAST.search(raw)
    if not a:
        return None
    return " ".join(x.group(1).decode() for x in (a, b) if x)


def fid(node: Node) -> str:
    return f"o{node.value_offset}"


def guid_hex(g: Gvas, node: Node) -> str:
    a, b, c, d = struct.unpack_from("<4I", g.data, node.value_offset)
    return f"{a:08X}{b:08X}{c:08X}{d:08X}"


def _pretty(s: str) -> str:
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", s.replace("_", " "))
    return s.strip()


def asset_name(path: str) -> str:
    return path.rsplit("/", 1)[-1].split(".", 1)[0]


class SaveModel:
    """Builds the field list for one loaded save."""

    def __init__(self, g: Gvas, metadata_json: Optional[str] = None):
        self.g = g
        self.fields: list[Field] = []
        self.by_id: dict[str, Field] = {}
        self.names: dict[str, str] = {}
        # Structured entities (each editable value is a Field, so views reuse the same ids).
        self.resources: dict[str, dict] = {}       # asset name -> {field, label, description}
        self.inventory: list[dict] = []
        self.prog: dict[str, Optional[Field]] = {}
        self.operators: dict[str, dict] = {}       # guid -> operator
        self.roster: list[str] = []                # guids, in the game's roster order
        self.dead: set[str] = set()
        self.bond_rows: list[dict] = []
        self.regions: list[dict] = []
        self._char_names(metadata_json)
        self.sd = self._find_strategy_data()
        self._resources_and_inventory()
        self._progression()
        self._operators()
        self._bonds()
        self._regions()

    # -- helpers -----------------------------------------------------------
    def _find_strategy_data(self) -> Node:
        g = self.g
        for w in g.root:
            if w.name == "GameInstanceSaveGameWrapper":
                ab = g.child(w, "ArchiveBytes")
                sd = g.child(ab, "StrategyData") if ab else None
                if sd is not None:
                    return sd
        raise GvasError("StrategyData not found - unsupported save layout")

    def _add(self, node: Optional[Node], group, section, label, *, lo=0, hi=INT_MAX,
             note="", linked=()) -> Optional[Field]:
        if node is None or node.tname not in SCALARS:
            return None
        kind = "float" if node.tname in ("FloatProperty", "DoubleProperty") else "int"
        f = Field(fid(node), group, section, label, kind, self.g.get(node), lo, hi, note,
                  node, list(linked))
        self.fields.append(f)
        self.by_id[f.id] = f
        return f

    def _char_names(self, metadata_json: Optional[str]):
        if not metadata_json:
            return
        try:
            text = metadata_json.lstrip("\ufeff")
            meta, _ = json.JSONDecoder().raw_decode(text)    # file has trailing NULs
        except ValueError:
            return
        # Fallback names from class names, then overridden by the indexed names.
        for e in meta.get("GameInstanceMetaData", {}).get("characterMetaData", []):
            self.names[e["guid"].upper()] = self._display_name(e.get("className", ""))
        for e in meta.get("StrategyMetaData", {}).get("characterInfoMetaDatas", []):
            self.names[e["guid"].upper()] = self._display_name(e.get("characterName", ""))

    def _display_name(self, cls: str) -> str:
        m = re.match(r"Char_Hero_(.+?)_C(?:_(\d+))?$", cls)
        if not m:
            return cls or "Unknown"
        body, idx = m.group(1), m.group(2)
        if body == "Humanoid":
            return f"Custom Operator {int(idx) + 1}" if idx is not None else "Custom Operator"
        key = body.split("_")[0] if body.split("_")[0] in KNOWN_NAMES else body
        return KNOWN_NAMES.get(key, KNOWN_NAMES.get(body, _pretty(body)))

    def name_of(self, h: str) -> str:
        return self.names.get(h, f"Unknown ({h[:8]})")

    # -- resources / inventory --------------------------------------------
    def _resources_and_inventory(self):
        g = self.g
        inv = g.child(self.sd, "InventoryItemData")
        for ent in (inv.children or []) if inv else []:
            val = g.child(ent, "value") if ent.children is None else ent.children[1]
            asset = g.child(val, "ItemAsset")
            cnt = g.child(val, "ItemCount")
            if not asset or not cnt:
                continue
            path = g.get(asset)
            name = asset_name(path)
            if "/Resources/" in path:
                label = _pretty(name.replace("Resource_", "", 1))
                label = {"Intelligence": "Intel", "Upgrade Facility Resource": "Facility upgrade resource"}.get(label, label)
                info = gamedata.items().get(name)
                label = gamedata.item_label(name) or label
                hi = INTEL_CAP if name == "Resource_Intelligence" else INT_MAX
                f = self._add(cnt, "Resources", "Stockpile", label, hi=hi,
                              note=info["description"] if info else "")
                self.resources[name] = {"field": f, "label": label,
                                        "description": info["description"] if info else ""}
            else:
                section = ("Modifications" if "Mod" in path else
                           "Utility" if "Utility" in path or "Grenade" in path else "Other")
                info = gamedata.items().get(name)
                label = gamedata.item_label(name) or _pretty(name)
                if info and info["tier"]:
                    label += f" (T{info['tier']})"
                f = self._add(cnt, "Inventory", section, label,
                              note=info["description"] if info else "")
                self.inventory.append({"asset": name, "label": label, "section": section,
                                       "kind": info["kind"] if info else section,
                                       "tier": info["tier"] if info else None,
                                       "rarity": info["rarity"] if info else "",
                                       "description": info["description"] if info else "",
                                       "field": f})

    # -- progression -------------------------------------------------------
    def _progression(self):
        g, sd = self.g, self.sd
        self.prog["roster_level"] = self._add(g.child(sd, "RosterLevel"), "Progression", "Roster",
                                              "Roster level", hi=1000)
        self.prog["roster_xp"] = self._add(g.child(sd, "RosterXP"), "Progression", "Roster", "Roster XP")
        self.prog["turn"] = self._add(g.child(sd, "StrategyTurn"), "Progression", "Campaign",
                                      "Strategy turn",
                                      note="Display copy in the save's info file is not updated.")
        self.prog["base_focus"] = self._add(g.child(sd, "BaseTotalFocusPoints"), "Progression",
                                            "Campaign", "Base total focus points", hi=1000)

    # -- operators ---------------------------------------------------------
    def _characters(self):
        g = self.g
        top = next((w for w in g.root if w.name == "GameInstanceSaveGameWrapper"), None)
        ab = g.child(top, "ArchiveBytes")
        cdw = g.child(ab, "CharacterDataWrapper")
        blob = g.child(cdw, "ArchiveBytes") if cdw else None
        if not blob:
            return
        guids = g.child(blob, "CharacterGuids")
        wraps = g.child(blob, "ObjectWrappers")
        for gn, w in zip(guids.children or [], wraps.children or []):
            inner = g.child(w, "ArchiveBytes")
            cd = g.child(inner, "CharacterData") if inner else None
            yield guid_hex(g, gn), cd, w

    def _operators(self):
        g = self.g
        focus = {}
        fmap = g.child(self.sd, "CharacterFocusData")
        for ent in (fmap.children or []) if fmap else []:
            focus[guid_hex(g, ent.children[0])] = ent.children[1]
        dead = self.dead
        dc = g.child(self.sd, "DeadCharacters")
        for e in (dc.children or []) if dc else []:
            dead.add(guid_hex(g, e))
        ro = g.child(self.sd, "Roster")
        self.roster = [guid_hex(g, e) for e in ((ro.children or []) if ro else [])]
        for h, cd, wrapper in self._characters():
            cls = g.get(g.child(cd, "CharacterClassName")) if cd and g.child(cd, "CharacterClassName") else ""
            if h not in self.names:
                self.names[h] = self._display_name(cls)
            if self.names[h].startswith("Custom Operator"):
                self.names[h] = real_name(g, wrapper) or self.names[h]
            name = self.name_of(h)
            sect = name + (" (dead)" if h in dead else "")
            m = re.match(r"Char_Hero_(.+?)_C(?:_\d+)?$", cls)
            parts = m.group(1).split("_") if m else []
            op = {"guid": h, "name": name, "class": cls, "dead": h in dead,
                  "role": ("Astromech" if parts[:1] == ["Astromech"] else parts[1] if len(parts) > 1 else ""),
                  "focus": None, "total_focus": None, "total_field": None, "abilities": [], "effects": [], "injuries": 0}
            self.operators[h] = op
            fv = focus.get(h)
            if fv is not None:
                tot = g.child(fv, "TotalFocusPoints")
                av = g.child(fv, "AvailableFocusPoints")
                op["focus"] = self._add(av, "Operators", sect, "Available focus points", hi=500,
                                        note="Total focus moves with it, so points spent stay the same.",
                                        linked=[tot] if tot else [])
                op["total_focus"] = g.get(tot) if tot else None
                op["total_field"] = (self._add(tot, "Operators", sect, "Total focus earned", hi=5000,
                                               note="Always equals focus spent + unspent.")
                                     if tot else None)
                op["abilities"] = self._abilities(fv, name, h in dead)
            ge = g.child(cd, "GameplayEffectsToPersist") if cd else None
            for e in (ge.children or []) if ge else []:
                d = asset_name(g.get(g.child(e, "Def"))).replace("Default__", "")
                d = re.sub(r"^GE_|_C$", "", d)
                if d.startswith(("Injured", "ApplyDead")):
                    if d.startswith("Injured"):
                        sc = g.child(e, "StackCount")
                        op["injuries"] = g.get(sc) if sc is not None else 1
                    continue
                label = _pretty(d.replace("CrossTraining_", "Cross-training: ").replace("Progression_", "Progression: "))
                title, edesc = gamedata.effect_text(asset_name(g.get(g.child(e, "Def"))))
                if title:
                    label = title
                eff = {"asset": asset_name(g.get(g.child(e, "Def"))), "title": label,
                       "description": edesc, "stacks": None, "magnitudes": []}
                eff["stacks"] = self._add(g.child(e, "StackCount"), "Operators", sect,
                                          f"{label} - stacks", hi=100, note=edesc)
                mods = g.child(e, "Modifiers")
                for i, mo in enumerate((mods.children or []) if mods else []):
                    mag = g.child(mo, "EvaluatedMagnitude")
                    sfx = f" #{i+1}" if len(mods.children) > 1 else ""
                    mf = self._add(mag, "Operators", sect, f"{label} - magnitude{sfx}", lo=-1e6, hi=1e6,
                                   note="Experimental: stat value the game applies.")
                    if mf is not None:
                        eff["magnitudes"].append(mf)
                op["effects"].append(eff)

    def _abilities(self, fv: Node, operator: str, is_dead: bool) -> list[dict]:
        """Per-ability focus-tree state: Level (1-6) and AllocatedFocus, both plain ints."""
        g = self.g
        out: list[dict] = []
        alloc = g.child(fv, "FocusPointAllocations")
        sect = operator + (" (dead)" if is_dead else "")
        for ent in (alloc.children or []) if alloc else []:
            key, val = ent.children
            tag_node = g.child(key, "TagName")
            if tag_node is None:
                continue
            tag = g.get(tag_node)
            name = gamedata.ability_name(tag) or _pretty(tag.rsplit(".", 1)[-1])
            lvl_node = g.child(val, "Level")
            levelable = g.child(val, "bIsLevelable")
            lf = None
            thr = gamedata.focus_thresholds().get(tag)
            if lvl_node is not None and (levelable is None or g.get(levelable)):
                lf = self._add(lvl_node, "Abilities", sect, f"{name} - level", lo=1, hi=len(thr) if thr else 6,
                               note="Allocated focus and unspent focus are NOT changed automatically.")
            sf = self._add(g.child(val, "AllocatedFocus"), "Abilities", sect, f"{name} - focus spent",
                           hi=500)
            kind = tag.split(".")[2] if tag.count(".") >= 3 else ""      # Class / Passive / Identity ...
            out.append({"tag": tag, "name": name, "kind": kind, "level": lf, "spent": sf, "thresholds": thr,
                        "base_level": g.get(lvl_node) if lvl_node is not None else None})
        return out

    # -- bonds -------------------------------------------------------------
    def _bonds(self):
        g = self.g
        bonds = g.child(self.sd, "Bonds")
        for b in (bonds.children or []) if bonds else []:
            ga, gb = guid_hex(g, g.child(b, "BondCharacterAID")), guid_hex(g, g.child(b, "BondCharacterBID"))
            a, c = self.name_of(ga), self.name_of(gb)
            sect = f"{a} & {c}"
            row = {"a": ga, "b": gb}
            self.bond_rows.append(row)
            scale = "In-game scale 0-8 = this + 4 (0 = Very low, 4 = Neutral, 8 = Very high)."
            for key, prop, label, lo, hi, note in (
                    ("level", "BondLevel", "Bond level", -4, 4, scale),
                    ("progress", "BondProgress", "Bond progress", 0, 1000, ""),
                    ("cross", "AvailableCrossTrainings", "Available cross-trainings", 0, 1000, ""),
                    ("next", "NextHighestCrossTrainingLevel", "Next cross-training level", -4, 8, ""),
                    ("highest", "HighestBondLevelReached", "Highest level reached", -4, 4, scale)):
                row[key] = self._add(g.child(b, prop), "Bonds", sect, label, lo=lo, hi=hi, note=note)

    # -- galaxy ------------------------------------------------------------
    def _regions(self):
        g = self.g
        regs = g.child(self.sd, "AvailableRegions")
        for ent in (regs.children or []) if regs else []:
            key, val = ent.children
            tag = g.get(g.child(key, "TagName")) if g.child(key, "TagName") else "?"
            region = _pretty(tag.rsplit(".", 1)[-1])
            fi = self._add(g.child(val, "Influence"), "Galaxy", region, "Influence", hi=1000)
            fc = self._add(g.child(val, "Contacts"), "Galaxy", region, "Contacts", hi=1000)
            fr = self._add(g.child(val, "InfluenceRewardIndex"), "Galaxy", region,
                           "Influence reward index", lo=-1, hi=100)
            self.regions.append({"tag": tag, "name": region, "influence": fi, "contacts": fc, "reward": fr})

    # -- public ------------------------------------------------------------
    def public(self) -> list[dict]:
        return [f.public() for f in self.fields]


def scalar_nodes(g: Gvas) -> dict[str, Node]:
    """All in-place-editable scalar nodes, keyed by field id (for the raw editor)."""
    out = {}
    for n in g.walk():
        if n.tname in SCALARS and n.size == SCALARS[n.tname][1]:
            out[fid(n)] = n
    return out


def validate(kind: str, tname: str, value, lo, hi):
    """Coerce + range-check a client-supplied value; raises ValueError."""
    if isinstance(value, bool):
        raise ValueError("boolean not allowed")
    if kind == "float":
        v = float(value)
        if v != v or v in (float("inf"), float("-inf")):
            raise ValueError("value must be finite")
    else:
        if isinstance(value, float) and not value.is_integer():
            raise ValueError("value must be a whole number")
        v = int(value)
    if not (lo <= v <= hi):
        raise ValueError(f"value {v} out of range [{lo}, {hi}]")
    return v


TYPE_RANGES = {
    "IntProperty": (-2**31, 2**31 - 1), "UInt32Property": (0, 2**32 - 1),
    "Int64Property": (-2**63, 2**63 - 1), "UInt64Property": (0, 2**64 - 1),
    "Int16Property": (-2**15, 2**15 - 1), "UInt16Property": (0, 2**16 - 1),
    "Int8Property": (-128, 127),
    "FloatProperty": (-3.4e38, 3.4e38), "DoubleProperty": (-1.7e308, 1.7e308),
}
