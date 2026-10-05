"""Operator portraits stored inside saves (SaveGamePortraits.zip -> *.bin -> ExrImage).

Each ``.bin`` is a plain property list whose ``ExrImage`` byte array is an OpenEXR picture.
Browsers cannot show EXR, so we convert to PNG.  Everything degrades to ``None`` when the
optional imaging libraries (OpenEXR, numpy, Pillow) are missing or a picture cannot be
decoded; the UI then shows an initials badge instead.
"""
from __future__ import annotations

import hashlib
import io
import os
import tempfile
import zipfile
from typing import Optional

from .gvas import Gvas, GvasError, Reader

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE_DIR = os.path.join(ROOT, "cache", "portraits")
ENTRY = "SaveGamePortraits.zip"


def libraries_available() -> bool:
    try:
        import numpy  # noqa: F401
        import OpenEXR  # noqa: F401
        from PIL import Image  # noqa: F401
        return True
    except Exception:
        return False


def exr_bytes(bin_data: bytes) -> Optional[bytes]:
    """Pull the raw EXR out of a portrait ``.bin`` (a property list with an ExrImage array)."""
    g = Gvas.__new__(Gvas)                         # reuse the property reader without a GVAS header
    g.data = bytearray(bin_data)
    g.opaque_count = 0
    try:
        props = g._props(Reader(g.data, 0), len(bin_data), None)
    except (GvasError, Exception):
        return None
    node = next((p for p in props if p.name == "ExrImage" and p.tname == "ArrayProperty"), None)
    if node is None or node.size < 8:
        return None
    return bytes(g.data[node.value_offset + 4: node.end])      # skip the int32 element count


def exr_to_png(exr: bytes) -> Optional[bytes]:
    """Linear float EXR -> sRGB PNG (RGBA).  None if the libraries or the image are unusable."""
    try:
        import numpy as np
        import OpenEXR
        from PIL import Image
    except Exception:
        return None
    tmp = None
    try:
        with tempfile.NamedTemporaryFile(suffix=".exr", delete=False) as f:
            f.write(exr)
            tmp = f.name
        with OpenEXR.File(tmp) as ef:
            ch = ef.channels()
            if "RGBA" in ch:
                px = np.asarray(ch["RGBA"].pixels, dtype=np.float32)
            elif "RGB" in ch:
                rgb = np.asarray(ch["RGB"].pixels, dtype=np.float32)
                px = np.concatenate([rgb, np.ones(rgb.shape[:2] + (1,), np.float32)], axis=2)
            else:
                return None
        rgb = np.clip(px[..., :3], 0.0, 1.0)
        srgb = np.where(rgb <= 0.0031308, 12.92 * rgb, 1.055 * np.power(rgb, 1 / 2.4) - 0.055)
        out = np.concatenate([srgb, np.clip(px[..., 3:4], 0.0, 1.0)], axis=2)
        img = Image.fromarray((out * 255 + 0.5).astype(np.uint8), "RGBA")
        buf = io.BytesIO()
        img.save(buf, "PNG", optimize=True)
        return buf.getvalue()
    except Exception:
        return None
    finally:
        if tmp and os.path.exists(tmp):
            os.remove(tmp)


class Portraits:
    """Portraits of one loaded save, addressed by operator GUID (32 hex chars, upper case)."""

    def __init__(self, blobs: Optional[dict]):
        self._entries: dict[str, bytes] = {}
        self._png: dict[str, Optional[bytes]] = {}
        raw = (blobs or {}).get(ENTRY)
        if not raw:
            return
        try:
            z = zipfile.ZipFile(io.BytesIO(raw))
            for info in z.infolist():
                parts = info.filename.rsplit(".", 1)[0].split("-")
                if len(parts) >= 2 and len(parts[-2]) == 32:       # <class>-<GUID>-<hash>.bin
                    self._entries[parts[-2].upper()] = z.read(info)
        except zipfile.BadZipFile:
            self._entries.clear()

    def guids(self) -> list[str]:
        return list(self._entries)

    def png(self, guid: str) -> Optional[bytes]:
        guid = guid.upper()
        if guid in self._png:
            return self._png[guid]
        data = self._entries.get(guid)
        out = None
        if data is not None:
            exr = exr_bytes(data)
            if exr is not None:
                key = hashlib.sha1(exr).hexdigest()
                path = os.path.join(CACHE_DIR, key + ".png")
                if os.path.exists(path):
                    with open(path, "rb") as f:
                        out = f.read()
                else:
                    out = exr_to_png(exr)
                    if out:
                        try:
                            os.makedirs(CACHE_DIR, exist_ok=True)
                            with open(path, "wb") as f:
                                f.write(out)
                        except OSError:
                            pass
        self._png[guid] = out
        return out
