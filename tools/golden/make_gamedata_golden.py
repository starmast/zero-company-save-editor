"""Dump what the Python editor derives from gamedata/*.json, as the oracle for the C# distiller tests.

Output goes to gamedata/golden/gamedata.json, which is git-ignored along with the rest of the extracted
(copyrighted) game content. Run from the repo root with the project venv:

    .venv\\Scripts\\python.exe tools\\golden\\make_gamedata_golden.py
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, ROOT)
from zc import gamedata as gd  # noqa: E402

up = gd.upgrades()
tags = sorted(gd.focus_thresholds())
crisis_ids = sorted(gd._crisis(gd.data_dir()))

out = {
    "counts": {"upgrades": len(up.by_name), "items": len(gd.items()), "effects": len(gd.effects()),
               "thresholds": len(tags), "crisis": len(crisis_ids)},
    "upgrades": {n: {**gd.Upgrades.summary(r), "tags_granted": r.get("completedFactTags", []),
                     "recipeTags": r.get("recipeTags", [])} for n, r in up.by_name.items()},
    "by_tag": {t: r["name"] for t, r in up.by_tag.items()},
    "item_labels": {a: gd.item_label(a) for a in gd.items()},
    "item_info": {a: {k: v for k, v in i.items() if k != "name"} for a, i in gd.items().items()},
    "effect_text": {a: list(gd.effect_text(a)) for a in gd.effects()},
    "thresholds": gd.focus_thresholds(),
    "abilities": {t: gd.ability_name(t) for t in tags},
    "coil": {i: gd.crisis_effect(i) for i in crisis_ids},
    "units": {u: gd.crisis_unit(u) for u in gd._CRISIS_UNIT_KEYS},
}
dest = os.path.join(gd.data_dir(), "golden")
os.makedirs(dest, exist_ok=True)
with open(os.path.join(dest, "gamedata.json"), "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=1, sort_keys=True)
print("wrote", os.path.join(dest, "gamedata.json"), out["counts"])
