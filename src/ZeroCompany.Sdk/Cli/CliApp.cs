using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZeroCompany.Core.Gvas;
using ZeroCompany.Core.Save;
using ZeroCompany.GameData.Extractor;

namespace ZeroCompany.Sdk.Cli;

/// <summary>The same start-up and error handling for every tool: help, UTF-8 output, and "tool: message" with an exit code.</summary>
public static class CliApp
{
    public const int Ok = 0, UserError = 1, Refused = 2;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Run <paramref name="body"/> with the arguments. Prints <paramref name="usage"/> for no arguments or -h/--help/help. Failures the user
    /// can act on print one line to stderr (a JSON object on stdout with <c>--json</c>) and return 1; a <see cref="ZcRefusal"/> returns 2.
    /// </summary>
    public static int Run(string name, string usage, string[] args, Func<CliArgs, int> body)
    {
        Console.OutputEncoding = new UTF8Encoding(false);          // game text has curly quotes and accents
        CliArgs a;
        try { a = new CliArgs(args); }
        catch (ZcException e) { Console.Error.WriteLine($"{name}: {e.Message}"); return UserError; }
        if (a.Count == 0 || a.First is "-h" or "--help" or "help") { Console.WriteLine(usage); return Ok; }
        try { return body(a); }
        catch (ZcRefusal e) { return Fail(name, e.Message, Refused, a.Json); }
        catch (Exception e) when (e is ZcException or EditException or GvasException or SaveFormatException or IOException or InvalidDataException or ExtractionException or UnauthorizedAccessException)
        {
            return Fail(name, e.Message, UserError, a.Json);
        }
    }

    static int Fail(string name, string message, int code, bool json)
    {
        if (json) Console.WriteLine(Serialize(new { error = message, exitCode = code }));
        else Console.Error.WriteLine($"{name}: {message}");
        return code;
    }

    public static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOpts);

    public static void WriteJson(object value) => Console.WriteLine(Serialize(value));
}
