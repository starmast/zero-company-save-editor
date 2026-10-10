using System.Buffers.Binary;
using System.Text;

namespace ZeroCompany.Core.Gvas;

/// <summary>
/// Offset-preserving GVAS (Unreal Engine save) reader/patcher.
///
/// The tree is only an *index* over the original bytes: every node records where its value lives.
/// Edits overwrite fixed-size values in place and never re-serialize, so everything we don't
/// understand is preserved byte-for-byte.
///
/// Format: UE 5.6 property tags (FPropertyTypeName tree): name FString, type tree, int32 size,
/// u8 flags, optional array index / guid, then <c>size</c> bytes of value.
/// </summary>
public sealed class GvasFile
{
    public const int FlagArrayIndex = 0x01;
    public const int FlagGuid = 0x02;
    public const int FlagExtensions = 0x04;
    public const int FlagBoolTrue = 0x10;

    /// <summary>Structs serialized as raw binary (no property list) and their byte sizes.</summary>
    public static readonly IReadOnlyDictionary<string, int> BinaryStructs = new Dictionary<string, int>
    {
        ["Guid"] = 16, ["DateTime"] = 8, ["Timespan"] = 8, ["IntPoint"] = 8, ["IntVector"] = 12,
        ["Vector"] = 24, ["Vector3d"] = 24, ["Rotator"] = 24, ["Rotator3d"] = 24, ["Vector2D"] = 16,
        ["Vector4"] = 32, ["Quat"] = 32, ["Quat4d"] = 32, ["LinearColor"] = 16, ["Color"] = 4,
        ["Transform"] = 80, ["Transform3d"] = 80,
    };

    /// <summary>Structs with engine-native serialization we deliberately keep as raw bytes.</summary>
    static readonly HashSet<string> NativeStructs = new() { "GameplayTagContainer", "InstancedStruct" };

    /// <summary>Fixed-size scalar leaf types and their byte sizes.</summary>
    public static readonly IReadOnlyDictionary<string, int> Scalars = new Dictionary<string, int>
    {
        ["IntProperty"] = 4, ["UInt32Property"] = 4, ["Int64Property"] = 8, ["UInt64Property"] = 8,
        ["Int16Property"] = 2, ["UInt16Property"] = 2, ["Int8Property"] = 1, ["FloatProperty"] = 4,
        ["DoubleProperty"] = 8,
    };

    public static readonly IReadOnlySet<string> StringTypes = new HashSet<string>
    {
        "StrProperty", "NameProperty", "ObjectProperty", "EnumProperty", "SoftObjectProperty", "ClassProperty",
    };

    public byte[] Data { get; private set; }
    public int OpaqueCount { get; private set; }
    public int SaveVersion { get; private set; }
    public int Ue4Version { get; private set; }
    public int Ue5Version { get; private set; }
    public string Branch { get; private set; } = "";
    public string SaveClass { get; private set; } = "";
    public int PropsStart { get; private set; }
    public int PropsEnd { get; private set; }
    public List<GvasNode> Root { get; private set; } = new();

    public GvasFile(byte[] data)
    {
        Data = (byte[])data.Clone();
        ParseHeader();
    }

    GvasFile(byte[] data, bool _) { Data = data; }

    /// <summary>Parse a bare property list (no GVAS header), e.g. a portrait <c>.bin</c>. Throws on malformed input.</summary>
    public static GvasFile FromPropertyList(byte[] data)
    {
        var g = new GvasFile((byte[])data.Clone(), false);
        g.Root = g.ReadProps(new ByteReader(g.Data, 0), g.Data.Length, null);
        return g;
    }

    // ---- header -----------------------------------------------------------
    void ParseHeader()
    {
        if (Data.Length < 4 || Data[0] != (byte)'G' || Data[1] != (byte)'V' || Data[2] != (byte)'A' || Data[3] != (byte)'S')
            throw new GvasException("not a GVAS file");
        var r = new ByteReader(Data, 4);
        SaveVersion = r.I32();
        Ue4Version = r.I32();
        Ue5Version = SaveVersion >= 3 ? r.I32() : 0;
        r.Skip(10);                                  // engine version: u16 u16 u16 u32
        Branch = r.FString();
        r.I32();                                     // custom version format
        int n = r.I32();
        if (n < 0) throw new GvasException("bad custom version count");
        r.Skip(checked(n * 20));
        SaveClass = r.FString();
        r.U8();                                      // Bruno saves carry one extra flag byte before the first property
        PropsStart = r.P;
        Root = ReadProps(r, Data.Length, null);
        PropsEnd = r.P;
    }

    // ---- property list ----------------------------------------------------
    List<GvasNode> ReadProps(ByteReader r, int limit, GvasNode? parent)
    {
        var @out = new List<GvasNode>();
        while (true)
        {
            if (r.P >= limit) throw new GvasException("property list ran past limit");
            int start = r.P;
            var name = r.FString();
            if (name == "None") return @out;
            var tn = r.TypeName();
            int sizeOff = r.P;
            int size = r.I32();
            int flags = r.U8();
            if ((flags & FlagArrayIndex) != 0) r.I32();
            if ((flags & FlagGuid) != 0) r.Skip(16);
            if ((flags & FlagExtensions) != 0) throw new GvasException("property extensions not supported");
            var node = new GvasNode(name, tn, size, start, r.P, flags, parent) { SizeOff = sizeOff };
            if (size < 0 || (long)r.P + size > limit) throw new GvasException($"bad size {size} for {name} at {start}");
            ReadValue(r, node);
            r.P = node.End;
            @out.Add(node);
        }
    }

    void ReadValue(ByteReader r, GvasNode node)
    {
        var t = node.TName;
        int saved = r.P;
        try
        {
            if (t == "StructProperty") ReadStruct(r, node);
            else if (t == "ArrayProperty" || t == "SetProperty") ReadArray(r, node);
            else if (t == "MapProperty") ReadMap(r, node);
        }
        catch (Exception e) when (e is GvasException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            node.Children = null;
            node.Opaque = true;
            node.Count = null;
            OpaqueCount++;
        }
        r.P = saved;
    }

    static string StructName(TypeName tn) => tn.Params.Count > 0 ? tn.Params[0].Name : "";

    void ReadStruct(ByteReader r, GvasNode node)
    {
        var sname = StructName(node.Type);
        if (BinaryStructs.ContainsKey(sname)) return;       // leaf
        if (NativeStructs.Contains(sname)) { node.Native = true; return; }
        var sub = new ByteReader(r.D, r.P);
        node.Children = ReadProps(sub, node.End, node);
        if (sub.P != node.End) throw new GvasException("struct size mismatch");
    }

    void ReadArray(ByteReader r, GvasNode node)
    {
        var inner = node.Type.Params.Count > 0 ? node.Type.Params[0] : new TypeName("");
        int end = node.End;
        int count = r.I32();
        node.Count = count;
        var it = inner.Name;
        if (count < 0) throw new GvasException("negative array count");
        if (it == "StructProperty")
        {
            var sname = StructName(inner);
            var kids = new List<GvasNode>();
            for (int i = 0; i < count; i++)
            {
                var el = new GvasNode($"[{i}]", inner, 0, r.P, r.P, 0, node);
                if (BinaryStructs.TryGetValue(sname, out var bs))
                {
                    el.Size = bs;
                    r.Skip(el.Size);
                }
                else
                {
                    el.Children = ReadProps(r, end, el);
                    el.Size = r.P - el.ValueOffset;
                }
                kids.Add(el);
            }
            if (r.P != end) throw new GvasException("array size mismatch");
            node.Children = kids;
        }
        else if (it == "ByteProperty" && inner.Params.Count == 0)
        {
            ReadNestedArchive(r, node, count, end);
        }
        else if (Scalars.ContainsKey(it) || it == "BoolProperty")
        {
            return;                                          // packed scalars: leaf, address by index
        }
        else if (StringTypes.Contains(it))
        {
            var kids = new List<GvasNode>();
            for (int i = 0; i < count; i++)
            {
                int s = r.P;
                r.FString();
                kids.Add(new GvasNode($"[{i}]", inner, r.P - s, s, s, 0, node));
            }
            if (r.P != end) throw new GvasException("string array size mismatch");
            node.Children = kids;
        }
        else throw new GvasException($"unsupported array element {it}");
    }

    /// <summary>A byte array may hold another serialized property list (ArchiveBytes).</summary>
    void ReadNestedArchive(ByteReader r, GvasNode node, int count, int end)
    {
        if (count < 16 || r.P >= r.D.Length || r.D[r.P] != 0) return;
        var sub = new ByteReader(r.D, r.P + 1);
        List<GvasNode> kids;
        try { kids = ReadProps(sub, end, node); }
        catch (Exception e) when (e is GvasException or IndexOutOfRangeException or ArgumentException or OverflowException) { return; }
        if (end - sub.P > 8) return;                         // didn't look like a full archive
        node.Children = kids;
        node.Nested = true;
    }

    void ReadMap(ByteReader r, GvasNode node)
    {
        if (node.Type.Params.Count < 2) throw new GvasException("map type params");
        var kt = node.Type.Params[0];
        var vt = node.Type.Params[1];
        int end = node.End;
        if (r.I32() != 0) throw new GvasException("map removal entries unsupported");
        int count = r.I32();
        node.Count = count;
        if (count < 0) throw new GvasException("negative map count");
        var kids = new List<GvasNode>();
        for (int i = 0; i < count; i++)
        {
            var ent = new GvasNode($"[{i}]", vt, 0, r.P, r.P, 0, node);
            ent.Children = new List<GvasNode>
            {
                ReadMapItem(r, kt, "key", ent, end),
                ReadMapItem(r, vt, "value", ent, end),
            };
            ent.Size = r.P - ent.ValueOffset;
            kids.Add(ent);
        }
        if (r.P != end) throw new GvasException("map size mismatch");
        node.Children = kids;
    }

    GvasNode ReadMapItem(ByteReader r, TypeName tn, string label, GvasNode parent, int end)
    {
        var n = new GvasNode(label, tn, 0, r.P, r.P, 0, parent);
        var t = tn.Name;
        if (Scalars.TryGetValue(t, out var sz)) { n.Size = sz; r.Skip(sz); }
        else if (StringTypes.Contains(t)) { r.FString(); n.Size = r.P - n.ValueOffset; }
        else if (t == "BoolProperty" || (t == "ByteProperty" && tn.Params.Count == 0)) { n.Size = 1; r.Skip(1); }
        else if (t == "StructProperty")
        {
            var sname = StructName(tn);
            if (BinaryStructs.TryGetValue(sname, out var bs)) { n.Size = bs; r.Skip(bs); }
            else { n.Children = ReadProps(r, end, n); n.Size = r.P - n.ValueOffset; }
        }
        else throw new GvasException($"unsupported map item {t}");
        return n;
    }

    // ---- navigation -------------------------------------------------------
    public IEnumerable<GvasNode> Walk(IEnumerable<GvasNode>? nodes = null)
    {
        foreach (var n in nodes ?? Root)
        {
            yield return n;
            if (n.Children is { Count: > 0 })
                foreach (var c in Walk(n.Children)) yield return c;
        }
    }

    public static GvasNode? Child(GvasNode node, string name)
    {
        if (node.Children == null) return null;
        foreach (var c in node.Children) if (c.Name == name) return c;
        return null;
    }

    // ---- values -----------------------------------------------------------
    /// <summary>Read a leaf. Returns int/uint/long/ulong/short/ushort/sbyte/float/double/bool/byte/string.</summary>
    public object Get(GvasNode node)
    {
        var t = node.TName;
        var span = Data.AsSpan();
        int o = node.ValueOffset;
        switch (t)
        {
            case "IntProperty": return BinaryPrimitives.ReadInt32LittleEndian(span[o..]);
            case "UInt32Property": return BinaryPrimitives.ReadUInt32LittleEndian(span[o..]);
            case "Int64Property": return BinaryPrimitives.ReadInt64LittleEndian(span[o..]);
            case "UInt64Property": return BinaryPrimitives.ReadUInt64LittleEndian(span[o..]);
            case "Int16Property": return BinaryPrimitives.ReadInt16LittleEndian(span[o..]);
            case "UInt16Property": return BinaryPrimitives.ReadUInt16LittleEndian(span[o..]);
            case "Int8Property": return (sbyte)Data[o];
            case "FloatProperty": return BinaryPrimitives.ReadSingleLittleEndian(span[o..]);
            case "DoubleProperty": return BinaryPrimitives.ReadDoubleLittleEndian(span[o..]);
            case "BoolProperty": return (node.Flags & FlagBoolTrue) != 0;
        }
        if (StringTypes.Contains(t)) return new ByteReader(Data, o).FString();
        if (t == "ByteProperty" && node.Type.Params.Count == 0) return Data[o];
        throw new GvasException($"cannot read {t}");
    }

    /// <summary>Overwrite a fixed-size scalar in place (never changes file size).</summary>
    public void Set(GvasNode node, object value)
    {
        var t = node.TName;
        var span = Data.AsSpan();
        int o = node.ValueOffset;
        switch (t)
        {
            case "IntProperty": BinaryPrimitives.WriteInt32LittleEndian(span[o..], Convert.ToInt32(value)); return;
            case "UInt32Property": BinaryPrimitives.WriteUInt32LittleEndian(span[o..], Convert.ToUInt32(value)); return;
            case "Int64Property": BinaryPrimitives.WriteInt64LittleEndian(span[o..], Convert.ToInt64(value)); return;
            case "UInt64Property": BinaryPrimitives.WriteUInt64LittleEndian(span[o..], Convert.ToUInt64(value)); return;
            case "Int16Property": BinaryPrimitives.WriteInt16LittleEndian(span[o..], Convert.ToInt16(value)); return;
            case "UInt16Property": BinaryPrimitives.WriteUInt16LittleEndian(span[o..], Convert.ToUInt16(value)); return;
            case "Int8Property": Data[o] = unchecked((byte)Convert.ToSByte(value)); return;
            case "FloatProperty": BinaryPrimitives.WriteSingleLittleEndian(span[o..], Convert.ToSingle(value)); return;
            case "DoubleProperty": BinaryPrimitives.WriteDoubleLittleEndian(span[o..], Convert.ToDouble(value)); return;
        }
        if (t == "ByteProperty" && node.Type.Params.Count == 0) { Data[o] = (byte)(Convert.ToInt32(value) & 0xFF); return; }
        throw new GvasException($"cannot edit {t} in place");
    }

    /// <summary>
    /// Replace an FString-valued property (Name/Str/Enum/Object...) with <paramref name="text"/>.
    /// The length changes, so every ancestor's size field (and nested-archive byte count) is adjusted.
    /// Returns a NEW GvasFile; old node refs are invalid.
    /// </summary>
    public GvasFile SetString(GvasNode node, string text)
    {
        if (!StringTypes.Contains(node.TName)) throw new GvasException($"cannot set string on {node.TName}");
        foreach (var ch in text) if (ch > 127) throw new GvasException("only ASCII strings supported");
        if (node.Size != FStringLen(node)) throw new GvasException("property is not a single FString");
        var raw = Encoding.ASCII.GetBytes(text + "\0");
        var neu = new byte[4 + raw.Length];
        BinaryPrimitives.WriteInt32LittleEndian(neu, raw.Length);
        raw.CopyTo(neu, 4);
        int delta = neu.Length - node.Size;
        var buf = Splice(Data, node.ValueOffset, node.End, neu);
        Grow(buf, node, delta);
        return new GvasFile(buf);
    }

    /// <summary>Append one int32 to an ArrayProperty&lt;IntProperty&gt;; returns a NEW GvasFile.</summary>
    public GvasFile ArrayAppendInt(GvasNode node, int value)
    {
        if (node.TName != "ArrayProperty" || node.Type.Params.Count == 0 || node.Type.Params[0].Name != "IntProperty" || node.Count == null)
            throw new GvasException("not an int array");
        var raw = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(raw, value);
        var buf = Splice(Data, node.End, node.End, raw);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(node.ValueOffset), node.Count.Value + 1);
        Grow(buf, node, 4);
        return new GvasFile(buf);
    }

    /// <summary>
    /// Remove element <paramref name="index"/> of an ArrayProperty of structs; returns a NEW GvasFile.
    /// The array's count drops by one and every ancestor's size field (and nested byte counts) shrink.
    /// </summary>
    public GvasFile ArrayRemoveElement(GvasNode arr, int index)
    {
        if (arr.TName != "ArrayProperty" || arr.Children == null || arr.Count == null || index < 0 || index >= arr.Children.Count)
            throw new GvasException("not a removable array element");
        var el = arr.Children[index];
        if (el.End <= el.Start) throw new GvasException("empty element");
        var buf = Splice(Data, el.Start, el.End, Array.Empty<byte>());
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(arr.ValueOffset), arr.Count.Value - 1);
        Grow(buf, arr, -(el.End - el.Start));
        return new GvasFile(buf);
    }

    /// <summary>Append <paramref name="n"/> elements (raw bytes, concatenated) to an ArrayProperty; returns a NEW GvasFile.</summary>
    public GvasFile ArrayAppendRaw(GvasNode arr, byte[] raw, int n = 1)
    {
        if (arr.TName != "ArrayProperty" || arr.Count == null || raw.Length == 0 || n < 1)
            throw new GvasException("not an appendable array");
        var buf = Splice(Data, arr.End, arr.End, raw);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(arr.ValueOffset), arr.Count.Value + n);
        Grow(buf, arr, raw.Length);
        return new GvasFile(buf);
    }

    /// <summary>Remove entry <paramref name="index"/> of a MapProperty; returns a NEW GvasFile.</summary>
    public GvasFile MapRemoveEntry(GvasNode mp, int index)
    {
        if (mp.TName != "MapProperty" || mp.Children == null || mp.Count == null || index < 0 || index >= mp.Children.Count)
            throw new GvasException("not a removable map entry");
        var el = mp.Children[index];
        var buf = Splice(Data, el.Start, el.End, Array.Empty<byte>());
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(mp.ValueOffset + 4), mp.Count.Value - 1);   // after the (zero) removal count
        Grow(buf, mp, -(el.End - el.Start));
        return new GvasFile(buf);
    }

    /// <summary>Append one fixed-size entry (key + value bytes) to a MapProperty; returns a NEW GvasFile.</summary>
    public GvasFile MapAppendRaw(GvasNode mp, byte[] raw)
    {
        if (mp.TName != "MapProperty" || mp.Count == null || raw.Length == 0)
            throw new GvasException("not an appendable map");
        var buf = Splice(Data, mp.End, mp.End, raw);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(mp.ValueOffset + 4), mp.Count.Value + 1);
        Grow(buf, mp, raw.Length);
        return new GvasFile(buf);
    }

    /// <summary>Replace <c>data[start..end)</c> with <paramref name="insert"/>.</summary>
    static byte[] Splice(byte[] data, int start, int end, byte[] insert)
    {
        var buf = new byte[data.Length - (end - start) + insert.Length];
        Buffer.BlockCopy(data, 0, buf, 0, start);
        Buffer.BlockCopy(insert, 0, buf, start, insert.Length);
        Buffer.BlockCopy(data, end, buf, start + insert.Length, data.Length - end);
        return buf;
    }

    /// <summary>
    /// Add <paramref name="delta"/> to the size field of <paramref name="node"/> and every ancestor (and to the
    /// byte count of any nested archive). Size fields precede the spliced bytes, so earlier offsets stay valid.
    /// </summary>
    static void Grow(byte[] buf, GvasNode node, int delta)
    {
        for (var n = node; n != null; n = n.Parent)
        {
            if (n.SizeOff is int so)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(so), n.Size + delta);
                if (n.Nested)
                {
                    var cur = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(n.ValueOffset));
                    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(n.ValueOffset), cur + delta);
                }
            }
        }
    }

    int FStringLen(GvasNode node)
    {
        int n = BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(node.ValueOffset));
        return 4 + (n < 0 ? 2 * -n : n);
    }

    /// <summary>Stable structural path (names + element indices) to re-find a node.</summary>
    public static string[] PathOf(GvasNode? node)
    {
        var @out = new List<string>();
        for (; node != null; node = node.Parent) @out.Add(node.Name);
        @out.Reverse();
        return @out.ToArray();
    }

    public GvasNode? FindPath(IReadOnlyList<string> path)
    {
        var nodes = Root;
        GvasNode? cur = null;
        foreach (var name in path)
        {
            cur = nodes.FirstOrDefault(n => n.Name == name);
            if (cur == null) return null;
            nodes = cur.Children ?? new List<GvasNode>();
        }
        return cur;
    }

    public byte[] ToBytes() => (byte[])Data.Clone();
}
