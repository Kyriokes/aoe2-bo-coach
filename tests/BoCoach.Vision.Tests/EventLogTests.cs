using BoCoach.Core.Data;
using BoCoach.Vision.Events;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Recording;
using Xunit.Abstractions;

namespace BoCoach.Vision.Tests;

public class EventLogTests(ITestOutputHelper output)
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015");
    private static readonly EventCatalog Catalog = EventCatalog.Load(Path.Combine(AppContext.BaseDirectory, "events.json"));
    private static readonly EventMatcher Matcher = new(Catalog);

    [Theory]
    [InlineData("-IJock Built--", "dock_built")]
    [InlineData("......Ga11ey Created-", "galley_created")]
    [InlineData("-Mmmg camp Bunt-", "mining_camp_built")]
    [InlineData("-Horsc Collar Rcscarcb Complete--", "horse_collar_researched")]
    [InlineData("i-Blacksmit11 Built--", "blacksmith_built")]
    [InlineData("-Slable Built-", "stable_built")]
    [InlineData("-Villager created", "villager_created")]
    public void Reconoce_renglones_con_errores_del_OCR(string line, string expected)
    {
        Assert.Equal(expected, Matcher.Match(line));
    }

    [Theory]
    [InlineData("-Warning: You arc being attacked by")]
    [InlineData("Player 2 Timur Qutlugh!!!-")]
    [InlineData("Age of Empires II: Definitive Editior")]
    [InlineData("Complete--")]
    public void Ignora_lo_que_no_es_un_evento_conocido(string line)
    {
        Assert.Null(Matcher.Match(line));
    }

    [Fact]
    public void Un_mensaje_largo_partido_en_dos_renglones_se_junta()
    {
        Assert.Equal(["scale_mail_armor_researched"], Matcher.MatchLines(["-Scale Mail Armor Researcb", "Complete--"]));
    }

    private static List<string> Run(EventLogTracker tracker, params string[][] frames) =>
        frames.SelectMany(f => tracker.Update(f)).ToList();

    [Fact]
    public void Mensajes_nuevos_entran_por_abajo_y_los_viejos_se_van_por_arriba()
    {
        var tracker = new EventLogTracker();

        var news = Run(tracker,
            ["house_built"], ["house_built"],
            ["house_built", "mill_built"], ["house_built", "mill_built"],
            ["mill_built"], ["mill_built"],
            ["mill_built", "dock_built"], ["mill_built", "dock_built"]);

        Assert.Equal(["house_built", "mill_built", "dock_built"], news);
    }

    [Fact]
    public void Un_mensaje_repetido_se_mueve_al_final_y_cuenta_de_nuevo()
    {
        var tracker = new EventLogTracker();

        var news = Run(tracker,
            ["villager_created", "mill_built"], ["villager_created", "mill_built"],
            ["mill_built", "villager_created"], ["mill_built", "villager_created"]);

        Assert.Equal(["villager_created", "mill_built", "villager_created"], news);
    }

    [Fact]
    public void Dos_mensajes_iguales_seguidos_son_dos_eventos()
    {
        var tracker = new EventLogTracker();

        var news = Run(tracker,
            ["house_built"], ["house_built"],
            ["house_built", "house_built"], ["house_built", "house_built"]);

        Assert.Equal(["house_built", "house_built"], news);
    }

    [Fact]
    public void Un_parpadeo_de_una_captura_no_cuenta()
    {
        var tracker = new EventLogTracker();

        var news = Run(tracker,
            ["house_built"], ["house_built"],
            [], ["house_built"], [], ["house_built"], ["house_built"]);

        Assert.Equal(["house_built"], news);
    }

    [Fact]
    public void El_log_se_da_por_vacio_recien_despues_de_varias_capturas()
    {
        var tracker = new EventLogTracker { EmptyFrames = 6 };
        Run(tracker, ["house_built"], ["house_built"]);

        // 4 capturas sin leer nada (el mensaje se está desvaneciendo) y vuelve a leerse: no es nuevo.
        Assert.Empty(Run(tracker, [], [], [], [], ["house_built"], ["house_built"]));

        // 6 capturas vacías: el log se vació de verdad, así que el mensaje que aparece es nuevo.
        Assert.Equal(["house_built"], Run(tracker, [], [], [], [], [], [], ["house_built"], ["house_built"]));
    }

    [Fact]
    public void Renglones_mal_leidos_no_borran_mensajes()
    {
        var tracker = new EventLogTracker();
        Run(tracker, ["dock_built", "feudal_age_researched"], ["dock_built", "feudal_age_researched"]);

        // El renglón de feudal se lee mal (no se reconoce) durante varias capturas y después vuelve.
        foreach (var _ in Enumerable.Range(0, 5))
            Assert.Empty(tracker.Update(["dock_built"], hasUnrecognized: true));
        Assert.Empty(Run(tracker, ["dock_built", "feudal_age_researched"], ["dock_built", "feudal_age_researched"]));
    }

    /// <summary>
    /// Criterio de la Fase 7 para el log: sobre la transcripción del OCR de una partida real (737 capturas con
    /// texto), más del 95 % de los eventos revisados a mano se detectan, sin duplicados ni falsos.
    /// </summary>
    [Fact]
    public void Detecta_los_eventos_de_la_partida_grabada_sin_duplicados()
    {
        var transcript = File.ReadLines(Path.Combine(Fixture, "event_log_ocr.tsv"))
            .Select(l => l.Split('\t'))
            .ToDictionary(p => int.Parse(p[0]), p => p[1].Split('|').Select(s => s[(s.IndexOf(':') + 1)..]).ToList());
        var golden = File.ReadLines(Path.Combine(Fixture, "event_log_golden.csv")).Skip(1)
            .Select(l => l.Split(','))
            .Select(p => (Frame: int.Parse(p[0]), Event: p[1]))
            .ToList();
        var frames = RecordedSession.Load(Fixture);
        var last = File.ReadLines(Path.Combine(Fixture, "event_log_ocr.tsv")).Select(l => int.Parse(l.Split('\t')[0])).Max();

        var tracker = new EventLogTracker();
        var detected = new List<(int Frame, string Event)>();
        for (var frame = 0; frame <= last; frame++)
        {
            foreach (var e in EventLogReader.Process(Matcher, tracker, transcript.GetValueOrDefault(frame) ?? []))
                detected.Add((frame, e));
        }

        // Cada evento detectado tiene que corresponder a uno del golden (mismo id, ±3 capturas), una sola vez.
        var unmatched = golden.ToList();
        var falsePositives = new List<(int, string)>();
        foreach (var d in detected)
        {
            var match = unmatched.FindIndex(g => g.Event == d.Event && Math.Abs(g.Frame - d.Frame) <= 3);
            if (match >= 0)
                unmatched.RemoveAt(match);
            else
                falsePositives.Add(d);
        }

        output.WriteLine($"{golden.Count - unmatched.Count}/{golden.Count} eventos detectados, {falsePositives.Count} de más");
        foreach (var m in unmatched)
            output.WriteLine($"  faltó: {m}");
        foreach (var f in falsePositives)
            output.WriteLine($"  de más: {f}");
        Assert.True(golden.Count - unmatched.Count >= Math.Ceiling(golden.Count * 0.95));
        Assert.True(falsePositives.Count <= Math.Floor(golden.Count * 0.05));
        Assert.NotEmpty(frames.Frames);
    }

    [Theory]
    [InlineData(317, new[] { "dock_built", "feudal_age_researched" })]
    [InlineData(556, new[] { "fishing_ship_created", "villager_created", "man_at_arms_created", "blacksmith_built" })]
    public async Task OCR_de_capturas_reales_del_log(int frame, string[] expected)
    {
        var ocr = OcrReader.TryCreate();
        if (ocr is null)
            return; // esta máquina no tiene el OCR en inglés instalado (puede pasar en CI)
        var image = RecordedSession.Load(Fixture).Image(frame, "event_log");

        var lines = await ocr.ReadLinesAsync(image);
        var ids = Matcher.MatchLines(lines.Select(l => l.Text).ToList());

        output.WriteLine(string.Join(" | ", lines.Select(l => $"{l.Top:0}:{l.Text}")));
        Assert.All(expected, e => Assert.Contains(e, ids));
        Assert.Equal(lines.OrderBy(l => l.Top).Select(l => l.Text), lines.Select(l => l.Text));
    }
}
