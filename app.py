"""Star Wars: Zero Company save editor - local Flask web app.

Run:  python app.py [--dir PATH ...] [--port 8765] [--no-browser]
"""
from __future__ import annotations

import argparse
import os
import threading
import webbrowser

from flask import Flask, Response, jsonify, render_template, request

from zc.service import EditError, Service

ROOT = os.path.dirname(os.path.abspath(__file__))
GAME_DIR = os.path.expandvars(r"%LOCALAPPDATA%\SWZeroCompany\Saved\SaveGames")
ALLOWED_HOSTS = {"localhost", "127.0.0.1", "[::1]"}


def build_dirs(extra: list[str]) -> dict[str, str]:
    dirs: dict[str, str] = {}
    if os.path.isdir(GAME_DIR):
        dirs["game"] = GAME_DIR
    proj = os.path.join(ROOT, "saves")
    if os.path.isdir(proj):
        dirs["project"] = proj
    for i, d in enumerate(extra):
        dirs[f"custom{i + 1}"] = os.path.abspath(d)
    return dirs


def create_app(service: Service) -> Flask:
    app = Flask(__name__)
    app.config["JSON_SORT_KEYS"] = False

    @app.before_request
    def guard():
        # Defend against DNS rebinding / cross-site requests to the local server.
        host = request.host.rsplit(":", 1)[0] if not request.host.startswith("[") else request.host.split("]")[0] + "]"
        if host not in ALLOWED_HOSTS:
            return jsonify(error="forbidden host"), 403
        if request.method == "POST":
            origin = request.headers.get("Origin")
            if origin and origin.split("://", 1)[-1].rsplit(":", 1)[0] not in ALLOWED_HOSTS:
                return jsonify(error="forbidden origin"), 403

    @app.errorhandler(EditError)
    def edit_error(e):
        return jsonify(error=str(e)), 400

    @app.get("/")
    def index():
        return render_template("index.html")

    @app.get("/api/saves")
    def saves():
        return jsonify(dirs=service.dirs, saves=service.list_saves())

    @app.post("/api/open")
    def open_save():
        b = request.get_json(force=True)
        return jsonify(service.open(b.get("dir"), b.get("name")))

    @app.get("/api/state")
    def state():
        return jsonify(service.state())

    @app.post("/api/apply")
    def apply():
        b = request.get_json(force=True)
        res = service.apply(b.get("changes", []), force=bool(b.get("force")),
                            as_copy=bool(b.get("copy")), actions=b.get("actions", []))
        res["state"] = service.state() if not res.get("copy") else None
        return jsonify(res)

    @app.post("/api/restore")
    def restore():
        b = request.get_json(force=True)
        res = service.restore(b.get("file", ""), force=bool(b.get("force")))
        res["state"] = service.state()
        return jsonify(res)

    @app.get("/api/portrait/<guid>.png")
    def portrait(guid):
        png = service.portrait(guid)
        if png is None:
            return jsonify(error="no portrait"), 404
        return Response(png, mimetype="image/png", headers={"Cache-Control": "private, max-age=300"})

    @app.get("/api/thumb/<dir_id>/<path:name>")
    def thumb(dir_id, name):
        jpg = service.thumbnail(dir_id, name)          # resolve() rejects bad folder/file names
        if jpg is None:
            return jsonify(error="no thumbnail"), 404
        return Response(jpg, mimetype="image/jpeg", headers={"Cache-Control": "private, max-age=60"})

    @app.get("/api/tree")
    def tree():
        return jsonify(service.tree(request.args.get("id")))

    return app


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", action="append", default=[], help="extra folder of .sav files")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--no-browser", action="store_true")
    a = ap.parse_args()
    service = Service(build_dirs(a.dir), os.path.join(ROOT, "backups"))
    app = create_app(service)
    url = f"http://127.0.0.1:{a.port}/"
    print(f"Zero Company Save Editor on {url}")
    for k, v in service.dirs.items():
        print(f"  [{k}] {v}")
    if not a.no_browser:
        threading.Timer(1.0, lambda: webbrowser.open(url)).start()
    app.run(host="127.0.0.1", port=a.port, debug=False)


if __name__ == "__main__":
    main()
