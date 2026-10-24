using System.Collections.ObjectModel;
using System.ComponentModel;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>
/// One achievement (or other counter) of the profile: its count, the target, and whether the count reaches it. An achievement the
/// profile does not count yet can be started at a count typed into <see cref="StartText"/>.
/// </summary>
public sealed class AchievementRowViewModel : ViewModelBase, IDisposable
{
    readonly int? _target;
    readonly PendingChanges _pending;
    readonly string _stat;
    string _startText = "";

    public AchievementRowViewModel(AchievementView a, FieldSet fields, PendingChanges pending)
    {
        Name = a.Name; _target = a.Target; _pending = pending; _stat = a.Stat;
        Count = fields.Make(a.Count, 0, a.Name);
        if (Count != null) Count.PropertyChanged += OnCount;
        else _pending.Changed += OnPendingChanged;
    }

    void OnCount(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableField.Value)) { OnPropertyChanged(nameof(Reached)); OnPropertyChanged(nameof(Progress)); }
    }

    void OnPendingChanged()
    {
        // the queue was cleared elsewhere (Discard, saving): show what is queued now
        long? queued = _pending.ProfileCounterAdds.TryGetValue(_stat, out var n) ? n : null;
        if (queued != Typed && !(queued == null && IsInvalid)) { _startText = queued?.ToString() ?? ""; OnPropertyChanged(nameof(StartText)); }
        OnPropertyChanged(nameof(IsQueued)); OnPropertyChanged(nameof(IsInvalid)); OnPropertyChanged(nameof(Progress));
    }

    public string Name { get; }
    public EditableField? Count { get; }
    public bool HasCount => Count != null;
    /// <summary>An achievement of the game the profile has no counter for yet.</summary>
    public bool CanStart => Count == null && _target != null;
    public bool NoCount => Count == null && _target == null;
    public bool IsQueued => _pending.ProfileCounterAdds.ContainsKey(_stat);
    public bool IsInvalid => _startText.Trim() != "" && Typed == null;
    long? Typed => long.TryParse(_startText.Trim(), out var n) && n >= 0 ? n : null;

    /// <summary>The count to start the counter at; a whole number of 0 or more queues it, an empty box takes it back out.</summary>
    public string StartText
    {
        get => _startText;
        set
        {
            _startText = value ?? "";
            _pending.SetProfileCounterAdd(_stat, Typed);
            OnPropertyChanged(nameof(StartText)); OnPropertyChanged(nameof(IsInvalid));
        }
    }

    public bool Reached => _target is { } t && Count != null && Count.Value >= t;
    public string Progress => _target is not { } t ? "Not an achievement's counter"
        : Count == null ? (IsQueued ? $"Target {t} · starts at {_pending.ProfileCounterAdds[_stat]} when saved" : $"Target {t} · not counted yet")
        : Reached ? $"Target {t} · reached" : $"Target {t}";

    public void Dispose()
    {
        if (Count != null) Count.PropertyChanged -= OnCount; else _pending.Changed -= OnPendingChanged;
    }
}

/// <summary>The profile file: achievement counters, the best reward tier per galaxy zone, and the profile's unlocks.</summary>
public sealed class ProfileViewModel : ScreenViewModel
{
    public ProfileViewModel(MainViewModel main, OpenSave open)
    {
        var fields = Own(new FieldSet(main.Session.Pending));
        var p = open.Profile!;
        foreach (var a in p.Achievements) Achievements.Add(Own(new AchievementRowViewModel(a, fields, main.Session.Pending)));
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
