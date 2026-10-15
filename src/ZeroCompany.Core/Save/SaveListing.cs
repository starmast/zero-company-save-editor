using System.Text.RegularExpressions;

namespace ZeroCompany.Core.Save;

/// <summary>Cheap listing of the saves in a set of folders; the one implementation behind the editor, <c>zc</c> and <c>zcdebug</c>.</summary>
public static class SaveListing
{
    public static readonly Regex SafeName = new(@"^[A-Za-z0-9._ \-+()]+\.sav$", RegexOptions.Compiled);

    /// <summary>Every <c>.sav</c> in each folder (id -> path), newest first within a folder. Reads only each save's small info entry.</summary>
    public static List<SaveEntry> List(IReadOnlyDictionary<string, string> dirs)
    {
        var @out = new List<SaveEntry>();
        foreach (var (id, dir) in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var fi in new DirectoryInfo(dir).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc))
                if (SafeName.IsMatch(fi.Name)) @out.Add(new SaveEntry(id, fi.Name, SaveContainer.Summarize(fi.FullName)));
        }
        return @out;
    }
}
