"""Healing injured operators: the removal must be exact, consistent and safe."""
import collections
import os

import pytest

from conftest import GAME_DIR
from zc import medbay, savefile
from zc.gvas import Gvas
from zc.service import EditError


def effects(path_or_loaded, guid):
    loaded = (savefile.load_bytes(open(path_or_loaded, "rb").read())
              if isinstance(path_or_loaded, str) else path_or_loaded)
    g = Gvas(loaded.gvas)
    arr = medbay.effects_node(g, guid)
    return g, loaded, collections.Counter(bytes(g.data[e.start:e.end]) for e in arr.children), \
        [medbay.effect_name(g, e) for e in arr.children]


def injured_guid(svc):
    """The sample save only has an injured *dead* operator; the machinery is the same, so test on it."""
    model = svc.current.model
    op = next(o for o in model.operators.values() if o["injuries"])
    return op


def test_cannot_heal_the_dead_or_the_healthy(svc):
    svc.open("t", svc.test_name)
    dead = injured_guid(svc)
    assert dead["dead"]
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[{"type": "heal_operator", "guid": dead["guid"]}])
    healthy = next(o for o in svc.current.model.operators.values() if not o["injuries"] and not o["dead"] and o["focus"])
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[{"type": "heal_operator", "guid": healthy["guid"]}])
    for bad in ("nope", "", "../" + "0" * 29, "G" * 32, None):
        with pytest.raises(EditError):
            svc.apply([], force=True, actions=[{"type": "heal_operator", "guid": bad}])


def test_heal_removes_only_the_injury_and_keeps_every_size_consistent(svc):
    svc.open("t", svc.test_name)
    op = injured_guid(svc)
    guid = op["guid"]
    before_g, before_l, before_fx, before_names = effects(svc.test_path, guid)
    others_before = {gid: effects(svc.test_path, gid)[2] for gid in medbay.characters(before_g) if gid != guid}
    op["dead"] = False                                                   # let the machinery run on this save
    res = svc.apply([], force=True, actions=[{"type": "heal_operator", "guid": guid}])
    assert res["count"] == 1 and res["backup"]
    g, loaded, fx, names = effects(svc.test_path, guid)
    assert not any(n.startswith("GE_Injured") for n in names)
    assert sorted(names) == sorted(n for n in before_names if not n.startswith("GE_Injured"))
    # only the injury element is gone; every remaining element is byte-for-byte what it was
    assert (before_fx - fx).total() == 1 and (fx - before_fx).total() == 0
    # no other operator was touched
    assert all(effects(svc.test_path, gid)[2] == fx0 for gid, fx0 in others_before.items())
    # payload shrank by exactly the removed element, and the metadata mirrors the new sizes
    removed = next(b for b in before_fx if b not in fx)
    assert len(loaded.gvas) == len(before_l.gvas) - len(removed)
    total, per = medbay.character_sizes(g)
    meta_total, meta_per = savefile.read_character_sizes(loaded)
    assert meta_total == total and all(meta_per[k] == v for k, v in per.items())
    assert g.opaque_count == before_g.opaque_count


def test_heal_combines_with_a_scalar_edit_in_one_apply(svc):
    st = svc.open("t", svc.test_name)
    op = injured_guid(svc)
    op["dead"] = False
    credits = next(f for f in st["fields"] if f["group"] == "Resources" and f["label"] == "Credits")
    svc.apply([{"id": credits["id"], "value": credits["value"] - 500}], force=True,
              actions=[{"type": "heal_operator", "guid": op["guid"]}])
    after = svc.state()
    assert next(f for f in after["fields"] if f["id"] == credits["id"] or f["label"] == "Credits")["value"] == credits["value"] - 500
    assert medbay.injury_count(Gvas(savefile.load_bytes(open(svc.test_path, "rb").read()).gvas), op["guid"]) == 0


# ---- regression against a real before/after pair written by the game itself (skipped when absent)
BEFORE = os.path.join(GAME_DIR, "HUB_Root_2026.10.06-20.43.17.sav")
AFTER = os.path.join(GAME_DIR, "HUB_Root_2026.10.06-20.43.39.sav")


@pytest.mark.skipif(not (os.path.exists(BEFORE) and os.path.exists(AFTER)), reason="game before/after saves not present")
def test_heal_matches_what_the_game_wrote_when_it_used_the_bacta_tank(tmp_path):
    import shutil
    from zc.service import Service
    d = tmp_path / "s"; d.mkdir()
    shutil.copy2(BEFORE, d / os.path.basename(BEFORE))
    svc = Service({"t": str(d)}, str(tmp_path / "b"))
    st = svc.open("t", os.path.basename(BEFORE))
    mosey = next(o for o in st["view"]["medbay"]["injured"] if o["name"] == "Mosey Poqua")
    svc.apply([], force=True, actions=[{"type": "heal_operator", "guid": mosey["guid"]}])
    ours = effects(str(d / os.path.basename(BEFORE)), mosey["guid"])
    game = effects(AFTER, mosey["guid"])
    assert ours[2] == game[2], "effect entries differ from the game's own healed save"
    assert sorted(ours[3]) == sorted(game[3])
