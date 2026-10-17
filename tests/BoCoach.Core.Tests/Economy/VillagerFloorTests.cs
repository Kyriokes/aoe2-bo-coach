using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Economy;

public class VillagerFloorTests
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    [Fact]
    public void Un_centro_urbano_consume_50_de_comida_cada_25_segundos()
    {
        Assert.Equal(2, VillagerFloor.FoodPerSecond(TestData.GameData, CivBonuses.None, Age.Dark, 1), 3);
        Assert.Equal(4, VillagerFloor.FoodPerSecond(TestData.GameData, CivBonuses.None, Age.Dark, 2), 3);
        Assert.Equal(0, VillagerFloor.FoodPerSecond(TestData.GameData, CivBonuses.None, Age.Dark, 0));
    }

    [Fact]
    public void Los_aldeanos_en_comida_se_redondean_para_arriba()
    {
        Assert.Equal(7, VillagerFloor.FoodVillagers(2, 0.3));    // 6,67
        Assert.Equal(5, VillagerFloor.FoodVillagers(2, 0.4));    // justo 5
        Assert.Equal(0, VillagerFloor.FoodVillagers(2, 0));
    }

    [Fact]
    public void Los_centros_urbanos_construidos_se_suman_al_inicial()
    {
        var tracker = new Tracker(Scouts, TestData.GameData, TestData.Events);
        Assert.Equal(1, VillagerFloor.TownCenters(tracker));

        var state = new PlayerState();
        state.Apply(new GameSnapshot(600, ResourceAmounts.Zero, 20, 25, [new GameEvent("town_center_built", 600)]) { Villagers = 19 });
        tracker.Update(state);
        Assert.Equal(2, VillagerFloor.TownCenters(tracker));
    }

    [Theory]
    [InlineData(20, 120, true)]
    [InlineData(120, 120, false)]
    public void El_piso_de_comida_vale_solo_hasta_el_maximo(int villagers, int max, bool floor)
    {
        var economy = new EconomyAdvisor(TestData.GameData, settings: new EconomySettings { MaxVillagers = max });
        var tracker = new Tracker(Scouts, TestData.GameData, TestData.Events);
        var state = new PlayerState();
        state.Apply(new GameSnapshot(300, new ResourceAmounts(100, 100, 0, 0), villagers + 1, villagers + 5, []) { Villagers = villagers });
        tracker.Update(state);

        var report = economy.Update(tracker);
        Assert.Equal(floor, report.FoodFloor > 0);
        if (floor)
            Assert.True(report.Demand.Food >= 2 - 1e-9);
    }
}

public class RepartoConPisoDeComidaTests
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    [Fact]
    public void La_comida_del_reparto_nunca_baja_del_piso()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        // Todos los pasos hasta los de madera hechos a mano: la BO pide gente en madera, pero el centro urbano sigue.
        for (var i = 0; i < 6; i++)
            coach.Advance();
        var view = coach.Update(new GameSnapshot(300, new ResourceAmounts(100, 100, 0, 0), 14, 15, []) { Villagers = 13 });

        Assert.True(view.FoodFloor > 0);
        Assert.NotNull(view.Distribution);
        Assert.True(view.Distribution!.Value.Food >= view.FoodFloor, $"comida {view.Distribution.Value.Food}, piso {view.FoodFloor}");
        Assert.Equal(13, view.Distribution.Value.Food + view.Distribution.Value.Wood + view.Distribution.Value.Gold + view.Distribution.Value.Stone);
        Assert.NotNull(view.Actual);
    }
}

public class ColorDelRepartoTests
{
    [Theory]
    [InlineData(15, 15.0, TargetMatch.Ok)]
    [InlineData(15, 12.0, TargetMatch.Short)]
    [InlineData(15, 17.0, TargetMatch.Over)]
    [InlineData(15, 14.6, TargetMatch.Ok)]
    public void Verde_si_coincide_rojo_si_faltan_amarillo_si_sobran(double target, double actual, TargetMatch expected) =>
        Assert.Equal(expected, CoachText.Match(target, actual));

    [Fact]
    public void Sin_saber_cuantos_hay_no_hay_color() => Assert.Null(CoachText.Match(15, null));
}
