"""HTTP layer: pages, images, and the request guards."""
import sys

import pytest

from app import create_app
from zc import portraits


@pytest.fixture
def client(svc):
    app = create_app(svc)
    app.testing = True
    return app.test_client()


@pytest.fixture
def bare_client(tmp_path):
    """A server with an empty save folder: enough for the guards and static pages (no real save needed)."""
    from zc.service import Service
    (tmp_path / "saves").mkdir()
    app = create_app(Service({"t": str(tmp_path / "saves")}, str(tmp_path / "backups")))
    app.testing = True
    return app.test_client()


def test_index_loads_module_entry(bare_client):
    client = bare_client
    r = client.get("/")
    assert r.status_code == 200
    assert b"js/main.js" in r.data and b'type="module"' in r.data
    assert client.get("/static/js/main.js").status_code == 200
    assert client.get("/static/css/game.css").status_code == 200


def test_open_and_view_over_http(client, svc):
    saves = client.get("/api/saves").get_json()
    assert any(s["name"] == svc.test_name and s["editable"] for s in saves["saves"])
    r = client.post("/api/open", json={"dir": "t", "name": svc.test_name})
    assert r.status_code == 200
    view = r.get_json()["view"]
    assert {"header", "command", "personnel", "upgrades", "armory", "galaxy"} <= set(view)


def test_thumbnail_route(client, svc):
    r = client.get(f"/api/thumb/t/{svc.test_name}")
    assert r.status_code == 200 and r.mimetype == "image/jpeg" and r.data[:2] == b"\xff\xd8"


@pytest.mark.parametrize("path", [
    "/api/thumb/t/..%2F..%2Fapp.py", "/api/thumb/t/../app.py", "/api/thumb/nope/x.sav",
    "/api/thumb/t/notasave.txt", "/api/thumb/t/missing.sav",
])
def test_thumbnail_rejects_bad_paths(bare_client, path):
    r = bare_client.get(path)
    assert r.status_code in (400, 404)
    assert r.mimetype != "image/jpeg"


def test_portrait_route(client, svc):
    client.post("/api/open", json={"dir": "t", "name": svc.test_name})
    view = client.get("/api/state").get_json()["view"]
    with_face = [o for o in view["personnel"]["roster"] + view["personnel"]["memorial"] if o["portrait"]]
    if not portraits.libraries_available() or not with_face:
        pytest.skip("imaging libraries or portraits not available")
    r = client.get(with_face[0]["portrait"])
    assert r.status_code == 200 and r.mimetype == "image/png" and r.data[:8] == b"\x89PNG\r\n\x1a\n"


@pytest.mark.parametrize("guid", ["nothex", "../etc", "0" * 32, "G" * 32])
def test_portrait_rejects_unknown_ids(bare_client, guid):
    assert bare_client.get(f"/api/portrait/{guid}.png").status_code == 404


def test_host_and_origin_guards(bare_client):
    client = bare_client
    assert client.get("/api/saves", headers={"Host": "evil.example"}).status_code == 403
    r = client.post("/api/open", json={}, headers={"Origin": "http://evil.example"})
    assert r.status_code == 403


def test_portrait_missing_libraries_degrades(monkeypatch):
    """Without OpenEXR/Pillow the conversion returns None (UI shows initials) instead of raising."""
    monkeypatch.setitem(sys.modules, "OpenEXR", None)
    assert portraits.exr_to_png(b"\x76\x2f\x31\x01junk") is None
    assert portraits.libraries_available() is False
