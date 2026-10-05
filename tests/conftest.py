import os, shutil, sys
import pytest
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
from zc.service import Service

SAMPLE_DIR = os.path.join(ROOT, "saves")
GAME_DIR = os.path.expandvars(r"%LOCALAPPDATA%\SWZeroCompany\Saved\SaveGames")   # may not exist (tests then skip)


def sample_name():
    names = sorted(f for f in (os.listdir(SAMPLE_DIR) if os.path.isdir(SAMPLE_DIR) else [])
                   if f.startswith("HUB_Root") and f.endswith(".sav"))
    if not names:
        pytest.skip("no sample save in saves/")
    return names[0]


@pytest.fixture
def svc(tmp_path):
    """A Service working on a throw-away copy of the sample save."""
    d = tmp_path / "saves"; d.mkdir()
    name = sample_name()
    shutil.copy2(os.path.join(SAMPLE_DIR, name), d / name)
    s = Service({"t": str(d)}, str(tmp_path / "backups"))
    s.test_name = name
    s.test_path = str(d / name)
    return s
