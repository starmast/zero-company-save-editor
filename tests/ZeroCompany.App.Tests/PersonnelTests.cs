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
