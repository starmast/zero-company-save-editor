"""Optional game data extracted from the install (see tools/extract/README.md).

Everything here degrades gracefully: with no `gamedata/` folder every lookup returns
None/empty and the editor behaves as it did before.  The data is the game's own
content, so it stays local (git-ignored) and is never written into saves.
"""
from __future__ import annotations

import json
import os
import re
from functools import lru_cache
from typing import Optional

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def data_dir() -> str:
    return os.environ.get("ZC_GAMEDATA") or os.path.join(ROOT, "gamedata")


@lru_cache(maxsize=None)
def _load(name: str, folder: str):
    path = os.path.join(folder, name)
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def clear_cache() -> None:
    _load.cache_clear()
    _thresholds.cache_clear()
    _strings.cache_clear()
    _items.cache_clear()
    _effects.cache_clear()


def available() -> bool:
    return _load("upgrades.json", data_dir()) is not None


_MARKUP = re.compile(r"<[^>]+>")


def plain(text: Optional[str]) -> str:
    """Strip the game's rich-text markup (<bold>, <Keyword ...>) for display."""
    return _MARKUP.sub("", text or "").strip()


class Upgrades:
    """Upgrade recipes keyed by asset name (same key the save's SoftRecipeClass ends with)."""

    def __init__(self, rows: list[dict]):
        self.by_name = {r["name"]: r for r in rows}
        self.by_tag: dict[str, dict] = {}              # fact tag -> recipe that grants it
        for r in rows:
            for t in r.get("completedFactTags", []):
                self.by_tag.setdefault(t, r)

    def get(self, name: str) -> Optional[dict]:
        return self.by_name.get(name)

    @staticmethod
    def summary(r: dict) -> dict:
        cost: dict[str, int] = {}
        den = None
        tags: list[str] = []
        for tier in r.get("tiers", []):
            for q in tier.get("requirements", []):
                if "item" in q and q.get("amount") is not None:
                    cost[q["item"].replace("Resource_", "")] = q["amount"]
                if q.get("rosterLevel") is not None:
                    den = q["rosterLevel"]
                tags += q.get("requireTags", [])
        return {"duration": r.get("duration") or 1, "cost": cost, "den_level": den,
                "require_tags": tags, "description": plain(r.get("description")),
                "title": r.get("title")}


def upgrades() -> Optional[Upgrades]:
    rows = _load("upgrades.json", data_dir())
    return Upgrades(rows) if rows else None


# ----------------------------------------------------------------------------- items / effects
def _text(tok) -> str:
    """Source string of a localised text property ({"SourceString": ...})."""
    if isinstance(tok, dict):
        return tok.get("SourceString") or tok.get("CultureInvariantString") or ""
    return ""


def _raw(name: str):
    return _load(name, data_dir()) or {}


@lru_cache(maxsize=None)
def _items(folder: str) -> dict:
    out = {}
    for asset, v in (_load("raw_items.json", folder) or {}).items():
        display = desc = ""
        tags: list[str] = []
        for o in v["objects"]:
            p = o.get("Properties") or {}
            if o["Type"] == "BitReactorUIDataFragment" and not display:
                display, desc = _text(p.get("DisplayName")), _text(p.get("ShortDescription"))
            tags += [t for t in p.get("ItemTags", []) if isinstance(t, str)]
        rarity = next((t.rsplit(".", 1)[-1] for t in tags if ".Rarity." in t), "")
        kind = ("Modification" if "Modification" in asset else "Utility" if "UtilityItem" in asset
                else "Resource" if asset.startswith("Resource_") else "Other")
        tier = re.search(r"_T(\d+)$", asset)
        out[asset] = {"name": asset, "display": display, "description": plain(desc), "kind": kind,
                      "rarity": rarity, "tier": int(tier.group(1)) if tier else None,
                      "tags": sorted(set(tags))}
    return out


def items() -> dict:
    """Inventory item data keyed by asset name (e.g. 'UtilityItem_Medkit_T2'). {} if not extracted."""
    return _items(data_dir())


@lru_cache(maxsize=None)
def _effects(folder: str) -> dict:
    out = {}
    for asset, v in (_load("raw_effects.json", folder) or {}).items():
        title = desc = ""
        mods: list[dict] = []
        for o in v["objects"]:
            p = o.get("Properties") or {}
            if not title and (p.get("Title") or p.get("DisplayName") or p.get("Name")):
                title = _text(p.get("Title") or p.get("DisplayName") or p.get("Name"))
                desc = _text(p.get("Description") or p.get("ShortDescription"))
            if o["Type"].endswith("_C"):
                for m in p.get("Modifiers", []) or []:
                    attr = ((m.get("Attribute") or {}).get("AttributeName")) or ""
                    mag = (((m.get("ModifierMagnitude") or {}).get("ScalableFloatMagnitude")) or {}).get("Value")
                    mods.append({"attribute": attr, "op": str(m.get("ModifierOp", "")).split("::")[-1], "value": mag})
        out[asset] = {"name": asset, "title": title, "description": plain(desc), "modifiers": mods}
    return out


def effects() -> dict:
    """Gameplay-effect UI text and modifiers keyed by asset name (e.g. 'GE_Upgrade_RifleDmg1')."""
    return _effects(data_dir())


_PLURAL = re.compile(r"\{Count\}\|plural\(one=([^,]+),\s*other=([^)]+)\)")


def item_label(asset: str) -> Optional[str]:
    """In-game display name for an inventory item asset, or None when unknown."""
    it = items().get(asset)
    if not it or not it["display"]:
        return None
    d = it["display"].strip()
    m = _PLURAL.search(d)
    return (m.group(2) if m else d).strip()


def effect_text(asset: str) -> tuple[Optional[str], str]:
    """(title, description) of a gameplay effect, with {0}/{1} filled from its modifiers."""
    e = effects().get(asset)
    if not e or not e["title"]:
        return None, ""
    desc = e["description"]
    vals = [m["value"] for m in e["modifiers"] if m.get("value") is not None]
    def fill(m):
        i = int(m.group(1))
        if i >= len(vals):
            return m.group(0)
        v = float(vals[i])
        if m.group(2):                                   # {1%} -> shown as a percentage
            v = v * 100 if abs(v) <= 1 else v
            return f"{abs(v):g}%"
        return str(int(v)) if v.is_integer() else f"{v:g}"
    desc = re.sub(r"\{(\d+)(%?)\}", fill, desc)
    return e["title"], desc


@lru_cache(maxsize=None)
def _strings(folder: str) -> dict:
    """Flat key -> English text from Game.locres (first namespace wins on duplicate keys)."""
    flat: dict[str, str] = {}
    for ns, d in (_load("strings_en.json", folder) or {}).items():
        for k, v in d.items():
            flat.setdefault(k, v)
    return flat


def ability_name(tag: str) -> Optional[str]:
    """Display name for an ability tag like 'br.AbilityID.Class.PrecisionShot', or None."""
    flat = _strings(data_dir())
    if not flat:
        return None
    seg = tag.rsplit(".", 1)[-1]
    for key in (f"GA_{seg}_T1_Name", f"GA_{seg}_Name", f"{seg}_Name", f"GA_{seg}_T2_Name",
                f"Passive_{seg}_Name", f"Aspect_{seg}_Name"):
        if key in flat:
            return plain(flat[key])
    return None


@lru_cache(maxsize=None)
def _thresholds(folder: str) -> dict:
    out: dict[str, list[int]] = {}
    raw = _load("raw_focus.json", folder) or {}
    for asset in raw.values():
        for o in asset.get("objects", []):
            for e in (o.get("Properties") or {}).get("FocusPointLevelThresholds", []) or []:
                tag = (e.get("Key") or {}).get("TagName")
                rows = ((e.get("Value") or {}).get("FocusPointLevelThresholds")) or []
                pairs = sorted((int(r["Key"]), int(r["Value"])) for r in rows)
                if tag and pairs:
                    out[tag] = [v for _, v in pairs]
    return out


def focus_thresholds() -> dict:
    """ability tag -> cumulative focus needed for level 1..n (e.g. Lethal: [0, 2, 5, 9, 15, 23])."""
    return _thresholds(data_dir())
