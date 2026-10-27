using BoCoach.Core.BuildOrders;

namespace BoCoach.Core.Tests.BuildOrders;

/// <summary>D12: el campo `assisted` de la BO (los objetivos del modo asistido) y su validación.</summary>
public class AssistedPlanTests
{
    private static readonly BuildOrderValidator Validator = new(TestData.GameData, TestData.Events);

    /// <summary>El ejemplo de la sección 2 de la spec.</summary>
    private const string Example = """
        {
          "schema_version": 2,
          "name": "Asistida",
          "assisted": {
            "opening": { "houses": true, "sheep": 6, "boar": true, "berries": "auto" },
            "villagers": { "until": 120 },
            "eco_upgrades": true,
            "goals": [
              { "age": "feudal_age" },
              { "unit": "scout_cavalry", "count": 20, "buildings": 2 },
              { "age": "castle_age", "at_pop": 32 },
              { "town_centers": 3 },
              { "tech": "bloodlines" },
              { "age": "imperial_age" }
            ]
          }
        }
        """;

    private static BuildOrder WithGoal(string goal) =>
        BuildOrderSerializer.Parse($$"""{ "schema_version": 2, "name": "x", "assisted": { "goals": [ {{goal}} ] } }""");

    [Fact]
    public void El_ejemplo_de_la_spec_va_y_vuelve_por_JSON()
    {
        var bo = BuildOrderSerializer.Parse(Example);

        var assisted = bo.Assisted!;
        Assert.True(assisted.EcoUpgrades);
        Assert.Equal(120, assisted.Villagers.Until);
        Assert.Equal(6, assisted.Goals.Count);
        Assert.Equal("scout_cavalry", assisted.Goals[1].Unit);
        Assert.Equal((20, 2), (assisted.Goals[1].Count, assisted.Goals[1].Buildings));
        Assert.Equal(32, assisted.Goals[2].AtPop);
        Assert.Equal(3, assisted.Goals[3].TownCenters);
        Assert.Equal("bloodlines", assisted.Goals[4].Tech);
        var again = BuildOrderSerializer.Parse(BuildOrderSerializer.Serialize(bo));
        Assert.Equal(BuildOrderSerializer.Serialize(bo), BuildOrderSerializer.Serialize(again));
        Assert.Null(BuildOrderSerializer.Parse("""{ "schema_version": 2, "name": "manual" }""").Assisted);
        // Sin generar todavía no tiene pasos (eso sí es un error de la BO); lo de assisted está bien.
        Assert.DoesNotContain(Validator.Validate(bo).Errors, e => e.Message.Contains("assisted"));
    }

    [Fact]
    public void Los_valores_por_defecto()
    {
        var assisted = BuildOrderSerializer.Parse("""{ "schema_version": 2, "name": "x", "assisted": {} }""").Assisted!;

        Assert.Equal((true, 6, true, "auto"), (assisted.Opening.Houses, assisted.Opening.Sheep, assisted.Opening.Boar, assisted.Opening.Berries));
        Assert.Equal(120, assisted.Villagers.Until);
        Assert.False(assisted.EcoUpgrades);
        Assert.Empty(assisted.Goals);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "age": "feudal_age", "unit": "archer", "count": 5 }""")]
    [InlineData("""{ "age": "dark_age" }""")]
    [InlineData("""{ "unit": "knigth", "count": 5 }""")]
    [InlineData("""{ "unit": "archer", "count": 0 }""")]
    [InlineData("""{ "unit": "archer", "count": 5, "buildings": 11 }""")]
    [InlineData("""{ "town_centers": 1 }""")]
    [InlineData("""{ "tech": "villager" }""")]
    [InlineData("""{ "age": "feudal_age", "at_pop": 0 }""")]
    public void Objetivos_mal_armados_dan_error(string goal)
    {
        Assert.NotEmpty(Validator.Validate(WithGoal(goal)).Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Until_fuera_de_rango_da_error(int until)
    {
        var bo = BuildOrderSerializer.Parse($$"""{ "schema_version": 2, "name": "x", "assisted": { "villagers": { "until": {{until}} } } }""");

        Assert.Contains(Validator.Validate(bo).Errors, e => e.Message.Contains("until"));
    }

    [Theory]
    [InlineData("""{ "sheep": 21 }""")]
    [InlineData("""{ "berries": "siempre" }""")]
    public void Una_apertura_mal_armada_da_error(string opening)
    {
        var bo = BuildOrderSerializer.Parse($$"""{ "schema_version": 2, "name": "x", "assisted": { "opening": {{opening}} } }""");

        Assert.NotEmpty(Validator.Validate(bo).Errors);
    }
}
