using System.Buffers.Binary;
using System.Text;

namespace ZeroCompany.Tests;

/// <summary>Builds tiny GVAS payloads in the UE 5.6 property-tag layout the game's saves use.</summary>
public static class Synth
{
    public static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    public static byte[] I32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); return b; }
    public static byte[] U16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); return b; }

    public static byte[] Fs(string s) { var b = Encoding.ASCII.GetBytes(s + "\0"); return Cat(I32(b.Length), b); }
    public static byte[] Tn(string name, params byte[][] ps) => Cat(Fs(name), I32(ps.Length), Cat(ps));
    public static byte[] Prop(string name, byte[] typ, byte[] value, byte flags = 0) =>
        Cat(Fs(name), typ, I32(value.Length), new[] { flags }, value);

    public static readonly byte[] None = Fs("None");

    public static byte[] IntProp(string n, int v) => Prop(n, Tn("IntProperty"), I32(v));
    public static byte[] StrProp(string n, string s) => Prop(n, Tn("StrProperty"), Fs(s));
    public static byte[] ObjProp(string n, string s) => Prop(n, Tn("ObjectProperty"), Fs(s));
    public static byte[] StructProp(string n, byte[] inner, string sname = "MyStruct") =>
        Prop(n, Tn("StructProperty", Tn(sname)), Cat(inner, None));
    public static byte[] IntArray(string n, params int[] vs) =>
        Prop(n, Tn("ArrayProperty", Tn("IntProperty")), Cat(I32(vs.Length), Cat(vs.Select(I32).ToArray())));
    public static byte[] NestedArchive(string n, byte[] innerProps)
    {
        var inner = Cat(new byte[] { 0 }, innerProps, None);
        return Prop(n, Tn("ArrayProperty", Tn("ByteProperty")), Cat(I32(inner.Length), inner));
    }
    public static byte[] StructArray(string n, IEnumerable<byte[]> elems, string sname = "Elem")
    {
        var l = elems.ToList();
        return Prop(n, Tn("ArrayProperty", Tn("StructProperty", Tn(sname))),
            Cat(I32(l.Count), Cat(l.Select(e => Cat(e, None)).ToArray())));
    }

    public static byte[] Gvas(byte[] body)
    {
        var head = Cat("GVAS"u8.ToArray(), I32(3), I32(522), I32(1017),
            U16(5), U16(6), U16(1), I32(0), Fs("++Test+Stable"), I32(3), I32(0), Fs("/Script/Test.TestSave"), new byte[] { 0 });
        return Cat(head, body, None, new byte[4]);
    }

    public static byte[] Sample()
    {
        var archive = NestedArchive("ArchiveBytes", StructProp("Data", Cat(IntProp("Credits", 1000), StrProp("Title", "Hi"))));
        var wrapper = StructProp("Wrapper", archive, "ObjectWrapper");
        return Gvas(Cat(IntProp("Turn", 7), IntArray("Squad", 1, 2, 3), wrapper, StrProp("Name", "Alpha")));
    }

    public static byte[] EffectsSample()
    {
        var elems = "ABC".Select((n, i) => Cat(ObjProp("Def", $"/Game/GE_{n}.GE_{n}"), IntProp("StackCount", i + 1)));
        var archive = NestedArchive("ArchiveBytes", StructProp("Data", Cat(StructArray("Effects", elems), IntProp("After", 5))));
        return Gvas(Cat(IntProp("Turn", 3), StructProp("Wrapper", archive, "ObjectWrapper"), StrProp("Name", "Alpha")));
    }
}

public static class Repo
{
    public static string Root { get; } = FindRoot();
    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ZeroCompany.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>The local sample save, or null (tests then skip).</summary>
    public static string? SampleSave()
    {
        var dir = Path.Combine(Root, "saves");
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "HUB_Root*.sav").OrderBy(x => x).FirstOrDefault()
            : null;
    }
}
