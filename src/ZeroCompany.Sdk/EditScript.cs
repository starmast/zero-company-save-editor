using System.Globalization;
using System.Text.Json;

namespace ZeroCompany.Sdk;

public sealed record ScriptAction(string Do, IReadOnlyList<string> Args);

/// <summary>
/// A batch of edits as JSON, applied as one verified write:
/// <code>{ "save": "latest", "set": { "resource.Intelligence": 100 }, "actions": [ { "do": "heal", "args": ["Neiya"] } ] }</code>
/// <c>save</c> is optional (the command line can name the save instead). Values may be numbers or strings; args are strings or numbers.
/// </summary>
public sealed record EditScript(string? Save, IReadOnlyList<(string Path, double Value)> Set, IReadOnlyList<ScriptAction> Actions)
{
    public static EditScript Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException e) { throw new ZcException("the edit script is not valid JSON: " + e.Message, e); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ZcException("the edit script must be a JSON object");
            string? save = null;
            var set = new List<(string, double)>();
            var actions = new List<ScriptAction>();
            foreach (var p in root.EnumerateObject())
                switch (p.Name.ToLowerInvariant())
                {
                    case "save": save = p.Value.GetString(); break;
                    case "set":
                        if (p.Value.ValueKind != JsonValueKind.Object) throw new ZcException("\"set\" must be an object of path: value");
                        foreach (var kv in p.Value.EnumerateObject()) set.Add((kv.Name, Number(kv.Value, kv.Name)));
                        break;
                    case "actions":
                        if (p.Value.ValueKind != JsonValueKind.Array) throw new ZcException("\"actions\" must be an array");
                        foreach (var a in p.Value.EnumerateArray()) actions.Add(Action(a));
                        break;
                    default: throw new ZcException($"unknown key \"{p.Name}\" in the edit script (use save, set, actions)");
                }
            return new EditScript(save, set, actions);
        }
    }

    static double Number(JsonElement v, string what) => v.ValueKind switch
    {
        JsonValueKind.Number => v.GetDouble(),
        JsonValueKind.String => SaveEditor.ParseNumber(v.GetString()!),
        _ => throw new ZcException($"the value for \"{what}\" must be a number"),
    };

    static ScriptAction Action(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) throw new ZcException("each action must be an object like { \"do\": \"heal\", \"args\": [\"Neiya\"] }");
        string? name = null;
        var args = new List<string>();
        foreach (var p in a.EnumerateObject())
            switch (p.Name.ToLowerInvariant())
            {
                case "do" or "action": name = p.Value.GetString(); break;
                case "args":
                    if (p.Value.ValueKind != JsonValueKind.Array) throw new ZcException("\"args\" must be an array");
                    foreach (var x in p.Value.EnumerateArray())
                        args.Add(x.ValueKind switch
                        {
                            JsonValueKind.String => x.GetString()!,
                            JsonValueKind.Number => x.GetDouble().ToString("0.###############", CultureInfo.InvariantCulture),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => throw new ZcException("action args must be strings, numbers or booleans"),
                        });
                    break;
                default: throw new ZcException($"unknown key \"{p.Name}\" in an action (use do, args)");
            }
        return new ScriptAction(name ?? throw new ZcException("an action needs \"do\""), args);
    }

    /// <summary>Queue everything on the editor (nothing is written until <see cref="SaveEditor.Apply"/>); returns one line per edit.</summary>
    public List<string> Queue(SaveEditor ed)
    {
        var lines = new List<string>();
        foreach (var (path, value) in Set) lines.Add($"set {ed.Set(path, value).Path} = {SaveEditor.Fmt(value)}");
        foreach (var a in Actions) lines.Add(ed.Do(a.Do, a.Args));
        return lines;
    }
}
