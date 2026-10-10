# Golden-data generators (maintainers only)

The C# test suite compares itself against the original Python editor (v0.3.0) on your local sample save and game data:
identical fields and screen views, and byte-identical files for scripted edits. The oracle files are produced by the two
scripts here, which need the **Python sources from the v0.3.0 tag**:

```
git worktree add ../zc-v030 v0.3.0
cp tools/golden/*.py ../zc-v030/tools/golden/       # (create the folder first)
cp -r saves gamedata ../zc-v030/                    # your sample save and extracted game data
cd ../zc-v030
python -m venv .venv && .venv\Scripts\python -m pip install -r requirements.txt
.venv\Scripts\python tools\golden\make_gamedata_golden.py   # -> gamedata/golden/gamedata.json
.venv\Scripts\python tools\golden\make_save_golden.py       # -> saves/golden/state.json, scenarios.json
cp -r gamedata/golden ../zero-company-save-editor/gamedata/ ; cp -r saves/golden ../zero-company-save-editor/saves/
```

The output contains game text and your save's contents, so `gamedata/` and `saves/` are git-ignored. The tests skip the
parity checks when the golden files are absent, so a fresh clone still builds and tests cleanly.
