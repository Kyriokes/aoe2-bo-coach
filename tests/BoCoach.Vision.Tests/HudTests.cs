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

    // Hasta que se confirma la primera lectura del reloj no hay hora: lo que se ve en esa captura no se puede fechar (la
    // cola se tomaría como vista en el 0:00, a mitad de partida).
    [Fact]
    public void Con_la_primera_lectura_del_reloj_sin_confirmar_no_hay_hora_para_la_cola()
    {
        var tracker = new HudTracker();
        var reading = new HudReading(370, null, null, null, null, null, null, []);
        IReadOnlyList<Core.Engine.QueueItem> queue = [new("loom")];

        var first = tracker.Update(0, reading) with { Queue = queue };
        var second = tracker.Update(0.5, reading) with { Queue = queue };

        Assert.True(first.ClockRead);
        Assert.Null(first.Clock);
        Assert.Null(first.ToReading().Queue);
        Assert.Equal(370, second.Clock);
        Assert.Equal(queue, second.ToReading().Queue);
    }

    // De punta a punta, como en LiveCoach: el coach arranca a mitad de partida (20:00, 20 aldeanos) y unas capturas leen
    // una hora de más ("1:20:00", un cuadro tapando el reloj). Si son las primeras, el tiempo del coach llega a la hora
    // real a los pocos segundos de verla, aunque el cuadro dure (antes quedaba en 0 o clavado una hora); si el reloj bueno
    // ya estaba, la racha mala no lo mueve.
    [Theory]
    [InlineData(0, 2)]
    [InlineData(0, 8)]
    [InlineData(0, 40)]
    [InlineData(2, 8)]
    [InlineData(2, 40)]
    public void Con_la_hora_tapada_al_arrancar_el_tiempo_del_coach_llega_al_real(int from, int bad)
    {
        var builder = new Core.Engine.SnapshotBuilder(GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "game_data")));
        var tracker = new HudTracker();

        for (var i = 0; i < 120; i++)
        {
            var t = i * 0.5;
            var truth = 1200 + Math.Floor(1.7 * t);
            var covered = i >= from && i < from + bad;
            var state = tracker.Update(t, new HudReading(covered ? truth + 3600 : truth, 300, 180, 100, 200, 20, 0, []));
            var time = builder.Build(state.ToReading())?.Time;
            if (from == 0 && t >= bad * 0.5 + 5)
                Assert.Equal(truth, time);
            if (from > 0 && t > 0)
            {
                Assert.InRange(time!.Value, 0, truth);
                if (!covered)
                    Assert.Equal(truth, time);
            }
        }
    }

    // De punta a punta también: el coach arranca a las 35:22 con 20 aldeanos (inicio estimado a las 28:17, tiempo 7:05) y
    // un cuadro hace leer la hora 10 minutos menos mientras el juego sigue. El reloj baja (un reemplazo) y, cuando vuelve
    // la hora buena, se deshace: el tiempo del coach vuelve al de antes, con el inicio de antes, y las dos correcciones
    // quedan marcadas en el snapshot. Antes el inicio quedaba recalculado con la hora baja (0) y el tiempo en 35:35 en vez
    // de 7:18, y la subida no se marcaba (la BO quedaba abandonada por el salto).
    [Fact]
    public void Si_el_reloj_deshace_un_reemplazo_equivocado_el_tiempo_del_coach_vuelve_al_de_antes()
    {
        var builder = new Core.Engine.SnapshotBuilder(GameDataLoader.LoadLatest(Path.Combine(AppContext.BaseDirectory, "game_data")));
        var tracker = new HudTracker();
        var corrections = 0;

        for (var i = 0; i < 120; i++)
        {
            var t = i * 0.5;
            var truth = 2122 + Math.Floor(1.7 * t);
            var covered = i >= 2 && i < 10;
            var state = tracker.Update(t, new HudReading(covered ? truth - 600 : truth, 300, 180, 100, 200, 20, 0, []));
            if (builder.Build(state.ToReading()) is not { } snapshot)
                continue;
            if (snapshot.ClockCorrected)
                corrections++;
            if (t >= 10)
                Assert.Equal(truth - 1697, snapshot.Time);
        }
        Assert.Equal(1697.0, builder.StartOffset);
        Assert.Equal(2, corrections);
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
