using System.Buffers.Binary;
using System.Text;

namespace ZeroCompany.Core.Gvas;

/// <summary>Bounds-checked little-endian cursor over a byte buffer.</summary>
public sealed class ByteReader
{
    public byte[] D { get; }
    public int P { get; set; }

    public ByteReader(byte[] data, int pos = 0) { D = data; P = pos; }

    public byte U8()
    {
        if (P >= D.Length) throw new GvasException("read past end");
        return D[P++];
    }

    public int I32()
    {
        if (P < 0 || P + 4 > D.Length) throw new GvasException("read past end");
        var v = BinaryPrimitives.ReadInt32LittleEndian(D.AsSpan(P, 4));
        P += 4;
        return v;
    }

    public void Skip(int n)
    {
        if (n < 0 || (long)P + n > D.Length) throw new GvasException("skip out of range");
        P += n;
    }

    public string FString()
    {
        int n = I32();
        if (n == 0) return "";
        if (n < 0)
        {
            n = -n;
            if ((long)P + 2L * n > D.Length) throw new GvasException("bad utf16 string");
            var s = Encoding.Unicode.GetString(D, P, 2 * n - 2);
            P += 2 * n;
            return s;
        }
        if (n > 4096 || (long)P + n > D.Length) throw new GvasException($"bad string length {n} at {P}");
        var u = Encoding.UTF8.GetString(D, P, n - 1);
        P += n;
        return u;
    }

    public TypeName TypeName(int depth = 0)
    {
        if (depth > 8) throw new GvasException("type tree too deep");
        var name = FString();
        int n = I32();
        if (n < 0 || n > 8) throw new GvasException($"bad type param count {n}");
        var ps = new List<TypeName>(n);
        for (int i = 0; i < n; i++) ps.Add(TypeName(depth + 1));
        return new TypeName(name, ps);
    }
}
