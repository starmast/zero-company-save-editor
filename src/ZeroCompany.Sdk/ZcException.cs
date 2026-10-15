namespace ZeroCompany.Sdk;

/// <summary>A failure the user can act on (no save matches, an ambiguous name, a missing folder). Nothing was written.</summary>
public class ZcException : Exception
{
    public ZcException(string message, Exception? inner = null) : base(message, inner) { }
}
