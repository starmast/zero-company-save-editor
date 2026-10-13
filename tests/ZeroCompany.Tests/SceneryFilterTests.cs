using ZeroCompany.Core.Views;

namespace ZeroCompany.Tests;

/// <summary>Finding props in the Scenery list: a real level has hundreds.</summary>
public class SceneryFilterTests
{
    /// <summary>A prop whose title is "name (n+1)", like the editor builds them from the instance number.</summary>
    static PropView P(string name, string area, string condition, int n) => new()
    {
        Actor = $"/Game/Maps/{area}.{area}:PersistentLevel.BP_{name.Replace(" ", "")}_C_{n}",
        Title = $"{name} {n + 1}", Area = area, Condition = condition,
    };

    static readonly PropView[] Props =
    {
        P("Concrete Planter 1x1", "Env", "Pristine", 0), P("Concrete Planter 1x1", "Env", "Destroyed", 9), P("Concrete Planter 1x1", "Env", "Pristine", 1),
        P("Blockout Cover 2x1", "Gameplay_Cover", "Pristine", 6), P("Fence Rail Mid", "Gameplay_Cover", "Damaged", 2), P("Crate Pile", "Env", "Pristine", 3),
    };

    static List<string> Titles(SceneryQuery q, Func<string, bool>? queued = null) =>
        SceneryFilter.Run(Props, q, queued ?? (_ => false)).Select(p => p.Title).ToList();

    [Fact]
    public void Search_needs_every_word_in_any_order_across_name_area_condition_and_internal_name()
    {
        Assert.Equal(new[] { "Concrete Planter 1x1 1", "Concrete Planter 1x1 2", "Concrete Planter 1x1 10" }, Titles(new SceneryQuery(Text: "planter")));
        Assert.Equal(new[] { "Blockout Cover 2x1 7", "Fence Rail Mid 3" }, Titles(new SceneryQuery(Text: "gameplay cover")));          // the sub-level
        Assert.Equal(new[] { "Fence Rail Mid 3" }, Titles(new SceneryQuery(Text: "RAIL damaged")));                                   // title + condition
        Assert.Equal(new[] { "Crate Pile 4" }, Titles(new SceneryQuery(Text: "BP_CratePile_C_3")));                                   // internal name
        Assert.Empty(Titles(new SceneryQuery(Text: "planter zzz")));
        Assert.Equal(Props.Length, Titles(new SceneryQuery(Text: "  ")).Count);
    }

    [Fact]
    public void Condition_and_queued_filters_combine_with_the_search()
    {
        Assert.Equal(new[] { "Concrete Planter 1x1 10" }, Titles(new SceneryQuery(Condition: "Destroyed")));
        Assert.Equal(new[] { "Concrete Planter 1x1 1", "Concrete Planter 1x1 2" }, Titles(new SceneryQuery(Text: "planter", Condition: "Pristine")));
        var queued = Props[2].Actor;
        Assert.Equal(new[] { "Concrete Planter 1x1 2" }, Titles(new SceneryQuery(QueuedOnly: true), a => a == queued));
        Assert.Empty(Titles(new SceneryQuery(QueuedOnly: true)));
    }

    [Fact]
    public void Generated_prop_names_become_short_readable_titles_with_their_instance_number()
    {
        // level-instance props repeat their name: "BP_X_GEN_VARIABLE_BP_X_C_CAT_127" is instance 127 (shown as the 128th)
        Assert.Equal("Barrel EP4 B 1x1 LC A A 128",
            ZeroCompany.Core.Actions.CombatOps.SceneryTitle("BP_UNI_Barrel_EP4B_1x1LC_A_A_GEN_VARIABLE_BP_UNI_Barrel_EP4B_1x1LC_A_A_C_CAT_127"));
        Assert.Equal("Crate EP4 A 2x1 LC A1 82",
            ZeroCompany.Core.Actions.CombatOps.SceneryTitle("BP_UNI_CrateEP4A_2x1LC_A1_GEN_VARIABLE_BP_UNI_CrateEP4A_2x1LC_A_DS_Snow_C_CAT_81"));
        Assert.Equal("Concrete Planter 1x1 LC A 5", ZeroCompany.Core.Actions.CombatOps.SceneryTitle("BP_OTR_ConcretePlanter_1x1LC_A_C_4"));
        Assert.DoesNotContain("GEN VARIABLE", ZeroCompany.Core.Actions.CombatOps.SceneryTitle("BP_UNI_X_GEN_VARIABLE_BP_UNI_X_C_CAT_0"));
    }

    [Fact]
    public void Numbered_props_sort_naturally_so_10_comes_after_9()
    {
        var p = new[] { P("Rail", "A", "Pristine", 9), P("Rail", "A", "Pristine", 1), P("Rail", "A", "Pristine", 0) };       // titles Rail 10, Rail 2, Rail 1
        var order = SceneryFilter.Run(p, new SceneryQuery(), _ => false).Select(x => x.Title).ToList();
        Assert.Equal(new[] { "Rail 1", "Rail 2", "Rail 10" }, order);
    }
}
