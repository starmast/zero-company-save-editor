using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.ViewModels;

public sealed class SaveCardViewModel
{
    public SaveCardViewModel(SaveEntry e, Bitmap? thumbnail, Action<SaveEntry> open)
    {
        Entry = e;
        OpenCommand = new RelayCommand(() => open(e));
        var i = e.Info;
        Title = i.Title is { Length: > 0 } ? i.Title : e.Name;
        var kind = i.AutoSave is { Length: > 0 } a && a != "None" ? a + " autosave" : "Manual save";
        Kind = i.Turn != null ? $"{kind} · Turn {i.Turn}" : kind;
        FileName = e.Name;
        When = $"{i.MTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {Size(i.Size)}";
        Thumbnail = thumbnail;
    }

    public SaveEntry Entry { get; }
    public System.Windows.Input.ICommand OpenCommand { get; }
    public string Title { get; }
    public string Kind { get; }
    public string FileName { get; }
    public string When { get; }
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail != null;

    static string Size(long b) => b >= 1 << 20 ? $"{b / (1024.0 * 1024.0):0.0} MB" : $"{Math.Max(1, b / 1024)} KB";
}

public sealed class SaveGroupViewModel
{
    public SaveGroupViewModel(string name, string path, List<SaveCardViewModel> cards, string others)
    {
        Name = name; Path = path; Cards = cards; Others = others;
    }

    public string Name { get; }
    public string Path { get; }
    public List<SaveCardViewModel> Cards { get; }
    public string Others { get; }
    public bool HasOthers => Others.Length > 0;
    public bool Empty => Cards.Count == 0;
}

/// <summary>Save picker: cards with the save's own screenshot, in-game name and key facts.</summary>
public sealed partial class SavesViewModel : ScreenViewModel
{
    readonly MainViewModel _main;

    public SavesViewModel(MainViewModel main)
    {
        _main = main;
        Reload();
    }

    public ObservableCollection<SaveGroupViewModel> Groups { get; } = new();
    [ObservableProperty] bool _noFolders;

    static string FolderName(string id) => id switch
    {
        "game" => "Game folder",
        _ when id.StartsWith("proton", StringComparison.Ordinal) => "Proton prefix " + id[6..],
        _ when id.StartsWith("user", StringComparison.Ordinal) => "Added folder " + id[4..],
        _ => id,
    };

    void Reload()
    {
        Groups.Clear();
        var svc = _main.Session.Service;
        var all = svc.ListSaves();
        foreach (var (id, path) in _main.Session.Env.DiscoverSaveDirs(_main.Session.Settings.SaveDirs))
        {
            var rows = all.Where(s => s.Dir == id).ToList();
            var editable = rows.Where(s => s.Info.Editable).ToList();
            var cards = editable.Select(s => new SaveCardViewModel(s, LoadThumb(svc, s), _main.OpenSave)).ToList();
            var others = string.Join(", ", rows.Where(s => !s.Info.Editable).Select(s => s.Name));
            Groups.Add(new SaveGroupViewModel(FolderName(id), path, cards, others.Length > 0 ? "Not editable here: " + others : ""));
        }
        NoFolders = Groups.Count == 0;
    }

    static Bitmap? LoadThumb(SaveService svc, SaveEntry s)
    {
        try
        {
            var bytes = svc.Thumbnail(s.Dir, s.Name);
            return bytes == null ? null : new Bitmap(new MemoryStream(bytes));
        }
        catch (Exception) { return null; }
    }

    [RelayCommand]
    async Task AddFolder()
    {
        var picked = await _main.Ui.PickFolderAsync("Choose a folder that contains Zero Company .sav files");
        if (string.IsNullOrEmpty(picked)) return;
        var s = _main.Session;
        if (!s.Settings.SaveDirs.Contains(picked)) s.Settings.SaveDirs.Add(picked);
        s.SaveSettings();
        s.Refresh();
        Reload();
    }
}
