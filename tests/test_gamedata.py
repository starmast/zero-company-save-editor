import os

import pytest

from conftest import GAME_DIR, SAMPLE_DIR, sample_name
from zc import gamedata, savefile, upgrades
from zc.gvas import Gvas

HAVE = os.path.exists(os.path.join(gamedata.ROOT, "gamedata", "upgrades.json"))
needs_data = pytest.mark.skipif(not HAVE, reason="gamedata/ not extracted (run tools/extract)")


def sample_gvas():
    p = os.path.join(SAMPLE_DIR, sample_name())
    return Gvas(savefile.load_bytes(open(p, "rb").read()).gvas)


def row(info, name):
    return next(r for r in info["items"] if r["name"] == name)


def test_editor_works_without_gamedata(tmp_path, monkeypatch):
    monkeypatch.setenv("ZC_GAMEDATA", str(tmp_path))          # empty folder: no data
    gamedata.clear_cache()
    try:
        assert not gamedata.available()
        info = upgrades.list_upgrades(sample_gvas())
        assert info["gamedata"] is False
        r = row(info, "Upgrade_Crew_FocusPoint_Focus_1")
        assert r["can_start"] and "duration" not in r         # old behaviour
        assert not row(info, "Upgrade_Crew_FocusPoint_Focus_2")["can_start"]   # naming-rule fallback
    finally:
        gamedata.clear_cache()


@needs_data
def test_upgrade_costs_match_the_game_screens():
    """Values read from the in-game upgrade screens (screenshots)."""
    gamedata.clear_cache()
    info = upgrades.list_upgrades(sample_gvas())
    assert info["gamedata"] is True
    # Combat Readiness III: 2 turns, 4,000 credits, 2 facility resource, Den Level 9
    r = row(info, "Upgrade_Crew_CombatReadyness_Advantage_3")
    assert (r["duration"], r["cost"], r["den_level"]) == (
        2, {"Credits": 4000, "UpgradeFacilityResource": 2}, 9)
    assert "Advantage" in r["description"] and "<" not in r["description"]
    # Medical Bed III / IV: 1 turn, 600 / 700 credits
    assert row(info, "Upgrade_Facilities_Medbay_Bed_2")["cost"] == {"Credits": 600}
    assert row(info, "Upgrade_Facilities_Medbay_Bed_3")["cost"] == {"Credits": 700}
    assert row(info, "Upgrade_Facilities_Medbay_Bed_3")["duration"] == 1


@needs_data
def test_multi_turn_upgrades_have_duration_two():
    info = upgrades.list_upgrades(sample_gvas())
    for n in ("Upgrade_Crew_Cantina_HUBBond_1", "Upgrade_Facilities_Shop_RestockUnlocked",
              "Upgrade_Crew_Combat_ModSlot_1", "Upgrade_Facilities_Shop_ItemQuality_2",
              "Upgrade_Weapons_Longarm_Range_2", "Upgrade_Crew_Combat_UtilitySlot_1"):
        assert row(info, n)["duration"] == 2, n


@needs_data
def test_fact_tag_prerequisites_gate_tiers():
    info = upgrades.list_upgrades(sample_gvas())
    r = row(info, "Upgrade_Crew_FocusPoint_Focus_2")
    assert not r["can_start"] and r["reason"].startswith("Needs ")
    assert row(info, "Upgrade_Crew_FocusPoint_Focus_1")["can_start"]


@needs_data
def test_every_save_recipe_is_in_the_extract():
    gd = gamedata.upgrades()
    for fn in os.listdir(GAME_DIR) if os.path.isdir(GAME_DIR) else []:
        p = os.path.join(GAME_DIR, fn)
        if fn.startswith("HUB_Root") and fn.endswith(".sav"):
            g = Gvas(savefile.load_bytes(open(p, "rb").read()).gvas)
            for r in upgrades.list_upgrades(g)["items"]:
                assert gd.get(r["name"]) is not None, (fn, r["name"])


@needs_data
def test_item_and_effect_names():
    gamedata.clear_cache()
    assert gamedata.item_label("UtilityItem_Medkit_T2") == "Medpac Mk. II"
    assert gamedata.item_label("Resource_Credits") == "Credits"
    assert gamedata.item_label("Resource_UpgradeFacilityResource") == "Capacitors"      # plural template resolved
    assert gamedata.item_label("NoSuchItem") is None
    title, desc = gamedata.effect_text("GE_Upgrade_RifleDmg1")
    assert title == "Rifle Damage I" and "{" not in desc
    title, desc = gamedata.effect_text("GE_CrossTraining_Health")
    assert title == "Fitness Work" and "+4" in desc
    assert gamedata.effect_text("GE_NoSuchEffect") == (None, "")


@needs_data
def test_domain_uses_game_names(svc):
    st = svc.open("t", svc.test_name)
    labels = {f["label"] for f in st["fields"] if f["group"] in ("Resources", "Inventory")}
    assert "Capacitors" in labels
    assert not any(l.startswith("Utility Item ") for l in labels)       # no raw asset-style names


def test_domain_falls_back_without_gamedata(svc, tmp_path, monkeypatch):
    monkeypatch.setenv("ZC_GAMEDATA", str(tmp_path))
    gamedata.clear_cache()
    try:
        st = svc.open("t", svc.test_name)
        labels = {f["label"] for f in st["fields"] if f["group"] == "Resources"}
        assert {"Credits", "Intel", "Contacts", "Facility upgrade resource"} <= labels
    finally:
        gamedata.clear_cache()


def test_abilities_tab_fields(svc):
    st = svc.open("t", svc.test_name)
    ab = [f for f in st["fields"] if f["group"] == "Abilities"]
    assert ab, "focus-tree allocations should be listed"
    lv = [f for f in ab if f["label"].endswith(" - level")]
    assert lv and all(f["min"] == 1 and 3 <= f["max"] <= 6 for f in lv)      # cap = levels the game defines
    assert all(f["min"] <= f["value"] <= f["max"] for f in lv)


@needs_data
def test_ability_names_from_game_strings():
    gamedata.clear_cache()
    assert gamedata.ability_name("br.AbilityID.Class.PrecisionShot") == "Precision Shot"
    assert gamedata.ability_name("br.AbilityID.Passive.FearlessLeader") == "Fearless Leader"
    assert gamedata.ability_name("br.AbilityID.Class.NoSuchThing") is None


def test_edit_ability_level_in_place(svc):
    st = svc.open("t", svc.test_name)
    f = next(f for f in st["fields"] if f["label"].endswith(" - level") and f["value"] < 6)
    payload = lambda: len(savefile.load_bytes(open(svc.test_path, "rb").read()).gvas)
    size = payload()
    svc.apply([{"id": f["id"], "value": f["value"] + 1}], force=True)
    after = next(x for x in svc.state()["fields"] if x["id"] == f["id"])
    assert after["value"] == f["value"] + 1
    assert payload() == size                                  # in place: payload size unchanged


@needs_data
def test_focus_thresholds_match_the_game_and_the_save():
    thr = gamedata.focus_thresholds()
    assert thr["br.AbilityID.Class.Lethal"] == [0, 2, 5, 9, 15, 23]
    assert thr["br.AbilityID.Class.PrecisionShot"] == [0, 3, 7, 13, 21, 31]
    # In every ability of the sample save, focus spent equals the threshold of its level.
    g = sample_gvas()
    sd = upgrades.strategy_data(g)
    checked = 0
    for e in g.child(sd, "CharacterFocusData").children:
        v = e.children[1]
        spent = 0
        for a in g.child(v, "FocusPointAllocations").children:
            tag = g.get(g.child(a.children[0], "TagName"))
            lvl, al = (g.get(g.child(a.children[1], k)) for k in ("Level", "AllocatedFocus"))
            spent += al
            if tag in thr:
                assert thr[tag][lvl - 1] == al, (tag, lvl, al)
                checked += 1
        # an operator's total is always spent + unspent
        assert g.get(g.child(v, "TotalFocusPoints")) == spent + g.get(g.child(v, "AvailableFocusPoints"))
    assert checked > 10


# ------------------------------------------------------------- focus tree levelling
def _focus_invariants(path):
    """(consistent abilities, operators whose total != spent + unspent) for a saved file."""
    thr = gamedata.focus_thresholds()
    g = Gvas(savefile.load_bytes(open(path, "rb").read()).gvas)
    sd = upgrades.strategy_data(g)
    bad_levels, bad_totals = [], []
    for e in g.child(sd, "CharacterFocusData").children:
        v = e.children[1]
        spent = 0
        for a in g.child(v, "FocusPointAllocations").children:
            tag = g.get(g.child(a.children[0], "TagName"))
            lvl, al = (g.get(g.child(a.children[1], k)) for k in ("Level", "AllocatedFocus"))
            spent += al
            if tag in thr and thr[tag][lvl - 1] != al:
                bad_levels.append((tag, lvl, al))
        if g.get(g.child(v, "TotalFocusPoints")) != spent + g.get(g.child(v, "AvailableFocusPoints")):
            bad_totals.append(e.name)
    return bad_levels, bad_totals


def _candidate(st, need_unspent):
    for o in st["view"]["personnel"]["roster"]:
        f = o["focus"]
        if not f or not f["total_ref"]:
            continue
        for a in o["abilities"]:
            t = a["thresholds"]
            if t and a["level"] and a["level"]["value"] < len(t):
                lvl = a["level"]["value"]
                d = t[lvl] - a["spent"]["value"]
                if not need_unspent or f["value"] >= d:
                    return o, a, lvl + 1, d
    return None


@needs_data
def test_level_up_spending_unspent_focus_keeps_invariants(svc):
    st = svc.open("t", svc.test_name)
    o, a, new_level, d = _candidate(st, need_unspent=True)
    f = o["focus"]
    svc.apply([{"id": a["level"]["id"], "value": new_level},
               {"id": a["spent"]["id"], "value": a["thresholds"][new_level - 1]},
               {"id": f["id"], "value": f["value"] - d, "link": False}], force=True)
    assert _focus_invariants(svc.test_path) == ([], [])
    after = next(x for x in svc.state()["view"]["personnel"]["roster"] if x["guid"] == o["guid"])
    assert after["focus"]["value"] == f["value"] - d and after["focus"]["total"] == f["total"]


@needs_data
def test_level_up_granting_focus_keeps_invariants(svc):
    st = svc.open("t", svc.test_name)
    o, a, new_level, d = _candidate(st, need_unspent=False)
    f = o["focus"]
    svc.apply([{"id": a["level"]["id"], "value": new_level},
               {"id": a["spent"]["id"], "value": a["thresholds"][new_level - 1]},
               {"id": f["total_ref"]["id"], "value": f["total_ref"]["value"] + d}], force=True)
    assert _focus_invariants(svc.test_path) == ([], [])
    after = next(x for x in svc.state()["view"]["personnel"]["roster"] if x["guid"] == o["guid"])
    assert after["focus"]["value"] == f["value"] and after["focus"]["total"] == f["total"] + d


@needs_data
def test_default_link_would_break_the_invariant(svc):
    """Why the per-edit `link` flag exists: the old 'total follows unspent' rule is wrong for levelling."""
    st = svc.open("t", svc.test_name)
    o, a, new_level, d = _candidate(st, need_unspent=True)
    f = o["focus"]
    svc.apply([{"id": a["level"]["id"], "value": new_level},
               {"id": a["spent"]["id"], "value": a["thresholds"][new_level - 1]},
               {"id": f["id"], "value": f["value"] - d}], force=True)          # linked (default)
    assert _focus_invariants(svc.test_path)[1], "linked write should have broken total == spent + unspent"
