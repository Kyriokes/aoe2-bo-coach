using System.Text.Json.Nodes;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Core.Record;

namespace BoCoach.Core.Tests.Record;

/// <summary>El registro de partida (spec 2026-11-02): JSON Lines comprimido, con meta, capturas y fin.</summary>
public sealed class GameRecordFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bocoach-registro-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static JsonObject Meta() => new() { ["t"] = GameRecordKeys.Meta, ["schema"] = 1, ["game_id"] = "abc", ["civ"] = "aztecs" };

    private static RawCapture Capture(double? clock, IReadOnlyList<string>? log) => new(
        clock, 200, 180, 100, 200, 4, 0, 5, 10,
        new Dictionary<Resource, int> { [Resource.Food] = 3, [Resource.Wood] = 1 },
        new Dictionary<string, float> { ["clock"] = 0.9f, ["food"] = 0.8f },
        ["gold"], log, "Dark Age",
        [new QueueItem("villager"), new QueueItem("loom", Waiting: true)],
        [new PlayerScore(1, 120, true), new PlayerScore(2, 95, true)]);

    [Fact]
    public void Ida_y_vuelta_con_meta_capturas_y_fin()
    {
        var path = PathOf("partida.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, Meta()))
        {
            writer.Write(Capture(1, ["--House Built--"]).ToJson(logChanged: true).With("ms", 500));
            writer.Write(Capture(null, ["--House Built--"]).ToJson(logChanged: false).With("ms", 1000));
            writer.End("fin", 123.5);
        }

        var record = GameRecordReader.Load(path);

        Assert.False(record.Truncated);
        Assert.Equal("abc", record.Meta?["game_id"]?.GetValue<string>());
        Assert.Equal(3, record.Lines.Count);
        var captures = record.Captures.ToList();
        Assert.Equal(2, captures.Count);
        var first = captures[0].Capture;
        Assert.Equal(1, first.Clock);
        Assert.Equal(200, first.Food);
        Assert.Equal(3, first.VillagersOn![Resource.Food]);
        Assert.Equal(0.9f, first.Confidence["clock"]);
        Assert.Equal(["gold"], first.Clipped);
        Assert.Equal(["--House Built--"], first.LogLines);
        Assert.Equal("Dark Age", first.AgeText);
        Assert.Equal([new QueueItem("villager"), new QueueItem("loom", true)], first.Queue);
        Assert.Equal([new PlayerScore(1, 120, true), new PlayerScore(2, 95, true)], first.Scores);
        Assert.Equal(500, captures[0].Ms);
        // La segunda no trae log: son los mismos renglones que la anterior.
        Assert.Null(captures[1].Capture.Clock);
        Assert.Equal(["--House Built--"], captures[1].Capture.LogLines);
        Assert.Equal(GameRecordKeys.End, record.Lines[^1]["t"]?.GetValue<string>());
        Assert.Equal("fin", record.Lines[^1]["reason"]?.GetValue<string>());
        Assert.Equal(123.5, record.Lines[^1]["length"]?.GetValue<double>());
    }

    [Fact]
    public void Una_captura_sin_log_nuevo_no_lo_repite()
    {
        var json = Capture(1, ["a"]).ToJson(logChanged: false);
        Assert.False(json.ContainsKey("log"));
        Assert.True(Capture(1, ["a"]).ToJson(logChanged: true).ContainsKey("log"));
        Assert.False(Capture(1, null).ToJson(logChanged: true).ContainsKey("log"));
    }

    [Fact]
    public void Un_archivo_cortado_se_lee_hasta_donde_llega()
    {
        var path = PathOf("cortada.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, Meta()))
        {
            for (var i = 0; i < 200; i++)
                writer.Write(Capture(i, [$"renglón {i}"]).ToJson(logChanged: true).With("ms", i * 500));
            writer.Flush();
            using var open = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[open.Length];
            open.ReadExactly(bytes);
            File.WriteAllBytes(PathOf("mitad.jsonl.gz"), bytes[..(bytes.Length / 2)]);
        }

        var record = GameRecordReader.Load(PathOf("mitad.jsonl.gz"));

        Assert.True(record.Truncated);
        Assert.NotNull(record.Meta);
        Assert.InRange(record.Captures.Count(), 1, 199);
    }

    [Fact]
    public void Dispose_sin_End_escribe_detenido()
    {
        var path = PathOf("detenida.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, Meta()))
            writer.Write(Capture(1, []).ToJson(logChanged: true));

        var record = GameRecordReader.Load(path);
        Assert.Equal("detenido", record.Lines[^1]["reason"]?.GetValue<string>());
    }
}

file static class JsonExtensions
{
    /// <summary>Un `with` para JsonObject: le suma una clave y lo devuelve (azúcar de los tests).</summary>
    public static JsonObject With(this JsonObject json, string key, JsonNode? value)
    {
        json[key] = value;
        return json;
    }
}
