using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.Data;

public class GameDataLoaderTests
{
    private const string Tasas = """
        "gather_rates": { "sheep": 0.33, "hunt": 0.41, "berries": 0.31, "fish": 0.43, "farm": 0.53, "wood": 0.39, "gold": 0.38, "stone": 0.36 }
        """;

    private static string Json(string entities, string rates = Tasas) => $$"""
        { "dat_version": "VER 1.0", "entities": { {{entities}} }, {{rates}} }
        """;

    private const string TownCenter = """
        "town_center": { "kind": "building", "dat_id": 621, "cost": { "wood": 275 }, "time": 150, "age": "castle", "pop_bonus": 5 }
        """;

    [Fact]
    public void Parsea_un_archivo_valido()
    {
        var data = GameDataLoader.Parse(Json(TownCenter + """
            , "villager": { "kind": "unit", "dat_id": 83, "cost": { "food": 50 }, "time": 25, "age": "dark", "made_in": "town_center", "pop": 1 }
            """));

        var villager = data.Get("villager");
        Assert.Equal(EntityKind.Unit, villager.Kind);
        Assert.Equal(50, villager.Cost.Food);
        Assert.Equal(1, villager.Pop);
        Assert.Equal(5, data.Get("town_center").PopBonus);
    }

    [Theory]
    [InlineData("""{ "kind": "unit", "dat_id": 1, "time": 1, "age": "dark" }""", "made_in")]
    [InlineData("""{ "dat_id": 1, "time": 1, "age": "dark" }""", "kind")]
    [InlineData("""{ "kind": "building", "dat_id": 1, "time": 1 }""", "age")]
    [InlineData("""{ "kind": "building", "dat_id": 1, "time": -3, "age": "dark" }""", "time")]
    [InlineData("""{ "kind": "unit", "dat_id": 1, "time": 1, "age": "dark", "made_in": "nada" }""", "nada")]
    [InlineData("""{ "kind": "building", "dat_id": 1, "time": 1, "age": "dark", "cost": { "oro_falso": 5 } }""", "oro_falso")]
    public void Entidad_mal_formada_da_error_que_nombra_el_problema(string entity, string expected)
    {
        var e = Assert.Throws<GameDataException>(() => GameDataLoader.Parse(Json($"\"x\": {entity}")));

        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public void Edad_desconocida_da_error_con_la_ruta_del_campo()
    {
        var e = Assert.Throws<GameDataException>(() =>
            GameDataLoader.Parse(Json("""  "x": { "kind": "building", "dat_id": 1, "time": 1, "age": "stone_age" } """)));

        Assert.Contains("age", e.Message);
    }

    [Fact]
    public void Falta_una_tasa_de_recoleccion()
    {
        var e = Assert.Throws<GameDataException>(() =>
            GameDataLoader.Parse(Json(TownCenter, """ "gather_rates": { "sheep": 0.33 } """)));

        Assert.Contains("Wood", e.Message);
    }

    [Fact]
    public void Una_fuente_desconocida_en_food_per_object_da_error()
    {
        var e = Assert.Throws<GameDataException>(() =>
            GameDataLoader.Parse(Json(TownCenter, Tasas + """, "food_per_object": { "cows": 150 } """)));

        Assert.Contains("food_per_object", e.Message);
    }

    [Fact]
    public void Una_fuente_desconocida_en_food_yield_de_una_civ_da_error()
    {
        var e = Assert.Throws<GameDataException>(() =>
            GameDataLoader.Parse(Json(TownCenter, Tasas + """, "civs": { "x": { "food_yield": { "cows": [1, 1, 1, 1] } } } """)));

        Assert.Contains("food_yield", e.Message);
    }

    [Fact]
    public void Sin_food_per_object_queda_vacio()
    {
        var data = GameDataLoader.Parse(Json(TownCenter));

        Assert.Empty(data.FoodPerObject);
    }

    [Fact]
    public void Un_requisito_que_no_es_un_edificio_da_error()
    {
        var e = Assert.Throws<GameDataException>(() =>
            GameDataLoader.Parse(Json(TownCenter + """, "stable": { "kind": "building", "dat_id": 101, "time": 50, "age": "feudal", "requires": "dragon" } """)));

        Assert.Contains("requires", e.Message);
    }

    [Theory]
    [InlineData("archery_range", "barracks")]
    [InlineData("stable", "barracks")]
    [InlineData("siege_workshop", "blacksmith")]
    [InlineData("market", "mill")]
    [InlineData("farm", "mill")]
    [InlineData("barracks", null)]
    [InlineData("blacksmith", null)]
    public void Los_edificios_piden_el_suyo_como_en_el_arbol(string building, string? requires)
    {
        Assert.Equal(requires, TestData.GameData.Get(building).Requires);
    }
}
