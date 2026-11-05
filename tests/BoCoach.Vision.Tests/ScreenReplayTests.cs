using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Core.Record;
using BoCoach.Vision.Events;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Ocr;
using BoCoach.Vision.Recording;

namespace BoCoach.Vision.Tests;

/// <summary>El registro de partida: lo crudo de cada captura alcanza para volver a leerla sin imágenes.</summary>
public class ScreenReplayTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015-214512");
    private static readonly HudFonts Fonts = HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts"));
    private static readonly EventCatalog Catalog = EventCatalog.Load(Path.Combine(AppContext.BaseDirectory, "events.json"));

    private static async Task Same_as_images(OcrReader? ocr)
    {
        var session = RecordedSession.Load(Fixture);
        var reader = new ScreenReader(Fonts, ocr, Catalog, session.Hud);
        var replay = new ScreenReplay(Catalog);
        var withClock = 0;
        foreach (var frame in session.Frames)
        {
            var time = frame.TimeMs / 1000.0;
            var read = await reader.ReadAsync(r => session.Has(frame.Index, r) ? session.Image(frame.Index, r) : null, time);
            // Por el JSON del registro, como en un archivo.
            var raw = RawCapture.FromJson(reader.LastRaw!.ToJson(logChanged: true), null);
            var again = replay.Read(raw, time);

            var (a, b) = (read.ToReading(), again.ToReading());
            Assert.Equal((a.Clock, a.Food, a.Wood, a.Gold, a.Stone, a.Villagers, a.Population, a.PopulationCap, a.VillagersOn, a.IdleVillagers),
                (b.Clock, b.Food, b.Wood, b.Gold, b.Stone, b.Villagers, b.Population, b.PopulationCap, b.VillagersOn, b.IdleVillagers));
            Assert.Equal(a.NewEvents, b.NewEvents);
            Assert.Equal(a.Queue, b.Queue);
            Assert.Equal(a.Scores, b.Scores);
            Assert.Equal(read.Age, again.Age);
            if (raw.Clock is not null)
            {
                withClock++;
                Assert.True(raw.Confidence.ContainsKey("clock"));
            }
        }
        Assert.True(withClock > 0);
    }

    [Fact]
    public Task Da_lo_mismo_que_leer_las_imagenes_sin_OCR() => Same_as_images(null);

    [Fact]
    public async Task Da_lo_mismo_que_leer_las_imagenes_con_OCR()
    {
        if (OcrReader.TryCreate() is { } ocr)
            await Same_as_images(ocr);
    }

    // Por el log puede pasar el chat ("Nombre: mensaje"): lo que no es un mensaje del juego no se guarda.
    [Fact]
    public void Lo_que_no_es_un_mensaje_del_juego_se_tapa()
    {
        var matcher = new EventMatcher(Catalog);

        var scrubbed = matcher.Scrub(["Kyriokes: hola a todos", "--House Built--", "xqzt wvvk", "--Villager Created--"]);

        Assert.Equal([EventMatcher.Scrubbed, "--House Built--", EventMatcher.Scrubbed, "--Villager Created--"], scrubbed);
    }

    // Tapar no tiene que cambiar qué se reconoce: de eso depende que el registro reproduzca la partida.
    [Theory]
    [InlineData("--House Built--", "--Villager Created--")]
    [InlineData("Kyriokes: hola", "--House Built--")]
    [InlineData("--House Built--", "Kyriokes: hola")]
    [InlineData("Kyriokes: hola", "otro: chau", "--Villager Created--")]
    [InlineData("--Leather Archer Armor Research", "Complete--")]
    [InlineData("Leather Archer Armor Research", "Complete--")]
    [InlineData("Complete--", "--Archer Created--")]
    [InlineData("xqzt wvvk", "Leather Archer Armor Research", "Complete--", "hola")]
    [InlineData("--Galería tiro con arco:", "construcción finalizada--", "Kyriokes: hola")]
    public void Tapar_no_cambia_lo_que_se_reconoce(params string[] lines)
    {
        var matcher = new EventMatcher(Catalog.Merged(EventCatalog.Load(Path.Combine(AppContext.BaseDirectory, "events.es.json"))));

        var (ids, unrecognized) = matcher.MatchAll(lines);
        var (scrubbedIds, scrubbedUnrecognized) = matcher.MatchAll(matcher.Scrub(lines));

        Assert.Equal(ids, scrubbedIds);
        Assert.Equal(unrecognized, scrubbedUnrecognized);
    }

    // La privacidad: al registro compartido va la frase del catálogo, nunca lo leído. Por la caja del log pasa el chat
    // ("Ana: gg" junto a un mensaje real se reconoce como un mensaje partido en dos, "Ana: house built" se reconoce solo)
    // y el aviso de ataque lleva el nombre del jugador.
    [Theory]
    [InlineData("Ana: gg", "'--Feudal Age Research Complete--")]
    [InlineData("Ana: house built")]
    [InlineData("-Warning: You arc being attacked by Player 2 Ana!!!-")]
    [InlineData("-Warning: You arc being attacked by", "Player 2 Ana!!!-")]
    [InlineData("--House Built--", "Ana: gg", "Complete--", "Ana: villager created")]
    public void Lo_que_se_guarda_no_lleva_nada_de_lo_leido(params string[] lines)
    {
        var matcher = new EventMatcher(Catalog);

        var saved = matcher.Scrub(lines);

        Assert.DoesNotContain(saved, line => line.Contains("Ana", StringComparison.OrdinalIgnoreCase));
        Assert.All(saved, line => Assert.True(line == EventMatcher.Scrubbed || IsCatalogPhrase(line, Catalog), line));
        Assert.Equal(matcher.MatchAll(lines), matcher.MatchAll(saved), MatchAllComparer);
    }

    [Fact]
    public void Un_mensaje_largo_se_guarda_en_un_solo_renglon_con_la_frase_del_catalogo()
    {
        var matcher = new EventMatcher(Catalog);

        var saved = matcher.Scrub(["xqzt wvvk", "—Leather Archer Armor Researcb", "Complete—", "-Vtllager Created-"]);

        Assert.Equal([EventMatcher.Scrubbed, "--Leather Archer Armor Research Complete--", "--Villager Created--"], saved);
    }

    public static TheoryData<string, string?> Catalogs => new()
    {
        { "events.json", null },
        { "events.json", "events.es.json" },
        { "events.json", "events.mx.json" },
    };

    private static EventCatalog LoadCatalog(string file, string? extra)
    {
        var data = GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "game_data"));
        var catalog = EventCatalog.Load(Path.Combine(AppContext.BaseDirectory, file));
        return (extra is null ? catalog : catalog.Merged(EventCatalog.Load(Path.Combine(AppContext.BaseDirectory, extra)))).PreferringEntitiesOf(data);
    }

    // Dos eventos pueden tener la misma frase ("Arquero emplumado" de élite y sin élite), y una lectura con errores puede
    // dar uno y la frase exacta otro: el registro se reproduce leyendo lo guardado, así que tiene que dar el mismo evento.
    [Theory]
    [MemberData(nameof(Catalogs))]
    public void Lo_guardado_de_cualquier_frase_da_el_mismo_evento_que_lo_leido(string file, string? extra)
    {
        var catalog = LoadCatalog(file, extra);
        var matcher = new EventMatcher(catalog);
        var wrong = new List<string>();
        foreach (var phrase in catalog.Phrases.Values.SelectMany(p => p))
        {
            // Tal cual, y con una letra mal leída.
            foreach (var reading in new[] { phrase, Misread(phrase) })
            {
                var read = matcher.MatchAll([reading]);
                var saved = matcher.MatchAll(matcher.Scrub([reading]));
                if (!read.EventIds.SequenceEqual(saved.EventIds) || read.Unrecognized != saved.Unrecognized)
                    wrong.Add($"\"{reading}\": leído {string.Join(",", read.EventIds)}, guardado {string.Join(",", saved.EventIds)}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    // Sobre lo que leyó de verdad el OCR en una partida, con errores y todo: captura por captura, los mismos eventos.
    [Theory]
    [InlineData("event_log_ocr.tsv")]
    [InlineData("event_log_ocr_gris.tsv")]
    public void Lo_guardado_de_una_partida_real_da_los_mismos_eventos(string transcript)
    {
        var matcher = new EventMatcher(LoadCatalog("events.json", "events.es.json"));
        var frames = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015-205040", transcript))
            .Select(l => l.Split('\t'))
            .Select(p => (Frame: int.Parse(p[0]), Lines: p[1].Split('|').Select(s => s[(s.IndexOf(':') + 1)..]).ToList()))
            .ToList();

        var (liveTracker, replayTracker) = (new EventLogTracker(), new EventLogTracker());
        var (live, replay) = (new List<string>(), new List<string>());
        foreach (var (frame, lines) in frames)
        {
            var saved = matcher.Scrub(lines);
            Assert.True(saved.Count <= lines.Count, $"captura {frame}");
            live.AddRange(EventLogReader.Process(matcher, liveTracker, lines));
            replay.AddRange(EventLogReader.Process(matcher, replayTracker, saved));
        }

        Assert.NotEmpty(live);
        Assert.Equal(live, replay);
    }

    private static string Misread(string phrase)
    {
        var chars = phrase.ToCharArray();
        var letters = Enumerable.Range(0, chars.Length).Where(i => char.IsLetter(chars[i])).ToList();
        chars[letters[letters.Count / 2]] = 'x';
        return new string(chars);
    }

    private static bool IsCatalogPhrase(string line, EventCatalog catalog) =>
        catalog.Phrases.Values.Any(phrases => phrases.Any(p => line == $"--{p}--"));

    private static readonly IEqualityComparer<(IReadOnlyList<string> EventIds, int Unrecognized)> MatchAllComparer =
        EqualityComparer<(IReadOnlyList<string> EventIds, int Unrecognized)>.Create(
            (a, b) => a.EventIds.SequenceEqual(b.EventIds) && a.Unrecognized == b.Unrecognized,
            a => a.Unrecognized);

    [Fact]
    public void El_marcador_de_edad_no_es_una_edad() => Assert.Null(AgeText.Parse(ScreenReader.UnknownAge));
}
