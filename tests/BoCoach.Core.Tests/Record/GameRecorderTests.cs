using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;
using BoCoach.Core.Record;

namespace BoCoach.Core.Tests.Record;

/// <summary>Lo que el registro guarda además de las capturas: creencia, overlay y pronósticos, sobre una partida real.</summary>
public sealed class GameRecorderTests : IDisposable
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));
    private static readonly string Readings = Path.Combine(AppContext.BaseDirectory, "fixtures", "scouts-20261015-214512.csv");

    private readonly string _dir = Directory.CreateTempSubdirectory("bocoach-recorder-").FullName;
    private readonly Lazy<GameRecordReader> _record;

    public GameRecorderTests() => _record = new(Run);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private GameRecordReader Run()
    {
        var path = Path.Combine(_dir, "partida.jsonl.gz");
        var builder = new SnapshotBuilder(TestData.GameData);
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        var meta = GameRecorder.Meta(gameId: "abc", started: DateTimeOffset.Now, app: "test", gameData: TestData.GameData.DatVersion, hud: null, hudScale: 1,
            civ: Scouts.Civ, map: null, rivalCiv: null, slot: null, language: "en", intervalMs: 500);
        using (var writer = GameRecordWriter.Create(path, meta))
        {
            var recorder = new GameRecorder(writer, TestData.GameData);
            foreach (var logged in ScreenReadingLog.Load(Readings))
            {
                var r = logged.Reading;
                recorder.Capture(new RawCapture(r.Clock, (int?)r.Food, (int?)r.Wood, (int?)r.Gold, (int?)r.Stone, r.Villagers, r.IdleVillagers, null, null,
                    null, new Dictionary<string, float>(), [], null, null, null, null), logged.TimeMs);
                if (builder.Build(r) is { } snapshot)
                {
                    coach.Update(snapshot);
                    recorder.Update(snapshot, coach, builder, logged.TimeMs);
                }
            }
            writer.End("fin", coach.Report().Length);
        }
        return GameRecordReader.Load(path);
    }

    private IEnumerable<JsonObject> Of(string type) => _record.Value.Lines.Where(l => l["t"]?.GetValue<string>() == type);

    private static JsonObject MetaOf(BuildOrder bo, DateTimeOffset started) => GameRecorder.Meta(gameId: "abc", started: started, app: "test", gameData: "v", hud: null, hudScale: 1,
        civ: "franks", map: "arabia", rivalCiv: "britons", slot: 1, language: "en", intervalMs: 500, buildOrder: bo);

    // AP-27: el código de una BO privada lleva a una sola cuenta, y la hora con el huso ayuda a reidentificar la partida.
    [Fact]
    public void La_meta_no_ata_el_registro_a_una_cuenta()
    {
        var meta = MetaOf(Scouts with { Id = "k7m2xq", Author = "Fulano" }, new DateTimeOffset(2026, 11, 5, 22, 30, 15, TimeSpan.FromHours(-3)));

        Assert.False(meta.ContainsKey("bo"));
        Assert.False(meta.ContainsKey("bo_name"));
        Assert.Equal("2026-11-06", meta["started"]!.GetValue<string>()); // en UTC y solo el día
        Assert.Equal("abc", meta["game_id"]!.GetValue<string>());
        var played = meta["build_order"]!.AsObject();
        Assert.False(played.ContainsKey("id"));
        Assert.False(played.ContainsKey("author"));
        Assert.DoesNotContain("k7m2xq", meta.ToJsonString());
    }

    // Lo que no viaja no hace falta para reproducir la partida (ReplaySession coach usa GameRecordReader.BuildOrder).
    [Fact]
    public void Un_registro_sin_el_codigo_de_la_BO_se_puede_reproducir()
    {
        var path = Path.Combine(_dir, "sin-codigo.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, MetaOf(Scouts with { Id = "k7m2xq" }, DateTimeOffset.Now)))
            writer.End("fin", 60);

        var played = GameRecordReader.Load(path).BuildOrder;

        Assert.NotNull(played);
        Assert.Equal("", played.Id);
        Assert.Equal(Scouts.Steps.Count, played.Steps.Count);
        Assert.DoesNotContain(new BuildOrderValidator(TestData.GameData, TestData.Events).Validate(played).Issues, i => i.Severity == Severity.Error);
        Assert.Equal(Scouts.Steps.Count, new Coach(played, TestData.GameData, TestData.Events).Tracker.Steps.Count);
    }

    [Fact]
    public void Show_se_escribe_solo_cuando_cambia_lo_mostrado()
    {
        var shows = Of(GameRecordKeys.Show).ToList();
        Assert.True(shows.Count > 10, $"{shows.Count} líneas show");
        Assert.All(shows, s => Assert.NotNull(s["ms"]));
        static string Shown(JsonObject s)
        {
            var copy = s.DeepClone().AsObject();
            foreach (var key in new[] { "t", "ms", "time", "tc_idle" })
                copy.Remove(key);
            return copy.ToJsonString();
        }
        foreach (var (a, b) in shows.Zip(shows.Skip(1)))
            Assert.NotEqual(Shown(a), Shown(b));
    }

    [Fact]
    public void Cada_pronostico_vencido_tiene_su_resultado_60_s_despues()
    {
        var forecasts = Of(GameRecordKeys.Forecast).ToList();
        var outcomes = Of(GameRecordKeys.Outcome).ToList();
        Assert.True(forecasts.Count > 10, $"{forecasts.Count} pronósticos");
        Assert.True(outcomes.Count >= forecasts.Count - 7, $"{outcomes.Count} resultados de {forecasts.Count}");
        foreach (var outcome in outcomes)
        {
            var at = outcome["forecast"]!.GetValue<double>();
            var forecast = forecasts.First(f => f["time"]!.GetValue<double>() == at);
            Assert.Equal(at + 60, forecast["at"]!.GetValue<double>(), 1);
            Assert.True(outcome["time"]!.GetValue<double>() >= at + 60 - 0.1);
            Assert.Equal(4, forecast["pred"]!.AsArray().Count);
            Assert.Equal(4, outcome["actual"]!.AsArray().Count);
            Assert.Equal(forecast["pred"]!.ToJsonString(), outcome["pred"]!.ToJsonString());
        }
    }

    [Fact]
    public void Las_completaciones_llevan_fuente_y_fuerza()
    {
        var done = Of(GameRecordKeys.Done).ToList();
        var house = done.First(d => d["id"]?.GetValue<string>() == "house_built");
        Assert.NotNull(house["src"]);
        Assert.NotNull(house["str"]);
        Assert.Contains(done, d => d["src"]?.GetValue<string>() == "Log");
    }

    [Fact]
    public void La_creencia_se_escribe_cuando_cambia()
    {
        var beliefs = Of(GameRecordKeys.Belief).ToList();
        Assert.Contains(beliefs, b => b["buildings"]?["house"]?.GetValue<int>() >= 1);
        static string Key(JsonObject b) => b["buildings"]!.ToJsonString() + b["research"]!.ToJsonString() + b["units"]!.ToJsonString() + b["age"] + b["built_cap"];
        foreach (var (a, b) in beliefs.Zip(beliefs.Skip(1)))
            Assert.NotEqual(Key(a), Key(b));
    }

    [Fact]
    public void Los_renglones_del_log_se_escriben_solo_cuando_cambian()
    {
        var path = Path.Combine(_dir, "log.jsonl.gz");
        static RawCapture Capture(IReadOnlyList<string>? log) =>
            new(1, null, null, null, null, null, null, null, null, null, new Dictionary<string, float>(), [], log, null, null, null);
        using (var writer = GameRecordWriter.Create(path, new JsonObject()))
        {
            var recorder = new GameRecorder(writer, TestData.GameData);
            recorder.Capture(Capture(["a"]), 0);
            recorder.Capture(Capture(["a"]), 500);
            recorder.Capture(Capture(["a", "b"]), 1000);
            recorder.Capture(Capture(null), 1500); // la región dejó de leerse
        }
        var record = GameRecordReader.Load(path);
        var lines = record.Lines.Where(l => l["t"]?.GetValue<string>() == GameRecordKeys.Capture).ToList();
        Assert.True(lines[0].ContainsKey("log"));
        Assert.False(lines[1].ContainsKey("log"));
        Assert.True(lines[2].ContainsKey("log"));
        var captures = record.Captures.ToList();
        Assert.Equal(["a"], captures[1].Capture.LogLines);
        Assert.Equal(["a", "b"], captures[2].Capture.LogLines);
        Assert.Null(captures[3].Capture.LogLines);
    }
}
