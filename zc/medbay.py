"""Healing injured operators.

An injury is a persisted gameplay effect (`GE_Injured`) in the operator's saved effect list.  Comparing a
game-written save before and after using the bacta tank shows that the heal removes that one array element; the
credits (-500), the tank charge and a "completed recipe" history entry are bookkeeping the game adds around it.
`heal_operator` performs only the removal (a free, instant heal).
"""
from __future__ import annotations

from .domain import asset_name, guid_hex
from .gvas import Gvas, GvasError, Node

INJURED = "GE_Injured"
EFFECTS = "GameplayEffectsToPersist"


def _blob(g: Gvas):
    top = next((w for w in g.root if w.name == "GameInstanceSaveGameWrapper"), None)
    cdw = g.child(g.child(top, "ArchiveBytes"), "CharacterDataWrapper") if top else None
    return g.child(cdw, "ArchiveBytes") if cdw else None


def characters(g: Gvas) -> dict[str, Node]:
    """GUID -> that character's ObjectWrappers element (the node whose ArchiveBytes holds CharacterData)."""
    blob = _blob(g)
    if blob is None:
        return {}
    guids = g.child(blob, "CharacterGuids")
    wraps = g.child(blob, "ObjectWrappers")
    return {guid_hex(g, a): w for a, w in zip(guids.children or [], wraps.children or [])}


def character_sizes(g: Gvas) -> tuple[int, dict[str, int]]:
    """(byte count of the whole character archive, GUID -> byte count of that character's archive).

    SaveGameMetaData.json mirrors these as `characterDataWrapperSize` and each character's `wrappedSize`.
    """
    blob = _blob(g)
    if blob is None:
        return 0, {}
    return blob.count or 0, {gid: (g.child(w, "ArchiveBytes").count or 0) for gid, w in characters(g).items()}


def effects_node(g: Gvas, guid: str) -> Node | None:
    w = characters(g).get(guid)
    inner = g.child(w, "ArchiveBytes") if w else None
    cd = g.child(inner, "CharacterData") if inner else None
    return g.child(cd, EFFECTS) if cd else None


def effect_name(g: Gvas, el: Node) -> str:
    d = g.child(el, "Def")
    return asset_name(g.get(d)) if d is not None else ""


def effect_keys(g: Gvas, arr: Node) -> list[str]:
    """Stable per-element keys ("GE_Name#occurrence"), so a removal does not shift every later element's path."""
    seen: dict[str, int] = {}
    out = []
    for i, el in enumerate(arr.children or []):
        name = effect_name(g, el) or f"[{i}]"
        out.append(f"{name}#{seen.get(name, 0)}")
        seen[name] = seen.get(name, 0) + 1
    return out


def injury_count(g: Gvas, guid: str) -> int:
    arr = effects_node(g, guid)
    return sum(1 for el in (arr.children or []) if effect_name(g, el).startswith(INJURED)) if arr else 0


def heal_operator(g: Gvas, guid: str):
    """Remove every `GE_Injured` effect from one operator.

    Returns (new Gvas, info) where info = {"removed": n, "opaque_removed": n, "prefixes": {path tuples},
    "count_paths": {path tuples}} describing exactly what was cut (for verification).
    """
    info = {"removed": 0, "opaque_removed": 0, "prefixes": set(), "count_paths": set()}
    while True:
        arr = effects_node(g, guid)
        if arr is None:
            raise GvasError("operator not found")
        keys = effect_keys(g, arr)
        hit = next((i for i, el in enumerate(arr.children or []) if effect_name(g, el).startswith(INJURED)), None)
        if hit is None:
            break
        el = arr.children[hit]
        info["opaque_removed"] += sum(1 for n in g.walk([el]) if n.opaque)
        base = tuple(g.path_of(arr))
        info["prefixes"].add(base + (keys[hit],))
        info["count_paths"].add(base + ("#count",))
        g = g.array_remove_element(arr, hit)
        info["removed"] += 1
    if not info["removed"]:
        raise GvasError("operator is not injured")
    return g, info
