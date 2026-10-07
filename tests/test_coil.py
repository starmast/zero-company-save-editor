"""Removing Coil upgrades: only the chosen crisis tags change, and the save stays consistent."""
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


def test_the_sample_save_lists_its_upgrades(svc):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    assert ids, "the sample save has permanent Coil upgrades"
    assert all(i.split(".")[0] in ("Major", "Minor") for i in ids)
    for row in svc.state()["view"]["coil"]["active"]:
        assert row["unit"] and row["name"]


def test_remove_one_turns_only_that_tag_back_to_available(svc):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    before = tags_of(svc.test_path)
    victim = ids[0]
    res = svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "ids": [victim]}])
    assert res["backup"]
    after = tags_of(svc.test_path)
    old_tag, new_tag = f"{coil.PREFIX}{victim}.Selected", f"{coil.PREFIX}{victim}.Available"
    assert old_tag in before and old_tag not in after and new_tag in after
    assert [t for t in before if t != old_tag] == [t for t in after if t != new_tag]       # nothing else moved
    assert len(after) == len(before)
    assert victim not in active_ids(svc) and set(active_ids(svc)) == set(ids) - {victim}


def test_remove_all_and_sizes_stay_consistent(svc):
    svc.open("t", svc.test_name)
    ids = active_ids(svc)
    raw_before = savefile.load_bytes(open(svc.test_path, "rb").read())
    g_before = Gvas(raw_before.gvas)
    svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "ids": ids}])
    loaded = savefile.load_bytes(open(svc.test_path, "rb").read())
    g = Gvas(loaded.gvas)
    assert coil.active(g) == [] and g.opaque_count == g_before.opaque_count
    assert len(g.data) - len(g_before.data) == len(ids)           # "Selected" -> "Available" is one byte longer
    assert active_ids(svc) == []
    # the file reopens cleanly and a second removal is refused
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "ids": ids[:1]}])


def test_bad_requests_are_refused(svc):
    svc.open("t", svc.test_name)
    for ids in ([], ["nope"], ["Major.Nobody_Z"], ["../x"], [None], ["Major.Striker_A.Selected"]):
        with pytest.raises(EditError):
            svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "ids": ids}])
    ids = active_ids(svc)
    with pytest.raises(EditError):                                 # the same upgrade twice
        svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "ids": [ids[0], ids[0]]}])
    with pytest.raises(EditError):                                 # two separate removal actions
        svc.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "ids": [ids[0]]},
                                          {"type": "remove_coil_upgrades", "ids": [ids[0]]}])


def test_removal_combines_with_a_scalar_edit(svc):
    st = svc.open("t", svc.test_name)
    credits = next(f for f in st["fields"] if f["group"] == "Resources" and f["label"] == "Credits")
    ids = active_ids(svc)
    svc.apply([{"id": credits["id"], "value": credits["value"] + 1}], force=True,
              actions=[{"type": "remove_coil_upgrades", "ids": ids[:1]}])
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
    assert sorted(active_ids(s)) == ["Major.BXM_A", "Major.Striker_A", "Minor.B1_A", "Minor.Striker_B"]
    s.apply([], force=True, actions=[{"type": "remove_coil_upgrades", "ids": active_ids(s)}])
    assert active_ids(s) == []
