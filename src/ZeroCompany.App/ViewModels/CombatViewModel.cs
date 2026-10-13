using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ZeroCompany.Core.Actions;
using ZeroCompany.Core.Save;
using ZeroCompany.Core.Views;

namespace ZeroCompany.App.ViewModels;

/// <summary>Combat: every unit and objective in a combat save, split into sub-tabs (allies, enemies, friendly AI, objectives, reinforcements).</summary>
public sealed class CombatViewModel : ScreenViewModel
{
    /// <summary>The game clamps Health to each unit's real maximum when it loads the save, so a large value is a full heal.</summary>
    public const double FullHealValue = 999;

    readonly PendingChanges _pending;
    readonly CombatView? _combat;
    readonly MainViewModel _main;
    readonly List<UnitCardViewModel> _cards = new();
    string _active = "allies";

    public CombatViewModel(MainViewModel main, OpenSave open)
    {
        _main = main;
        _pending = main.Session.Pending;
        _combat = open.Combat;
        if (_combat == null) return;
        var fields = Own(new FieldSet(_pending));
        var choices = _combat.Units.SelectMany(u => u.Effects.Select(e => (u, e)))
            .GroupBy(x => x.e.Def).Select(g => g.First())
            .OrderBy(x => x.e.Title, StringComparer.Ordinal)
            .Select(x => new EffectChoice($"{x.e.Title} (on {x.u.Name})", x.u.Index, x.e.Index)).ToList();
        foreach (var u in _combat.Units) _cards.Add(Own(new UnitCardViewModel(_pending, fields, _combat, u, choices)));
        foreach (var o in _combat.Objectives) Objectives.Add(Own(new ObjectiveRowViewModel(_pending, fields, o)));
        foreach (var t in _combat.Timers) Timers.Add(new TimerRowViewModel(fields, t));
        if (_combat.TurnStructure != null) Turn = Own(new TurnStructurePicker(_pending, _combat.TurnStructure));
        var rv = _combat.Reinforcements;
        foreach (var w in rv.Waves) Waves.Add(Own(new WaveRowViewModel(_pending, fields, rv, w)));
        foreach (var c in rv.Configs) Configs.Add(Own(new WaveConfigRowViewModel(_pending, fields, rv, c)));
        foreach (var t in rv.Triggers) Triggers.Add(Own(new TriggerRowViewModel(_pending, t)));

        void Tab(string key, string label, int n) { if (n > 0) Tabs.Add(new TabViewModel(key, $"{label} {n}", Select)); }
        foreach (var (key, label) in new[] { ("allies", "Allies"), ("enemies", "Enemies"), ("civilians", "Friendly AI"), ("other", "Other") })
            Tab(key, label, _cards.Count(c => c.TabKey == key));
        Tab("objectives", "Objectives", Objectives.Count);
        Tab("reinforcements", "Reinforcements", rv.Waves.Count + rv.Configs.Count + rv.Triggers.Count);
        var want = main.Memory.CombatTab;
        _active = Tabs.Any(t => t.Key == want) ? want : Tabs.FirstOrDefault()?.Key ?? "allies";
        Summary = string.IsNullOrWhiteSpace(_combat.Title) ? "Combat autosave" : _combat.Title;
        EnemiesTo1Hp = new RelayCommand(() => SetAllHealth("Enemy", 1));
        SquadHeal = new RelayCommand(() => SetAllHealth("Player", FullHealValue));
        Refresh();
    }

    public bool HasCombat => _combat != null;
    public bool NoCombat => _combat == null;
    public string Summary { get; } = "";
    public ObservableCollection<TabViewModel> Tabs { get; } = new();
    /// <summary>Unit cards for the active tab.</summary>
    public ObservableCollection<UnitCardViewModel> Units { get; } = new();
    public ObservableCollection<ObjectiveRowViewModel> Objectives { get; } = new();
    public ObservableCollection<TimerRowViewModel> Timers { get; } = new();
    public ObservableCollection<WaveRowViewModel> Waves { get; } = new();
    public ObservableCollection<WaveConfigRowViewModel> Configs { get; } = new();
    public ObservableCollection<TriggerRowViewModel> Triggers { get; } = new();
    public bool NoWaves => Waves.Count == 0;
    public bool HasTriggers => Triggers.Count > 0;
    public System.Windows.Input.ICommand? EnemiesTo1Hp { get; }
    public System.Windows.Input.ICommand? SquadHeal { get; }

    public string ActiveTab => _active;
    public bool ShowUnits => HasCombat && _active is "allies" or "enemies" or "civilians" or "other";
    public TurnStructurePicker? Turn { get; }
    public bool HasTurn => Turn != null;
    public bool ShowObjectives => HasCombat && _active == "objectives";
    public bool ShowTimers => ShowObjectives && Timers.Count > 0;
    public bool ShowReinforcements => HasCombat && _active == "reinforcements";
    public bool ShowAllyActions => _active == "allies";
    public bool ShowEnemyActions => _active == "enemies";

    void Select(string key)
    {
        _active = key;
        _main.Memory.CombatTab = key;
        Refresh();
    }

    void Refresh()
    {
        foreach (var t in Tabs) t.IsActive = t.Key == _active;
        Units.Clear();
        foreach (var c in _cards.Where(c => c.TabKey == _active)) Units.Add(c);
        foreach (var n in new[] { nameof(ActiveTab), nameof(ShowUnits), nameof(ShowObjectives), nameof(ShowReinforcements), nameof(ShowTimers), nameof(ShowAllyActions), nameof(ShowEnemyActions) })
            OnPropertyChanged(n);
    }

    void SetAllHealth(string team, double health) =>
        _pending.SetEdits(_combat!.Units.Where(u => (team == "Enemy" ? CombatOps.IsEnemyTeam(u.Team) : u.Team == team) && !u.Dead && u.Attr("Health") != null)
                                        .Select(u => (u.Attr("Health")!, health)));
}
