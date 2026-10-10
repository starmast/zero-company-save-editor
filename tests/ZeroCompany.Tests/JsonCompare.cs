using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZeroCompany.Tests;

/// <summary>Deep JSON comparison used by the parity tests: nulls and listed keys are dropped, numbers compare by value.</summary>
public static class JsonCompare
{
    public static readonly JsonSerializerOptions Snake = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        IncludeFields = false,
    };

    public static JsonNode? ToNode(object? value) => JsonSerializer.SerializeToNode(value, Snake);

    /// <summary>First difference as a path + message, or null when the two trees match.</summary>
    public static string? Diff(JsonNode? expected, JsonNode? actual, ISet<string>? ignore = null, string path = "$")
    {
        ignore ??= new HashSet<string>();
        if (IsNull(expected) && IsNull(actual)) return null;
        if (expected is JsonObject eo && actual is JsonObject ao)
        {
            foreach (var key in eo.Select(p => p.Key).Union(ao.Select(p => p.Key)))
            {
                if (ignore.Contains(key)) continue;
                var e = eo[key]; var a = ao[key];
                if (IsNull(e) && IsNull(a)) continue;
                var d = Diff(e, a, ignore, $"{path}.{key}");
                if (d != null) return d;
            }
            return null;
        }
        if (expected is JsonArray ea && actual is JsonArray aa)
        {
            if (ea.Count != aa.Count) return $"{path}: array length {ea.Count} != {aa.Count}";
            for (int i = 0; i < ea.Count; i++)
            {
                var d = Diff(ea[i], aa[i], ignore, $"{path}[{i}]");
                if (d != null) return d;
            }
            return null;
        }
        if (expected is JsonValue ev && actual is JsonValue av)
        {
            var ee = JsonSerializer.SerializeToElement(ev);
            var ae = JsonSerializer.SerializeToElement(av);
            if (ee.ValueKind == JsonValueKind.Number && ae.ValueKind == JsonValueKind.Number)
            {
                double x = ee.GetDouble(), y = ae.GetDouble();
                return Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Abs(x)) ? null : $"{path}: {x} != {y}";
            }
            return ee.ValueKind == ae.ValueKind && ee.ToString() == ae.ToString() ? null : $"{path}: '{ee}' != '{ae}'";
        }
        return $"{path}: {Describe(expected)} vs {Describe(actual)}";
    }

    static bool IsNull(JsonNode? n) => n == null || (n is JsonValue v && v.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Null);

    static string Describe(JsonNode? n) => n == null ? "null" : n.ToJsonString().Length > 80 ? n.ToJsonString()[..80] + "…" : n.ToJsonString();
}
