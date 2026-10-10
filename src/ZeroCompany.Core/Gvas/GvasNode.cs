namespace ZeroCompany.Core.Gvas;

/// <summary>One indexed property (or array/map element). Records where its bytes live; never owns them.</summary>
public sealed class GvasNode
{
    public string Name { get; }
    public TypeName Type { get; }
    public int Size { get; set; }                 // value size in bytes (0 for bool)
    public int Start { get; }                     // offset of the property tag
    public int ValueOffset { get; }               // offset of the value bytes
    public int Flags { get; }
    public List<GvasNode>? Children { get; set; } // struct fields / array+map elements
    public bool Opaque { get; set; }              // could not be parsed deeper (still has bytes)
    public int? Count { get; set; }               // array / map element count
    public GvasNode? Parent { get; }
    public bool Native { get; set; }
    public int? SizeOff { get; set; }             // offset of this property's int32 size field
    public bool Nested { get; set; }              // byte array that contains a property list

    public GvasNode(string name, TypeName type, int size, int start, int valueOffset, int flags = 0, GvasNode? parent = null)
    {
        Name = name; Type = type; Size = size; Start = start; ValueOffset = valueOffset; Flags = flags; Parent = parent;
    }

    public string TName => Type.Name;
    public int End => ValueOffset + Size;

    public override string ToString() => $"{Name}: {Type} @{ValueOffset}+{Size}";
}
