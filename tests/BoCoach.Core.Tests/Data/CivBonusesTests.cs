using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Data;

/// <summary>Bonificaciones de civ sacadas del .dat (v8.9) y cómo las usa el Planner.</summary>
public class CivBonusesTests
{
    private static readonly GameData Data = TestData.GameData;

    private static BuildOrder Load(string file) => BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, file));

    [Theory]
    [InlineData("britons", GatherSource.Sheep, 1.25)]
    [InlineData("franks", GatherSource.Berries, 1.15)]
    [InlineData("celts", GatherSource.Wood, 1.15)]
    [InlineData("mongols", GatherSource.Hunt, 1.4)]
    [InlineData("turks", GatherSource.Gold, 1.25)]
    [InlineData("mayan", GatherSource.Wood, 1.0)] // "los recursos duran más" no es juntar más rápido
    public void Recoleccion_por_civ(string civ, GatherSource source, double multiplier)
    {
        Assert.Equal(multiplier, Data.Civ(civ).GatherMultiplier(source, Age.Dark), 3);
    }

    [Fact]
    public void Arranques_distintos()
    {
        var chinese = Data.Civ("chinese").Start;
        Assert.Equal((-200, -50, 3), (chinese.Resources.Food, chinese.Resources.Wood, chinese.ExtraVillagers));
        Assert.True(Data.Civ("huns").Start.NoHouses);
        Assert.Equal(1, Data.Civ("mayan").Start.ExtraVillagers);
    }

    [Fact]
    public void Costos_y_velocidades_por_edad()
    {
        var tc = Data.Get("town_center");
        Assert.Equal(275, Data.Civ("britons").Cost(tc, Age.Feudal).Wood);
        Assert.Equal(138, Data.Civ("britons").Cost(tc, Age.Castle).Wood); // centro urbano a mitad de madera en Castillos
        Assert.Equal(ResourceAmounts.Zero, Data.Civ("vikings").Cost(Data.Get("wheelbarrow"), Age.Dark));
        Assert.Equal(15, Data.Civ("chinese").PopBonus(tc, Age.Dark));
        Assert.Equal([1.05, 1.1, 1.15, 1.2], Enum.GetValues<Age>().Select(a => Data.Civ("persians").WorkRate("town_center", a)));
        Assert.Equal(1.3, Data.Civ("spanish").BuildRate(Age.Dark), 3);
    }

    [Fact]
    public void Any_y_una_civ_desconocida_no_tienen_bonificaciones()
    {
        Assert.True(Data.Civ("any").IsNone);
        Assert.True(Data.Civ("atlantes").IsNone);
        Assert.True(Data.Civ(null).IsNone);
        Assert.False(Data.IsKnownCiv("atlantes"));
        Assert.True(Data.IsKnownCiv(" Britons "));
    }

    [Fact]
    public void El_inicio_del_Planner_usa_la_civ()
    {
        var chinese = PlanStart.For(Data.Civ("chinese"), Data);
        Assert.Equal((0, 150, 7, 15), (chinese.Stock.Food, chinese.Stock.Wood, chinese.Population, chinese.PopulationCap));
        // Los 3 de siempre esperan la BO; los 3 de más arrancan en las ovejas.
        Assert.Equal((3, 3), (chinese.IdleVillagers, chinese.Villagers[GatherSource.Sheep]));
        Assert.Equal(BuildOrderValidator.MaxPopulation, PlanStart.For(Data.Civ("huns"), Data).PopulationCap);
        Assert.Same(PlanStart.Standard, PlanStart.For(CivBonuses.None, Data));
    }

    [Fact]
    public void Con_mejor_economia_la_misma_BO_junta_mas()
    {
        var planner = new Planner(Data);
        var scouts = Load("scouts_feudal.json");
        static double FoodAt(PlanResult plan, double time) => plan.Timeline.Last(s => s.Time <= time).Stock.Food;

        var any = planner.Simulate(scouts);
        var britons = planner.Simulate(scouts with { Civ = "britons" });

        // Los británicos juntan de las ovejas un 25 % más rápido y la BO arranca con 6 a 9 aldeanos en ovejas.
        // (La Feudal no cambia: esta BO hace clic a 22 de población, y eso lo marca el centro urbano.)
        Assert.True(FoodAt(britons, 300) > FoodAt(any, 300) + 50, $"británicos {FoodAt(britons, 300):0}, any {FoodAt(any, 300):0}");
        Assert.Equal(any.ClickTime("feudal_age"), britons.ClickTime("feudal_age"));
    }

    [Fact]
    public void Los_hunos_no_se_traban_por_poblacion_sin_casas()
    {
        var planner = new Planner(Data);
        var noHouses = Load("scouts_feudal.json") is var bo
            ? bo with { Steps = bo.Steps.Where(s => s.What != "house").ToList() }
            : null!;

        var huns = planner.Simulate(noHouses with { Civ = "huns" });
        var any = planner.Simulate(noHouses);

        Assert.DoesNotContain(huns.Blocks, b => b.Reason == BlockReason.Population);
        Assert.Contains(any.Blocks, b => b.Reason == BlockReason.Population);
    }

    [Fact]
    public void Una_civ_desconocida_avisa_en_la_validacion()
    {
        var result = new BuildOrderValidator(Data).Validate(Load("scouts_feudal.json") with { Civ = "atlantes" });

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Message.Contains("atlantes"));
    }
}
