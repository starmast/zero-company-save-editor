"""Completing an operator's focus-tree tier data.

Every ability in an operator's saved `LeveledAbilities` carries one record per tier (T1..T6: the ability asset plus
MinLevel / FocusLevel / FocusCost).  Those lists are static game data: every operator who holds the same ability has the
identical list.  The tutorial operator (Aurelio) only has the first tier of each of his abilities, so the game cannot
draw the rest of his tree.  `complete` appends the missing tier records, copied byte-for-byte from another operator who
holds the same ability, but only where the operator's existing records are exactly the start of that list.
"""
from __future__ import annotations

from . import medbay
from .gvas import Gvas, GvasError, Node


def _tag(g: Gvas, entry: Node) -> str | None:
    t = g.child(entry, "BaseAbilityTag")
    n = g.child(t, "TagName") if t else None
    return g.get(n) if n is not None else None


def _lists(g: Gvas, wrapper: Node):
    """(ability tag, its tier-record array) for every ability of one character."""
    for n in g.walk([wrapper]):
        if n.name == "LeveledAbilities":
            for e in n.children or []:
                lst = g.child(e, "LeveledAbilityList_Struct")
                tag = _tag(g, e)
                if tag and lst is not None and lst.children is not None:
                    yield tag, lst


def _raw(g: Gvas, lst: Node) -> list[bytes]:
    return [bytes(g.data[e.start:e.end]) for e in (lst.children or [])]


def _donors(g: Gvas, skip: str) -> dict[str, list[bytes]]:
    best: dict[str, list[bytes]] = {}
    for gid, w in medbay.characters(g).items():
        if gid == skip:
            continue
        for tag, lst in _lists(g, w):
            recs = _raw(g, lst)
            if len(recs) > len(best.get(tag, [])):
                best[tag] = recs
    return best


def _gaps(g: Gvas, guid: str, donors: dict[str, list[bytes]]):
    w = medbay.characters(g).get(guid)
    if w is None:
        return
    for tag, lst in _lists(g, w):
        have, d = _raw(g, lst), donors.get(tag)
        if have and d and len(d) > len(have) and d[:len(have)] == have:
            yield tag, lst, have, d


def incomplete(g: Gvas) -> dict[str, list[str]]:
    """{operator guid: [ability tags whose tiers are missing]} for operators that complete() can fix."""
    out = {}
    for gid in medbay.characters(g):
        tags = [t for t, *_ in _gaps(g, gid, _donors(g, gid))]
        if tags:
            out[gid] = tags
    return out


def complete(g: Gvas, guid: str):
    """Append the missing tier records.  Returns (new Gvas, {"prefixes", "expected": {tag: [record bytes]}})."""
    info = {"prefixes": set(), "expected": {}}
    while True:
        gap = next(_gaps(g, guid, _donors(g, guid)), None)
        if gap is None:
            break
        tag, lst, have, donor = gap
        info["prefixes"].add(tuple(g.path_of(lst)))
        info["expected"][tag] = donor
        g = g.array_append_raw(lst, b"".join(donor[len(have):]), len(donor) - len(have))
    if not info["expected"]:
        raise GvasError("nothing to complete")
    return g, info


def records(g: Gvas, guid: str) -> dict[str, list[bytes]]:
    w = medbay.characters(g).get(guid)
    return {tag: _raw(g, lst) for tag, lst in _lists(g, w)} if w is not None else {}
