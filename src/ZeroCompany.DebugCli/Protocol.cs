using System.Text;

namespace ZeroCompany.DebugCli;

/// <summary>
/// The line protocol between zcdebug and the ZcDebug Lua mod. Lua has no JSON library, so both sides use one tab-separated line:
/// <c>id TAB command TAB args</c> for a request and <c>id TAB ok|err TAB text</c> for a reply. Backslash, newline, carriage return
/// and tab inside a field are escaped as <c>\\ \n \r \t</c>. A line is complete only when it ends with a newline, so a half-written
/// file is never mistaken for an answer.
/// </summary>
public static class Protocol
{
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        return sb.ToString();
    }

    public static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            char n = s[++i];
            sb.Append(n switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => n });
        }
        return sb.ToString();
    }

    public static string FormatRequest(string id, string command, string args) => $"{id}\t{command}\t{Escape(args)}\n";

    /// <summary>Parse a request line (the mod does this in Lua; here for tests and fakes).</summary>
    public static bool TryParseRequest(string text, out string id, out string command, out string args)
    {
        id = command = args = "";
        if (!text.EndsWith('\n')) return false;
        var p = text.TrimEnd('\n', '\r').Split('\t', 3);
        if (p.Length < 2) return false;
        id = p[0]; command = p[1]; args = p.Length > 2 ? Unescape(p[2]) : "";
        return id.Length > 0;
    }

    public static string FormatReply(string id, bool ok, string text) => $"{id}\t{(ok ? "ok" : "err")}\t{Escape(text)}\n";

    /// <summary>Parse a complete reply line; false while the file is missing the final newline or is malformed.</summary>
    public static bool TryParseReply(string text, out string id, out Reply reply)
    {
        id = ""; reply = new Reply(false, "");
        if (!text.EndsWith('\n')) return false;
        var p = text.TrimEnd('\n', '\r').Split('\t', 3);
        if (p.Length < 2 || (p[1] != "ok" && p[1] != "err")) return false;
        id = p[0];
        reply = new Reply(p[1] == "ok", p.Length > 2 ? Unescape(p[2]) : "");
        return id.Length > 0;
    }
}

/// <summary>What the mod answered.</summary>
public sealed record Reply(bool Ok, string Text);
