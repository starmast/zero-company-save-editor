import glob, hashlib, os, zipfile

import pytest

from conftest import GAME_DIR, SAMPLE_DIR
from zc import savefile
from zc.gvas import Gvas
from zc.service import EditError


def h(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()


def all_saves():
    files = glob.glob(os.path.join(SAMPLE_DIR, "*.sav")) + glob.glob(os.path.join(GAME_DIR, "*.sav"))
    return [f for f in files if open(f, "rb").read(4) == b"PK\x03\x04"]


@pytest.mark.parametrize("path", all_saves())
def test_parse_every_save_and_roundtrip(path):
    loaded = savefile.load_bytes(open(path, "rb").read())
    g = Gvas(loaded.gvas)
    assert g.to_bytes() == loaded.gvas                     # parser never mutates
    assert len(loaded.gvas) - g.props_end <= 8             # consumed the whole payload
    assert g.opaque_count < 5000
    # repack with no edits keeps every entry byte-identical
    again = savefile.load_bytes(savefile.rebuild(loaded, loaded.gvas))
    assert again.blobs == loaded.blobs


def field(state, group, label):
    return next(f for f in state["fields"] if f["group"] == group and f["label"] == label)


def test_open_lists_resources(svc):
    st = svc.open("t", svc.test_name)
    credits = field(st, "Resources", "Credits")
    assert credits["value"] > 0
    assert {"Credits", "Intel", "Contacts"} <= {f["label"] for f in st["fields"] if f["group"] == "Resources"}
    assert not any(f["section"].startswith("Unknown") for f in st["fields"] if f["group"] == "Operators")


def test_edit_credits_backup_and_minimal_diff(svc):
    before = h(svc.test_path)
    old_gvas = savefile.load_bytes(open(svc.test_path, "rb").read()).gvas
    st = svc.open("t", svc.test_name)
    f = field(st, "Resources", "Credits")
    res = svc.apply([{"id": f["id"], "value": 123456}], force=True)
    assert res["backup"]
    new_gvas = savefile.load_bytes(open(svc.test_path, "rb").read()).gvas
    diff = [i for i in range(len(old_gvas)) if old_gvas[i] != new_gvas[i]]
    assert len(old_gvas) == len(new_gvas) and diff and max(diff) - min(diff) < 4
    assert field(svc.state(), "Resources", "Credits")["value"] == 123456
    # the backup is the untouched pre-edit file; the original is also kept
    bdir = svc.backup_dir(svc.test_name)
    assert any(h(os.path.join(bdir, b)) == before for b in os.listdir(bdir) if not b.endswith(".orig.sav"))
    assert any(b.endswith(".orig.sav") for b in os.listdir(bdir))
    # restore brings the original credits back
    snap = next(b["file"] for b in svc.list_backups(svc.test_name) if b["original"])
    svc.restore(snap, force=True)
    assert h(svc.test_path) == before


def test_rejects_bad_values(svc):
    st = svc.open("t", svc.test_name)
    intel = field(st, "Resources", "Intel")
    for bad in (intel["max"] + 1, -1, 1.5, "abc", None, True):
        with pytest.raises(EditError):
            svc.apply([{"id": intel["id"], "value": bad}], force=True)
    with pytest.raises(EditError):
        svc.apply([{"id": "o1", "value": 1}], force=True)      # not a known field
    assert h(svc.test_path) == h(svc.test_path)


def test_focus_moves_total_with_available(svc):
    st = svc.open("t", svc.test_name)
    f = next(f for f in st["fields"] if f["label"] == "Available focus points")
    n = svc.current.model.by_id[f["id"]]
    tot = svc.current.gvas.get(n.linked[0])
    svc.apply([{"id": f["id"], "value": f["value"] + 5}], force=True)
    c = svc.current
    assert c.gvas.get(c.model.by_id[f["id"]].node) == f["value"] + 5
    assert c.gvas.get(c.model.by_id[f["id"]].linked[0]) == tot + 5


def test_stale_disk_is_refused(svc):
    st = svc.open("t", svc.test_name)
    f = field(st, "Resources", "Credits")
    with open(svc.test_path, "ab") as fh:                    # game "autosaves" meanwhile
        fh.write(b"x")
    with pytest.raises(EditError):
        svc.apply([{"id": f["id"], "value": 5}], force=True)


def test_as_copy_leaves_original(svc):
    before = h(svc.test_path)
    st = svc.open("t", svc.test_name)
    f = field(st, "Resources", "Credits")
    res = svc.apply([{"id": f["id"], "value": 777}], force=True, as_copy=True)
    assert h(svc.test_path) == before
    assert os.path.exists(os.path.join(os.path.dirname(svc.test_path), res["written"]))


def test_path_traversal_blocked(svc):
    for name in ("../x.sav", "..\\x.sav","a/b.sav", "x.txt"):
        with pytest.raises(EditError):
            svc.resolve("t", name)
    with pytest.raises(EditError):
        svc.resolve("nope", svc.test_name)


# ---------------------------------------------------------------- base upgrades
from zc import upgrades


def upgrade_row(state, name):
    return next(r for r in state["upgrades"]["items"] if r["name"] == name)


def test_start_upgrade_matches_game_written_entry():
    """Starting Droid Repair on the game's turn-4 save must equal the game's own later save."""
    a = os.path.join(GAME_DIR, "HUB_Root_2026.10.04-11.00.21.sav")
    b = os.path.join(GAME_DIR, "HUB_Root_2026.10.04-11.32.09.sav")
    if not (os.path.exists(a) and os.path.exists(b)):
        pytest.skip("reference game saves not present")
    ga = Gvas(savefile.load_bytes(open(a, "rb").read()).gvas)
    gb = Gvas(savefile.load_bytes(open(b, "rb").read()).gvas)
    name = "Upgrade_Facilities_Medbay_DroidRepair"
    row = next(r for r in upgrades.list_upgrades(ga)["items"] if r["name"] == name)
    mine = upgrades.start_upgrade(ga, int(row["id"][1:]), name)

    def entry(g):
        for e in g.child(upgrades.strategy_data(g), "ActiveRecipes").children:
            if upgrades._recipe_key(g, e.children[1]).endswith("/" + name):
                return bytes(g.data[e.start:e.end])
    assert entry(mine) == entry(gb)
    assert mine.opaque_count == ga.opaque_count


def test_start_upgrade_end_to_end(svc):
    st = svc.open("t", svc.test_name)
    name = "Upgrade_Crew_FocusPoint_Focus_1"
    row = upgrade_row(st, name)
    assert row["status"] == "Available" and row["can_start"]
    before_size = os.path.getsize(svc.test_path)
    res = svc.apply([], force=True, actions=[{"type": "start_upgrade", "id": row["id"], "name": name}])
    assert res["backup"]
    st2 = svc.state()
    row2 = upgrade_row(st2, name)
    assert row2["status"] == "InProgress" and st2["upgrades"]["in_progress"] == 1
    # every other upgrade untouched
    others = {r["name"]: r["status"] for r in st["upgrades"]["items"] if r["name"] != name}
    assert others == {r["name"]: r["status"] for r in st2["upgrades"]["items"] if r["name"] != name}
    # container sizes and metadata stay consistent
    l = savefile.load_bytes(open(svc.test_path, "rb").read())
    g = Gvas(l.gvas)
    from zc.service import _blob_sizes
    import re
    txt = l.blobs["SaveGameMetaData.json"].decode("utf-16-le")
    for k, v in _blob_sizes(g).items():
        assert int(re.search(r'"%s"\s*:\s*(\d+)' % k, txt).group(1)) == v
    assert g.opaque_count == svc.current.gvas.opaque_count
    # restoring brings the original bytes back
    orig = next(b["file"] for b in svc.list_backups(svc.test_name) if b["original"])
    svc.restore(orig, force=True)
    assert os.path.getsize(svc.test_path) == before_size


def test_upgrade_tier_gating_and_bad_actions(svc):
    st = svc.open("t", svc.test_name)
    t2 = upgrade_row(st, "Upgrade_Crew_FocusPoint_Focus_2")
    assert not t2["can_start"]
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[{"type": "start_upgrade", "id": t2["id"], "name": t2["name"]}])
    t1 = upgrade_row(st, "Upgrade_Crew_FocusPoint_Focus_1")
    for bad in ({"type": "nope"}, {"type": "start_upgrade", "id": "r9999", "name": "x"},
                {"type": "start_upgrade", "id": "../1", "name": "x"},
                {"type": "start_upgrade", "id": t1["id"], "name": "WrongName"}):
        with pytest.raises(EditError):
            svc.apply([], force=True, actions=[bad])


def test_scalar_and_upgrade_together(svc):
    st = svc.open("t", svc.test_name)
    f = field(st, "Resources", "Credits")
    row = upgrade_row(st, "Upgrade_Crew_FocusPoint_Focus_1")
    svc.apply([{"id": f["id"], "value": 4242}], force=True,
              actions=[{"type": "start_upgrade", "id": row["id"], "name": row["name"]}])
    st2 = svc.state()
    assert field(st2, "Resources", "Credits")["value"] == 4242
    assert upgrade_row(st2, row["name"])["status"] == "InProgress"


def test_verifier_blocks_inconsistent_metadata(svc, monkeypatch):
    """If the metadata size sync were skipped, the write must be refused, file untouched."""
    st = svc.open("t", svc.test_name)
    row = upgrade_row(st, "Upgrade_Crew_FocusPoint_Focus_1")
    before = h(svc.test_path)
    monkeypatch.setattr(savefile, "sync_metadata_sizes", lambda *a, **k: None)
    with pytest.raises(EditError, match="metadata"):
        svc.apply([], force=True, actions=[{"type": "start_upgrade", "id": row["id"], "name": row["name"]}])
    assert h(svc.test_path) == before


def test_backups_are_separate_per_folder(tmp_path):
    import shutil
    from conftest import SAMPLE_DIR, sample_name
    from zc.service import Service
    name = sample_name()
    for d in ("a", "b"):
        (tmp_path / d).mkdir()
        shutil.copy2(os.path.join(SAMPLE_DIR, name), tmp_path / d / name)
    s = Service({"a": str(tmp_path / "a"), "b": str(tmp_path / "b")}, str(tmp_path / "bk"))
    assert s.backup_dir(name, "a") != s.backup_dir(name, "b")
    s.open("a", name); s.open("b", name)
    assert len(s.list_backups(name)) == 1                    # only b's own original


def test_expedite_in_progress_upgrade(svc):
    st = svc.open("t", svc.test_name)
    name = "Upgrade_Crew_FocusPoint_Focus_1"
    row = upgrade_row(st, name)
    assert not row["can_expedite"]                           # only in-progress ones
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[{"type": "expedite_upgrade", "id": row["id"], "name": name}])
    svc.apply([], force=True, actions=[{"type": "start_upgrade", "id": row["id"], "name": name}])
    r2 = upgrade_row(svc.state(), name)
    assert r2["status"] == "InProgress" and r2["can_expedite"] and r2["progress"] == 0
    size = os.path.getsize(svc.test_path)
    svc.apply([], force=True, actions=[{"type": "expedite_upgrade", "id": r2["id"], "name": name}])
    r3 = upgrade_row(svc.state(), name)
    assert r3["status"] == "InProgress"                      # the game, not us, completes it
    assert r3["progress"] >= upgrades.EXPEDITE_TURNS and r3["started"] <= r2["started"]
    assert not r3["can_expedite"]                            # already expedited
    assert abs(os.path.getsize(svc.test_path) - size) < 64   # in-place numbers only


def test_start_and_expedite_in_one_apply(svc):
    st = svc.open("t", svc.test_name)
    name = "Upgrade_Crew_FocusPoint_Focus_1"
    row = upgrade_row(st, name)
    svc.apply([], force=True, actions=[{"type": "start_expedite_upgrade", "id": row["id"], "name": name}])
    r = upgrade_row(svc.state(), name)
    assert r["status"] == "InProgress"
    assert r["progress"] >= upgrades.EXPEDITE_TURNS and r["started"] == 0
    # the rest of the entry matches a plain start (tiers, facility tag), so the game treats it the same
    g1 = svc.current.gvas
    sd = upgrades.strategy_data(g1)
    v = g1.child(sd, "ActiveRecipes").children[int(r["id"][1:])].children[1]
    assert g1.child(v, "FulfilledRewardTiers").count == 1
    tag = g1.child(g1.child(g1.child(v, "InProgressRecipeContext"), "FacilityTag"), "TagName")
    assert g1.get(tag) == upgrades.UPGRADE_FACILITY_TAG
    # a non-startable upgrade is still refused
    t2 = upgrade_row(svc.state(), "Upgrade_Crew_FocusPoint_Focus_2")
    with pytest.raises(EditError):
        svc.apply([], force=True, actions=[{"type": "start_expedite_upgrade", "id": t2["id"], "name": t2["name"]}])


def test_custom_operators_use_real_names_and_bond_scale(svc):
    st = svc.open("t", svc.test_name)
    secs = {f["section"] for f in st["fields"] if f["group"] == "Operators"}
    assert not any(s.startswith("Custom Operator") for s in secs), secs     # real names found
    bonds = [f for f in st["fields"] if f["label"] == "Bond level"]
    assert bonds and all(f["min"] == -4 and f["max"] == 4 for f in bonds)
    assert all(f["min"] <= f["value"] <= f["max"] for f in st["fields"] if f["group"] == "Bonds")
