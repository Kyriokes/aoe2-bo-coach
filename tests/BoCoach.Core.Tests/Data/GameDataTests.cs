using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.Data;

public class GameDataTests
{
    private static readonly GameData V89 = GameDataLoader.Load(Path.Combine(TestData.GameDataDir, "v8.9.json"));

    // Criterio de salida de la Fase 1: valores verificados contra el juego (parche VER 8.9).
    [Theory]
    [InlineData("villager", 50, 0, 0, 0, 25)]
    [InlineData("house", 0, 25, 0, 0, 25)]
    [InlineData("barracks", 0, 175, 0, 0, 50)]
    [InlineData("mill", 0, 100, 0, 0, 35)]
    [InlineData("lumber_camp", 0, 100, 0, 0, 35)]
    [InlineData("mining_camp", 0, 100, 0, 0, 35)]
    [InlineData("archer", 0, 25, 0, 45, 35)]
    [InlineData("loom", 0, 0, 0, 50, 25)]
    [InlineData("feudal_age", 500, 0, 0, 0, 130)]
    [InlineData("castle_age", 800, 0, 0, 200, 160)]
    [InlineData("settlement", 0, 125, 0, 0, 40)]
    [InlineData("camel_rider", 55, 0, 0, 60, 22)]
    [InlineData("eagle_scout", 20, 0, 0, 50, 46)]
    [InlineData("battering_ram", 0, 160, 0, 75, 36)]
    public void Valores_clave_coinciden_con_el_juego(string key, double food, double wood, double stone, double gold, int time)
    {
        var entity = V89.Get(key);

        Assert.Equal(new ResourceAmounts(food, wood, stone, gold), entity.Cost);
        Assert.Equal(time, entity.Time);
    }

    [Fact]
    public void El_Settlement_de_los_incas_cuenta_como_cuatro_edificios()
    {
        var settlement = V89.Get("settlement");

        Assert.Equal(EntityKind.Building, settlement.Kind);
        Assert.Equal(Age.Dark, settlement.Age);
        Assert.Equal(["lumber_camp", "mining_camp", "mill", "house"], settlement.CountsAs);
        Assert.Empty(V89.Get("house").CountsAs);
        Assert.True(V89.Civ("incas").PopBonus(settlement, Age.Dark) >= 5);
    }

    [Fact]
    public void Ids_del_dat_estan_mapeados_por_id_y_no_por_nombre()
    {
        // El tech 101 se llama "Middle Age" en el .dat pero es el paso a Feudal.
        Assert.Equal(101, V89.Get("feudal_age").DatId);
        Assert.Equal(102, V89.Get("castle_age").DatId);
        Assert.Equal(83, V89.Get("villager").DatId);
    }

    [Theory]
    [InlineData("house", 5)]
    [InlineData("town_center", 5)]
    [InlineData("castle", 20)]
    [InlineData("barracks", 0)]
    public void Bono_de_poblacion(string key, int popBonus)
    {
        Assert.Equal(popBonus, V89.Get(key).PopBonus);
    }

    [Theory]
    [InlineData("villager", "town_center")]
    [InlineData("loom", "town_center")]
    [InlineData("feudal_age", "town_center")]
    [InlineData("archer", "archery_range")]
    [InlineData("double_bit_axe", "lumber_camp")]
    [InlineData("eagle_scout", "barracks")]
    [InlineData("hand_cannoneer", "archery_range")]
    [InlineData("galley", "dock")]
    [InlineData("scorpion", "siege_workshop")]
    public void Lugar_de_produccion(string key, string madeIn)
    {
        Assert.Equal(madeIn, V89.Get(key).MadeIn);
    }

    [Theory]
    [InlineData("villager", Age.Dark)]
    [InlineData("feudal_age", Age.Dark)]
    [InlineData("archery_range", Age.Feudal)]
    [InlineData("wheelbarrow", Age.Feudal)]
    [InlineData("knight", Age.Castle)]
    [InlineData("imperial_age", Age.Castle)]
    // La edad de las unidades sale de los árboles de las civs: el camello lo habilita un efecto sin edad.
    [InlineData("camel_rider", Age.Castle)]
    [InlineData("scout_cavalry", Age.Feudal)]
    [InlineData("hand_cannoneer", Age.Imperial)]
    [InlineData("transport_ship", Age.Dark)]
    public void Edad_requerida(string key, Age age)
    {
        Assert.Equal(age, V89.Get(key).Age);
    }

    [Fact]
    public void Hay_tasa_para_cada_fuente_de_recoleccion()
    {
        Assert.All(Enum.GetValues<GatherSource>(), s => Assert.True(V89.GatherRates[s] > 0));
        Assert.Equal(0.33, V89.GatherRates[GatherSource.Sheep]);
        Assert.Equal(0.39, V89.GatherRates[GatherSource.Wood]);
    }

    [Fact]
    public void LoadLatest_elige_la_version_mas_alta()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var json = File.ReadAllText(Path.Combine(TestData.GameDataDir, "v8.9.json"));
            File.WriteAllText(Path.Combine(dir.FullName, "v8.9.json"), json);
            File.WriteAllText(Path.Combine(dir.FullName, "v8.10.json"), json.Replace("VER 8.9", "VER 8.10"));

            Assert.Equal("VER 8.10", GameDataLoader.LoadLatest(dir.FullName).DatVersion);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Entidad_inexistente_da_error_claro()
    {
        var e = Assert.Throws<KeyNotFoundException>(() => V89.Get("trebuchet_volador"));
        Assert.Contains("trebuchet_volador", e.Message);
    }

    // D12: la comida finita del modo asistido (verificado en el .dat el 2026-10-26).
    [Fact]
    public void Trae_la_comida_de_cada_fuente_y_lo_que_suman_las_mejoras_de_granja()
    {
        Assert.Equal(100, V89.FoodPerObject[GatherSource.Sheep]);
        Assert.Equal(340, V89.FoodPerObject[GatherSource.Hunt]);
        Assert.Equal(125, V89.FoodPerObject[GatherSource.Berries]);
        Assert.Equal(175, V89.FoodPerObject[GatherSource.Farm]);
        Assert.Equal(75, V89.Get("horse_collar").FarmFood);
        Assert.Equal(125, V89.Get("heavy_plow").FarmFood);
        Assert.Equal(0, V89.Get("wheelbarrow").FarmFood);
    }
}
