using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>D12: el Planner con la comida finita (ovejas, caza, bayas y granjas que se agotan).</summary>
public class PlannerFoodTests
{
    private static readonly FoodSupply Arabia = new(new Dictionary<GatherSource, double>
    {
        [GatherSource.Sheep] = 800,
        [GatherSource.Hunt] = 680,
        [GatherSource.Berries] = 750,
    });

    private static readonly FoodSupply Plenty = new(FoodSupply.Counted.ToDictionary(s => s, _ => 1e9)) { FarmFood = 1e9 };

    public static TheoryData<string> AllBuildOrders()
    {
        var files = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(TestData.BuildOrdersDir, "*.json")
                     .Concat(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "civs"), "*.json")))
            files.Add(Path.GetRelativePath(AppContext.BaseDirectory, file));
        return files;
    }

    private static double Rate(GatherSource source) =>
        new GatherModel(TestData.GameData, PlannerSettings.DefaultEfficiency).Rate(source, CivBonuses.None, Age.Dark, []);

    private static PlanStart Start(GatherSource source, int villagers, double food = 200, double wood = 0) =>
        PlanStart.Standard with
        {
            IdleVillagers = 0,
            Villagers = new Dictionary<GatherSource, int> { [source] = villagers },
            Stock = new ResourceAmounts(food, wood, 0, 100),
        };

    private static Step Loom(int at) => new() { Id = "telar", Do = StepAction.Research, What = "loom", AtTime = at };

    private static PlanResult Run(FoodSupply food, PlanStart start, params Step[] steps) =>
        new Planner(TestData.GameData, new PlannerSettings { Food = food })
            .Simulate(steps.Select(s => new PlanItem(s, s.Count)).ToList(), start);

    [Theory]
    [MemberData(nameof(AllBuildOrders))]
    public void Con_comida_de_sobra_todas_las_BO_dan_lo_mismo_que_con_la_infinita(string file)
    {
        var bo = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, file));

        var infinite = new Planner(TestData.GameData).Simulate(bo);
        var plenty = new Planner(TestData.GameData, new PlannerSettings { Food = Plenty }).Simulate(bo);

        Assert.Equal(infinite.Steps.Select(s => (s.Step.Id, s.Start, s.End)), plenty.Steps.Select(s => (s.Step.Id, s.Start, s.End)));
        Assert.Equal(infinite.Blocks, plenty.Blocks);
        Assert.Equal(infinite.Timeline, plenty.Timeline);
        Assert.Empty(infinite.Idle);
        Assert.Empty(plenty.Idle);
    }

    [Fact]
    public void Las_ovejas_se_acaban_y_los_pastores_quedan_parados()
    {
        var plan = Run(Arabia, Start(GatherSource.Sheep, 6), Loom(600));

        Assert.Equal(1000, plan.Timeline[^1].Stock.Food, 6);
        var idle = Assert.Single(plan.Idle);
        Assert.Equal((GatherSource.Sheep, IdleReason.Exhausted, 6), (idle.Source, idle.Reason, idle.Villagers));
        var exhausted = Math.Ceiling(800 / (6 * Rate(GatherSource.Sheep)));
        Assert.InRange(idle.From, exhausted - 1, exhausted + 1);
        Assert.True(idle.To >= 600);
    }

    [Fact]
    public void Mover_a_los_parados_al_jabali_los_pone_a_juntar()
    {
        var move = new Step { Id = "jabali", Do = StepAction.Move, Count = 6, From = GatherSource.Sheep, Assign = GatherSource.Hunt, AtTime = 500 };

        var plan = Run(Arabia, Start(GatherSource.Sheep, 6), move, Loom(1000));

        Assert.Equal(1680, plan.Timeline[^1].Stock.Food, 6);
        Assert.Equal(2, plan.Idle.Count);
        Assert.Equal(GatherSource.Sheep, plan.Idle[0].Source);
        Assert.InRange(plan.Idle[0].To, 499, 501);
        Assert.Equal((GatherSource.Hunt, IdleReason.Exhausted, 6), (plan.Idle[1].Source, plan.Idle[1].Reason, plan.Idle[1].Villagers));
    }

    [Fact]
    public void Un_mapa_sin_jabali_deja_parados_a_los_que_van_al_jabali()
    {
        var noHunt = new FoodSupply(new Dictionary<GatherSource, double>(Arabia.Pools) { [GatherSource.Hunt] = 0 });

        var plan = Run(noHunt, Start(GatherSource.Hunt, 3), Loom(60));

        Assert.Equal(200, plan.Timeline[^1].Stock.Food, 6);
        var idle = Assert.Single(plan.Idle);
        Assert.Equal((GatherSource.Hunt, IdleReason.Exhausted, 3, 0.0), (idle.Source, idle.Reason, idle.Villagers, idle.From));
    }
}
