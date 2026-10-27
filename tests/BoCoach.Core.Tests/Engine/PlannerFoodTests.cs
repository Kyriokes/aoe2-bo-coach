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

    private static PlanResult Run(FoodSupply food, PlanStart start, params Step[] steps) => Run(food, start, null, steps);

    private static PlanResult Run(FoodSupply food, PlanStart start, CivBonuses? civ, params Step[] steps) =>
        new Planner(TestData.GameData, new PlannerSettings { Food = food })
            .Simulate(steps.Select(s => new PlanItem(s, s.Count)).ToList(), start, civ);

    private static readonly FoodSupply SmallFarms = Arabia with { FarmFood = 50 };

    [Theory]
    [MemberData(nameof(AllBuildOrders))]
    public void Con_comida_de_sobra_las_BO_dan_lo_mismo_salvo_los_granjeros_que_esperan_su_granja(string file)
    {
        var bo = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, file));

        var infinite = new Planner(TestData.GameData).Simulate(bo);
        var plenty = new Planner(TestData.GameData, new PlannerSettings { Food = Plenty }).Simulate(bo);

        // Con comida de sobra no se acaba nada. Lo único que cambia es el granjero que espera su granja: con la comida
        // infinita juntaba de una granja que todavía no existía (35 de las 129 BO del repo, revisión del 2026-10-26).
        Assert.Empty(infinite.Idle);
        Assert.All(plenty.Idle, i => Assert.Equal(IdleReason.WaitingFarm, i.Reason));
        if (plenty.Idle.Count > 0)
            return;
        Assert.Equal(infinite.Steps.Select(s => (s.Step.Id, s.Start, s.End)), plenty.Steps.Select(s => (s.Step.Id, s.Start, s.End)));
        Assert.Equal(infinite.Blocks, plenty.Blocks);
        Assert.Equal(infinite.Timeline, plenty.Timeline);
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

    [Fact]
    public void La_granja_se_resiembra_con_madera_y_sin_madera_el_granjero_espera()
    {
        var plan = Run(SmallFarms, Start(GatherSource.Farm, 1, food: 0, wood: 60), Loom(600));

        Assert.Equal(100, plan.Timeline[^1].Stock.Food, 6);
        Assert.Equal(0, plan.Timeline[^1].Stock.Wood, 6);
        var idle = Assert.Single(plan.Idle);
        Assert.Equal((GatherSource.Farm, IdleReason.Reseed, 1), (idle.Source, idle.Reason, idle.Villagers));
        Assert.True(idle.To >= 600);
    }

    [Fact]
    public void La_collera_agranda_las_granjas_nuevas()
    {
        var start = Start(GatherSource.Farm, 1, food: 0, wood: 0) with { Researched = ["horse_collar"] };

        var plan = Run(SmallFarms, start, Loom(600));

        Assert.Equal(125, plan.Timeline[^1].Stock.Food, 6);
    }

    [Fact]
    public void Los_teutones_resiembran_mas_barato()
    {
        var plan = Run(SmallFarms, Start(GatherSource.Farm, 1, food: 0, wood: 72), TestData.GameData.Civ("teutons"), Loom(600));

        Assert.Equal(150, plan.Timeline[^1].Stock.Food, 6);
        Assert.Equal(0, plan.Timeline[^1].Stock.Wood, 6);
    }

    [Fact]
    public void La_resiembra_paga_antes_que_los_pasos()
    {
        var start = Start(GatherSource.Farm, 1, food: 0, wood: 60) with { IdleVillagers = 1 };
        var house = new Step { Id = "casa", Do = StepAction.Build, What = "house", Builders = 1, AtTime = 1 };

        var plan = Run(Arabia with { FarmFood = 0.0001 }, start, house);

        Assert.Contains(plan.Blocks, b => b.StepId == "casa" && b.Reason == BlockReason.Resources && b.Missing == Resource.Wood);
        Assert.All(plan.Timeline, s => Assert.True(s.Stock.Wood >= 0));
    }

    [Fact]
    public void Un_granjero_que_espera_su_granja_no_junta()
    {
        // El paso de granjas que la hace desde la granja espera madera: su granjero todavía no tiene dónde trabajar.
        var farm = new Step { Id = "granja", Do = StepAction.Build, What = "farm", From = GatherSource.Farm, Assign = GatherSource.Farm };

        var plan = Run(Arabia, Start(GatherSource.Farm, 1, food: 0, wood: 0), farm);

        Assert.Equal(0, plan.Timeline[^1].Stock.Food, 6);
        var idle = Assert.Single(plan.Idle);
        Assert.Equal((GatherSource.Farm, IdleReason.WaitingFarm, 1, 0.0), (idle.Source, idle.Reason, idle.Villagers, idle.From));
    }

    [Theory]
    [InlineData("tatars", GatherSource.Sheep, 200 + 800 * 1.57)] // 8 ovejas de 157
    [InlineData("goths", GatherSource.Hunt, 200 + 680 * 1.23)]
    [InlineData("mayan", GatherSource.Berries, 200 + 750 * 1.15)]
    [InlineData("britons", GatherSource.Sheep, 200 + 800)]
    public void La_comida_de_cada_fuente_rinde_segun_la_civ(string civ, GatherSource source, double food)
    {
        var plan = Run(Arabia, Start(source, 6), TestData.GameData.Civ(civ), Loom(1500));

        Assert.Equal(food, plan.Timeline[^1].Stock.Food, 6);
        Assert.Equal((source, IdleReason.Exhausted), (plan.Idle.Single().Source, plan.Idle.Single().Reason));
    }

    [Fact]
    public void Las_mejoras_de_granja_de_los_sicilianos_rinden_mas()
    {
        var start = Start(GatherSource.Farm, 1, food: 0, wood: 0) with { Researched = ["horse_collar"] };

        var plan = Run(SmallFarms, start, TestData.GameData.Civ("sicilians"), Loom(1500));

        Assert.Equal(50 + 75 * 2.25, plan.Timeline[^1].Stock.Food, 6);
    }

    [Fact]
    public void El_folwark_de_los_polacos_suma_un_diez_por_ciento_a_cada_granja()
    {
        var plan = Run(SmallFarms, Start(GatherSource.Farm, 1, food: 0, wood: 0), TestData.GameData.Civ("poles"), Loom(600));

        Assert.Equal(55, plan.Timeline[^1].Stock.Food, 6);
    }

    [Fact]
    public void Un_paso_de_granjas_que_todavia_no_llego_no_hace_esperar_al_granjero()
    {
        // El granjero llega sin un paso de granjas alcanzado: trae la suya. Si contara el paso de más adelante, una
        // simulación nueva de una BO generada por la política (D12 b) daría otra cosa que la de la política.
        var move = new Step { Id = "a-granja", Do = StepAction.Move, Count = 1, From = GatherSource.Sheep, Assign = GatherSource.Farm };
        var farm = new Step { Id = "granja", Do = StepAction.Build, What = "farm", From = GatherSource.Farm, Assign = GatherSource.Farm };

        var plan = Run(Arabia, Start(GatherSource.Sheep, 1, food: 200, wood: 200), move, Loom(100), farm);

        Assert.DoesNotContain(plan.Idle, i => i.Reason == IdleReason.WaitingFarm && i.From < 100);
    }

    [Fact]
    public void Los_granjeros_que_se_van_y_vuelven_traen_granja_llena()
    {
        var leave = new Step { Id = "a-madera", Do = StepAction.Move, Count = 1, From = GatherSource.Farm, Assign = GatherSource.Wood, AtTime = 5 };
        var back = new Step { Id = "a-granja", Do = StepAction.Move, Count = 1, From = GatherSource.Wood, Assign = GatherSource.Farm, AtTime = 10 };

        var plan = Run(Arabia with { FarmFood = 40 }, Start(GatherSource.Farm, 2, food: 0, wood: 0), leave, back, Loom(400));

        var rate = Rate(GatherSource.Farm);
        Assert.InRange(plan.Timeline[^1].Stock.Food, 80 + 4 * rate, 80 + 6 * rate);
    }
}
