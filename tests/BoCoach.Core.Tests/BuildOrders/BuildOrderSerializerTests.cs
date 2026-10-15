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
        Assert.Contains("\"schema_version\": 1", json);
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
}
