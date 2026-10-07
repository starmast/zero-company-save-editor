"""Reordering the roster: a pure permutation of the operator ids, nothing else moves."""
import pytest

from zc import roster, savefile
from zc.gvas import Gvas
from zc.service import EditError


def file_order(path):
    return roster.order(Gvas(savefile.load_bytes(open(path, "rb").read()).gvas))


def view_order(svc):
    return [o["guid"] for o in svc.state()["view"]["personnel"]["roster"]]


def act(order):
    return {"type": "reorder_roster", "order": order}


def test_the_view_follows_the_roster_array(svc):
    svc.open("t", svc.test_name)
    assert view_order(svc) == file_order(svc.test_path)


def test_move_changes_only_the_roster_entries(svc):
    svc.open("t", svc.test_name)
    cur = view_order(svc)
    assert len(cur) >= 3
    new = cur[1:] + cur[:1]                                    # first operator to the back
    before = savefile.load_bytes(open(svc.test_path, "rb").read())
    res = svc.apply([], force=True, actions=[act(new)])
    assert res["backup"]
    after = savefile.load_bytes(open(svc.test_path, "rb").read())
    assert file_order(svc.test_path) == new and view_order(svc) == new
    assert len(after.gvas) == len(before.gvas)                  # same size, nothing resized
    diff = [i for i in range(len(before.gvas)) if before.gvas[i] != after.gvas[i]]
    arr = roster.roster_node(Gvas(before.gvas))
    lo, hi = arr.children[0].start, arr.children[-1].end
    assert diff and lo <= min(diff) and max(diff) < hi          # every changed byte is inside the Roster array
    for k, v in before.blobs.items():                           # metadata and other entries untouched
        if k != savefile.MAIN_ENTRY:
            assert after.blobs[k] == v


def test_a_swap_of_two_neighbours(svc):
    svc.open("t", svc.test_name)
    cur = view_order(svc)
    new = cur[:]
    new[2], new[3] = new[3], new[2]
    svc.apply([], force=True, actions=[act(new)])
    assert file_order(svc.test_path) == new


def test_bad_orders_are_refused(svc):
    svc.open("t", svc.test_name)
    cur = view_order(svc)
    bad = [None, [], "x", cur[:-1], cur + cur[:1], cur[:-1] + ["0" * 32], cur[:-1] + ["nope"], [cur[0]] * len(cur),
           cur[:-1] + [None], cur]                              # last one: unchanged order
    for order in bad:
        with pytest.raises(EditError):
            svc.apply([], force=True, actions=[act(order)])
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[act(cur[1:] + cur[:1]), act(cur[2:] + cur[:2])])
    assert file_order(svc.test_path) == cur


def test_reorder_combines_with_a_scalar_edit(svc):
    st = svc.open("t", svc.test_name)
    credits = next(f for f in st["fields"] if f["group"] == "Resources" and f["label"] == "Credits")
    cur = view_order(svc)
    new = list(reversed(cur))
    svc.apply([{"id": credits["id"], "value": credits["value"] + 1}], force=True, actions=[act(new)])
    after = svc.state()
    assert next(f for f in after["fields"] if f["id"] == credits["id"])["value"] == credits["value"] + 1
    assert view_order(svc) == new
