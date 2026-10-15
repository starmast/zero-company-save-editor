using System.Globalization;
using ZeroCompany.Core.Platform;

namespace ZeroCompany.Sdk.Cli;

/// <summary>
/// The command line as the tools read it: flags and options are taken out of the list as they are read, and what is left are the
/// positional arguments. <c>--json</c> and <c>--data-dir</c> are read up front for every command.
/// </summary>
public sealed class CliArgs
{
    readonly List<string> _a;
    ZcWorkspace? _ws;

    public CliArgs(IEnumerable<string> args)
    {
        _a = args.ToList();
        Json = Flag("--json");
        DataDir = Opt("--data-dir");
    }

    /// <summary>Print results (and errors) as JSON.</summary>
    public bool Json { get; }
    public string? DataDir { get; }

    /// <summary>The user's setup, opened on first use (<c>--data-dir</c>, else <c>ZC_DATA_DIR</c>, else the default).</summary>
    public ZcWorkspace Workspace => _ws ??= ZcWorkspace.Open(DataDir);

    /// <summary>Positional arguments left.</summary>
    public IReadOnlyList<string> Rest => _a;
    public int Count => _a.Count;
    public string? First => _a.Count > 0 ? _a[0] : null;

    /// <summary>True (and removed) when any of the names is present.</summary>
    public bool Flag(params string[] names)
    {
        bool found = false;
        foreach (var n in names)
            for (int i = _a.IndexOf(n); i >= 0; i = _a.IndexOf(n)) { _a.RemoveAt(i); found = true; }
        return found;
    }

    /// <summary>The value after <paramref name="name"/> (and both removed), or null when the option is absent.</summary>
    public string? Opt(string name)
    {
        var i = _a.IndexOf(name);
        if (i < 0) return null;
        if (i + 1 >= _a.Count) throw new ZcException($"{name} needs a value");
        var v = _a[i + 1];
        _a.RemoveRange(i, 2);
        return v;
    }

    public int Num(string name, int def)
    {
        var v = Opt(name);
        if (v == null) return def;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new ZcException($"{name} needs a number");
    }

    /// <summary>Remove and return the next positional argument.</summary>
    public string Take(string usage) => _a.Count > 0 ? Pop() : throw new ZcException("usage: " + usage);

    public string? TakeOptional() => _a.Count > 0 ? Pop() : null;

    /// <summary>Remove and return all positional arguments left.</summary>
    public List<string> TakeAll() { var l = _a.ToList(); _a.Clear(); return l; }

    string Pop() { var v = _a[0]; _a.RemoveAt(0); return v; }
}
