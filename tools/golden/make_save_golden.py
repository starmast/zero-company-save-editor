"""Dump the Python editor's view of the sample save, and the exact bytes of scripted edits, as the C# test oracle.

Writes saves/golden/state.json and saves/golden/scenarios.json (saves/ is git-ignored: it holds your save and
text from the game).  Run from the repo root with the project venv:

    .venv\\Scripts\\python.exe tools\\golden\\make_save_golden.py
"""
import hashlib
import io
import json
import os
import shutil
import sys
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, ROOT)
from zc import service as svc_mod  # noqa: E402
from zc.service import Service  # noqa: E402

SAVES = os.path.join(ROOT, "saves")
NAME = sorted(f for f in os.listdir(SAVES) if f.startswith("HUB_Root") and f.endswith(".sav"))[0]
svc_mod.game_running = lambda: None            # never depends on the game being closed


def fresh():
    d = tempfile.mkdtemp()
    os.makedirs(os.path.join(d, "saves"))
    shutil.copy2(os.path.join(SAVES, NAME), os.path.join(d, "saves", NAME))
    s = Service({"t": os.path.join(d, "saves")}, os.path.join(d, "backups"))
    s.open("t", NAME)
    return s, d


def sha(b):
    return hashlib.sha256(b).hexdigest()


def written(s, d, res):
    """sha256 of the SaveGame payload and the metadata of the file apply() wrote."""
    path = os.path.join(d, "saves", res["written"])
    with zipfile.ZipFile(path) as z:
        out = {"gvas": sha(z.read("SaveGame")), "gvas_len": len(z.read("SaveGame")),
               "meta": sha(z.read("SaveGameMetaData.json")),
               "others": {n: sha(z.read(n)) for n in z.namelist() if n not in ("SaveGame", "SaveGameMetaData.json")}}
    return out


s, d = fresh()
st = s.state()
view = st["view"]
state = {"fields": st["fields"], "upgrades": st["upgrades"], "view": view,
         "parsed_nodes": st["parsed_nodes"], "opaque_nodes": st["opaque_nodes"]}
os.makedirs(os.path.join(SAVES, "golden"), exist_ok=True)
with open(os.path.join(SAVES, "golden", "state.json"), "w", encoding="utf-8") as f:
    json.dump(state, f, ensure_ascii=False, indent=1, sort_keys=True)

m = s.current.model
ops = m.operators
injured = [o["guid"] for o in ops.values() if o["injuries"] and not o["dead"]]
dead = [g for g in m.roster if False] + sorted(m.dead)
fields = {f["label"]: f for f in st["fields"]}
credits = next((f for f in st["fields"] if f["label"] == "Credits"), None)
focus_f = next((f for f in st["fields"] if f["label"] == "Available focus points"), None)
from zc import coil, focus, roster  # noqa: E402
gaps = list(focus.incomplete(s.current.gvas))
active_coil = coil.active(s.current.gvas)
upg = [r for r in st["upgrades"]["items"] if r["can_start"]]
prog = [r for r in st["upgrades"]["items"] if r["status"] == "InProgress" and r["can_expedite"]]
ro = roster.order(s.current.gvas)

scen = {}


def run(key, changes=None, actions=None):
    s2, d2 = fresh()
    try:
        res = s2.apply(changes or [], as_copy=True, actions=actions or [])
        scen[key] = {"changes": changes or [], "actions": actions or [], **written(s2, d2, res)}
    except Exception as e:                                    # a scenario that cannot run is itself a result
        scen[key] = {"changes": changes or [], "actions": actions or [], "error": str(e)}
    finally:
        shutil.rmtree(d2, ignore_errors=True)


if credits:
    run("edit_credits", [{"id": credits["id"], "value": credits["value"] + 1000}])
if focus_f:
    run("edit_focus_linked", [{"id": focus_f["id"], "value": focus_f["value"] + 3}])
    run("edit_focus_unlinked", [{"id": focus_f["id"], "value": focus_f["value"] + 3, "link": False}])
if injured:
    run("heal", actions=[{"type": "heal_operator", "guid": injured[0]}])
    if len(injured) > 1:
        run("heal_all", actions=[{"type": "heal_operator", "guid": g} for g in injured])
if dead:
    run("revive", actions=[{"type": "revive_operator", "guid": dead[0]}])
if gaps:
    g0 = sorted(gaps)[0]
    run("focus_tree", actions=[{"type": "complete_focus_tree", "guid": g0}])
if len(ro) > 1:
    run("reorder", actions=[{"type": "reorder_roster", "order": list(reversed(ro))}])
if active_coil:
    run("coil_available", actions=[{"type": "remove_coil_upgrades", "changes": [{"id": active_coil[0]["id"], "to": "Available"}]}])
    run("coil_prevented", actions=[{"type": "remove_coil_upgrades", "changes": [{"id": a["id"], "to": "Prevented"} for a in active_coil]}])
if upg:
    run("start_upgrade", actions=[{"type": "start_upgrade", "id": upg[0]["id"], "name": upg[0]["name"]}])
    run("start_expedite", actions=[{"type": "start_expedite_upgrade", "id": upg[0]["id"], "name": upg[0]["name"]}])
if prog:
    run("expedite", actions=[{"type": "expedite_upgrade", "id": prog[0]["id"], "name": prog[0]["name"]}])
if credits and injured and upg:
    run("combo", [{"id": credits["id"], "value": credits["value"] + 5}],
        [{"type": "heal_operator", "guid": injured[0]},
         {"type": "start_expedite_upgrade", "id": upg[0]["id"], "name": upg[0]["name"]}])

with open(os.path.join(SAVES, "golden", "scenarios.json"), "w", encoding="utf-8") as f:
    json.dump(scen, f, indent=1, sort_keys=True)
shutil.rmtree(d, ignore_errors=True)
print("state fields:", len(st["fields"]), " scenarios:", {k: ("ERR " + v["error"]) if "error" in v else "ok" for k, v in scen.items()})
