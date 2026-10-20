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
