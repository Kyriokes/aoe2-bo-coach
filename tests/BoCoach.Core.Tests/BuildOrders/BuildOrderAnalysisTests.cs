using System.Text.Json;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.BuildOrders;

/// <summary>El JSON que la web recibe del Core en WebAssembly.</summary>
public class BuildOrderAnalysisTests
{
    private static JsonElement Analyze(string json) =>
        JsonDocument.Parse(BuildOrderAnalysis.Analyze(json, TestData.GameData, TestData.Events)).RootElement;

    [Fact]
    public void Una_BO_valida_trae_pasos_descritos_y_la_simulacion()
    {
        var r = Analyze(File.ReadAllText(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json")));

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.True(r.GetProperty("valid").GetBoolean());
        Assert.Equal("Scouts Feudal (22 pop)", r.GetProperty("name").GetString());
        var steps = r.GetProperty("steps").EnumerateArray().ToList();
        Assert.Equal(17, steps.Count);
        Assert.Equal("Construir 2 casas (3 constructores) → ovejas", steps[0].GetProperty("text").GetString());
        // D8: las unidades de cada paso. La BO vieja (formato 1) dice "6 nuevos a ovejas" después de las 2 casas: con los
        // 3 constructores que ya están ahí, count queda en 9 (el total) y units en 6.
        Assert.Equal(2, steps[0].GetProperty("units").GetInt32());
        Assert.Equal(9, steps[1].GetProperty("count").GetInt32());
        Assert.Equal(6, steps[1].GetProperty("units").GetInt32());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("map").ValueKind);
        Assert.All(steps, s => Assert.Equal(JsonValueKind.Number, s.GetProperty("end").ValueKind));
        Assert.InRange(r.GetProperty("feudal_click").GetDouble(), 7 * 60, 10 * 60);
        Assert.NotEmpty(r.GetProperty("timeline").EnumerateArray());
        Assert.Equal(540, r.GetProperty("targets").GetProperty("feudal_click_time").GetInt32());
        Assert.Equal(BuildOrder.CurrentSchemaVersion, r.GetProperty("build_order").GetProperty("schema_version").GetInt32());
        // D2: la postura de cierre deducida (la BO no la trae).
        var posture = r.GetProperty("posture");
        Assert.Equal("aldeanos y exploradores siempre en cola", posture.GetProperty("text").GetString());
        Assert.Equal("Edad de los Castillos", posture.GetProperty("next_age").GetString());
        Assert.True(posture.GetProperty("derived").GetBoolean());
        Assert.Empty(r.GetProperty("branches").EnumerateArray());
    }

    [Fact]
    public void Una_BO_con_errores_trae_los_problemas_y_no_se_simula()
    {
        var r = Analyze("""{ "schema_version": 1, "name": "x", "steps": [ { "id": "a", "do": "train", "what": "dragon" } ] }""");

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.False(r.GetProperty("valid").GetBoolean());
        var issue = Assert.Single(r.GetProperty("issues").EnumerateArray());
        Assert.Equal("error", issue.GetProperty("severity").GetString());
        Assert.Equal("a", issue.GetProperty("step_id").GetString());
        Assert.False(r.TryGetProperty("timeline", out _));
        Assert.Equal(JsonValueKind.Null, r.GetProperty("steps")[0].GetProperty("start").ValueKind);
    }

    [Fact]
    public void Con_mapas_escribe_el_mapa_y_avisa_lo_que_no_tiene()
    {
        var maps = Maps.Load(Path.Combine(AppContext.BaseDirectory, "maps.json"));
        var json = """{ "schema_version": 2, "name": "Pesca", "map": "arabia", "steps": [ { "id": "a", "do": "train", "what": "villager", "count": 4, "assign": "fish" } ] }""";

        var r = JsonDocument.Parse(BuildOrderAnalysis.Analyze(json, TestData.GameData, TestData.Events, maps)).RootElement;

        Assert.True(r.GetProperty("valid").GetBoolean());
        Assert.Equal("arabia", r.GetProperty("map").GetString());
        Assert.Equal("arabia", r.GetProperty("build_order").GetProperty("map").GetString());
        var issue = Assert.Single(r.GetProperty("issues").EnumerateArray());
        Assert.Equal("warning", issue.GetProperty("severity").GetString());
        Assert.Contains("Arabia no tiene pesca", issue.GetProperty("message").GetString());
        // move y research valen 1 unidad; un paso de aldeanos con el total ya alcanzado, 0.
        var moved = Analyze("""{ "schema_version": 2, "name": "x", "steps": [ { "id": "a", "do": "move", "count": 2, "from": "sheep", "assign": "wood" }, { "id": "b", "do": "train", "what": "villager", "count": 6, "assign": "sheep" }, { "id": "c", "do": "train", "what": "villager", "count": 3, "assign": "sheep" } ] }""");
        Assert.Equal(1, moved.GetProperty("steps")[0].GetProperty("units").GetInt32());
        Assert.Equal(6, moved.GetProperty("steps")[1].GetProperty("units").GetInt32());
        Assert.Equal(0, moved.GetProperty("steps")[2].GetProperty("units").GetInt32());
    }

    [Fact]
    public void Un_JSON_roto_devuelve_el_error()
    {
        var r = Analyze("{ no es json");

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.StartsWith("JSON inválido", r.GetProperty("error").GetString());
    }
}
