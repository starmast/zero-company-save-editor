"""Bringing a fallen operator back to the roster.

A dead operator differs from a living one in a handful of places (found by comparing a fallen operator with the
living roster; the game's own save format does the rest):

  * StrategyData.DeadCharacters lists them and StrategyData.CharacterIDsToDiedTurns has their death turn;
  * they are absent from StrategyData.Roster and StrategyData.CharacterIDsToRecruitedTurns;
  * their saved effects contain `GE_ApplyDead` (and usually `GE_Injured`).

`revive_operator` reverses those: it removes them from the two death records, appends them to the roster with the
current turn as their recruited turn, and removes their death/injury effects.  Everything else (bonds, focus,
equipment, memorial statistics) already exists for a fallen operator and is left alone.
"""
from __future__ import annotations

import struct

from . import medbay
from .domain import asset_name, guid_hex
from .gvas import Gvas, GvasError, Node

DEAD_EFFECTS = ("GE_ApplyDead", "GE_Injured")


def _sd(g: Gvas) -> Node:
    top = next((w for w in g.root if w.name == "GameInstanceSaveGameWrapper"), None)
    ab = g.child(top, "ArchiveBytes") if top else None
    sd = g.child(ab, "StrategyData") if ab else None
    if sd is None:
        raise GvasError("strategy data not found")
    return sd


def _node(g: Gvas, name: str) -> Node:
    n = g.child(_sd(g), name)
    if n is None:
        raise GvasError(f"{name} not found")
    return n


def dead_guids(g: Gvas) -> list[str]:
    return [guid_hex(g, e) for e in (_node(g, "DeadCharacters").children or [])]


def died_map(g: Gvas) -> dict[str, int]:
    mp = _node(g, "CharacterIDsToDiedTurns")
    return {guid_hex(g, e.children[0]): g.get(e.children[1]) for e in (mp.children or [])}


def recruited_map(g: Gvas) -> dict[str, int]:
    mp = _node(g, "CharacterIDsToRecruitedTurns")
    return {guid_hex(g, e.children[0]): g.get(e.children[1]) for e in (mp.children or [])}


def roster_order(g: Gvas) -> list[str]:
    return [guid_hex(g, e) for e in (_node(g, "Roster").children or [])]


def strategy_turn(g: Gvas) -> int:
    return int(g.get(_node(g, "StrategyTurn")))


def revive_operator(g: Gvas, guid: str, max_roster: int | None = None):
    """Bring `guid` back.  Returns (new Gvas, info) with what the verifier needs:

    info = {"prefixes", "opaque_removed", "count_paths", "expected": {dead, died, roster, recruited}}.
    """
    guid = guid.upper()
    dead = dead_guids(g)
    if guid not in dead:
        raise GvasError("operator is not among the fallen")
    if guid in roster_order(g):
        raise GvasError("operator is already on the roster")
    if max_roster is not None and len(roster_order(g)) >= max_roster:
        raise GvasError(f"the roster is full ({max_roster})")
    if guid not in medbay.characters(g):
        raise GvasError("no saved character data for this operator")
    turn = strategy_turn(g)
    info = {"opaque_removed": 0, "prefixes": set(), "count_paths": set()}

    # 1. DeadCharacters
    arr = _node(g, "DeadCharacters")
    idx = dead.index(guid)
    raw = bytes(g.data[arr.children[idx].start:arr.children[idx].end])
    info["prefixes"].add(tuple(g.path_of(arr)))
    g = g.array_remove_element(arr, idx)

    # 2. CharacterIDsToDiedTurns
    mp = _node(g, "CharacterIDsToDiedTurns")
    info["prefixes"].add(tuple(g.path_of(mp)))
    hit = next((i for i, e in enumerate(mp.children or []) if guid_hex(g, e.children[0]) == guid), None)
    if hit is not None:
        g = g.map_remove_entry(mp, hit)

    # 3. Roster (append) and 4. CharacterIDsToRecruitedTurns (append guid + turn)
    arr = _node(g, "Roster")
    info["prefixes"].add(tuple(g.path_of(arr)))
    g = g.array_append_raw(arr, raw)
    mp = _node(g, "CharacterIDsToRecruitedTurns")
    info["prefixes"].add(tuple(g.path_of(mp)))
    if guid not in recruited_map(g):
        g = g.map_append_raw(mp, raw + struct.pack("<i", turn))

    # 5. death / injury effects
    while True:
        eff = medbay.effects_node(g, guid)
        if eff is None:
            raise GvasError("operator effects not found")
        keys = medbay.effect_keys(g, eff)
        i = next((k for k, el in enumerate(eff.children or [])
                  if medbay.effect_name(g, el).startswith(DEAD_EFFECTS)), None)
        if i is None:
            break
        el = eff.children[i]
        info["opaque_removed"] += sum(1 for n in g.walk([el]) if n.opaque)
        base = tuple(g.path_of(eff))
        info["prefixes"].add(base + (keys[i],))
        info["count_paths"].add(base + ("#count",))
        g = g.array_remove_element(eff, i)

    info["expected"] = {"dead": [d for d in dead if d != guid], "roster": roster_order(g),
                        "guid": guid, "turn": turn}
    return g, info
