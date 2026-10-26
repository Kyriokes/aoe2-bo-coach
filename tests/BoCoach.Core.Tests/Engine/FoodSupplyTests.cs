using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>D12: cuánta comida tiene cada mapa para la comida finita del Planner.</summary>
public class FoodSupplyTests
{
    private static readonly Maps Real = Maps.Load(Path.Combine(AppContext.BaseDirectory, "maps.json"));

    [Fact]
    public void En_Arabia_hay_800_de_ovejas_680_de_jabali_y_750_de_bayas()
    {
        var supply = FoodSupply.For(TestData.GameData, Real.Get("arabia"));

        Assert.Equal(800, supply.Pools[GatherSource.Sheep]);
        Assert.Equal(680, supply.Pools[GatherSource.Hunt]);
        Assert.Equal(750, supply.Pools[GatherSource.Berries]);
        Assert.Null(supply.FarmFood);
    }

    [Fact]
    public void Sin_mapa_es_la_de_Arabia()
    {
        var supply = FoodSupply.For(TestData.GameData, null);

        Assert.Equal(800, supply.Pools[GatherSource.Sheep]);
        Assert.Equal(680, supply.Pools[GatherSource.Hunt]);
        Assert.Equal(750, supply.Pools[GatherSource.Berries]);
    }

    [Fact]
    public void Una_fuente_que_el_mapa_no_tiene_vale_cero()
    {
        var map = new GameMap("x", "X", false, new HashSet<GatherSource> { GatherSource.Sheep, GatherSource.Wood }, null);

        var supply = FoodSupply.For(TestData.GameData, map);

        Assert.Equal(800, supply.Pools[GatherSource.Sheep]);
        Assert.Equal(0, supply.Pools[GatherSource.Hunt]);
        Assert.Equal(0, supply.Pools[GatherSource.Berries]);
    }

    [Fact]
    public void Sin_la_comida_en_game_data_avisa_que_falta_correr_el_extractor()
    {
        var old = new GameData("t", TestData.GameData.Entities, TestData.GameData.GatherRates);

        var e = Assert.Throws<InvalidOperationException>(() => FoodSupply.For(old, null));

        Assert.Contains("food_per_object", e.Message);
    }
}
