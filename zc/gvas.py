"""Offset-preserving GVAS (Unreal Engine save) reader/patcher.

The tree is only an *index* over the original bytes: every node records where its
value lives.  Edits overwrite fixed-size values in place and never re-serialize,
so everything we don't understand is preserved byte-for-byte.

Format: UE 5.6 property tags (FPropertyTypeName tree): name FString, type tree,
int32 size, u8 flags, optional array index / guid, then `size` bytes of value.
"""
from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import Iterator, Optional

FLAG_ARRAY_INDEX = 0x01
FLAG_GUID = 0x02
FLAG_EXTENSIONS = 0x04
FLAG_BOOL_TRUE = 0x10

# Structs serialized as raw binary (no property list) and their byte sizes.
BINARY_STRUCTS = {
    "Guid": 16, "DateTime": 8, "Timespan": 8, "IntPoint": 8, "IntVector": 12,
    "Vector": 24, "Vector3d": 24, "Rotator": 24, "Rotator3d": 24, "Vector2D": 16,
    "Vector4": 32, "Quat": 32, "Quat4d": 32, "LinearColor": 16, "Color": 4,
    "Transform": 80, "Transform3d": 80,
}

# Structs with engine-native serialization we deliberately keep as raw bytes.
NATIVE_STRUCTS = {"GameplayTagContainer", "InstancedStruct"}

# Fixed-size scalar leaf types: name -> (struct format, size)
SCALARS = {
    "IntProperty": ("<i", 4), "UInt32Property": ("<I", 4),
    "Int64Property": ("<q", 8), "UInt64Property": ("<Q", 8),
    "Int16Property": ("<h", 2), "UInt16Property": ("<H", 2),
    "Int8Property": ("<b", 1), "FloatProperty": ("<f", 4),
    "DoubleProperty": ("<d", 8),
}
STRING_TYPES = {"StrProperty", "NameProperty", "ObjectProperty", "EnumProperty",
                "SoftObjectProperty", "ClassProperty"}


class GvasError(Exception):
    pass


@dataclass
class TypeName:
    name: str
    params: list["TypeName"] = field(default_factory=list)

    def __repr__(self):
        return self.name + (f"<{','.join(map(repr, self.params))}>" if self.params else "")


@dataclass
class Node:
    name: str
    type: TypeName
    size: int                      # value size in bytes (0 for bool)
    start: int                     # offset of the property tag
    value_offset: int              # offset of the value bytes
    flags: int = 0
    children: Optional[list["Node"]] = None   # struct fields / array+map elements
    opaque: bool = False           # could not be parsed deeper (still has bytes)
    count: Optional[int] = None    # array / map element count
    parent: Optional["Node"] = field(default=None, repr=False)
    native: bool = False
    size_off: Optional[int] = None  # offset of this property's int32 size field
    nested: bool = False           # byte array that contains a property list

    @property
    def tname(self) -> str:
        return self.type.name

    @property
    def end(self) -> int:
        return self.value_offset + self.size


class Reader:
    def __init__(self, data: bytes | bytearray, pos: int = 0):
        self.d = data
        self.p = pos

    def u8(self):
        v = self.d[self.p]; self.p += 1; return v

    def i32(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]; self.p += 4; return v

    def skip(self, n):
        if n < 0 or self.p + n > len(self.d):
            raise GvasError("skip out of range")
        self.p += n

    def fstring(self) -> str:
        n = self.i32()
        if n == 0:
            return ""
        if n < 0:
            n = -n
            if self.p + 2 * n > len(self.d):
                raise GvasError("bad utf16 string")
            s = bytes(self.d[self.p:self.p + 2 * n - 2]).decode("utf-16-le")
            self.p += 2 * n
            return s
        if n > 4096 or self.p + n > len(self.d):
            raise GvasError(f"bad string length {n} at {self.p}")
        s = bytes(self.d[self.p:self.p + n - 1]).decode("utf-8", "replace")
        self.p += n
        return s

    def type_name(self, depth=0) -> TypeName:
        if depth > 8:
            raise GvasError("type tree too deep")
        name = self.fstring()
        n = self.i32()
        if not 0 <= n <= 8:
            raise GvasError(f"bad type param count {n}")
        return TypeName(name, [self.type_name(depth + 1) for _ in range(n)])


class Gvas:
    def __init__(self, data: bytes):
        self.data = bytearray(data)
        self.opaque_count = 0
        self._parse_header()

    # ---- header -----------------------------------------------------------
    def _parse_header(self):
        r = Reader(self.data)
        if bytes(self.data[:4]) != b"GVAS":
            raise GvasError("not a GVAS file")
        r.p = 4
        self.save_version = r.i32()
        self.ue4_version = r.i32()
        self.ue5_version = r.i32() if self.save_version >= 3 else 0
        self.engine = struct.unpack_from("<HHHI", self.data, r.p); r.p += 10
        self.branch = r.fstring()
        self.custom_format = r.i32()
        n = r.i32()
        r.skip(n * 20)
        self.save_class = r.fstring()
        self.props_start = r.p
        # Bruno saves carry one extra flag byte before the first property.
        self.pre_byte = r.u8()
        self.props_start = r.p
        self.root: list[Node] = []
        self.root = self._props(r, len(self.data), None)
        self.props_end = r.p

    # ---- property list ----------------------------------------------------
    def _props(self, r: Reader, limit: int, parent: Optional[Node]) -> list[Node]:
        out: list[Node] = []
        while True:
            if r.p >= limit:
                raise GvasError("property list ran past limit")
            start = r.p
            name = r.fstring()
            if name == "None":
                return out
            tn = r.type_name()
            size_off = r.p
            size = r.i32()
            flags = r.u8()
            if flags & FLAG_ARRAY_INDEX:
                r.i32()
            if flags & FLAG_GUID:
                r.skip(16)
            if flags & FLAG_EXTENSIONS:
                raise GvasError("property extensions not supported")
            node = Node(name, tn, size, start, r.p, flags, parent=parent)
            node.size_off = size_off
            if size < 0 or r.p + size > limit:
                raise GvasError(f"bad size {size} for {name} at {start}")
            self._value(r, node)
            r.p = node.end
            out.append(node)

    def _value(self, r: Reader, node: Node):
        t = node.tname
        saved = r.p
        try:
            if t == "StructProperty":
                self._struct(r, node)
            elif t == "ArrayProperty" or t == "SetProperty":
                self._array(r, node)
            elif t == "MapProperty":
                self._map(r, node)
        except (GvasError, struct.error, IndexError) as e:
            node.children = None
            node.opaque = True
            node.count = None
            self.opaque_count += 1
        r.p = saved

    def _struct(self, r: Reader, node: Node):
        sname = node.type.params[0].name if node.type.params else ""
        if sname in BINARY_STRUCTS:
            return                                  # leaf
        if sname in NATIVE_STRUCTS:
            node.native = True                      # engine-native blob, kept as bytes
            return
        sub = Reader(r.d, r.p)
        node.children = self._props(sub, node.end, node)
        if sub.p != node.end:
            raise GvasError("struct size mismatch")

    def _array(self, r: Reader, node: Node):
        inner = node.type.params[0] if node.type.params else TypeName("")
        end = node.end
        count = r.i32()
        node.count = count
        it = inner.name
        if it == "StructProperty":
            sname = inner.params[0].name if inner.params else ""
            kids = []
            for i in range(count):
                el = Node(f"[{i}]", inner, 0, r.p, r.p, parent=node)
                if sname in BINARY_STRUCTS:
                    el.size = BINARY_STRUCTS[sname]
                    r.skip(el.size)
                else:
                    el.children = self._props(r, end, el)
                    el.size = r.p - el.value_offset
                kids.append(el)
            if r.p != end:
                raise GvasError("array size mismatch")
            node.children = kids
        elif it == "ByteProperty" and not inner.params:
            self._nested_archive(r, node, count, end)
        elif it in SCALARS or it == "BoolProperty":
            return                                  # packed scalars: leaf, address by index
        elif it in STRING_TYPES:
            kids = []
            for i in range(count):
                s = r.p
                r.fstring()
                kids.append(Node(f"[{i}]", inner, r.p - s, s, s, parent=node))
            if r.p != end:
                raise GvasError("string array size mismatch")
            node.children = kids
        else:
            raise GvasError(f"unsupported array element {it}")

    def _nested_archive(self, r: Reader, node: Node, count: int, end: int):
        """A byte array may hold another serialized property list (ArchiveBytes)."""
        if count < 16 or r.d[r.p] != 0:
            return
        sub = Reader(r.d, r.p + 1)
        try:
            kids = self._props(sub, end, node)
        except (GvasError, struct.error, IndexError, UnicodeDecodeError):
            return
        if end - sub.p > 8:
            return                                  # didn't look like a full archive
        node.children = kids
        node.nested = True

    def _map(self, r: Reader, node: Node):
        if len(node.type.params) < 2:
            raise GvasError("map type params")
        kt, vt = node.type.params[:2]
        end = node.end
        if r.i32() != 0:
            raise GvasError("map removal entries unsupported")
        count = r.i32()
        node.count = count
        kids = []
        for i in range(count):
            ent = Node(f"[{i}]", vt, 0, r.p, r.p, parent=node)
            ent.children = [self._map_item(r, kt, "key", ent, end),
                            self._map_item(r, vt, "value", ent, end)]
            ent.size = r.p - ent.value_offset
            kids.append(ent)
        if r.p != end:
            raise GvasError("map size mismatch")
        node.children = kids

    def _map_item(self, r: Reader, tn: TypeName, label: str, parent: Node, end: int) -> Node:
        n = Node(label, tn, 0, r.p, r.p, parent=parent)
        t = tn.name
        if t in SCALARS:
            n.size = SCALARS[t][1]; r.skip(n.size)
        elif t in STRING_TYPES:
            r.fstring(); n.size = r.p - n.value_offset
        elif t == "BoolProperty" or (t == "ByteProperty" and not tn.params):
            n.size = 1; r.skip(1)
        elif t == "StructProperty":
            sname = tn.params[0].name if tn.params else ""
            if sname in BINARY_STRUCTS:
                n.size = BINARY_STRUCTS[sname]; r.skip(n.size)
            else:
                n.children = self._props(r, end, n); n.size = r.p - n.value_offset
        else:
            raise GvasError(f"unsupported map item {t}")
        return n

    # ---- navigation -------------------------------------------------------
    def walk(self, nodes: Optional[list[Node]] = None) -> Iterator[Node]:
        for n in (self.root if nodes is None else nodes):
            yield n
            if n.children:
                yield from self.walk(n.children)

    @staticmethod
    def child(node: Node, name: str) -> Optional[Node]:
        for c in node.children or ():
            if c.name == name:
                return c
        return None

    # ---- values -----------------------------------------------------------
    def get(self, node: Node):
        t = node.tname
        if t in SCALARS:
            fmt, _ = SCALARS[t]
            return struct.unpack_from(fmt, self.data, node.value_offset)[0]
        if t == "BoolProperty":
            return bool(node.flags & FLAG_BOOL_TRUE)
        if t in STRING_TYPES:
            return Reader(self.data, node.value_offset).fstring()
        if t == "ByteProperty" and not node.type.params:
            return self.data[node.value_offset]
        raise GvasError(f"cannot read {t}")

    def set(self, node: Node, value) -> None:
        """Overwrite a fixed-size scalar in place (never changes file size)."""
        t = node.tname
        if t in SCALARS:
            fmt, _ = SCALARS[t]
            struct.pack_into(fmt, self.data, node.value_offset, value)
        elif t == "ByteProperty" and not node.type.params:
            self.data[node.value_offset] = int(value) & 0xFF
        else:
            raise GvasError(f"cannot edit {t} in place")

    def set_string(self, node: Node, text: str) -> "Gvas":
        """Replace an FString-valued property (Name/Str/Enum/Object...) with `text`.

        The length changes, so every ancestor's size field (and nested-archive
        byte count) is adjusted.  Returns a NEW Gvas; old node refs are invalid.
        """
        if node.tname not in STRING_TYPES:
            raise GvasError(f"cannot set string on {node.tname}")
        if not text.isascii():
            raise GvasError("only ASCII strings supported")
        if Reader(self.data, node.value_offset).fstring() is None or node.size != self._fstring_len(node):
            raise GvasError("property is not a single FString")
        raw = text.encode("ascii") + b"\x00"
        new = struct.pack("<i", len(raw)) + raw
        delta = len(new) - node.size
        buf = bytearray(self.data)
        buf[node.value_offset:node.end] = new
        self._grow(buf, node, delta)
        return Gvas(bytes(buf))

    def array_append_int(self, node: Node, value: int) -> "Gvas":
        """Append one int32 to an ArrayProperty<IntProperty>; returns a NEW Gvas."""
        if node.tname != "ArrayProperty" or not node.type.params or node.type.params[0].name != "IntProperty":
            raise GvasError("not an int array")
        buf = bytearray(self.data)
        buf[node.end:node.end] = struct.pack("<i", value)
        struct.pack_into("<i", buf, node.value_offset, node.count + 1)
        self._grow(buf, node, 4)
        return Gvas(bytes(buf))

    def array_remove_element(self, arr: Node, index: int) -> "Gvas":
        """Remove element `index` of an ArrayProperty of structs; returns a NEW Gvas.

        The array's count drops by one and every ancestor's size field (and nested byte counts) shrink by the
        removed bytes.  Everything after the element moves up unchanged.
        """
        if arr.tname != "ArrayProperty" or not arr.children or not 0 <= index < len(arr.children):
            raise GvasError("not a removable array element")
        el = arr.children[index]
        if el.end <= el.start:
            raise GvasError("empty element")
        buf = bytearray(self.data)
        del buf[el.start:el.end]
        struct.pack_into("<i", buf, arr.value_offset, arr.count - 1)
        self._grow(buf, arr, -(el.end - el.start))
        return Gvas(bytes(buf))

    @staticmethod
    def _grow(buf: bytearray, node: Node, delta: int) -> None:
        """Add `delta` to the size field of `node` and every ancestor (and to the
        byte count of any nested archive).  Size fields precede the spliced
        bytes, so earlier offsets stay valid."""
        n: Optional[Node] = node
        while n is not None:
            if n.size_off is not None:
                struct.pack_into("<i", buf, n.size_off, n.size + delta)
                if n.nested:
                    struct.pack_into("<i", buf, n.value_offset,
                                     struct.unpack_from("<i", buf, n.value_offset)[0] + delta)
            n = n.parent

    def _fstring_len(self, node: Node) -> int:
        n = struct.unpack_from("<i", self.data, node.value_offset)[0]
        return 4 + (2 * -n if n < 0 else n)

    def path_of(self, node: Node) -> tuple:
        """Stable structural path (names + element indices) to re-find a node."""
        out = []
        while node is not None:
            out.append(node.name)
            node = node.parent
        return tuple(reversed(out))

    def find_path(self, path: tuple) -> Optional[Node]:
        nodes = self.root
        cur = None
        for name in path:
            cur = next((n for n in nodes if n.name == name), None)
            if cur is None:
                return None
            nodes = cur.children or []
        return cur

    def to_bytes(self) -> bytes:
        return bytes(self.data)
