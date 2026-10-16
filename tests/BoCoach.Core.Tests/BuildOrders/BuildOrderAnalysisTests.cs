using System.Text.Json;
using BoCoach.Core.BuildOrders;

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
        Assert.All(steps, s => Assert.Equal(JsonValueKind.Number, s.GetProperty("end").ValueKind));
        Assert.InRange(r.GetProperty("feudal_click").GetDouble(), 7 * 60, 10 * 60);
        Assert.NotEmpty(r.GetProperty("timeline").EnumerateArray());
        Assert.Equal(540, r.GetProperty("targets").GetProperty("feudal_click_time").GetInt32());
        Assert.Equal(BuildOrder.CurrentSchemaVersion, r.GetProperty("build_order").GetProperty("schema_version").GetInt32());
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
    public void Un_JSON_roto_devuelve_el_error()
    {
        var r = Analyze("{ no es json");

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.StartsWith("JSON inválido", r.GetProperty("error").GetString());
    }
}
