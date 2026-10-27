using BoCoach.Core.Assisted;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Assisted;

/// <summary>D12 (c): el generador del modo asistido de punta a punta.</summary>
public class AssistedGeneratorTests
{
    private static readonly Maps RealMaps = Maps.Load(Path.Combine(AppContext.BaseDirectory, "maps.json"));

    private static readonly AssistedGenerator Generator = new(TestData.GameData, RealMaps);

    private static BuildOrder Assisted(string civ, int until, params AssistedGoal[] goals) => new()
    {
        Name = "Asistida",
        Civ = civ,
        Assisted = new AssistedPlan { Villagers = new AssistedVillagers { Until = until }, Goals = goals },
    };

    /// <summary>La BO generada, simulada de nuevo sin la política, da lo mismo, y valida sin errores.</summary>
    private static void AssertConsistent(AssistedResult result, Maps? maps = null)
    {
        var again = new Planner(TestData.GameData, AssistedGenerator.Settings(TestData.GameData, maps ?? RealMaps, result.BuildOrder))
            .Simulate(result.BuildOrder);
        Assert.Equal(result.Plan.Steps.Select(s => (s.Step.Id, s.Start, s.End)), again.Steps.Select(s => (s.Step.Id, s.Start, s.End)));
        Assert.Equal(result.Plan.Blocks, again.Blocks);
        Assert.Equal(result.Plan.Timeline, again.Timeline);
        Assert.Equal(result.Plan.Idle, again.Idle);
        Assert.Empty(new BuildOrderValidator(TestData.GameData, TestData.Events).Validate(result.BuildOrder).Errors);
    }

    private static IEnumerable<Step> VillagerSteps(AssistedResult result) =>
        result.BuildOrder.Steps.Where(s => s is { Do: StepAction.Train, What: "villager" });

    [Theory]
    [InlineData("any")]
    [InlineData("franks")]
    public void Genera_aldeanos_hasta_N_con_casas(string civ)
    {
        var result = Generator.Generate(Assisted(civ, 18));

        Assert.Equal(18, result.Plan.Timeline[^1].Villagers);
        var villagerIds = VillagerSteps(result).Select(s => s.Id).ToHashSet();
        Assert.DoesNotContain(result.Plan.Blocks, b => villagerIds.Contains(b.StepId) && b.Reason == BlockReason.Population && b.Duration > 5);
        AssertConsistent(result);
    }

    [Fact]
    public void Los_pasos_de_aldeanos_salen_por_tramos()
    {
        var result = Generator.Generate(Assisted("any", 18));

        var units = LivePlan.Units(result.BuildOrder);
        var counts = result.BuildOrder.Steps.Select((s, i) => (s, n: units[i])).Where(x => LivePlan.IsVillagerTarget(x.s)).Select(x => x.n).ToList();
        Assert.True(counts.Count <= 9, $"{counts.Count} pasos de aldeanos para 15 aldeanos");
        Assert.All(counts, n => Assert.InRange(n, 1, 6));
    }

    [Fact]
    public void Until_chico_no_entrena_de_mas()
    {
        var result = Generator.Generate(Assisted("any", 5));

        Assert.Equal(5, result.Plan.Timeline[^1].Villagers);
        AssertConsistent(result);
    }

    [Fact]
    public void Sin_assisted_da_error()
    {
        Assert.Throws<ArgumentException>(() => Generator.Generate(new BuildOrder { Name = "manual" }));
    }

    [Theory]
    [InlineData("any")]
    [InlineData("tatars")]
    [InlineData("poles")]
    public void Con_la_comida_de_Arabia_nadie_queda_parado(string civ)
    {
        var result = Generator.Generate(Assisted(civ, 60));

        Assert.Equal(60, result.Plan.Timeline[^1].Villagers);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        Assert.Contains(result.BuildOrder.Steps, s => s is { Do: StepAction.Build, What: "farm" });
        AssertConsistent(result);
    }

    [Fact]
    public void Un_mapa_sin_jabali_ni_bayas_va_directo_a_las_granjas()
    {
        var maps = new Maps([new GameMap("pelado", "Pelado", false,
            new HashSet<GatherSource> { GatherSource.Sheep, GatherSource.Farm, GatherSource.Wood, GatherSource.Gold, GatherSource.Stone }, null)]);
        var bo = Assisted("any", 40) with { Map = "pelado" };

        var result = new AssistedGenerator(TestData.GameData, maps).Generate(bo);

        Assert.DoesNotContain(result.BuildOrder.Steps, s => s.Assign is GatherSource.Hunt or GatherSource.Berries);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result, maps);
    }

    private static AssistedGoal Age(string age, int? atPop = null) => new() { Age = age, AtPop = atPop };

    [Theory]
    [InlineData("any")]
    [InlineData("franks")]
    public void Llega_a_Imperial(string civ)
    {
        var result = Generator.Generate(Assisted(civ, 100, Age("feudal_age"), Age("castle_age"), Age("imperial_age")));

        Assert.Empty(result.Problems);
        Assert.All(result.Goals, g => Assert.NotNull(g.End));
        Assert.True(result.Goals[0].End < result.Goals[1].Start && result.Goals[1].End < result.Goals[2].Start);
        AssertConsistent(result);
    }

    [Fact]
    public void Objetivos_que_todavia_no_se_generan()
    {
        var archers = new AssistedGoal { Unit = "archer", Count = 20, Buildings = 2 };

        var result = Generator.Generate(Assisted("any", 60, Age("feudal_age"), archers, Age("castle_age")));

        Assert.Contains("todavía no", result.Goals[1].Problem);
        Assert.NotNull(result.Goals[0].End);
        Assert.NotNull(result.Goals[2].End);
    }

    [Fact]
    public void Una_edad_a_una_poblacion_que_nunca_llega()
    {
        var result = Generator.Generate(Assisted("any", 20, Age("feudal_age", atPop: 60)));

        Assert.Contains("No llega", result.Goals[0].Problem);
        Assert.Null(result.Goals[0].End);
    }

    [Fact]
    public void La_postura_sale_de_los_objetivos()
    {
        var result = Generator.Generate(Assisted("any", 50, Age("feudal_age"), Age("castle_age")));

        Assert.Equal("imperial_age", result.BuildOrder.Posture?.NextAge);
    }

    [Fact]
    public void Llega_a_Castillos_aunque_ya_no_salgan_aldeanos()
    {
        // Con 20 aldeanos el centro urbano deja de producir antes de juntar Castillos: el clic tiene que salir igual.
        var result = Generator.Generate(Assisted("any", 20, Age("feudal_age"), Age("castle_age")));

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Goals[1].End);
        AssertConsistent(result);
    }
}
