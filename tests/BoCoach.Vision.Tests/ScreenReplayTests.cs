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

    [Fact]
    public void El_marcador_de_edad_no_es_una_edad() => Assert.Null(AgeText.Parse(ScreenReader.UnknownAge));
}
