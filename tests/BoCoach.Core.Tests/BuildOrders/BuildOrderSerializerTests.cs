using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.BuildOrders;

public class BuildOrderSerializerTests
{
    [Fact]
    public void Lee_todos_los_campos()
    {
        var bo = BuildOrderSerializer.Parse("""
            {
              "schema_version": 1, "id": "x", "name": "Prueba", "civ": "britons", "tags": ["a"],
              "steps": [
                { "id": "s1", "do": "train", "what": "villager", "count": 6, "assign": "sheep", "confirm_by": "villager_created" },
                { "id": "s2", "do": "move", "count": 3, "from": "wood", "assign": "gold", "after": ["s1"] },
                { "id": "s3", "do": "research", "what": "feudal_age", "at_pop": 22, "at_time": 500, "note": "dale" }
              ],
              "targets": { "feudal_click_time": 540, "villagers_at_feudal": { "food": 14, "wood": 6, "gold": 0, "stone": 0 } }
            }
            """);

        Assert.Equal("britons", bo.Civ);
        Assert.Equal(3, bo.Steps.Count);
        Assert.Equal(StepAction.Train, bo.Steps[0].Do);
        Assert.Equal(GatherSource.Sheep, bo.Steps[0].Assign);
        Assert.Equal(GatherSource.Wood, bo.Steps[1].From);
        Assert.Equal(["s1"], bo.Steps[1].After);
        Assert.Equal(22, bo.Steps[2].AtPop);
        Assert.Equal(500, bo.Steps[2].AtTime);
        Assert.Equal(1, bo.Steps[2].Count);
        Assert.Equal(540, bo.Targets!.FeudalClickTime);
        Assert.Equal(20, bo.Targets.VillagersAtFeudal!.Total);
    }

    [Theory]
    [MemberData(nameof(Ejemplos))]
    public void Ida_y_vuelta_no_pierde_nada(string path)
    {
        var original = BuildOrderSerializer.Load(path);

        var json = BuildOrderSerializer.Serialize(original);
        var again = BuildOrderSerializer.Parse(json);

        Assert.Equal(json, BuildOrderSerializer.Serialize(again));
        Assert.Contains("\"schema_version\": 2", json);
        Assert.Contains("\"do\": \"train\"", json);
    }

    [Theory]
    [InlineData("""{ "schema_version": 1, "steps": [ { "id": "s1", "do": "volar" } ] }""", "steps[0].do")]
    [InlineData("""{ "schema_version": 1, "steps": [ { "id": "s1", "do": "train", "assign": "piedra_filosofal" } ] }""", "steps[0].assign")]
    [InlineData("""{ "schema_version": 1, "steps": [ { "id": "s1", "do": "train", "count": "seis" } ] }""", "steps[0].count")]
    [InlineData("""{ "name": "sin versión" }""", "schema_version")]
    [InlineData("""{ "schema_version": "uno" }""", "schema_version")]
    [InlineData("""{ "schema_version": 99 }""", "99")]
    [InlineData("""{ "schema_version": 0 }""", "0")]
    [InlineData("""[1, 2]""", "objeto")]
    [InlineData("""{ "schema_version": 1, "name": "x" """, "línea")]
    public void Formato_invalido_da_error_que_dice_donde(string json, string expected)
    {
        var e = Assert.Throws<BuildOrderFormatException>(() => BuildOrderSerializer.Parse(json));

        Assert.Contains(expected, e.Message);
    }

    // Un null pasa zod y la base. Si hay un valor por defecto, vale lo mismo que si la clave no viniera.
    [Fact]
    public void Un_null_vale_lo_mismo_que_si_la_clave_no_viniera()
    {
        var bo = BuildOrderSerializer.Parse("""
            {
              "schema_version": 2, "id": null, "name": null, "civ": null, "map": null, "author": null, "description": null, "tags": null, "targets": null,
              "steps": [ { "id": null, "do": "train", "what": "villager", "assign": "sheep", "after": null, "builders": null, "at_pop": null, "at_time": null, "confirm_by": null, "group": null, "note": null } ],
              "posture": { "queue": null, "next_age": null },
              "branches": [ { "on": "under_attack", "before": null, "note": null, "steps": null } ],
              "assisted": { "opening": null, "villagers": null, "goals": null }
            }
            """);

        var expected = BuildOrderSerializer.Parse("""
            {
              "schema_version": 2,
              "steps": [ { "do": "train", "what": "villager", "assign": "sheep" } ],
              "posture": { },
              "branches": [ { "on": "under_attack" } ],
              "assisted": { }
            }
            """);
        Assert.Equal(BuildOrderSerializer.Serialize(expected), BuildOrderSerializer.Serialize(bo));
        Assert.Equal("any", bo.Civ);
        Assert.Empty(bo.Tags);
        Assert.Empty(bo.Steps[0].After);
        Assert.Empty(bo.Posture!.Queue);
        Assert.Empty(bo.Branches![0].Steps);
        Assert.Equal(120, bo.Assisted!.Villagers.Until);
        Assert.Equal("auto", bo.Assisted.Opening.Berries);
        Assert.Empty(bo.Assisted.Goals);

        Assert.Empty(BuildOrderSerializer.Parse("""{ "schema_version": 2, "steps": null }""").Steps);
        Assert.Null(BuildOrderSerializer.Parse("""{ "schema_version": 2, "posture": null, "branches": null, "assisted": null }""").Posture);
        Assert.Equal("auto", BuildOrderSerializer.Parse("""{ "schema_version": 2, "assisted": { "opening": { "berries": null } } }""").Assisted!.Opening.Berries);
    }

    // Adentro de una lista, o en lo que no tiene un valor por defecto, un null no se sabe qué quiso decir.
    [Theory]
    [InlineData("""{ "schema_version": 2, "steps": [null] }""", "steps[0]")]
    [InlineData("""{ "schema_version": 1, "steps": [null] }""", "steps[0]")]
    [InlineData("""{ "schema_version": 2, "steps": [ { "id": "a" }, null ] }""", "steps[1]")]
    [InlineData("""{ "schema_version": 2, "tags": ["a", null] }""", "tags[1]")]
    [InlineData("""{ "schema_version": 2, "steps": [ { "id": "a", "after": [null] } ] }""", "steps[0].after[0]")]
    [InlineData("""{ "schema_version": 1, "steps": [ { "id": "a", "after": [null] } ] }""", "steps[0].after[0]")]
    [InlineData("""{ "schema_version": 2, "posture": { "queue": [null] } }""", "posture.queue[0]")]
    [InlineData("""{ "schema_version": 2, "branches": [null] }""", "branches[0]")]
    [InlineData("""{ "schema_version": 2, "branches": [ { "on": null } ] }""", "branches[0].on")]
    [InlineData("""{ "schema_version": 2, "branches": [ { "on": "under_attack", "steps": [null] } ] }""", "branches[0].steps[0]")]
    [InlineData("""{ "schema_version": 2, "branches": [ { "on": "under_attack", "steps": [ { "id": "a", "after": [null] } ] } ] }""", "branches[0].steps[0].after[0]")]
    [InlineData("""{ "schema_version": 2, "assisted": { "goals": [null] } }""", "assisted.goals[0]")]
    public void Un_null_sin_valor_por_defecto_da_error_que_dice_donde(string json, string expected)
    {
        var e = Assert.Throws<BuildOrderFormatException>(() => BuildOrderSerializer.Parse(json));

        Assert.Contains(expected, e.Message);
    }

    [Theory]
    [InlineData("""{ "schema_version": 2, "name": "\ud800" }""")]
    [InlineData("""{ "schema_version": 2, "steps": [ { "id": "\ud800" } ] }""")]
    public void Un_texto_que_no_se_puede_leer_da_error(string json)
    {
        Assert.Throws<BuildOrderFormatException>(() => BuildOrderSerializer.Parse(json));
    }

    [Fact]
    public void Una_mitad_de_un_caracter_tal_cual_en_el_texto_da_error()
    {
        Assert.Throws<BuildOrderFormatException>(() => BuildOrderSerializer.Parse("{ \"schema_version\": 2, \"name\": \"" + (char)0xD800 + "\" }"));
    }

    // Lo que no se conoce se ignora (las versiones viejas abren las BO nuevas), también con nulls o claves raras adentro.
    [Fact]
    public void Lo_que_no_se_conoce_se_ignora_aunque_tenga_nulls()
    {
        var bo = BuildOrderSerializer.Parse("""
            { "schema_version": 2, "name": "x", "algo_nuevo": [null, { "a": null }], "steps": [ { "id": "a", "do": "research", "what": "loom", "otra": [null] } ] }
            """);

        Assert.Equal("a", Assert.Single(bo.Steps).Id);
    }

    [Fact]
    public void Error_al_cargar_archivo_incluye_su_nombre()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bo-rota-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ nada");
        try
        {
            var e = Assert.Throws<BuildOrderFormatException>(() => BuildOrderSerializer.Load(path));
            Assert.StartsWith(Path.GetFileName(path), e.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static TheoryData<string> Ejemplos() => new(TestData.ExampleBuildOrders);

    [Fact]
    public void El_formato_1_pasa_los_aldeanos_a_totales_en_la_fuente()
    {
        var bo = BuildOrderSerializer.Parse("""
            {
              "schema_version": 1, "name": "Vieja",
              "steps": [
                { "id": "s1", "do": "build", "what": "house", "count": 2, "builders": 3, "assign": "sheep" },
                { "id": "s2", "do": "train", "what": "villager", "count": 6, "assign": "sheep" },
                { "id": "s3", "do": "train", "what": "villager", "count": 4, "assign": "wood" }
              ]
            }
            """);

        // Antes "6 nuevos a ovejas" después de que los 3 iniciales fueron a las ovejas: ahora "9 en ovejas".
        Assert.Equal(BuildOrder.CurrentSchemaVersion, bo.SchemaVersion);
        Assert.Equal([2, 9, 4], bo.Steps.Select(s => s.Count));
        Assert.Equal([2, 6, 4], BoCoach.Core.Engine.LivePlan.Units(bo));
    }
}
