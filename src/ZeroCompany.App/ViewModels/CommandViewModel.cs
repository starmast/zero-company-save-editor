using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Save;

namespace ZeroCompany.App.ViewModels;

public sealed class FieldRowViewModel
{
    public FieldRowViewModel(string label, EditableField field, string note = "")
    {
        Label = label; Field = field; Note = note;
    }

    public string Label { get; }
    public string Note { get; }
    public bool HasNote => Note.Length > 0;
    public EditableField Field { get; }
}

public sealed class BackupRowViewModel
{
    readonly CommandViewModel _owner;

    public BackupRowViewModel(CommandViewModel owner, BackupInfo b)
    {
        _owner = owner;
        File = b.File; IsOriginal = b.Original;
        Detail = $"{b.MTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {(b.Size >= 1 << 20 ? $"{b.Size / (1024.0 * 1024.0):0.0} MB" : $"{Math.Max(1, b.Size / 1024)} KB")}";
        RestoreCommand = new AsyncRelayCommand(() => _owner.RestoreAsync(File));
    }

    public string File { get; }
    public bool IsOriginal { get; }
    public string Detail { get; }
    public IAsyncRelayCommand RestoreCommand { get; }
}

/// <summary>Command: campaign overview, progression, stockpile, save info and backups (the "hub" screen).</summary>
public sealed class CommandViewModel : ScreenViewModel
{
    readonly MainViewModel _main;

    public CommandViewModel(MainViewModel main, OpenSave open)
    {
        _main = main;
        var fields = Own(new FieldSet(main.Session.Pending));
        var v = open.View;
        var info = v.Command.Save;
        Title = info.Title?.ToString() is { Length: > 0 } t ? t : open.Name;
        Where = $"{open.Name} ({open.DirId})";
        var kind = info.Type?.ToString() == "AutoSave" ? "Autosave" : "Manual save";
        Line1 = string.Join(" · ", new[] { info.World?.ToString(), info.Mode?.ToString(), kind }.Where(s => !string.IsNullOrEmpty(s)));
        bool perma = info.Permadeath?.ToString() == "true";
        Line2 = $"Difficulty level {info.Difficulty?.ToString() ?? "?"}{(perma ? " · Permadeath on" : "")}";
        Line3 = $"Created {info.Created?.ToString() ?? "?"}";
        Summary = $"{v.Personnel.Roster.Count} operators on the roster · {v.Personnel.Memorial.Count} in the Memorial · "
                  + $"{open.Upgrades.Items.Count(u => u.Status == "Completed")} base upgrades completed.";

        var p = v.Command.Progression;
        void Add(string key, string label, int offset, string note)
        {
            if (p.TryGetValue(key, out var r) && r != null) Campaign.Add(new FieldRowViewModel(label, fields.Make(r, offset, label)!, note));
        }
        Add("turn", "Strategy turn", 0, "The save's info file keeps its own copy, which is not updated.");
        Add("roster_level", "Roster level (LV)", 1, "Shown as LV in the game; Den Level is the same scale.");
        Add("roster_xp", "Roster XP", 0, "");
        Add("base_focus", "Base total focus points", 0, "Raised by the Crew Focus upgrades.");
        foreach (var r in v.Header.Resources)
            Stockpile.Add(new FieldRowViewModel(r.Label, fields.Make(r, 0, r.Label)!, r.Description));
        foreach (var b in main.Session.Service.ListBackups(open.Name)) Backups.Add(new BackupRowViewModel(this, b));
    }

    public string Title { get; }
    public string Where { get; }
    public string Line1 { get; }
    public string Line2 { get; }
    public string Line3 { get; }
    public string Summary { get; }
    public List<FieldRowViewModel> Campaign { get; } = new();
    public List<FieldRowViewModel> Stockpile { get; } = new();
    public List<BackupRowViewModel> Backups { get; } = new();

    public Task RestoreAsync(string file) => _main.RestoreAsync(file, _main.PendingBar.Force);
}
