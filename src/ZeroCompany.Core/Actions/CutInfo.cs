namespace ZeroCompany.Core.Actions;

/// <summary>Everything a structural action deliberately changed, so the verifier can prove nothing else did.</summary>
public sealed class CutInfo
{
    /// <summary>Path prefixes (see PathKey) under which differences are expected (removed/added elements).</summary>
    public HashSet<string> Prefixes { get; } = new();
    public int OpaqueRemoved { get; set; }
    /// <summary>Expected FactTags after a Coil removal.</summary>
    public List<string>? FactTags { get; set; }
    /// <summary>GUIDs brought back from the fallen.</summary>
    public List<string> Revived { get; } = new();
    public List<(string Guid, Dictionary<string, List<byte[]>> Expected)> Focus { get; } = new();
    /// <summary>Expected roster order after a reorder.</summary>
    public List<string>? Roster { get; set; }
}
