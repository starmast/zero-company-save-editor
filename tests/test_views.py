"""View models for the game-style UI: every editable value must map to a real field id."""
import re

import pytest

from zc import upgrades
from zc.views import BOND_OFFSET, LEVEL_OFFSET


def refs(obj):
    """All {id,...} refs anywhere in a view model."""
    if isinstance(obj, dict):
        if "id" in obj and re.fullmatch(r"o\d+", str(obj["id"])) and "min" in obj:
            yield obj
        for v in obj.values():
            yield from refs(v)
    elif isinstance(obj, list):
        for v in obj:
            yield from refs(v)


@pytest.fixture
def state(svc):
    return svc.open("t", svc.test_name)


def test_every_view_ref_is_a_real_field(state):
    fields = {f["id"]: f for f in state["fields"]}
    seen = list(refs(state["view"]))
    assert len(seen) > 200
    for r in seen:
        f = fields[r["id"]]                              # KeyError = view invented an id
        assert (f["value"], f["min"], f["max"]) == (r["value"], r["min"], r["max"])


def test_header_matches_resource_fields(state):
    hd = state["view"]["header"]
    labels = {r["label"] for r in hd["resources"]}
    assert {"Credits", "Intel", "Contacts"} <= labels
    fields = {f["id"]: f for f in state["fields"]}
    for r in hd["resources"]:
        assert fields[r["id"]]["group"] == "Resources"
    lvl = hd["level"]
    assert lvl["offset"] == LEVEL_OFFSET
    assert fields[lvl["id"]]["label"] == "Roster level"
    assert hd["turn"] and hd["xp"]


def test_personnel_roster_and_memorial(state):
    p = state["view"]["personnel"]
    assert p["roster"], "roster should list the live operators"
    assert all(not o["dead"] for o in p["roster"])
    assert all(o["dead"] for o in p["memorial"])
    names = [o["name"] for o in p["roster"]]
    assert len(names) == len(set(names))
    assert not any(n.startswith(("Unknown", "Custom Operator")) for n in names)


def test_bonds_use_the_game_scale(state):
    p = state["view"]["personnel"]
    for o in p["roster"] + p["memorial"]:
        for b in o["bonds"]:
            assert 0 <= b["game_level"] <= 8
            assert b["game_level"] == b["level"]["value"] + BOND_OFFSET
            assert b["level"]["min"] == -4 and b["level"]["max"] == 4
    # a bond is visible from both operators
    by = {o["guid"]: o for o in p["roster"] + p["memorial"]}
    a = p["roster"][0]
    b0 = a["bonds"][0]
    if b0["partner"] in by:
        back = next(x for x in by[b0["partner"]]["bonds"] if x["partner"] == a["guid"])
        assert back["level"]["id"] == b0["level"]["id"]


def test_cross_training_total_matches_pairs(state):
    p = state["view"]["personnel"]
    flat = sum(f["value"] for f in state["fields"] if f["label"] == "Available cross-trainings")
    assert p["cross_training_total"] == flat


def test_upgrades_screen_covers_every_upgrade(state):
    u = state["view"]["upgrades"]
    nodes = [n for t in u["tabs"] for r in t["rows"] for n in r["nodes"]]
    assert len(nodes) == len(state["upgrades"]["items"])
    assert {t["key"] for t in u["tabs"]} == {"Facilities", "Crew", "Weapons"}
    assert all(n["den"] is None or 1 <= n["den"] <= 10 for n in nodes)
    assert len(u["slots"]) == state["upgrades"]["in_progress"]
    assert u["level"] == state["view"]["header"]["level"]["value"] + LEVEL_OFFSET


def test_armory_and_galaxy(state):
    a = state["view"]["armory"]["items"]
    assert a and all(i["kind"] in ("Utility", "Modification", "Other") for i in a)
    names = [i["name"] for i in a]
    assert len(names) == len(set(names)), "duplicate inventory entries must be told apart"
    g = state["view"]["galaxy"]["regions"]
    assert len(g) >= 10 and all(r["influence"] for r in g)


def test_edit_through_view_ref_changes_the_same_bytes(svc, state):
    """Editing via a view ref id is identical to editing via the flat field id."""
    op = next(o for o in state["view"]["personnel"]["roster"] if o["focus"])
    ref = op["focus"]
    svc.apply([{"id": ref["id"], "value": ref["value"] + 3}], force=True)
    after = svc.state()
    new = next(o for o in after["view"]["personnel"]["roster"] if o["guid"] == op["guid"])
    assert new["focus"]["value"] == ref["value"] + 3
    assert new["focus"]["total"] == op["focus"]["total"] + 3        # total moves with it
    assert next(f for f in after["fields"] if f["id"] == ref["id"])["value"] == ref["value"] + 3


def test_medbay_view(state):
    mb = state["view"]["medbay"]
    assert mb["beds"] >= 1 and mb["tanks"] >= 1
    assert mb["cost"]["bed"] > 0 and mb["cost"]["tank"] > mb["cost"]["bed"]
    assert mb["injured"] == []                          # the only injury in this save is on a dead operator
    assert mb["treating"] == []
    assert all(isinstance(v, int) for v in (mb["beds"], mb["tanks"]))


def test_medbay_lists_injured_living_operators_only(svc, state):
    from zc import views
    model = svc.current.model
    living = next(o for o in model.operators.values() if not o["dead"] and o["focus"])
    dead = next(o for o in model.operators.values() if o["dead"])
    living["injuries"], dead["injuries"] = 2, 1
    mb = views.medbay(model, {living["guid"]})
    assert [o["guid"] for o in mb["injured"]] == [living["guid"]]
    assert mb["injured"][0]["injuries"] == 2 and mb["injured"][0]["portrait"].endswith(".png")
    assert views.medbay(model, set())["injured"][0]["portrait"] is None
