"""Core parser/patcher tests on tiny synthetic payloads (no game files needed).

The builder below writes the UE 5.6 property-tag layout used by the game's saves:
  name FString | type tree (name, param count, params...) | int32 size | u8 flags | value bytes
"""
import io
import struct
import zipfile

import pytest

from zc import savefile
from zc.gvas import Gvas, GvasError


def fs(s: str) -> bytes:
    b = s.encode() + b"\0"
    return struct.pack("<i", len(b)) + b


def tn(name: str, *params: bytes) -> bytes:
    return fs(name) + struct.pack("<i", len(params)) + b"".join(params)


def prop(name: str, typ: bytes, value: bytes, flags: int = 0) -> bytes:
    return fs(name) + typ + struct.pack("<i", len(value)) + bytes([flags]) + value


NONE = fs("None")


def int_prop(name, v):
    return prop(name, tn("IntProperty"), struct.pack("<i", v))


def str_prop(name, s):
    return prop(name, tn("StrProperty"), fs(s))


def struct_prop(name, inner: bytes, sname="MyStruct"):
    return prop(name, tn("StructProperty", tn(sname)), inner + NONE)


def int_array(name, values):
    return prop(name, tn("ArrayProperty", tn("IntProperty")),
                struct.pack("<i", len(values)) + b"".join(struct.pack("<i", v) for v in values))


def nested_archive(name, inner_props: bytes):
    inner = b"\0" + inner_props + NONE
    return prop(name, tn("ArrayProperty", tn("ByteProperty")), struct.pack("<i", len(inner)) + inner)


def gvas(body: bytes) -> bytes:
    head = (b"GVAS" + struct.pack("<iii", 3, 522, 1017) + struct.pack("<HHHI", 5, 6, 1, 0) + fs("++Test+Stable")
            + struct.pack("<ii", 3, 0) + fs("/Script/Test.TestSave") + b"\0")
    return head + body + NONE + b"\0\0\0\0"


def sample() -> bytes:
    inner_archive = nested_archive("ArchiveBytes", struct_prop("Data", int_prop("Credits", 1000) + str_prop("Title", "Hi")))
    wrapper = struct_prop("Wrapper", inner_archive, "ObjectWrapper")
    return gvas(int_prop("Turn", 7) + int_array("Squad", [1, 2, 3]) + wrapper + str_prop("Name", "Alpha"))


def find(g, *names):
    nodes, cur = g.root, None
    for n in names:
        cur = next(c for c in nodes if c.name == n)
        nodes = cur.children or []
    return cur


def test_parses_the_tree_and_reads_values():
    g = Gvas(sample())
    assert [n.name for n in g.root] == ["Turn", "Squad", "Wrapper", "Name"]
    assert g.get(find(g, "Turn")) == 7
    assert g.get(find(g, "Name")) == "Alpha"
    assert find(g, "Squad").count == 3
    assert g.get(find(g, "Wrapper", "ArchiveBytes", "Data", "Credits")) == 1000      # nested archive parsed
    assert g.opaque_count == 0 and find(g, "Wrapper", "ArchiveBytes").nested


def test_in_place_edit_changes_only_that_value():
    raw = sample()
    g = Gvas(raw)
    g.set(find(g, "Wrapper", "ArchiveBytes", "Data", "Credits"), 99999)
    new = g.to_bytes()
    assert len(new) == len(raw)
    diff = [i for i in range(len(raw)) if raw[i] != new[i]]
    assert diff and max(diff) - min(diff) < 4
    assert Gvas(new).get(find(Gvas(new), "Wrapper", "ArchiveBytes", "Data", "Credits")) == 99999


def test_set_string_resizes_and_fixes_every_ancestor():
    g = Gvas(sample())
    node = find(g, "Wrapper", "ArchiveBytes", "Data", "Title")
    g2 = g.set_string(node, "A much longer title than before")
    delta = len(g2.data) - len(g.data)
    assert delta == len("A much longer title than before") - len("Hi")
    # the tree re-parses with identical structure, so all size fields and the archive byte count are consistent
    assert g2.opaque_count == 0
    assert g2.get(find(g2, "Wrapper", "ArchiveBytes", "Data", "Title")) == "A much longer title than before"
    for names in (("Wrapper",), ("Wrapper", "ArchiveBytes"), ("Wrapper", "ArchiveBytes", "Data")):
        assert find(g2, *names).size == find(g, *names).size + delta
    assert find(g2, "Wrapper", "ArchiveBytes").count == find(g, "Wrapper", "ArchiveBytes").count + delta
    assert g2.get(find(g2, "Name")) == "Alpha" and g2.get(find(g2, "Turn")) == 7      # neighbours untouched


def test_array_append_int_updates_count_and_sizes():
    g = Gvas(sample())
    g2 = g.array_append_int(find(g, "Squad"), 42)
    arr = find(g2, "Squad")
    assert arr.count == 4 and arr.size == find(g, "Squad").size + 4
    assert struct.unpack_from("<4i", g2.data, arr.value_offset + 4) == (1, 2, 3, 42)
    assert g2.opaque_count == 0 and g2.get(find(g2, "Name")) == "Alpha"


def test_rejects_non_gvas_and_unsupported_edits():
    with pytest.raises(GvasError):
        Gvas(b"NOPE" + b"\0" * 64)
    g = Gvas(sample())
    with pytest.raises(GvasError):
        g.set(find(g, "Name"), 5)                      # strings are not editable in place
    with pytest.raises(GvasError):
        g.set_string(find(g, "Turn"), "x")             # not a string property
    with pytest.raises(GvasError):
        g.set_string(find(g, "Name"), "naïve")         # ASCII only


def test_zip_container_roundtrip_keeps_other_entries_identical():
    payload = sample()
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        z.writestr("SaveGameInfo", b"info", compress_type=zipfile.ZIP_DEFLATED)
        z.writestr("SaveGame", payload, compress_type=zipfile.ZIP_DEFLATED)
        z.writestr("SaveGamePortraits.zip", b"\x01\x02\x03", compress_type=zipfile.ZIP_STORED)
    loaded = savefile.load_bytes(buf.getvalue())
    assert loaded.is_zip and loaded.gvas == payload
    g = Gvas(loaded.gvas)
    g.set(find(g, "Turn"), 8)
    again = savefile.load_bytes(savefile.rebuild(loaded, g.to_bytes()))
    assert again.blobs["SaveGameInfo"] == b"info" and again.blobs["SaveGamePortraits.zip"] == b"\x01\x02\x03"
    assert Gvas(again.gvas).get(find(Gvas(again.gvas), "Turn")) == 8
    assert [i.filename for i in again.infos] == ["SaveGameInfo", "SaveGame", "SaveGamePortraits.zip"]
    assert again.infos[2].compress_type == zipfile.ZIP_STORED


def test_corrupt_inputs_are_rejected_cleanly():
    with pytest.raises(savefile.SaveFormatError):
        savefile.load_bytes(b"not a save at all")
    with pytest.raises(savefile.SaveFormatError):
        savefile.load_bytes(b"PK\x03\x04garbage")
