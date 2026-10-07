"""Taking Coil upgrades away: only the chosen crisis tags change, and the save stays consistent."""
import os
import shutil

import pytest

from conftest import GAME_DIR
from zc import coil, gamedata, savefile
from zc.gvas import Gvas
from zc.service import EditError, Service


def tags_of(path):
    return coil.tags(Gvas(savefile.load_bytes(open(path, "rb").read()).gvas))


def active_ids(svc):
    return [u["id"] for u in svc.state()["view"]["coil"]["active"]]


def act(ids, to="Available"):
    return {"type": "remove_coil_upgrades", "changes": [{"id": i, "to": to} for i in ids]}


def test_the_sample_save_lists_its_upgrades(svc):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    assert ids, "the sample save has permanent Coil upgrades"
    assert all(i.split(".")[0] in ("Major", "Minor") for i in ids)
    for row in svc.state()["view"]["coil"]["active"]:
        assert row["unit"] and row["name"]


@pytest.mark.parametrize("to", ["Available", "Prevented"])
def test_taking_one_away_renames_only_that_tag(svc, to):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    before = tags_of(svc.test_path)
    victim = ids[0]
    res = svc.apply([], force=True, actions=[act([victim], to)])
    assert res["backup"]
    after = tags_of(svc.test_path)
    old_tag, new_tag = f"{coil.PREFIX}{victim}.Selected", f"{coil.PREFIX}{victim}.{to}"
    assert old_tag in before and old_tag not in after and new_tag in after
    assert [t for t in before if t != old_tag] == [t for t in after if t != new_tag]       # nothing else moved
    assert len(after) == len(before)
    assert set(active_ids(svc)) == set(ids) - {victim}


def test_each_upgrade_can_go_its_own_way_in_one_apply(svc):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    assert len(ids) >= 2
    svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "changes": [
        {"id": ids[0], "to": "Available"}, {"id": ids[1], "to": "Prevented"}]}])
    after = set(tags_of(svc.test_path))
    assert f"{coil.PREFIX}{ids[0]}.Available" in after and f"{coil.PREFIX}{ids[1]}.Prevented" in after
    assert not set(active_ids(svc)) & set(ids[:2])


@pytest.mark.parametrize("to", ["Available", "Prevented"])
def test_all_away_and_sizes_stay_consistent(svc, to):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    g_before = Gvas(savefile.load_bytes(open(svc.test_path, "rb").read()).gvas)
    svc.apply([], force=True, actions=[act(ids, to)])
    loaded = savefile.load_bytes(open(svc.test_path, "rb").read())
    g = Gvas(loaded.gvas)
    assert coil.active(g) == [] and g.opaque_count == g_before.opaque_count
    grew = len(to) - len("Selected")                                   # Available +1, Prevented +0
    assert len(g.data) - len(g_before.data) == grew * len(ids)
    assert active_ids(svc) == []
    with pytest.raises(EditError):                                      # nothing left to take away
        svc.apply([], force=True, actions=[act(ids[:1], to)])


def test_bad_requests_are_refused(svc):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    bad = [None, [], "x", [{}], [{"id": "Major.Nobody_Z", "to": "Available"}], [{"id": "../x", "to": "Available"}],
           [{"id": None, "to": "Available"}], [{"id": "Major.Striker_A.Selected", "to": "Available"}],
           [{"id": ids[0], "to": "Selected"}], [{"id": ids[0], "to": "Nope"}], [{"id": ids[0]}],
           [{"id": ids[0], "to": "Available"}, {"id": ids[0], "to": "Prevented"}]]
    for changes in bad:
        with pytest.raises(EditError):
            svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "changes": changes}])
    with pytest.raises(EditError):                                      # two separate actions
        svc.apply([], force=True, actions=[act(ids[:1]), act(ids[1:2])])
    assert active_ids(svc) == ids                                       # all refusals left the save alone


def test_removal_combines_with_a_scalar_edit(svc):
    st = svc.open("t", svc.test_name)
    credits = next(f for f in st["fields"] if f["group"] == "Resources" and f["label"] == "Credits")
    ids = active_ids(svc)
    svc.apply([{"id": credits["id"], "value": credits["value"] + 1}], force=True, actions=[act(ids[:1], "Prevented")])
    after = svc.state()
    assert next(f for f in after["fields"] if f["id"] == credits["id"])["value"] == credits["value"] + 1
    assert ids[0] not in active_ids(svc)


def test_names_come_from_the_game_data_when_available(svc):
    svc.open("t", svc.test_name)
    rows = svc.state()["view"]["coil"]["active"]
    if not gamedata.available() or not gamedata.crisis_effect(rows[0]["id"]):
        pytest.skip("game data not extracted")
    assert all(r["description"] for r in rows)
    assert all(r["unit"].startswith("Coil") for r in rows)


def test_real_before_state_lists_the_four_upgrades_the_game_shows(tmp_path):
    """MOD BASE 7.6 is the save the in-game screen showed four active Coil upgrades for."""
    src = None
    if os.path.isdir(GAME_DIR):
        for f in sorted(os.listdir(GAME_DIR)):
            if f.startswith("HUB_Root_2026.10.06-20.57.36"):
                src = os.path.join(GAME_DIR, f)
    if not src:
        pytest.skip("game save not present")
    d = tmp_path / "s"; d.mkdir()
    shutil.copy2(src, d / os.path.basename(src))
    s = Service({"t": str(d)}, str(tmp_path / "b"))
    s.open("t", os.path.basename(src))
    ids = sorted(active_ids(s))
    assert ids == ["Major.BXM_A", "Major.Striker_A", "Minor.B1_A", "Minor.Striker_B"]
    s.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "changes": [
        {"id": ids[0], "to": "Prevented"}, {"id": ids[1], "to": "Available"}, {"id": ids[2], "to": "Prevented"},
        {"id": ids[3], "to": "Available"}]}])
    assert active_ids(s) == []
