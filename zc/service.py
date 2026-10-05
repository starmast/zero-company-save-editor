"""Open/edit/save workflow with backups and post-write verification."""
from __future__ import annotations

import hashlib
import os
import re
import shutil
import subprocess
import threading
import time
from dataclasses import dataclass
from typing import Optional

from . import portraits as portraits_mod, savefile, upgrades, views
from .domain import SaveModel, TYPE_RANGES, scalar_nodes, validate
from .gvas import Gvas, GvasError, SCALARS

KEEP_BACKUPS = 20
SAFE_NAME = re.compile(r"^[A-Za-z0-9._ \-+()]+\.sav$")
GAME_PROCESS_HINTS = ("swzerocompany", "bruno")


def _blob_sizes(g: Gvas) -> dict[str, int]:
    """Byte counts of the two top-level archives, as mirrored in SaveGameMetaData.json."""
    out = {}
    for w in g.root:
        ab = g.child(w, "ArchiveBytes")
        if ab is not None and ab.count is not None:
            if w.name == "GameInstanceSaveGameWrapper":
                out["GameInstanceSize"] = ab.count
            elif w.name == "StrategySaveGameWrapper":
                out["StrategySize"] = ab.count
    return out


def _flatten(g: Gvas) -> dict:
    """Map structural path -> comparable content for every node (leaf bytes / counts)."""
    out: dict = {}

    def rec(nodes, prefix):
        for n in nodes:
            p = prefix + (n.name,)
            if n.children is not None:
                if not n.nested:                 # nested count is a byte length, not elements
                    out[p + ("#count",)] = n.count
                rec(n.children, p)
            else:
                out[p] = bytes(g.data[n.value_offset:n.end])
    rec(g.root, ())
    return out


class EditError(Exception):
    """User-facing failure (nothing was written)."""


def sha(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def game_running() -> Optional[str]:
    try:
        out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True,
                             text=True, timeout=10, creationflags=0x08000000).stdout
    except Exception:
        return None
    for line in out.splitlines():
        name = line.split('","')[0].strip('"').lower()
        if any(h in name for h in GAME_PROCESS_HINTS):
            return name
    return None


@dataclass
class OpenSave:
    dir_id: str
    name: str
    path: str
    loaded: savefile.Loaded
    gvas: Gvas
    model: SaveModel
    scalars: dict
    disk_hash: str
    portraits: object = None


class Service:
    def __init__(self, save_dirs: dict[str, str], backup_root: str):
        self.dirs = save_dirs                    # id -> absolute dir
        self.backup_root = backup_root
        self.current: Optional[OpenSave] = None
        self.lock = threading.Lock()
        os.makedirs(backup_root, exist_ok=True)

    # -- path safety -------------------------------------------------------
    def resolve(self, dir_id: str, name: str) -> str:
        if dir_id not in self.dirs:
            raise EditError("unknown save folder")
        if os.path.basename(name) != name or not SAFE_NAME.match(name):
            raise EditError("invalid file name")
        path = os.path.join(self.dirs[dir_id], name)
        if not os.path.isfile(path):
            raise EditError("file not found")
        return path

    def backup_dir(self, name: str, dir_id: Optional[str] = None) -> str:
        """Backups live in backups/<folder id>/<save name>/ so the game file and a
        same-named project copy never share history."""
        dir_id = dir_id or (self.current.dir_id if self.current else None)
        if dir_id not in self.dirs:
            raise EditError("unknown save folder")
        stem = os.path.splitext(name)[0]
        d = os.path.join(self.backup_root, dir_id, stem)
        legacy = os.path.join(self.backup_root, stem)         # pre-folder-id layout
        if not os.path.isdir(d):
            os.makedirs(os.path.dirname(d), exist_ok=True)
            if os.path.isdir(legacy):
                shutil.copytree(legacy, d)                     # keep old history; never delete it
            else:
                os.makedirs(d)
        return d

    # -- images (read-only) ------------------------------------------------
    def portrait(self, guid: str) -> Optional[bytes]:
        c = self.current
        if not c or not re.fullmatch(r"[0-9A-Fa-f]{32}", guid):
            return None
        return c.portraits.png(guid)

    def thumbnail(self, dir_id: str, name: str) -> Optional[bytes]:
        return savefile.read_thumbnail(self.resolve(dir_id, name))

    # -- listing -----------------------------------------------------------
    def list_saves(self) -> list[dict]:
        out = []
        for dir_id, d in self.dirs.items():
            if not os.path.isdir(d):
                continue
            for fn in sorted(os.listdir(d), key=lambda f: -os.path.getmtime(os.path.join(d, f))):
                if SAFE_NAME.match(fn):
                    info = savefile.summarize(os.path.join(d, fn))
                    info.update(dir=dir_id, name=fn)
                    out.append(info)
        return out

    # -- open --------------------------------------------------------------
    def open(self, dir_id: str, name: str) -> dict:
        path = self.resolve(dir_id, name)
        with open(path, "rb") as f:
            raw = f.read()
        try:
            loaded = savefile.load_bytes(raw)
            if not loaded.is_zip:
                raise EditError("This file type (settings/databank) is not editable here.")
            gv = Gvas(loaded.gvas)
            model = SaveModel(gv, loaded.metadata_json)
        except (savefile.SaveFormatError, GvasError) as e:
            raise EditError(f"Could not read save: {e}") from e
        self._ensure_original(dir_id, name, raw)
        with self.lock:
            self.current = OpenSave(dir_id, name, path, loaded, gv, model,
                                    scalar_nodes(gv), sha(raw),
                                    portraits_mod.Portraits(loaded.blobs))
        return self.state()

    def state(self) -> dict:
        c = self.current
        if not c:
            raise EditError("no save open")
        up = upgrades.list_upgrades(c.gvas)
        return {"dir": c.dir_id, "name": c.name, "fields": c.model.public(),
                "upgrades": up,
                "view": views.build(c.model, savefile.save_info(c.loaded), c.portraits.guids(), up),
                "parsed_nodes": sum(1 for _ in c.gvas.walk()),
                "opaque_nodes": c.gvas.opaque_count,
                "game_running": game_running(), "backups": self.list_backups(c.name)}

    # -- backups -----------------------------------------------------------
    def _ensure_original(self, dir_id: str, name: str, raw: bytes):
        d = self.backup_dir(name, dir_id)
        if not any(f.endswith(".orig.sav") for f in os.listdir(d)):
            self._write_file(os.path.join(d, f"{time.strftime('%Y%m%d-%H%M%S')}.orig.sav"), raw)

    def snapshot(self, name: str, path: str) -> str:
        d = self.backup_dir(name)
        dest = os.path.join(d, f"{time.strftime('%Y%m%d-%H%M%S')}.sav")
        n = 1
        while os.path.exists(dest):
            dest = os.path.join(d, f"{time.strftime('%Y%m%d-%H%M%S')}-{n}.sav"); n += 1
        shutil.copy2(path, dest)
        snaps = sorted(f for f in os.listdir(d) if not f.endswith(".orig.sav"))
        for old in snaps[:-KEEP_BACKUPS]:
            os.remove(os.path.join(d, old))
        return dest

    def list_backups(self, name: str) -> list[dict]:
        d = self.backup_dir(name)
        out = []
        for f in sorted(os.listdir(d), reverse=True):
            st = os.stat(os.path.join(d, f))
            out.append({"file": f, "size": st.st_size, "mtime": st.st_mtime,
                        "original": f.endswith(".orig.sav")})
        return out

    @staticmethod
    def _write_file(path: str, data: bytes):
        with open(path, "wb") as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())

    def _atomic_replace(self, path: str, data: bytes):
        tmp = path + ".tmp"
        try:
            self._write_file(tmp, data)
            with open(tmp, "rb") as f:
                if sha(f.read()) != sha(data):
                    raise EditError("temp file verification failed; original untouched")
            os.replace(tmp, path)
        finally:
            if os.path.exists(tmp):
                os.remove(tmp)

    # -- apply -------------------------------------------------------------
    def apply(self, changes: list[dict], force: bool = False, as_copy: bool = False,
              actions: Optional[list[dict]] = None) -> dict:
        c = self.current
        actions = actions or []
        if not c:
            raise EditError("no save open")
        if not changes and not actions:
            raise EditError("no changes")
        if not as_copy and not force:
            proc = game_running()
            if proc:
                raise EditError(f"The game appears to be running ({proc}). Close it first "
                                "(or tick the override).")
        with open(c.path, "rb") as f:
            on_disk = f.read()
        if sha(on_disk) != c.disk_hash and not as_copy:
            raise EditError("The save changed on disk since you opened it (the game may have "
                            "autosaved). Re-open it before editing.")

        edited: list[tuple[int, int, str, object]] = []   # offset, size, type, value
        # 1) validate and compute every scalar write up front
        writes: dict[int, tuple] = {}                     # offset -> (node, value)
        for ch in changes:
            fid = ch.get("id")
            node = c.scalars.get(fid)
            if node is None:
                raise EditError(f"unknown field {fid!r}")
            f = c.model.by_id.get(fid)
            if f:
                lo, hi, kind = f.min, f.max, f.kind
            else:
                lo, hi = TYPE_RANGES[node.tname]
                kind = "float" if node.tname in ("FloatProperty", "DoubleProperty") else "int"
            lo = max(lo, TYPE_RANGES[node.tname][0]); hi = min(hi, TYPE_RANGES[node.tname][1])
            try:
                new = validate(kind, node.tname, ch.get("value"), lo, hi)
            except (ValueError, TypeError) as e:
                raise EditError(f"{f.label if f else fid}: {e}") from e
            old = c.gvas.get(node)
            writes[node.value_offset] = (node, new)
            if f and f.linked and new != old and ch.get("link", True):
                for ln in f.linked:
                    cur = writes.get(ln.value_offset, (None, c.gvas.get(ln)))[1]
                    ln_new = cur + (new - old)
                    llo, lhi = TYPE_RANGES[ln.tname]
                    if not llo <= ln_new <= lhi:
                        raise EditError(f"{f.label}: linked value out of range")
                    writes[ln.value_offset] = (ln, ln_new)

        # 2) patch an independent Gvas copy (scalars are in place, offsets unchanged)
        patched = Gvas(c.loaded.gvas)
        for off, (node, val) in writes.items():
            patched.set(node, val)
            edited.append((off, SCALARS[node.tname][1], node.tname, val))

        # 3) structural actions (change sizes; every ancestor size is fixed up)
        expected_paths: set = {tuple(c.gvas.path_of(n)) for n, _ in writes.values()}
        if actions:
            patched, started = self._run_actions(patched, actions)
            expected_paths |= started
        new_gvas = patched.to_bytes()
        structural = len(new_gvas) != len(c.loaded.gvas)

        replace = {}
        if structural and c.loaded.is_zip:
            meta = savefile.sync_metadata_sizes(c.loaded, _blob_sizes(patched))
            if meta is not None:
                replace["SaveGameMetaData.json"] = meta
        new_file = savefile.rebuild(c.loaded, new_gvas, replace)

        # 4) verify before touching the disk
        if structural or actions:
            self._verify_semantic(c, new_file, expected_paths)
        else:
            self._verify(c, new_file, new_gvas, edited)

        count = len(writes) + len(actions)
        if as_copy:
            base, ext = os.path.splitext(c.name)
            dest_name = f"{base}.edited-{time.strftime('%H%M%S')}{ext}"
            dest = os.path.join(os.path.dirname(c.path), dest_name)
            self._atomic_replace(dest, new_file)
            return {"written": dest_name, "copy": True, "count": count}

        snap = self.snapshot(c.name, c.path)
        self._atomic_replace(c.path, new_file)
        # re-open so the UI shows what is really on disk now
        self.open(c.dir_id, c.name)
        return {"written": c.name, "backup": os.path.basename(snap), "count": count}

    def _run_actions(self, g: Gvas, actions: list[dict]) -> tuple[Gvas, set]:
        touched: set = set()
        seen: set = set()
        for a in actions:
            kind = a.get("type")
            if kind not in ("start_upgrade", "expedite_upgrade", "start_expedite_upgrade"):
                raise EditError(f"unknown action {kind!r}")
            rid, name = str(a.get("id", "")), a.get("name")
            if not re.fullmatch(r"r\d+", rid) or rid in seen:
                raise EditError("bad upgrade reference")
            seen.add(rid)
            row = next((r for r in upgrades.list_upgrades(g)["items"] if r["id"] == rid), None)
            if row is None or row["name"] != name:
                raise EditError("upgrade list changed; re-open the save")
            if kind in ("start_upgrade", "start_expedite_upgrade") and not row["can_start"]:
                raise EditError(f"{row['title']}: {row['reason'] or 'cannot be started'}")
            if kind == "expedite_upgrade" and not row["can_expedite"]:
                raise EditError(f"{row['title']}: not in progress")
            idx = int(rid[1:])
            sd = upgrades.strategy_data(g)
            v = g.child(sd, "ActiveRecipes").children[idx].children[1]
            base = tuple(g.path_of(v))
            try:
                if kind in ("start_upgrade", "start_expedite_upgrade"):
                    g = upgrades.start_upgrade(g, idx, name)
                if kind in ("expedite_upgrade", "start_expedite_upgrade"):
                    g = upgrades.expedite_upgrade(g, idx, name)
            except GvasError as e:
                raise EditError(f"{row['title']}: {e}") from e
            tails = ()
            if kind in ("start_upgrade", "start_expedite_upgrade"):
                tails += (("Status",), ("TurnStarted",), ("FulfilledRewardTiers",),
                          ("InProgressRecipeContext", "FacilityTag", "TagName"))
            if kind in ("expedite_upgrade", "start_expedite_upgrade"):
                tails += (("TurnStarted",), ("InProgressTurns",))
            for tail in tails:
                touched.add(base + tail)
        return g, touched

    def _verify_semantic(self, c: OpenSave, new_file: bytes, allowed_paths: set):
        """For structural edits: reparse, then prove the ONLY differences are the intended ones."""
        try:
            re_loaded = savefile.load_bytes(new_file)
            g2 = Gvas(re_loaded.gvas)
        except Exception as e:
            raise EditError(f"verification failed (rebuilt file unreadable): {e}") from e
        if g2.opaque_count != c.gvas.opaque_count:
            raise EditError("verification failed: tree shape changed (size fields inconsistent)")
        if len(g2.data) - g2.props_end != len(c.gvas.data) - c.gvas.props_end:
            raise EditError("verification failed: trailing data changed")
        if g2.data[:c.gvas.props_start] != c.gvas.data[:c.gvas.props_start]:
            raise EditError("verification failed: header changed")
        if c.loaded.is_zip:
            for k, v in c.loaded.blobs.items():
                if k not in (savefile.MAIN_ENTRY, "SaveGameMetaData.json") and re_loaded.blobs.get(k) != v:
                    raise EditError(f"verification failed: entry {k} altered")
            txt = re_loaded.blobs["SaveGameMetaData.json"].decode("utf-16-le")
            for key, size in _blob_sizes(g2).items():
                m = re.search(r'"%s"\s*:\s*(\d+)' % key, txt)
                if m and int(m.group(1)) != size:
                    raise EditError(f"verification failed: metadata {key} out of sync")
        a, b = _flatten(c.gvas), _flatten(g2)
        diff = {k for k in set(a) | set(b) if a.get(k) != b.get(k)}
        stray = sorted(k for k in diff
                       if k not in allowed_paths and k[:-1] not in allowed_paths)
        if stray:
            raise EditError("verification failed: unexpected change at " + "/".join(stray[0][-6:]))

    def _verify(self, c: OpenSave, new_file: bytes, new_gvas: bytes, edited):
        try:
            re_loaded = savefile.load_bytes(new_file)
            g2 = Gvas(re_loaded.gvas)
        except Exception as e:
            raise EditError(f"verification failed (rebuilt file unreadable): {e}") from e
        old = c.loaded.gvas
        if len(new_gvas) != len(old):
            raise EditError("verification failed: payload size changed")
        if c.loaded.is_zip:
            for k, v in c.loaded.blobs.items():
                if k != savefile.MAIN_ENTRY and re_loaded.blobs.get(k) != v:
                    raise EditError(f"verification failed: entry {k} altered")
            if [i.filename for i in re_loaded.infos] != [i.filename for i in c.loaded.infos]:
                raise EditError("verification failed: entry list changed")
        allowed = bytearray(len(old))
        for off, size, _t, _v in edited:
            allowed[off:off + size] = b"\x01" * size
        diff = [i for i in range(len(old)) if old[i] != new_gvas[i]]
        if any(not allowed[i] for i in diff):
            raise EditError("verification failed: unexpected bytes changed")
        nodes = scalar_nodes(g2)
        for off, _s, _t, val in edited:
            n = nodes.get(f"o{off}")
            if n is None:
                raise EditError("verification failed: edited field not found after rebuild")
            got = g2.get(n)
            ok = (abs(got - val) <= 1e-6 * max(1.0, abs(val))) if isinstance(val, float) else got == val
            if not ok:
                raise EditError(f"verification failed: {got} != {val}")
        if g2.opaque_count != c.gvas.opaque_count:
            raise EditError("verification failed: tree shape changed")

    # -- restore -----------------------------------------------------------
    def restore(self, backup_file: str, force: bool = False) -> dict:
        c = self.current
        if not c:
            raise EditError("no save open")
        if os.path.basename(backup_file) != backup_file or not backup_file.endswith(".sav"):
            raise EditError("invalid backup name")
        src = os.path.join(self.backup_dir(c.name), backup_file)
        if not os.path.isfile(src):
            raise EditError("backup not found")
        if not force and game_running():
            raise EditError("The game appears to be running. Close it first (or tick the override).")
        with open(src, "rb") as f:
            data = f.read()
        try:
            savefile.load_bytes(data)
            Gvas(savefile.load_bytes(data).gvas)
        except Exception as e:
            raise EditError(f"backup is not a valid save: {e}") from e
        snap = self.snapshot(c.name, c.path)               # keep what we are replacing
        self._atomic_replace(c.path, data)
        self.open(c.dir_id, c.name)
        return {"restored": backup_file, "previous_saved_as": os.path.basename(snap)}

    # -- raw tree viewer ---------------------------------------------------
    def tree(self, node_id: Optional[str]) -> list[dict]:
        c = self.current
        if not c:
            raise EditError("no save open")
        g = c.gvas
        if not node_id:
            kids = g.root
        else:
            n = self._index().get(node_id)
            if n is None:
                raise EditError("unknown node")
            kids = n.children or []
        return [self._tree_row(k) for k in kids[:2000]]

    def _index(self) -> dict:
        c = self.current
        if not hasattr(c, "_idx"):
            c._idx = {f"n{n.start}": n for n in c.gvas.walk()}
        return c._idx

    def _tree_row(self, n) -> dict:
        g = self.current.gvas
        row = {"id": f"n{n.start}", "name": n.name, "type": repr(n.type), "size": n.size,
               "expandable": bool(n.children), "count": n.count, "opaque": n.opaque or n.native}
        if n.tname in SCALARS:
            row["value"] = g.get(n)
            row["field"] = f"o{n.value_offset}"
            row["kind"] = "float" if n.tname in ("FloatProperty", "DoubleProperty") else "int"
        elif n.tname in ("StrProperty", "NameProperty", "ObjectProperty", "SoftObjectProperty",
                         "EnumProperty") and n.size < 2000:
            try:
                row["value"] = g.get(n)
            except Exception:
                pass
        elif n.tname == "BoolProperty":
            row["value"] = g.get(n)
        return row
