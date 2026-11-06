using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// El reloj del juego no siempre arranca con la partida: en la 20261017-163738 el jugador se quedó 36 minutos quieto
/// con los 3 aldeanos iniciales (reloj 36:26 con 6 aldeanos) y todos los objetivos de la BO quedaban atrasados. El
/// tiempo de los snapshots cuenta desde que el jugador empieza a jugar (<see cref="SnapshotBuilder.StartOffset"/>).
/// </summary>
public class GameStartTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly ResourceAmounts Start = PlanStart.Standard.Stock;

    private static ScreenReading Reading(double clock, ResourceAmounts stock, int villagers) =>
        new(clock, stock.Food, stock.Wood, stock.Gold, stock.Stone, villagers, Population: null, PopulationCap: null, NewEvents: []);

    private static GameSnapshot Build(SnapshotBuilder builder, double clock, ResourceAmounts stock, int villagers) =>
        builder.Build(Reading(clock, stock, villagers)) ?? throw new InvalidOperationException("sin snapshot");

    [Fact]
    public void El_tiempo_cuenta_desde_que_el_jugador_actua_si_antes_estuvo_quieto()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        var queued = Start with { Food = 150 };

        Assert.Equal(0.0, Build(builder, 1, Start, 3).Time);
        Assert.Equal(0.0, Build(builder, 1200, Start, 3).Time);
        Assert.Equal(0.0, Build(builder, 2186, queued, 3).Time); // primer aldeano encolado a las 36:26
        Assert.Equal(7.0, Build(builder, 2193, queued, 3).Time);
        Assert.Equal(2186.0, builder.StartOffset);
    }

    [Fact]
    public void Si_actua_enseguida_el_reloj_queda_como_esta()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        Build(builder, 1, Start, 3);
        Build(builder, 2, Start, 3);

        Assert.Equal(4.0, Build(builder, 4, Start with { Food = 100 }, 3).Time);
        Assert.Equal(30.0, Build(builder, 30, Start with { Food = 50 }, 4).Time);
        Assert.Equal(0.0, builder.StartOffset);
    }

    [Fact]
    public void A_mitad_de_una_partida_casi_toda_parada_se_estima_el_inicio_por_los_aldeanos()
    {
        // Las primeras capturas de la 20261017-163738 no sirven: la primera lectura es a las 36:26 con 6 aldeanos.
        // Tres aldeanos nuevos son 75 s de centro urbano; el resto del reloj fue idle.
        var builder = new SnapshotBuilder(TestData.GameData);

        var snapshot = Build(builder, 2186, Start with { Food = 230, Wood = 160 }, 6);

        Assert.Equal(75.0, snapshot.Time, 1.0);
        Assert.Equal(2186.0 - 75, builder.StartOffset, 1.0);
    }

    [Fact]
    public void A_mitad_de_una_partida_normal_no_se_corre_el_reloj()
    {
        // 12 aldeanos a las 7:00 es un jugador lento, no una partida que arrancó tarde.
        var builder = new SnapshotBuilder(TestData.GameData);

        Assert.Equal(420.0, Build(builder, 420, Start with { Food = 300, Wood = 180 }, 12).Time);
        Assert.Equal(0.0, builder.StartOffset);
    }

    // Coach arrancado a mitad de partida (20:00, 20 aldeanos) con un cuadro tapando la hora en las primeras capturas: el
    // reloj suavizado da "1:20:00" hasta que el bueno lo corrige (ValueFilter.ConfirmFirst), lo único que lo hace volver
    // para atrás. El inicio se había fijado con la hora mala (1:12:55) y el tiempo quedaba en 0 hasta pasarla.
    [Fact]
    public void Si_el_reloj_corrige_una_hora_mala_del_arranque_el_tiempo_llega_al_bueno()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        var moved = Start with { Food = 300, Wood = 180 };

        foreach (var bad in new[] { 4800.0, 4801, 4802, 4803, 4803, 4804 })
            Build(builder, bad, moved, 20);
        for (var clock = 1206.0; clock < 1216; clock++)
            Assert.Equal(clock, Build(builder, clock, moved, 20).Time);
        Assert.Equal(0.0, builder.StartOffset);
    }

    // Lo mismo en la partida que arrancó tarde (36 minutos quieto): el inicio sale de los aldeanos con el reloj bueno.
    [Fact]
    public void Si_el_reloj_corrige_una_hora_mala_en_una_partida_que_arranco_tarde_el_inicio_sale_del_bueno()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        var moved = Start with { Food = 230, Wood = 160 };

        Assert.Equal(75.0, Build(builder, 2186 + 3600, moved, 6).Time, 1.0);
        Build(builder, 2187 + 3600, moved, 6);

        Assert.Equal(75.0, Build(builder, 2188, moved, 6).Time, 1.0);
        Assert.Equal(2188.0 - 75, builder.StartOffset, 1.0);
        Assert.Equal(85.0, Build(builder, 2198, moved, 6).Time, 1.0);
    }

    // Y si el coach lo vio quieto con la hora mala y empezó a jugar antes de que se corrigiera, el inicio pasa a la hora
    // buena: si no, el tiempo quedaba en 0 una hora.
    [Fact]
    public void Si_el_reloj_corrige_una_hora_mala_despues_de_verlo_quieto_el_inicio_pasa_a_la_buena()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        var queued = Start with { Food = 150 };

        Assert.Equal(0.0, Build(builder, 2180 + 3600, Start, 3).Time);
        Assert.Equal(0.0, Build(builder, 2186 + 3600, queued, 3).Time);
        Build(builder, 2187 + 3600, queued, 3);

        Build(builder, 2188, queued, 3);
        Assert.InRange(builder.StartOffset, 2186, 2188);
        Assert.Equal(7.0, Build(builder, 2195, queued, 3).Time, 2.0);
    }

    // Una hora mala que llega después de fijar el inicio con el reloj bueno (un lector nuevo al volver a calibrar) y se
    // corrige no toca el inicio.
    [Fact]
    public void Una_hora_mala_despues_de_fijar_el_inicio_no_lo_mueve()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        var queued = Start with { Food = 150 };
        Build(builder, 1, Start, 3);
        Build(builder, 2186, queued, 3);

        Build(builder, 2190 + 3600, queued, 3);
        Build(builder, 2191 + 3600, queued, 3);

        Assert.Equal(9.0, Build(builder, 2195, queued, 3).Time);
        Assert.Equal(2186.0, builder.StartOffset);
    }

    // Cuando el reloj se corrige, el tiempo del coach salta (de 7:05 a 20:06 con la hora mala de las primeras capturas, o
    // de 1:20:02 a 20:06 si llegó con un lector nuevo) y la BO quedaba abandonada, "hace 12:58 que no avanza ningún paso",
    // hasta que el jugador avanzara uno que no fuera un aldeano o una casa.
    [Theory]
    [InlineData(new[] { 4800.0, 4801, 4802, 4803, 4803, 4804, 1206, 1207, 1208 })]
    [InlineData(new[] { 1200.0, 1201, 4802, 4803, 4803, 4804, 1206, 1207, 1208 })]
    public void Cuando_el_reloj_se_corrige_la_BO_no_queda_abandonada_por_el_salto(double[] clocks)
    {
        var scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));
        var builder = new SnapshotBuilder(TestData.GameData);
        var coach = new Coach(scouts, TestData.GameData, TestData.Events);
        var moved = Start with { Food = 300, Wood = 180 };

        foreach (var clock in clocks)
            coach.Update(Build(builder, clock, moved, 20));

        Assert.Equal(1208.0, coach.State.Time);
        Assert.Null(coach.Abandonment.Reason);
    }

    [Fact]
    public void Una_partida_que_arranco_tarde_sigue_la_BO_con_los_mismos_tiempos()
    {
        // La partida de scouts grabada, con el reloj corrido 2000 s: como si el jugador se hubiera quedado 33 minutos
        // quieto antes de empezar. Los pasos tienen que cerrarse a los mismos tiempos de juego que sin correr el reloj
        // (ver PartidaDeScoutsGrabadaTests).
        const double Delay = 2000;
        var scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));
        var readings = ScreenReadingLog.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "scouts-20261015-214512.csv"));
        var builder = new SnapshotBuilder(TestData.GameData);
        var coach = new Coach(scouts, TestData.GameData, TestData.Events);

        double? feudalClick = null;
        foreach (var logged in readings)
        {
            if (builder.Build(logged.Reading with { Clock = logged.Reading.Clock + Delay }) is not { } snapshot)
                continue;
            coach.Update(snapshot);
            feudalClick ??= coach.Tracker.Steps.First(s => s.Step.Id == "s13").StartedAt; // solo vale mientras está en curso
        }

        var steps = coach.Tracker.Steps.ToDictionary(s => s.Step.Id);
        output.WriteLine($"inicio a los {builder.StartOffset} s del reloj");
        foreach (var s in steps.Values)
            output.WriteLine($"{s.Step.Id,-4} {s.State,-8} {(s.DoneAt is { } t ? GameClock.Format(t) : "")}");
        Assert.InRange(builder.StartOffset, Delay, Delay + 10);
        Assert.Equal(Clock("8:10"), feudalClick!.Value, 5.0); // clic de Feudal
        Assert.Equal(Clock("11:02"), steps["s13"].DoneAt!.Value, 5.0);
        Assert.Equal(Clock("15:42"), steps["s17"].DoneAt!.Value, 5.0);
        Assert.All(steps.Values, s => Assert.Equal(StepState.Done, s.State));
    }

    private static double Clock(string text) => GameClock.TryParse(text, out var seconds) ? seconds : throw new ArgumentException(text);
}
