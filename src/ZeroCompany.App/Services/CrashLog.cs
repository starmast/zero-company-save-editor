namespace ZeroCompany.App.Services;

/// <summary>Appends unexpected exceptions to crash.log in the app's data folder, so a bug report can include them.</summary>
public static class CrashLog
{
    /// <summary>Write <paramref name="e"/> to the log; returns the log's path, or null when it could not be written.</summary>
    public static string? Write(string dataDir, Exception e)
    {
        try
        {
            var path = Path.Combine(dataDir, "crash.log");
            File.AppendAllText(path, $"---- {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e}\n");
            return path;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return null; }
    }
}
