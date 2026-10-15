namespace ZeroCompany.Sdk;

public enum CheckStatus { Ok, Warn, Fail }

/// <summary>One line of a health report (<see cref="ZcDoctor"/> for the setup, <see cref="SaveCheck"/> for a save).</summary>
public sealed record Check(string Name, CheckStatus Status, string Detail, string? Fix = null)
{
    public static Check Ok(string name, string detail) => new(name, CheckStatus.Ok, detail);
    public static Check Warn(string name, string detail, string? fix = null) => new(name, CheckStatus.Warn, detail, fix);
    public static Check Fail(string name, string detail, string? fix = null) => new(name, CheckStatus.Fail, detail, fix);
}

public static class CheckExtensions
{
    public static bool HasFailure(this IEnumerable<Check> checks) => checks.Any(c => c.Status == CheckStatus.Fail);
}
