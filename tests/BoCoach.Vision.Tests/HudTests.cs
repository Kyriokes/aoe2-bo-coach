using BoCoach.Core.Data;
using BoCoach.Vision.Hud;
using BoCoach.Vision.Recording;
using BoCoach.Vision.Regions;

namespace BoCoach.Vision.Tests;

public class HudTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "rec-20261015-205040");
    private static readonly HudReader Reader = new(HudFonts.Load(Path.Combine(AppContext.BaseDirectory, "fonts")));

    private static List<(int Frame, long TimeMs, Dictionary<string, string> Values)> Golden()
    {
        var session = RecordedSession.Load(Fixture);
        var lines = File.ReadAllLines(Path.Combine(Fixture, "golden.csv"));
        var header = lines[0].Split(',');
        return lines.Skip(1)
            .Select(l => l.Split(','))
            .Select(c => (int.Parse(c[0]),
                session.Frames.First(f => f.Index == int.Parse(c[0])).TimeMs,
                header.Skip(1).Zip(c.Skip(1)).ToDictionary(p => p.First, p => p.Second)))
            .ToList();
    }

    [Fact]
    public void Lee_todo_el_HUD_de_una_captura_y_avisa_las_cajas_que_cortan_el_numero()
    {
        var session = RecordedSession.Load(Fixture);
        foreach (var (frame, _, values) in Golden())
        {
            var reading = Reader.Read(session, frame);

            Assert.True(GameClockSeconds(values["clock"]) == reading.Clock, $"reloj en {frame}");
            Assert.Equal(int.Parse(values["villagers"]), reading.Villagers);
            AssertResource(values["food"], reading.Food, reading.Clipped.Contains(RegionNames.Food), frame);
            AssertResource(values["wood"], reading.Wood, reading.Clipped.Contains(RegionNames.Wood), frame);
            AssertResource(values["gold"], reading.Gold, reading.Clipped.Contains(RegionNames.Gold), frame);
            AssertResource(values["stone"], reading.Stone, reading.Clipped.Contains(RegionNames.Stone), frame);
            Assert.Null(reading.IdleVillagers); // esta grabación no tiene la región de inactivos
        }
    }

    [Fact]
    public void El_estado_suavizado_coincide_con_lo_que_mostraba_el_juego()
    {
        var session = RecordedSession.Load(Fixture);
        var tracker = new HudTracker();
        foreach (var (frame, time, values) in Golden())
        {
            // Las capturas del golden están salteadas; en vivo cada valor se lee varias veces seguidas
            // (2 por segundo), así que cada una se pasa 3 veces, como si se viera durante 1,5 s.
            var reading = Reader.Read(session, frame);
            HudState state = null!;
            for (var k = 0; k < 3; k++)
                state = tracker.Update(time / 1000.0 + k * 0.5, reading);

            Assert.Equal(GameClockSeconds(values["clock"]), state.Clock);
            Assert.Equal(int.Parse(values["villagers"]), state.Villagers);
            Assert.Equal(double.Parse(values["stone"]), state.Stone);
            if (!values["food"].EndsWith('*'))
                Assert.Equal(double.Parse(values["food"]), state.Food);
        }
    }

    [Theory]
    [InlineData("Dark Age", Age.Dark)]
    [InlineData("Feudal Agc", Age.Feudal)]
    [InlineData("Castle Age", Age.Castle)]
    [InlineData("lmperial Age", Age.Imperial)]
    // Juego en castellano (España y Latinoamérica; captura del amigo del 2026-10-20: "Alta Edad Media").
    [InlineData("Alta Edad Media", Age.Dark)]
    [InlineData("Edad Feudal", Age.Feudal)]
    [InlineData("Edad Fcudal", Age.Feudal)]
    [InlineData("Edad de los Castillos", Age.Castle)]
    [InlineData("Ed. Castillos", Age.Castle)]
    [InlineData("Edad Imperial", Age.Imperial)]
    [InlineData("Edad lmperial", Age.Imperial)]
    [InlineData("Normal", null)]
    [InlineData("", null)]
    public void Lee_la_edad_del_texto_del_HUD(string text, Age? expected)
    {
        Assert.Equal(expected, AgeText.Parse(text));
    }

    [Fact]
    public void La_edad_cambia_recien_cuando_se_lee_igual_varias_veces()
    {
        var tracker = new HudTracker { ConfirmAge = 3 };
        var empty = new HudReading(null, null, null, null, null, null, null, []);

        Assert.Equal(Age.Dark, tracker.Update(0, empty, ageRead: Age.Dark).Age);
        Assert.Equal(Age.Dark, tracker.Update(1, empty, ageRead: Age.Feudal).Age);
        Assert.Equal(Age.Dark, tracker.Update(2, empty, ageRead: Age.Feudal).Age);
        Assert.Equal(Age.Feudal, tracker.Update(3, empty, ageRead: Age.Feudal).Age);
    }

    [Fact]
    public void Las_capturas_sin_lectura_de_edad_no_borran_la_cuenta()
    {
        // ScreenReader lee la edad cada 2 s: en las capturas del medio no hay lectura (null). En la partida
        // 20261019-202635 la edad quedó en "Dark" hasta el final aunque el HUD decía "Feudal Age" desde las 11:08.
        var tracker = new HudTracker { ConfirmAge = 3 };
        var empty = new HudReading(null, null, null, null, null, null, null, []);
        tracker.Update(0, empty, ageRead: Age.Dark);

        var t = 0.0;
        for (var read = 0; read < 3; read++)
        {
            Assert.Equal(Age.Dark, tracker.Update(t += 0.5, empty, ageRead: null).Age);
            Assert.Equal(Age.Dark, tracker.Update(t += 0.5, empty, ageRead: null).Age);
            Assert.Equal(Age.Dark, tracker.Update(t += 0.5, empty, ageRead: null).Age);
            var age = tracker.Update(t += 0.5, empty, ageRead: Age.Feudal).Age;
            Assert.Equal(read < 2 ? Age.Dark : Age.Feudal, age);
        }
    }

    private static void AssertResource(string expected, int? read, bool clipped, int frame)
    {
        if (expected.EndsWith('*'))
        {
            Assert.True(clipped, $"captura {frame}: el número estaba cortado y no avisó");
            Assert.Null(read);
        }
        else
        {
            Assert.Equal(int.Parse(expected), read);
        }
    }

    private static double GameClockSeconds(string text) =>
        Core.GameClock.TryParse(text, out var s) ? s : throw new FormatException(text);

    // Una grabación sin el mod nuevo leída con el diseño a mano: ninguna caja tiene el casillero, así que da lo mismo.
    [Fact]
    public void Una_grabacion_del_mod_viejo_se_lee_igual_con_el_diseno()
    {
        var session = RecordedSession.Load(Fixture);
        var layout = HudLayout.Load(Path.Combine(AppContext.BaseDirectory, "hud_layout.json"));
        foreach (var (frame, _, _) in Golden())
        {
            var plain = Reader.Read(region => session.Has(frame, region) ? session.Image(frame, region) : null);
            var withLayout = Reader.Read(region => session.Has(frame, region) ? session.Image(frame, region) : null, layout);

            Assert.Null(withLayout.LayoutName);
            Assert.Equal(plain with { LayoutName = null }, withLayout);
        }
    }
}
