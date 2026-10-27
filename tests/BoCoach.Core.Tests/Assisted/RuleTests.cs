using BoCoach.Core.Assisted;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Assisted;

/// <summary>D12 (c): cada regla del generador con fotos armadas a mano.</summary>
public class RuleTests
{
    private static GoalState State(string civ = "any", AssistedPlan? plan = null) =>
        new(TestData.GameData, TestData.GameData.Civ(civ), plan ?? new AssistedPlan());

    private static readonly IReadOnlyDictionary<GatherSource, double> Rates =
        new GatherModel(TestData.GameData, PlannerSettings.DefaultEfficiency).Rates(CivBonuses.None, Age.Dark, []);

    private static Dictionary<GatherSource, int> On(params (GatherSource Source, int Count)[] villagers)
    {
        var all = Enum.GetValues<GatherSource>().ToDictionary(s => s, _ => 0);
        foreach (var (source, count) in villagers)
            all[source] = count;
        return all;
    }

    [Fact]
    public void La_casa_se_pide_con_tres_lugares()
    {
        var view = new PlanView { Population = 12, InProduction = 1, PopulationCap = 15, Villagers = On((GatherSource.Wood, 4)), Rates = Rates };

        var house = Assert.Single(new HouseRule().Next(view, State()));

        Assert.Equal((StepAction.Build, "house", GatherSource.Wood, GatherSource.Wood), (house.Do, house.What, house.From, house.Assign));
    }

    [Fact]
    public void Sin_casas_para_los_hunos()
    {
        var view = new PlanView { Population = 12, InProduction = 1, PopulationCap = 15, Villagers = On((GatherSource.Wood, 4)), Rates = Rates };

        Assert.Empty(new HouseRule().Next(view, State("huns")));
    }

    [Fact]
    public void El_tramo_llena_las_ovejas_y_despues_reparte()
    {
        var view = new PlanView
        {
            Population = 4,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 3)),
            Stock = new ResourceAmounts(200, 0, 0, 100),
            Rates = Rates,
        };

        var steps = new VillagerRule().Next(view, State()).ToList();

        Assert.Equal((StepAction.Train, "villager", 3, GatherSource.Sheep), (steps[0].Do, steps[0].What, steps[0].Count, steps[0].Assign));
        Assert.Equal(6, steps.Where(s => s.Do == StepAction.Train).Sum(s => s.Count));
    }

    [Fact]
    public void El_primer_aldeano_a_la_madera_hace_el_campamento()
    {
        var view = new PlanView
        {
            Population = 7,
            InProduction = 1,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 6)),
            Stock = new ResourceAmounts(300, 0, 0, 100),
            Rates = Rates,
        };

        var steps = new VillagerRule().Next(view, State()).ToList();

        var builder = steps[0];
        var camp = steps[1];
        Assert.Equal((StepAction.Train, 1, GatherSource.Wood), (builder.Do, builder.Count, builder.Assign));
        Assert.Equal((StepAction.Build, "lumber_camp", GatherSource.Wood, GatherSource.Wood), (camp.Do, camp.What, camp.From, camp.Assign));
        Assert.Equal(9, camp.AtPop); // 7 de población, 1 en producción, y este es el siguiente
        Assert.NotNull(builder.Group);
        Assert.Equal(builder.Group, camp.Group);
    }
}
