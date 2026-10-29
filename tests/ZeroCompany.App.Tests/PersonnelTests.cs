using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ZeroCompany.App.ViewModels;
using ZeroCompany.App.Views;

namespace ZeroCompany.App.Tests;

public class PersonnelTests
{
    static TestRig Open(string screen, params string[] args)
    {
        Skip.If(Repo.SampleSave() == null, "no sample save");
        var rig = new TestRig();
        rig.Start();
        Headless.Run(() => rig.Main.Navigate(screen, args));
        return rig;
    }

    static PersonnelViewModel Vm(TestRig rig) => (PersonnelViewModel)rig.Main.Screen!;

    [SkippableFact]
    public void Overview_bonds_and_focus_tabs_render()
    {
        using var rig = Open("personnel");
        var vm = Assert.IsType<PersonnelViewModel>(rig.Main.Screen);
        Assert.NotEmpty(vm.Live);
        Assert.IsType<OverviewTabViewModel>(vm.Content);
        rig.Shot("20-personnel-overview");
        var guid = vm.SelectedGuid;
        Headless.Run(() => rig.Main.Navigate("personnel", guid, "bonds"));
        var bonds = Assert.IsType<BondsTabViewModel>(Vm(rig).Content);
        Assert.Equal(9, bonds.Columns.Count);
        rig.Shot("21-personnel-bonds");
        Headless.Run(() => rig.Main.Navigate("personnel", guid, "focus"));
        Assert.IsType<FocusTabViewModel>(Vm(rig).Content);
        rig.Shot("22-personnel-focus");
    }

    static LoadoutTabViewModel Loadout(TestRig rig, string guid)
    {
        Headless.Run(() => rig.Main.Navigate("personnel", guid, "loadout"));
        return Assert.IsType<LoadoutTabViewModel>(Vm(rig).Content);
    }

    [SkippableFact]
    public void Loadout_tab_shows_the_weapon_and_a_copy_goes_into_the_pending_set_and_a_working_weapon_asks_first()
    {
        using var rig = Open("personnel");
        var view = rig.Main.Session.Current!.View;
        Skip.If(view.Weapons.Rows.Count == 0, "no operator in the sample save has a weapon");
        var first = Loadout(rig, view.Weapons.Rows[0].Guid);
        Assert.NotNull(first.Weapon);
        Assert.All(first.Weapon!.Parts, p => Assert.False(string.IsNullOrEmpty(p.Name)));
        rig.Shot("23-personnel-loadout");

        var damaged = view.Weapons.Rows.FirstOrDefault(r => r.Damaged && r.Donors.Count > 0);
        if (damaged != null)
        {
            var w = Loadout(rig, damaged.Guid).Weapon!;
            Headless.Run(() => w.Toggle.Execute(null));
            Assert.Equal(1, rig.Main.Session.Pending.Count);
            Assert.True(w.IsQueued);
            Headless.Run(() => w.Toggle.Execute(null));
            Assert.Equal(0, rig.Main.Session.Pending.Count);
        }

        var working = view.Weapons.Rows.FirstOrDefault(r => !r.Damaged && r.Donors.Count > 0);
        Skip.If(working == null, "no working weapon with a donor in the sample save");
        var ww = Loadout(rig, working!.Guid).Weapon!;
        Headless.Run(() => ww.Toggle.Execute(null));
        Assert.True(ww.Confirming);                                         // a weapon that works is only replaced after a confirmation
        Assert.Equal(0, rig.Main.Session.Pending.Count);
        Headless.Run(() => ww.Cancel.Execute(null));
        Assert.False(ww.Confirming);
        Headless.Run(() => ww.Toggle.Execute(null));
        Headless.Run(() => ww.Confirm.Execute(null));
        Assert.True(ww.IsQueued);
        Assert.Equal(1, rig.Main.Session.Pending.Count);
        Headless.Run(() => ww.Toggle.Execute(null));
        Assert.Equal(0, rig.Main.Session.Pending.Count);
    }

    [SkippableFact]
    public void Loadout_tab_picks_gear_for_a_slot_and_taking_an_item_from_another_operator_is_shown()
    {
        using var rig = Open("personnel");
        var view = rig.Main.Session.Current!.View;
        var held = view.Loadout.Items.FirstOrDefault(i => i.Type == "utility" && i.HeldBy.Length > 0);
        Skip.If(held == null, "no utility item is held in the sample save");
        var target = view.Loadout.Slots.FirstOrDefault(kv => kv.Key != held!.HeldBy && kv.Value.Any(s => s.Type == "utility")).Key;
        Skip.If(target == null, "no second operator with a utility slot");
        var tab = Loadout(rig, target!);
        var row = tab.Slots.First(s => s.Label.StartsWith("Utility"));
        var choice = row.Choices.First(c => c.Guid == held!.Guid);
        Assert.Contains("on ", choice.Label);                                // says who has it now
        Headless.Run(() => row.Selected = choice);
        Assert.Equal(1, rig.Main.Session.Pending.Count);
        Assert.True(row.IsQueued);
        Assert.Contains(rig.Main.Session.Pending.Describe(rig.Main.Session.Current!), l => l.Text != null && l.Text.Contains("takes it from"));
        Headless.Run(() => row.Selected = row.Choices[0]);                   // Empty: the slot is empty in the save, or this queues emptying it
        Headless.Run(() => rig.Main.Session.Pending.Discard());
        Assert.Equal(0, rig.Main.Session.Pending.Count);
    }

    [SkippableFact]
    public void Editing_unspent_focus_moves_the_total_with_it()
    {
        using var rig = Open("personnel");
        var op = rig.Main.Session.Current!.View.Personnel.Roster.First(o => o.Focus != null);
        Headless.Run(() => rig.Main.Navigate("personnel", op.Guid, "overview"));
        var tab = Assert.IsType<OverviewTabViewModel>(Vm(rig).Content);
        var before = op.Focus!.Total ?? 0;
        Headless.Run(() => tab.Unspent!.Step(3));
        Assert.Equal((before + 3).ToString("N0"), tab.TotalText);
        Assert.Equal((op.Focus.Value + 3).ToString("N0"), Vm(rig).UnspentText);
    }

    [SkippableFact]
    public void Bond_level_edits_move_the_face_between_columns()
    {
        using var rig = Open("personnel");
        var op = rig.Main.Session.Current!.View.Personnel.Roster.First(o => o.Bonds.Count > 0);
        Headless.Run(() => rig.Main.Navigate("personnel", op.Guid, "bonds"));
        var bonds = (BondsTabViewModel)Vm(rig).Content!;
        Assert.True(bonds.HasSelection);
        int Column() => bonds.Columns.ToList().FindIndex(c => c.Faces.Any(f => f.IsSelected));
        int from = Column();
        Assert.True(from >= 0);
        int to = from == 8 ? 7 : from + 1;
        Headless.Run(() => bonds.Level!.Value = to);
        Assert.Equal(to, Column());
        Assert.True(bonds.Columns[to].Faces.First(f => f.IsSelected).IsChanged);
        Assert.Equal(1, rig.Main.PendingBar.Count);
    }

    [SkippableFact]
    public void Focus_levelling_spends_unspent_focus_or_grants_it()
    {
        using var rig = Open("personnel");
        var op = rig.Main.Session.Current!.View.Personnel.Roster
            .FirstOrDefault(o => o.Focus?.TotalRef != null && o.Abilities.Any(a => a.Thresholds is { Length: > 2 } && a.Level != null && a.Spent != null));
        Skip.If(op == null, "no operator with a known focus cost table");
        Headless.Run(() => rig.Main.Navigate("personnel", op!.Guid, "focus"));
        var tab = (FocusTabViewModel)Vm(rig).Content!;
        var row = tab.Groups.SelectMany(g => g.Rows).First(r => r.IsLevelled && r.Ability.Thresholds!.Length > 2);
        var a = row.Ability;
        int top = a.Thresholds!.Length;
        double need = a.Thresholds[top - 1] - a.Spent!.Value;
        double avail = op!.Focus!.Value;
        var pending = rig.Main.Session.Pending;
        if (need > avail)
        {
            Headless.Run(() => row.Pips[top - 1].Command.Execute(null));
            Assert.Contains("needs", tab.Notice);                       // refused: not enough unspent focus
            Assert.Equal(0, pending.Count);
            Headless.Run(() => tab.GrantMode = true);
            Headless.Run(() => row.Pips[top - 1].Command.Execute(null));
            Assert.Equal("", tab.Notice);
            Assert.Equal(a.Thresholds[top - 1], pending.GetValue(a.Spent));
            Assert.Equal(top, pending.GetValue(a.Level!));
            Assert.Equal(op.Focus.TotalRef!.Value + need, pending.GetValue(op.Focus.TotalRef));        // granted: total grows, unspent stays
            Assert.Equal(avail, pending.GetValue(op.Focus));
        }
        else
        {
            Headless.Run(() => row.Pips[top - 1].Command.Execute(null));
            Assert.Equal(avail - need, pending.GetValue(op.Focus));         // spent: unspent shrinks, total stays
            Assert.False(pending.IsChanged(op.Focus.TotalRef!));
        }
        Assert.True(row.Pips[top - 1].IsOn);
        rig.Shot("23-personnel-focus-levelled");
    }

    [SkippableFact]
    public void Old_game_data_is_flagged_on_the_banner_the_game_data_screen_and_the_stat_panel()
    {
        Skip.If(Repo.CombatSaveNamed("Hub_Traits.sav") == null, "no late-campaign sample save");
        using var rig = new TestRig(combatFile: "Hub_Traits.sav", staleGameData: true);
        rig.Start(height: 1900);
        Assert.True(rig.Session.Db.IsStale);
        Assert.Equal(ZeroCompany.GameData.Models.GameDatabase.StaleNotice, rig.Main.BannerText);
        Assert.Equal("info", rig.Main.BannerKind);
        var op = rig.Main.Session.Current!.View.Personnel.Roster.First(o => !o.Dead);
        Headless.Run(() => rig.Main.Navigate("personnel", op.Guid, "overview"));
        Assert.True(((OverviewTabViewModel)Vm(rig).Content!).StaleGameData);
        rig.Shot("personnel-give-stat-old-game-data");
        Headless.Run(() => rig.Main.Navigate("gamedata"));
        Assert.Contains("extracted by an older version", ((GameDataViewModel)rig.Main.Screen!).Status);

        using var fresh = new TestRig(combatFile: "Hub_Traits.sav");
        fresh.Start();
        Assert.False(fresh.Session.Db.IsStale);
        Assert.NotEqual(ZeroCompany.GameData.Models.GameDatabase.StaleNotice, fresh.Main.BannerText);
    }

    [SkippableFact]
    public void Overview_gives_a_stat_upgrade_and_the_operator_keeps_it_after_the_write()
    {
        Skip.If(Repo.CombatSaveNamed("Hub_Traits.sav") == null, "no late-campaign sample save");
        using var rig = new TestRig(combatFile: "Hub_Traits.sav");
        rig.Start(height: 1900);
        var op = rig.Main.Session.Current!.View.Personnel.Roster.First(o => !o.Dead
            && ZeroCompany.Core.Actions.StatOps.Givable(rig.Main.Session.Current.Gvas, o.Guid).Any(s => s.Asset == "GE_CrossTraining_RangedDamage"));
        Headless.Run(() => rig.Main.Navigate("personnel", op.Guid, "overview"));
        var tab = (OverviewTabViewModel)Vm(rig).Content!;
        Assert.True(tab.CanGiveStat);
        var ranged = tab.StatOptions.Single(s => s.Asset == "GE_CrossTraining_RangedDamage");
        Headless.Run(() => { tab.ChosenStat = ranged; tab.AddStatCommand.Execute(null); rig.Pump(); });
        Assert.Contains((op.Guid, "GE_CrossTraining_RangedDamage"), rig.Main.Session.Pending.Stats);
        Assert.DoesNotContain(tab.StatOptions, s => s.Asset == ranged.Asset);
        Assert.Single(tab.QueuedStats);
        rig.Shot("personnel-give-stat");

        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: false, force: false)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        Assert.Contains("GE_CrossTraining_RangedDamage", ZeroCompany.Core.Actions.StatOps.Held(rig.Main.Session.Current!.Gvas, op.Guid));
    }

    [SkippableFact]
    public void Overview_gives_a_bond_trait_and_the_operator_keeps_it_after_the_write()
    {
        Skip.If(Repo.CombatSaveNamed("Hub_Traits.sav") == null, "no late-campaign sample save");
        using var rig = new TestRig(combatFile: "Hub_Traits.sav");
        rig.Start(height: 1600);
        var op = rig.Main.Session.Current!.View.Personnel.Roster.First(o => !o.Dead
            && ZeroCompany.Core.Actions.TraitOps.Givable(rig.Main.Session.Current.Gvas, o.Guid).Any(t => t.Class == "Soldier"));
        Headless.Run(() => rig.Main.Navigate("personnel", op.Guid, "overview"));
        var tab = (OverviewTabViewModel)Vm(rig).Content!;
        Assert.True(tab.CanGiveTrait);
        var grenadier = tab.TraitOptions.Single(t => t.Asset == "GE_Offense_Soldier_Grenadier");
        Headless.Run(() => { tab.ChosenTrait = grenadier; tab.AddTraitCommand.Execute(null); rig.Pump(); });
        Assert.Contains(("" + op.Guid, "GE_Offense_Soldier_Grenadier"), rig.Main.Session.Pending.Traits);
        Assert.DoesNotContain(tab.TraitOptions, t => t.Asset == grenadier.Asset);                // queued: out of the list, into the queue
        Assert.Single(tab.QueuedTraits);
        rig.Shot("personnel-give-trait");

        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: false, force: false)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        Assert.Contains("GE_Offense_Soldier_Grenadier", ZeroCompany.Core.Actions.TraitOps.Held(rig.Main.Session.Current!.Gvas, op.Guid));
    }

    [SkippableFact]
    public void Raising_unspent_focus_then_spending_it_keeps_the_total_in_step()
    {
        using var rig = Open("personnel");
        var op = rig.Main.Session.Current!.View.Personnel.Roster
            .FirstOrDefault(o => o.Focus?.TotalRef != null && o.Abilities.Any(a => a.Thresholds is { Length: > 2 } && a.Level != null && a.Spent != null));
        Skip.If(op == null, "no operator with a known focus cost table");
        Headless.Run(() => rig.Main.Navigate("personnel", op!.Guid, "overview"));
        var overview = (OverviewTabViewModel)Vm(rig).Content!;
        Headless.Run(() => rig.Main.Navigate("personnel", op!.Guid, "focus"));
        var tab = (FocusTabViewModel)Vm(rig).Content!;
        var row = tab.Groups.SelectMany(g => g.Rows).First(r => r.IsLevelled && r.Ability.Thresholds!.Length > 2);
        var a = row.Ability;
        int top = a.Thresholds!.Length;
        double need = a.Thresholds[top - 1] - a.Spent!.Value;
        Skip.If(need <= 0, "ability already at its top level");
        double total0 = op!.Focus!.TotalRef!.Value, avail0 = op.Focus.Value;
        var pending = rig.Main.Session.Pending;
        Headless.Run(() => overview.Unspent!.SetTo(avail0 + need));                // grant exactly what the level costs ...
        Headless.Run(() => tab.SpendMode = true);
        Headless.Run(() => row.Pips[top - 1].Command.Execute(null));            // ... then spend it
        Assert.Equal("", tab.Notice);
        Assert.Equal(avail0, pending.GetValue(op.Focus));
        Assert.Equal(total0 + need, pending.GetValue(op.Focus.TotalRef));      // the grant stays in the total

        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: false, force: false)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        var after = rig.Main.Session.Current!.Model.Operators[op.Guid];
        Assert.Equal((long)(total0 + need), after.TotalFocus);
        Assert.Equal(avail0, after.Focus!.Value);
    }

    [SkippableFact]
    public void Roster_order_changes_apply_to_the_save()
    {
        using var rig = Open("personnel");
        var vm = Vm(rig);
        var before = vm.Live.Select(c => c.Guid).ToList();
        Skip.If(before.Count < 2, "needs two operators");
        Headless.Run(() => vm.MoveTo(before[0], before.Count - 1));                 // as a drag would: first operator to the end
        var expected = before.Skip(1).Append(before[0]).ToList();
        Assert.Equal(expected, Vm(rig).Live.Select(c => c.Guid).ToList());
        Assert.Equal(1, rig.Main.PendingBar.Count);
        rig.Shot("24-personnel-reordered");
        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: false, force: false)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        Headless.Run(() => rig.Main.Navigate("personnel"));
        Assert.Equal(expected, Vm(rig).Live.Select(c => c.Guid).ToList());
    }

    [Theory]
    [InlineData(-10, 0)]       // left of everything: drop first
    [InlineData(40, 0)]
    [InlineData(60, 1)]        // between the first and second chip centres
    [InlineData(260, 3)]
    [InlineData(10_000, 4)]    // right of everything: drop last
    public void Drop_index_counts_the_chips_left_of_the_pointer(double pointerX, int expected)
    {
        var centers = new[] { 50.0, 150.0, 250.0, 350.0 };          // the other chips (the dragged one is excluded)
        Assert.Equal(expected, PersonnelView.DropIndex(centers, pointerX));
    }

    [SkippableFact]
    public void A_fallen_operator_can_be_brought_back_and_returns_to_the_roster()
    {
        using var rig = Open("personnel");
        var dead = rig.Main.Session.Current!.View.Personnel.Memorial.FirstOrDefault();
        Skip.If(dead == null, "no fallen operator in the sample save");
        Headless.Run(() => rig.Main.Navigate("personnel", dead!.Guid, "overview"));
        var vm = Vm(rig);
        Assert.True(vm.SelectedIsDead);
        Assert.NotNull(vm.ReviveToggle);
        rig.Shot("25-personnel-memorial");
        Headless.Run(() => vm.ReviveToggle!.Command.Execute(null));
        Assert.True(rig.Main.Session.Pending.Revives.Contains(dead.Guid));
        Headless.RunAsync(() => rig.Main.ApplyAsync(copy: false, force: false)).GetAwaiter().GetResult();
        Assert.Equal("ok", rig.Main.BannerKind);
        var view = rig.Main.Session.Current!.View.Personnel;
        Assert.Contains(view.Roster, o => o.Guid == dead.Guid);
        Assert.DoesNotContain(view.Memorial, o => o.Guid == dead.Guid);
    }
}
