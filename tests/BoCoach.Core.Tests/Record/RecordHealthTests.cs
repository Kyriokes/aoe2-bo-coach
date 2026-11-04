using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Core.Record;

namespace BoCoach.Core.Tests.Record;

/// <summary>El resumen de los registros que llegan de otros jugadores: qué se leyó bien en cada partida y qué contaron.</summary>
public sealed class RecordHealthTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("bocoach-salud-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static JsonObject Meta(string id = "abc") => new()
    {
        ["t"] = GameRecordKeys.Meta,
        ["schema"] = 1,
        ["game_id"] = id,
        ["app"] = "v0.9.90 · 1234567 · 04/11 10:00",
        ["civ"] = "persians",
        ["rival_civ"] = "mongols",
        ["map"] = "Arabia",
        ["language"] = "en",
        ["hud"] = "bocoach-minimo-2026-11",
        ["hud_scale"] = 1.333,
        ["interval_ms"] = 500,
    };

    private static readonly Dictionary<Resource, int> AllResources = new()
    {
        [Resource.Food] = 3,
        [Resource.Wood] = 1,
        [Resource.Gold] = 0,
        [Resource.Stone] = 0,
    };

    private static JsonObject Capture(double? clock, int? wood = 180, int? villagers = 4, IReadOnlyDictionary<Resource, int>? on = null, int? population = 5,
        IReadOnlyList<string>? log = null, IReadOnlyList<QueueItem>? queue = null, IReadOnlyList<PlayerScore>? scores = null) =>
        new RawCapture(clock, 200, wood, 100, 200, villagers, 0, population, 10, on, new Dictionary<string, float>(), [], log, null, queue, scores).ToJson(log is not null);

    private string WriteGame(string name)
    {
        var path = PathOf(name);
        using var writer = GameRecordWriter.Create(path, Meta());
        writer.Write(Capture(10, on: AllResources, log: ["--House Built--", "?-", "--Villager Created--", "?-"], queue: [new QueueItem("villager"), new QueueItem("?")],
            scores: [new PlayerScore(1, 120, true)]));
        writer.Write(Capture(10.5, wood: null, villagers: null, population: null));
        writer.Write(Capture(11));
        writer.Write(Capture(null, wood: null, villagers: null, population: null));
        writer.Write(new JsonObject { ["t"] = GameRecordKeys.Done, ["id"] = "house_built", ["src"] = "Log", ["str"] = "Avisada", ["time"] = 10.0 });
        writer.Write(new JsonObject { ["t"] = GameRecordKeys.Done, ["id"] = "archery_range_built", ["src"] = "Queue", ["str"] = "Observada", ["time"] = 11.0 });
        writer.Write(new JsonObject { ["t"] = GameRecordKeys.Flag, ["ms"] = 750, ["time"] = 625.0 });
        writer.End("fin", 754);
        return path;
    }

    [Fact]
    public void Cuenta_cuanto_se_leyo_de_cada_cosa_sobre_las_capturas_con_reloj()
    {
        var path = WriteGame("partida.jsonl.gz");

        var health = RecordHealth.From(GameRecordReader.Load(path), "partida.jsonl.gz");

        Assert.Equal(4, health.Captures);
        Assert.Equal(0.75, health.ClockRead);
        Assert.Equal(2 / 3.0, health.StocksRead, 3);
        Assert.Equal(2 / 3.0, health.VillagersRead, 3);
        Assert.Equal(1 / 3.0, health.VillagersOnRead, 3);
        Assert.Equal(2 / 3.0, health.PopulationRead, 3);
        Assert.Equal(4, health.LogLines);
        Assert.Equal(2, health.LogUnknown);
        Assert.Equal(2, health.QueueSlots);
        Assert.Equal(1, health.QueueUnknown);
        Assert.Equal(754, health.Length);
        Assert.Equal("fin", health.EndReason);
        Assert.Equal(new Dictionary<string, int> { ["Log"] = 1, ["Queue"] = 1 }, health.DoneBySource);
        Assert.Equal([625.0], health.Flags);
    }

    // El panel de puntajes se lee cada 2 s: una lectura cada 4 capturas ya es el 100 %.
    [Fact]
    public void Los_puntajes_se_miden_contra_las_veces_que_se_intento_leerlos()
    {
        var path = PathOf("puntajes.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, Meta()))
        {
            for (var i = 0; i < 8; i++)
                writer.Write(Capture(i, scores: i % 4 == 0 ? [new PlayerScore(1, 120, true)] : null));
            writer.End("fin", 8);
        }

        Assert.Equal(1, RecordHealth.From(GameRecordReader.Load(path), "puntajes.jsonl.gz").ScoresRead);
    }

    // Un registro cortado (la app se cerró de golpe) no dice cuánto duró: vale el último reloj.
    [Fact]
    public void Sin_el_final_la_duracion_es_el_ultimo_reloj()
    {
        var path = PathOf("cortado.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, Meta()))
        {
            writer.Write(Capture(90));
            writer.Write(Capture(91.5));
        }

        var health = RecordHealth.From(GameRecordReader.Load(path), "cortado.jsonl.gz");

        Assert.Equal(91.5, health.Length);
    }

    [Fact]
    public void La_carpeta_junta_cada_nota_con_su_partida_y_baja_a_las_subcarpetas()
    {
        var folder = Directory.CreateDirectory(PathOf("instalacion1")).FullName;
        WriteGame(Path.Combine("instalacion1", "partida.jsonl.gz"));
        GameRecordNote.Write(Path.Combine(folder, "nota.jsonl.gz"), "n1", "abc", DateTimeOffset.Now, "v0.9.90", "pidió una casa de más");

        var report = RecordHealthReport.Scan(_dir);

        var game = Assert.Single(report.Games);
        Assert.Equal(["pidió una casa de más"], game.Notes);
        Assert.Empty(report.LooseNotes);
    }

    [Fact]
    public void Una_nota_de_una_partida_que_no_esta_se_muestra_igual()
    {
        GameRecordNote.Write(PathOf("nota.jsonl.gz"), "n1", "otra", DateTimeOffset.Now, "v0.9.90", "algo anduvo mal");

        var report = RecordHealthReport.Scan(_dir);

        Assert.Empty(report.Games);
        Assert.Equal(["algo anduvo mal"], report.LooseNotes);
    }

    // Cualquiera puede armar una identidad y mandar cualquier cosa: nada de eso corta el resumen.
    [Theory]
    [InlineData("no es gzip")]
    [InlineData("")]
    public void Un_archivo_roto_se_cuenta_y_no_corta_nada(string contents)
    {
        File.WriteAllText(PathOf("roto.jsonl.gz"), contents);
        WriteGame("partida.jsonl.gz");

        var report = RecordHealthReport.Scan(_dir);

        Assert.Single(report.Games);
        Assert.Equal(1, report.Unreadable);
    }

    [Fact]
    public void Un_registro_que_no_empieza_con_su_meta_se_cuenta_como_roto()
    {
        using (var gzip = new GZipStream(File.Create(PathOf("raro.jsonl.gz")), CompressionMode.Compress))
            gzip.Write(Encoding.UTF8.GetBytes("""{"t":7}""" + "\n"));

        var report = RecordHealthReport.Scan(_dir);

        Assert.Empty(report.Games);
        Assert.Equal(1, report.Unreadable);
    }

    [Fact]
    public void Los_tipos_raros_adentro_de_un_registro_se_ignoran()
    {
        var path = PathOf("raro.jsonl.gz");
        using (var writer = GameRecordWriter.Create(path, new JsonObject { ["game_id"] = 5, ["civ"] = 3, ["hud_scale"] = "mucha" }))
        {
            writer.Write(new JsonObject { ["t"] = 7, ["time"] = "ayer" });
            writer.Write(new JsonObject { ["t"] = GameRecordKeys.Capture, ["clock"] = "ya", ["log"] = 4, ["queue"] = "x" });
            writer.Write(new JsonObject { ["t"] = GameRecordKeys.Flag, ["time"] = "ayer" });
            writer.Write(new JsonObject { ["t"] = GameRecordKeys.Done, ["src"] = 3 });
        }

        var game = Assert.Single(RecordHealthReport.Scan(_dir).Games);

        Assert.Null(game.GameId);
        Assert.Null(game.Civ);
        Assert.Empty(game.Flags);
        Assert.Equal(0, game.ClockRead);
        Assert.NotEmpty(RecordHealthReport.Scan(_dir).Lines());
    }

    [Fact]
    public void El_texto_dice_lo_que_hace_falta_mirar()
    {
        WriteGame("partida.jsonl.gz");
        GameRecordNote.Write(PathOf("nota.jsonl.gz"), "n1", "abc", DateTimeOffset.Now, "v0.9.90", "pidió una casa de más");

        var text = string.Join("\n", RecordHealthReport.Scan(_dir).Lines());

        Assert.Contains("1 partida", text);
        Assert.Contains("persians", text);
        Assert.Contains("mongols", text);
        Assert.Contains("Arabia", text);
        Assert.Contains("12:34", text);
        Assert.Contains("v0.9.90", text);
        Assert.Contains("1,33", text);
        Assert.Contains("reloj 75 %", text);
        Assert.Contains("marcas: 10:25", text);
        Assert.Contains("pidió una casa de más", text);
        Assert.Contains("FLOJO", text);
    }

    [Fact]
    public void Lo_que_escribio_otro_no_puede_mandarle_nada_a_la_terminal()
    {
        WriteGame("partida.jsonl.gz");
        GameRecordNote.Write(PathOf("nota.jsonl.gz"), "n1", "abc", DateTimeOffset.Now, "v0.9.90", "hola\u001b[2Jchau\notro renglón");

        var text = string.Join("\n", RecordHealthReport.Scan(_dir).Lines());

        Assert.DoesNotContain('\u001b', text);
        Assert.Contains("hola[2Jchau otro renglón", text);
    }
}
