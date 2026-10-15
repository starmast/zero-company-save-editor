using System.Diagnostics;

namespace ZeroCompany.Core.Platform;

/// <summary>The running game, if any: the one place that decides which processes count as Zero Company.</summary>
public static class GameProcess
{
    static readonly string[] Hints = { "swzerocompany", "bruno" };

    public sealed record Info(string Name, int Id, DateTime? Started);

    /// <summary>Every running game process (its launcher and shipping exe); empty when none or when processes cannot be listed.</summary>
    public static IReadOnlyList<Info> Running()
    {
        var list = new List<Info>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var name = p.ProcessName;
                    if (!Hints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase))) continue;
                    DateTime? start = null;
                    try { start = p.StartTime; } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                    list.Add(new Info(name, p.Id, start));
                }
                catch (InvalidOperationException) { }
                finally { p.Dispose(); }
            }
        }
        catch (Exception) { }
        return list;
    }

    public static bool IsRunning => Running().Count > 0;

    /// <summary>When the game started (the earliest of its processes), or null when it is not running or cannot be asked.</summary>
    public static DateTime? StartedAt => Running().Select(p => p.Started).Where(s => s != null).Min();

    /// <summary>Lower-cased name of a running game process, or null.</summary>
    public static string? Name => Running().FirstOrDefault()?.Name.ToLowerInvariant();
}
