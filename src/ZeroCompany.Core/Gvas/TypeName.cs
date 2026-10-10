namespace ZeroCompany.Core.Gvas;

/// <summary>A UE 5.6 FPropertyTypeName tree, e.g. <c>ArrayProperty&lt;StructProperty&lt;Guid&gt;&gt;</c>.</summary>
public sealed class TypeName
{
    public string Name { get; }
    public IReadOnlyList<TypeName> Params { get; }

    public TypeName(string name, IReadOnlyList<TypeName>? @params = null)
    {
        Name = name;
        Params = @params ?? Array.Empty<TypeName>();
    }

    public override string ToString() =>
        Params.Count == 0 ? Name : $"{Name}<{string.Join(",", Params)}>";
}
