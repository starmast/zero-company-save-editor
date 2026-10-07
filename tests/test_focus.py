"""Completing a focus tree's tier records: copied from other operators, exact, and only where it applies."""
import pytest

from zc import focus, medbay, revive, savefile
from zc.gvas import Gvas
from zc.service import EditError


def load(path):
    loaded = savefile.load_bytes(open(path, "rb").read())
    return loaded, Gvas(loaded.gvas)


def fallen(svc):
    return svc.state()["view"]["personnel"]["memorial"][0]["guid"]


def test_the_tutorial_operator_is_flagged_and_nobody_else(svc):
    svc.open("t", svc.test_name)
    flagged = [o["guid"] for o in svc.state()["view"]["personnel"]["memorial"] if o["tree_incomplete"]]
    assert flagged == [fallen(svc)]
    assert not any(o["tree_incomplete"] for o in svc.state()["view"]["personnel"]["roster"])


def test_complete_copies_the_donor_records_and_keeps_everything_else(svc):
    svc.open("t", svc.test_name)
    guid = fallen(svc)
    before_l, before = load(svc.test_path)
    others = {gid: bytes(before.data[n.start:n.end]) for gid, n in medbay.characters(before).items() if gid != guid}
    old = focus.records(before, guid)
    res = svc.apply([], force=True, actions=[{"type": "complete_focus_tree", "guid": guid}])
    assert res["backup"]
    loaded, g = load(svc.test_path)
    new = focus.records(g, guid)
    grew = [t for t in new if len(new[t]) > len(old[t])]
    assert grew, "some ability gained tiers"
    for tag in grew:
        assert new[tag][:len(old[tag])] == old[tag]                                # existing records untouched
        donor = max((focus.records(g, o).get(tag, []) for o in others), key=len)
        assert new[tag] == donor                                                   # identical to the other operators'
    for tag in set(new) - set(grew):
        assert new[tag] == old[tag]
    for gid, raw in others.items():                                                # nobody else touched
        n = medbay.characters(g)[gid]
        assert bytes(g.data[n.start:n.end]) == raw
    total, per = medbay.character_sizes(g)
    meta_total, meta_per = savefile.read_character_sizes(loaded)
    assert meta_total == total and all(meta_per[k] == v for k, v in per.items())
    assert g.opaque_count == before.opaque_count
    assert svc.state()["view"]["personnel"]["memorial"][0]["tree_incomplete"] is False


def test_a_second_run_and_complete_trees_are_refused(svc):
    svc.open("t", svc.test_name)
    guid = fallen(svc)
    svc.apply([], force=True, actions=[{"type": "complete_focus_tree", "guid": guid}])
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[{"type": "complete_focus_tree", "guid": guid}])
    living = svc.state()["view"]["personnel"]["roster"][0]["guid"]
    for bad in (living, "nope", "", None, "0" * 32):
        with pytest.raises(EditError):
            svc.apply([], force=True, actions=[{"type": "complete_focus_tree", "guid": bad}])


def test_bringing_someone_back_completes_their_tree_too(svc):
    svc.open("t", svc.test_name)
    guid = fallen(svc)
    svc.apply([], force=True, actions=[{"type": "revive_operator", "guid": guid}])
    _, g = load(svc.test_path)
    assert guid in revive.roster_order(g)
    assert guid not in focus.incomplete(g)
    assert all(len(r) in (0, 6) for r in focus.records(g, guid).values())          # full six tiers (or no ability picked)
    view = svc.state()["view"]["personnel"]
    assert not next(o for o in view["roster"] if o["guid"] == guid)["tree_incomplete"]
