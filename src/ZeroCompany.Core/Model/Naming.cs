using System.Text;
using System.Text.RegularExpressions;
using ZeroCompany.Core.Gvas;

namespace ZeroCompany.Core.Model;

public static class Naming
{
    static readonly Regex CamelBreak = new(@"([a-z0-9])([A-Z])", RegexOptions.Compiled);
    static readonly Regex FirstRx = new(@"Name_First_([A-Za-z0-9]+)", RegexOptions.Compiled);
    static readonly Regex LastRx = new(@"Name_Last_([A-Za-z0-9]+)", RegexOptions.Compiled);
    static readonly Regex HeroRx = new(@"^Char_Hero_(.+?)_C(?:_(\d+))?$", RegexOptions.Compiled);

    public static readonly IReadOnlyDictionary<string, string> KnownNames = new Dictionary<string, string>
    {
        ["JaeMordant"] = "Jae Mordant", ["KabbUppercut"] = "Kabb Uppercut", ["Trick"] = "Trick",
        ["HAWKS"] = "Hawks", ["Astromech_BR-1"] = "BR-1", ["Aurelio"] = "Aurelio", ["TelRea"] = "Tel-Rea",
        ["ClyKullervo"] = "Cly Kullervo",
    };

    /// <summary>"SomeAssetName_Foo" -> "Some Asset Name Foo".</summary>
    public static string Pretty(string s) => CamelBreak.Replace(s.Replace("_", " "), "$1 $2").Trim();

    /// <summary>Last path segment without the object suffix: "/Game/X/Foo.Foo_C" -> "Foo".</summary>
    public static string AssetName(string path)
    {
        var last = path[(path.LastIndexOf('/') + 1)..];
        int dot = last.IndexOf('.');
        return dot < 0 ? last : last[..dot];
    }

    public static string FieldId(GvasNode node) => $"o{node.ValueOffset}";

    /// <summary>Custom operators store their name as localisation keys (Name_First_Neiya ...).</summary>
    public static string? RealName(GvasFile g, GvasNode wrapper)
    {
        var raw = Encoding.Latin1.GetString(g.Data, wrapper.Start, wrapper.End - wrapper.Start);
        var a = FirstRx.Match(raw);
        if (!a.Success) return null;
        var b = LastRx.Match(raw);
        return b.Success ? $"{a.Groups[1].Value} {b.Groups[1].Value}" : a.Groups[1].Value;
    }

    /// <summary>Operator name derived from a class name like "Char_Hero_Aurelio_C".</summary>
    public static string DisplayName(string cls)
    {
        var m = HeroRx.Match(cls);
        if (!m.Success) return string.IsNullOrEmpty(cls) ? "Unknown" : cls;
        var body = m.Groups[1].Value;
        if (body == "Humanoid")
            return m.Groups[2].Success ? $"Custom Operator {int.Parse(m.Groups[2].Value) + 1}" : "Custom Operator";
        var first = body.Split('_')[0];
        var key = KnownNames.ContainsKey(first) ? first : body;
        return KnownNames.TryGetValue(key, out var n) ? n
             : KnownNames.TryGetValue(body, out var n2) ? n2 : Pretty(body);
    }
}
