using System.Text.Json;
using BoCoach.Core.Assisted;
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
        Assert.Equal("scouts", r.GetProperty("style").GetString());
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
        // D8: los edificios y los trabajos de cada uno.
        Assert.Equal("town_center#1", r.GetProperty("buildings")[0].GetProperty("id").GetString());
        var jobs = r.GetProperty("jobs").EnumerateArray().ToList();
        Assert.Contains(jobs, j => j.GetProperty("kind").GetString() == "produce" && j.GetProperty("what").GetString() == "villager" && j.GetProperty("building").GetString() == "town_center#1");
        Assert.Contains(jobs, j => j.GetProperty("kind").GetString() == "build" && j.GetProperty("what").GetString() == "house");
        // Cada bloqueo dice si es una espera de algo que ya viene (el campamento espera al primer leñador).
        var blocks = r.GetProperty("blocks").EnumerateArray().ToList();
        Assert.All(blocks, b => Assert.Contains(b.GetProperty("expected").ValueKind, new[] { JsonValueKind.True, JsonValueKind.False }));
        Assert.Contains(blocks, b => b.GetProperty("reason").GetString() == "villagers" && b.GetProperty("expected").GetBoolean());
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

    // D8: el arranque de la partida para la civ de la BO, para la caja "Inicio" del editor de la web.
    [Theory]
    [InlineData("any", 200, 200, 4, 5, 0, true)]
    [InlineData("chinese", 0, 150, 7, 15, 3, true)]
    [InlineData("huns", 200, 100, 4, 200, 0, false)]
    public void Trae_el_arranque_de_la_civ(string civ, int food, int wood, int population, int cap, int sheep, bool houses)
    {
        var r = Analyze($$"""{ "schema_version": 2, "name": "x", "civ": "{{civ}}", "steps": [ { "id": "a", "do": "train", "what": "villager", "count": 6, "assign": "sheep" } ] }""");

        var start = r.GetProperty("start");
        Assert.Equal(food, start.GetProperty("food").GetInt32());
        Assert.Equal(wood, start.GetProperty("wood").GetInt32());
        Assert.Equal(100, start.GetProperty("gold").GetInt32());
        Assert.Equal(200, start.GetProperty("stone").GetInt32());
        Assert.Equal(population, start.GetProperty("population").GetInt32());
        Assert.Equal(cap, start.GetProperty("population_cap").GetInt32());
        Assert.Equal(3, start.GetProperty("idle_villagers").GetInt32());
        Assert.Equal(sheep, start.GetProperty("villagers").TryGetProperty("sheep", out var s) ? s.GetInt32() : 0);
        Assert.Equal(houses, start.GetProperty("houses").GetBoolean());
    }

    [Fact]
    public void Un_JSON_roto_devuelve_el_error()
    {
        var r = Analyze("{ no es json");

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.StartsWith("JSON inválido", r.GetProperty("error").GetString());
    }

    private static string AssistedJson(int until, string steps = "[]") =>
        "{ \"schema_version\": 2, \"id\": \"asis\", \"name\": \"Asistida\", \"civ\": \"franks\", \"steps\": " + steps +
        ", \"assisted\": { \"villagers\": { \"until\": " + until + " }, \"goals\": [ { \"age\": \"feudal_age\" } ] } }";

    // D12 (e): la web llama a Analyze con la BO asistida; el Core genera los pasos y los analiza.
    [Fact]
    public void Una_BO_asistida_se_genera_y_se_analiza()
    {
        var r = Analyze(AssistedJson(60));

        Assert.True(r.GetProperty("valid").GetBoolean());
        var steps = r.GetProperty("steps").EnumerateArray().ToList();
        Assert.NotEmpty(steps);
        var assisted = r.GetProperty("assisted");
        Assert.Equal(AssistedGenerator.Version, assisted.GetProperty("version").GetInt32());
        Assert.False(assisted.GetProperty("regenerated").GetBoolean());
        Assert.All(steps, s => Assert.False(string.IsNullOrEmpty(assisted.GetProperty("reasons").GetProperty(s.GetProperty("id").GetString()!).GetString())));
        Assert.Equal(JsonValueKind.Number, assisted.GetProperty("goals")[0].GetProperty("end").ValueKind);
        Assert.Empty(assisted.GetProperty("problems").EnumerateArray());
        var saved = r.GetProperty("build_order");
        Assert.Equal(steps.Count, saved.GetProperty("steps").GetArrayLength());
        Assert.Equal(60, saved.GetProperty("assisted").GetProperty("villagers").GetProperty("until").GetInt32());
    }

    [Fact]
    public void Con_assisted_mal_armado_no_se_genera()
    {
        var r = Analyze(AssistedJson(0));

        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.False(r.GetProperty("valid").GetBoolean());
        Assert.Contains(r.GetProperty("issues").EnumerateArray(), i => i.GetProperty("message").GetString()!.Contains("assisted.villagers.until"));
        Assert.Empty(r.GetProperty("steps").EnumerateArray());
    }

    [Fact]
    public void Con_pasos_viejos_avisa_que_se_regenero()
    {
        var old = Analyze(AssistedJson(60, "[ { \"id\": \"v\", \"do\": \"train\", \"what\": \"villager\", \"count\": 4, \"assign\": \"sheep\" } ]"));
        Assert.True(old.GetProperty("assisted").GetProperty("regenerated").GetBoolean());

        var generated = Analyze(Analyze(AssistedJson(60)).GetProperty("build_order").GetRawText());
        Assert.False(generated.GetProperty("assisted").GetProperty("regenerated").GetBoolean());
    }
}
