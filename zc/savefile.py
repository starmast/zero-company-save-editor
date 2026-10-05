"""Reading and rebuilding Zero Company .sav containers.

Most saves are ZIP archives (SaveGame + metadata); settings/databank saves are raw GVAS.
"""
from __future__ import annotations

import io
import json
import zipfile
from dataclasses import dataclass
from typing import Optional

GVAS_MAGIC = b"GVAS"
MAIN_ENTRY = "SaveGame"


class SaveFormatError(Exception):
    pass


def _decode_utf16_json(raw: bytes) -> Optional[dict]:
    try:
        text = raw.decode("utf-16").lstrip("\ufeff")
        obj, _ = json.JSONDecoder().raw_decode(text)
        return obj
    except (UnicodeDecodeError, ValueError):
        return None


@dataclass
class Loaded:
    is_zip: bool
    raw: bytes                              # whole file as read
    gvas: bytes                             # the GVAS payload
    infos: Optional[list[zipfile.ZipInfo]] = None
    blobs: Optional[dict[str, bytes]] = None

    @property
    def metadata_json(self) -> Optional[str]:
        if self.blobs and "SaveGameMetaData.json" in self.blobs:
            try:
                return self.blobs["SaveGameMetaData.json"].decode("utf-16")
            except UnicodeDecodeError:
                return None
        return None


def load_bytes(raw: bytes) -> Loaded:
    if raw[:4] == b"PK\x03\x04":
        try:
            z = zipfile.ZipFile(io.BytesIO(raw))
            bad = z.testzip()
            if bad:
                raise SaveFormatError(f"corrupt zip entry: {bad}")
            infos = z.infolist()
            blobs = {i.filename: z.read(i) for i in infos}
        except zipfile.BadZipFile as e:
            raise SaveFormatError(f"bad zip: {e}") from e
        if MAIN_ENTRY not in blobs or blobs[MAIN_ENTRY][:4] != GVAS_MAGIC:
            raise SaveFormatError("zip has no GVAS 'SaveGame' entry")
        return Loaded(True, raw, blobs[MAIN_ENTRY], infos, blobs)
    if raw[:4] == GVAS_MAGIC:
        return Loaded(False, raw, raw)
    raise SaveFormatError("not a ZIP or GVAS file")


def rebuild(loaded: Loaded, new_gvas: bytes, replace: Optional[dict[str, bytes]] = None) -> bytes:
    """Return a new file with the main GVAS payload (and any `replace` entries) swapped."""
    if not loaded.is_zip:
        return new_gvas
    replace = replace or {}
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as z:
        for info in loaded.infos:
            data = (new_gvas if info.filename == MAIN_ENTRY
                    else replace.get(info.filename, loaded.blobs[info.filename]))
            zi = zipfile.ZipInfo(info.filename, info.date_time)
            zi.compress_type = info.compress_type
            zi.external_attr = info.external_attr
            zi.create_system = info.create_system
            if info.compress_type == zipfile.ZIP_DEFLATED:
                z.writestr(zi, data, compress_type=zipfile.ZIP_DEFLATED, compresslevel=6)
            else:
                z.writestr(zi, data, compress_type=info.compress_type)
    return out.getvalue()


def summarize(path: str) -> dict:
    """Cheap listing info; only reads the small info entry, not the 7MB payload."""
    import os
    st = os.stat(path)
    info = {"size": st.st_size, "mtime": st.st_mtime, "kind": "raw", "editable": False}
    try:
        with open(path, "rb") as f:
            head = f.read(4)
        if head == b"PK\x03\x04":
            with zipfile.ZipFile(path) as z:
                info["kind"] = "save"
                info["editable"] = MAIN_ENTRY in z.namelist()
                if "SaveGameInfoJSON.txt" in z.namelist():
                    j = _decode_utf16_json(z.read("SaveGameInfoJSON.txt")) or {}
                    info["title"] = j.get("comment")
                    info["world"] = j.get("worldName")
                    info["created"] = j.get("creationTime")
                    info["turn"] = j.get("strategyTurn")
                    info["difficulty"] = j.get("currentDifficultyLevel")
                    info["autosave"] = j.get("autoSaveType")
        elif head == GVAS_MAGIC:
            info["kind"] = "settings"
    except Exception as e:                        # listing must never crash
        info["error"] = str(e)
    return info


def sync_metadata_sizes(loaded: Loaded, sizes: dict[str, int]) -> Optional[bytes]:
    """Return a new SaveGameMetaData.json with `GameInstanceSize` / `StrategySize` updated.

    Only touches a field whose current value matched the old payload (so we never
    "fix" something we do not understand).  Returns None if nothing changes.
    """
    import re
    raw = (loaded.blobs or {}).get("SaveGameMetaData.json")
    if raw is None:
        return None
    text = raw.decode("utf-16-le")
    changed = False
    for key, new in sizes.items():
        m = re.search(r'("%s"\s*:\s*)(\d+)' % re.escape(key), text)
        if m and int(m.group(2)) != new:
            text = text[:m.start(2)] + str(new) + text[m.end(2):]
            changed = True
    return text.encode("utf-16-le") if changed else None


def save_info(loaded: Loaded) -> Optional[dict]:
    """Parsed SaveGameInfoJSON.txt (title, world, turn, difficulty ...) or None."""
    raw = (loaded.blobs or {}).get("SaveGameInfoJSON.txt")
    return _decode_utf16_json(raw) if raw else None


def read_thumbnail(path: str) -> Optional[bytes]:
    """The save's screenshot (SaveGame.jpg) without loading the 7 MB payload."""
    try:
        with zipfile.ZipFile(path) as z:
            return z.read("SaveGame.jpg") if "SaveGame.jpg" in z.namelist() else None
    except (OSError, zipfile.BadZipFile):
        return None
