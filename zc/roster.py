"""Roster order.

StrategyData.Roster is an array of operator GUIDs (16 bytes each) in the order the Personnel strip shows them (the
game appends new recruits).  Reordering permutes those fixed-size entries in place: the file size, every size field
and the metadata stay exactly as they were.
"""
from __future__ import annotations

from .domain import guid_hex
from .gvas import Gvas, GvasError, Node


def roster_node(g: Gvas) -> Node | None:
    top = next((w for w in g.root if w.name == "GameInstanceSaveGameWrapper"), None)
    ab = g.child(top, "ArchiveBytes") if top else None
    sd = g.child(ab, "StrategyData") if ab else None
    return g.child(sd, "Roster") if sd else None


def order(g: Gvas) -> list[str]:
    arr = roster_node(g)
    return [guid_hex(g, e) for e in (arr.children or [])] if arr else []


def reorder(g: Gvas, new_order: list[str]) -> tuple[Gvas, dict]:
    """Put the roster in `new_order` (the same operators, any order).

    Returns (new Gvas, {"paths": paths of the entries that changed, "expected": the resulting order}).
    """
    arr = roster_node(g)
    if arr is None or not arr.children:
        raise GvasError("roster not found")
    cur = order(g)
    if len(set(new_order)) != len(new_order) or sorted(new_order) != sorted(cur):
        raise GvasError("the roster changed; re-open the save")
    if any(e.end - e.start != 16 for e in arr.children):
        raise GvasError("unexpected roster layout")
    raw = {guid: bytes(g.data[e.start:e.end]) for guid, e in zip(cur, arr.children)}
    buf = bytearray(g.data)
    paths = set()
    for e, old, new in zip(arr.children, cur, new_order):
        if old != new:
            buf[e.start:e.end] = raw[new]
            paths.add(tuple(g.path_of(e)))
    if not paths:
        raise GvasError("the roster order is unchanged")
    return Gvas(bytes(buf)), {"paths": paths, "expected": list(new_order)}
