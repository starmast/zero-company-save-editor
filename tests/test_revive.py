"""Bringing a fallen operator back: exact, consistent, and refuses what it should."""
import pytest

from zc import medbay, revive, roster, savefile
from zc.gvas import Gvas
from zc.service import EditError


def load(path):
    loaded = savefile.load_bytes(open(path, "rb").read())
    return loaded, Gvas(loaded.gvas)


def fallen(svc):
    st = svc.state()
    return st["view"]["personnel"]["memorial"][0]["guid"]


def act(guid):
    return {"type": "revive_operator", "guid": guid}


def test_the_sample_has_a_fallen_operator_with_death_markers(svc):
    svc.open("t", svc.test_name)
    guid = fallen(svc)
    _, g = load(svc.test_path)
    assert guid in revive.dead_guids(g) and guid in revive.died_map(g)
    assert guid not in revive.roster_order(g) and guid not in revive.recruited_map(g)
    assert any(medbay.effect_name(g, e).startswith("GE_ApplyDead") for e in medbay.effects_node(g, guid).children)


def test_revive_moves_the_operator_back_and_keeps_every_size_consistent(svc):
    svc.open("t", svc.test_name)
    guid = fallen(svc)
    before_l, before = load(svc.test_path)
    others = {gid: bytes(before.data[n.start:n.end]) for gid, n in medbay.characters(before).items() if gid != guid}
    res = svc.apply([], force=True, actions=[act(guid)])
    assert res["backup"] and res["count"] == 1
    loaded, g = load(svc.test_path)
    assert guid not in revive.dead_guids(g) and guid not in revive.died_map(g)
    assert revive.roster_order(g) == revive.roster_order(before) + [guid]          # last place
    assert revive.recruited_map(g)[guid] == revive.strategy_turn(g)
    assert not any(medbay.effect_name(g, e).startswith(revive.DEAD_EFFECTS) for e in medbay.effects_node(g, guid).children)
    # nobody else was touched
    for gid, raw in others.items():
        n = medbay.characters(g)[gid]
        assert bytes(g.data[n.start:n.end]) == raw
    # sizes: file re-parses with the same tree shape, and the metadata mirrors the new sizes
    assert g.opaque_count <= before.opaque_count
    total, per = medbay.character_sizes(g)
    meta_total, meta_per = savefile.read_character_sizes(loaded)
    assert meta_total == total and all(meta_per[k] == v for k, v in per.items())
    for k, v in before_l.blobs.items():
        if k not in (savefile.MAIN_ENTRY, "SaveGameMetaData.json"):
            assert loaded.blobs[k] == v
    # the app now shows them on the roster, not in the Memorial
    view = svc.state()["view"]["personnel"]
    assert guid in [o["guid"] for o in view["roster"]] and guid not in [o["guid"] for o in view["memorial"]]
    assert not view["roster"][-1]["dead"]


def test_revive_combines_with_a_scalar_edit(svc):
    st = svc.open("t", svc.test_name)
    credits = next(f for f in st["fields"] if f["group"] == "Resources" and f["label"] == "Credits")
    guid = fallen(svc)
    svc.apply([{"id": credits["id"], "value": credits["value"] + 1}], force=True, actions=[act(guid)])
    after = svc.state()
    assert next(f for f in after["fields"] if f["id"] == credits["id"])["value"] == credits["value"] + 1
    assert guid in [o["guid"] for o in after["view"]["personnel"]["roster"]]


def test_a_revived_operator_can_be_reordered_afterwards(svc):
    svc.open("t", svc.test_name)
    guid = fallen(svc)
    svc.apply([], force=True, actions=[act(guid)])
    order = [o["guid"] for o in svc.state()["view"]["personnel"]["roster"]]
    new = [order[-1]] + order[:-1]
    svc.apply([], force=True, actions=[{"type": "reorder_roster", "order": new}])
    assert roster.order(load(svc.test_path)[1]) == new


def test_refusals(svc):
    svc.open("t", svc.test_name)
    living = svc.state()["view"]["personnel"]["roster"][0]["guid"]
    for bad in (living, "nope", "", None, "0" * 32, "G" * 32):
        with pytest.raises(EditError):
            svc.apply([], force=True, actions=[act(bad)])
    guid = fallen(svc)
    with pytest.raises(EditError):                                 # the same operator twice
        svc.apply([], force=True, actions=[act(guid), act(guid)])
    assert guid in revive.dead_guids(load(svc.test_path)[1])       # refusals left the save alone


def test_a_full_roster_refuses(svc):
    svc.open("t", svc.test_name)
    guid = fallen(svc)
    model = svc.current.model
    full = len(model.roster)
    real = model.fact
    model.fact = lambda name, d=0: full if name == "Facts.Values.MaximumRosterCount" else real(name, d)
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[act(guid)])
