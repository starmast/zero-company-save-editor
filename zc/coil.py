"""Coil crisis upgrades.

When a Crisis mission or operation expires or fails, the game makes that enemy upgrade permanent by switching the
crisis's state fact tag to `.Selected` (a crisis starts as `.Available`; winning it makes it `.Prevented`).  The tags
live in StrategyData.FactTags, a native array: int32 count, then one FString per tag.

Taking an upgrade away renames `<crisis>.Selected` to one of the two other states the game itself uses:
`.Available` (the inverse of gaining it: the crisis can be failed, and the upgrade gained, once more) or `.Prevented`
(as if the crisis had been won: the upgrade is gone and the crisis is settled).  Every other tag is untouched.
"""
from __future__ import annotations

import re
import struct

from .gvas import Gvas, GvasError, Node

PREFIX = "BitReactor.Design.Crisis."
STATES = ("Available", "Prevented")
SELECTED = re.compile(r"^BitReactor\.Design\.Crisis\.(Major|Minor)\.([A-Za-z0-9]+_[A-Z])\.Selected$")


def fact_tags_node(g: Gvas) -> Node | None:
    top = next((w for w in g.root if w.name == "GameInstanceSaveGameWrapper"), None)
    ab = g.child(top, "ArchiveBytes") if top else None
    sd = g.child(ab, "StrategyData") if ab else None
    return g.child(sd, "FactTags") if sd else None


def read_tags(g: Gvas, node: Node) -> list[str]:
    """The FactTags array as text.  Raises if the bytes are not exactly count + FStrings."""
    d, pos = g.data, node.value_offset
    (n,) = struct.unpack_from("<i", d, pos)
    pos += 4
    out = []
    for _ in range(n):
        (ln,) = struct.unpack_from("<i", d, pos)
        if ln <= 0 or pos + 4 + ln > node.end or d[pos + 3 + ln] != 0:
            raise GvasError("unexpected FactTags layout")
        out.append(bytes(d[pos + 4:pos + 3 + ln]).decode("ascii"))
        pos += 4 + ln
    if pos != node.end:
        raise GvasError("unexpected FactTags layout")
    return out


def tags(g: Gvas) -> list[str]:
    node = fact_tags_node(g)
    return read_tags(g, node) if node is not None else []


def active(g: Gvas) -> list[dict]:
    """Upgrades the Coil currently hold: {"id": "Major.Striker_A", "tier", "unit", "variant"}."""
    out = []
    for t in tags(g):
        m = SELECTED.match(t)
        if m:
            unit, variant = m.group(2).rsplit("_", 1)
            out.append({"id": f"{m.group(1)}.{m.group(2)}", "tier": m.group(1), "unit": unit, "variant": variant})
    return out


def remove(g: Gvas, changes: dict[str, str]) -> tuple[Gvas, dict]:
    """Take upgrades away: `changes` maps an upgrade id ("Major.Striker_A") to its new state ("Available"/"Prevented").

    Returns (new Gvas, {"path": FactTags path, "expected": new tag list}).
    """
    node = fact_tags_node(g)
    if node is None:
        raise GvasError("fact tags not found")
    if not changes or any(s not in STATES for s in changes.values()):
        raise GvasError("unknown Coil upgrade state")
    old = read_tags(g, node)
    have = set(old)
    target = {f"{PREFIX}{i}.Selected": s for i, s in changes.items()}
    if not set(target) <= have:
        raise GvasError("that Coil upgrade is not active")
    new = []
    for t in old:
        if t in target:
            moved = t[:-len("Selected")] + target[t]
            if moved in have:                       # never write a duplicate tag
                continue
            new.append(moved)
        else:
            new.append(t)
    body = b"".join(struct.pack("<i", len(t) + 1) + t.encode("ascii") + b"\0" for t in new)
    blob = struct.pack("<i", len(new)) + body
    buf = bytearray(g.data)
    buf[node.value_offset:node.end] = blob
    Gvas._grow(buf, node, len(blob) - (node.end - node.value_offset))
    return Gvas(bytes(buf)), {"path": tuple(g.path_of(node)), "expected": new}
