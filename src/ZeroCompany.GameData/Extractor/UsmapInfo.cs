using System.Text.RegularExpressions;

namespace ZeroCompany.GameData.Extractor;

public enum UsmapStatus { Missing, NotFound, NotAMappingsFile, Ok, OkWithWarning }

/// <summary>
/// What we can tell about a .usmap mappings file before spending time on an extraction: that it exists, that it really is a
/// mappings file (magic 0x30C4), and which Unreal Engine version it was made for (read from the community naming scheme,
/// e.g. <c>SWZeroCompany-5.6.1-196320+++ProjectBruno+Stable-a1e7f571.usmap</c>).
/// </summary>
public sealed record UsmapInfo(UsmapStatus Status, string Message, string? EngineVersion = null, string? Changelist = null)
{
    /// <summary>The Unreal Engine version the game's files are read as.</summary>
    public const string ExpectedEngine = "5.6";

    public bool IsUsable => Status is UsmapStatus.Ok or UsmapStatus.OkWithWarning;

    static readonly Regex NameVersion = new(@"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?-(\d+)(?!\d)", RegexOptions.Compiled);

    public const string WhereToGetIt =
        "A .usmap is a community-made description of the game's data layout for your game version. Search for "
        + "\"Star Wars Zero Company Unreal Mappings\" on Nexus Mods, download it (unzip it if it came as an archive), then pick the .usmap file here.";

    public static UsmapInfo Inspect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new UsmapInfo(UsmapStatus.Missing, "No mappings file chosen yet. " + WhereToGetIt);
        if (!File.Exists(path))
            return new UsmapInfo(UsmapStatus.NotFound, "That file doesn't exist. Pick the .usmap again.");
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[2];
            if (f.Read(head, 0, 2) < 2 || head[0] != 0xC4 || head[1] != 0x30)
                return new UsmapInfo(UsmapStatus.NotAMappingsFile,
                    "That doesn't look like a .usmap mappings file (its header is wrong). Make sure you picked the .usmap itself, "
                    + "not an archive, text file or something else; if you downloaded a .zip, unzip it first.");
        }
        catch (IOException e)
        {
            return new UsmapInfo(UsmapStatus.NotFound, "That file can't be read: " + e.Message);
        }

        var m = NameVersion.Match(Path.GetFileName(path));
        if (!m.Success) return new UsmapInfo(UsmapStatus.Ok, "Mappings file found.");
        var engine = $"{m.Groups[1].Value}.{m.Groups[2].Value}" + (m.Groups[3].Success ? "." + m.Groups[3].Value : "");
        var cl = m.Groups[4].Value;
        if ($"{m.Groups[1].Value}.{m.Groups[2].Value}" != ExpectedEngine)
            return new UsmapInfo(UsmapStatus.OkWithWarning,
                $"This file is for Unreal Engine {engine}, but the game uses {ExpectedEngine}. It probably won't read the data; get the mappings made for your game version.",
                engine, cl);
        return new UsmapInfo(UsmapStatus.Ok, $"Mappings for Unreal Engine {engine} (game build {cl}).", engine, cl);
    }

    /// <summary>Shown when the game opened but nothing useful came out: the classic symptom of mappings for another game version.</summary>
    public const string NothingReadable =
        "The game's files were opened but no upgrade data could be read. This almost always means the mappings file is for a "
        + "different version of the game (for example after a game update). Get the newest .usmap for your game version and try again.";
}
