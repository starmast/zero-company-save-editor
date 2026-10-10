using System.Text;

namespace ZeroCompany.Core.Gvas;

public static class GvasExtensions
{
    public static GvasNode? Child(this GvasFile g, GvasNode? node, string name) => GvasFile.Child(node, name);

    public static long GetInt(this GvasFile g, GvasNode n) => Convert.ToInt64(g.Get(n));
    public static double GetDouble(this GvasFile g, GvasNode n) => Convert.ToDouble(g.Get(n));
    public static string GetStr(this GvasFile g, GvasNode n) => (string)g.Get(n);

    /// <summary>16-byte GUID as the game's four little-endian uint32 words, 32 upper-case hex chars.</summary>
    public static string GuidHex(this GvasFile g, GvasNode n)
    {
        var d = g.Data; int o = n.ValueOffset;
        var sb = new StringBuilder(32);
        for (int i = 0; i < 4; i++) sb.Append(BitConverter.ToUInt32(d, o + 4 * i).ToString("X8"));
        return sb.ToString();
    }

    public static byte[] Slice(this GvasFile g, int start, int end) => g.Data[start..end];

    /// <summary>The top-level GameInstanceSaveGameWrapper node, or null.</summary>
    public static GvasNode? GameInstance(this GvasFile g) => g.Root.FirstOrDefault(w => w.Name == "GameInstanceSaveGameWrapper");

    /// <summary>GameInstanceSaveGameWrapper.ArchiveBytes.StrategyData, or null.</summary>
    public static GvasNode? StrategyDataOrNull(this GvasFile g) =>
        g.Child(g.Child(g.GameInstance(), "ArchiveBytes"), "StrategyData");

    public static GvasNode StrategyData(this GvasFile g) =>
        g.StrategyDataOrNull() ?? throw new GvasException("StrategyData not found - unsupported save layout");
}

/// <summary>Structural node paths as a single comparable string (names and element indices joined by U+001F).</summary>
public static class PathKey
{
    public const char Sep = '\u001f';

    public static string Of(GvasFile _, GvasNode node) => Of(GvasFile.PathOf(node));
    public static string Of(IEnumerable<string> parts) => string.Join(Sep, parts);
    public static string Of(IEnumerable<string> parts, params string[] tail) => string.Join(Sep, parts.Concat(tail));
    public static string Parent(string key) { int i = key.LastIndexOf(Sep); return i < 0 ? "" : key[..i]; }
    /// <summary>True when <paramref name="key"/> equals or lies below <paramref name="prefix"/>.</summary>
    public static bool HasPrefix(string key, string prefix) =>
        key == prefix || key.StartsWith(prefix + Sep, StringComparison.Ordinal);
}
