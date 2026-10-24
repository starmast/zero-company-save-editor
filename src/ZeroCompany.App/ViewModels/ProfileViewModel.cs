using System.Collections.ObjectModel;
using System.ComponentModel;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>One achievement (or other counter) of the profile: its count, the target, and whether the count reaches it.</summary>
public sealed class AchievementRowViewModel : ViewModelBase
{
    readonly int? _target;

    public AchievementRowViewModel(AchievementView a, FieldSet fields)
    {
        Name = a.Name; _target = a.Target;
        Count = fields.Make(a.Count, 0, a.Name);
        if (Count != null) Count.PropertyChanged += OnCount;
    }

    void OnCount(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableField.Value)) { OnPropertyChanged(nameof(Reached)); OnPropertyChanged(nameof(Progress)); }
    }

    public string Name { get; }
    public EditableField? Count { get; }
    public bool HasCount => Count != null;
    public bool NoCount => Count == null;
    public bool Reached => _target is { } t && Count != null && Count.Value >= t;
    public string Progress => _target is not { } t ? "Not an achievement's counter"
        : Count == null ? $"Target {t} · not counted yet" : Reached ? $"Target {t} · reached" : $"Target {t}";
}

/// <summary>The profile file: achievement counters, the best reward tier per galaxy zone, and the profile's unlocks.</summary>
public sealed class ProfileViewModel : ScreenViewModel
{
    public ProfileViewModel(MainViewModel main, OpenSave open)
    {
        var fields = Own(new FieldSet(main.Session.Pending));
        var p = open.Profile!;
        foreach (var a in p.Achievements) Achievements.Add(new AchievementRowViewModel(a, fields));
        foreach (var (name, best) in p.Zones) Zones.Add(new FieldRowViewModel(name, fields.Make(best, 0, name)!, "-1 = none"));
        foreach (var u in p.Unlocks) Unlocks.Add(u);
        Summary = $"{Achievements.Count(a => a.Reached)} of {Achievements.Count(a => a.Progress.StartsWith("Target", StringComparison.Ordinal))} achievement targets reached · {Unlocks.Count} unlocks";
    }

    public string Summary { get; }
    public ObservableCollection<AchievementRowViewModel> Achievements { get; } = new();
    public ObservableCollection<FieldRowViewModel> Zones { get; } = new();
    public ObservableCollection<string> Unlocks { get; } = new();
    public bool HasZones => Zones.Count > 0;
    public bool HasUnlocks => Unlocks.Count > 0;
}
