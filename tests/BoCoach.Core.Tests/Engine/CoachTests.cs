using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>Aldeanos contados con el total del HUD (número azul) en vez del log.</summary>
public class AldeanosDelHudTests
{
    private static GameSnapshot At(double time, int? villagers, params string[] events) =>
        new(time, PlanStart.Standard.Stock, 4, 5, events.Select(e => new GameEvent(e, time)).ToArray()) { Villagers = villagers };

    [Fact]
    public void Cada_suba_del_total_es_un_aldeano_y_los_del_log_se_ignoran()
    {
        var state = new PlayerState();
        state.Apply(At(10, 3));
        state.Apply(At(30, 4, "villager_created"));
        // Dos aldeanos seguidos: el log muestra uno solo, el HUD sube 2.
        state.Apply(At(55, 6, "villager_created"));

        Assert.Equal(3, state.Count(EventConventions.VillagerCreated));
        Assert.All(state.Events, e => Assert.True(e.Synthetic));
        Assert.Equal(6, state.Villagers);
    }

    [Fact]
    public void Las_demas_unidades_salen_del_log()
    {
        var state = new PlayerState();
        state.Apply(At(10, 3));
        state.Apply(At(40, 3, "scout_cavalry_created", "house_built"));

        Assert.Equal(1, state.Count("scout_cavalry_created"));
        Assert.Equal(1, state.Count("house_built"));
        Assert.Equal(0, state.Count(EventConventions.VillagerCreated));
    }

    [Fact]
    public void Si_el_total_no_se_lee_un_rato_se_pone_al_dia_despues()
    {
        var state = new PlayerState();
        state.Apply(At(10, 3));
        state.Apply(At(20, null, "villager_created"));
        state.Apply(At(40, 5));

        Assert.Equal(2, state.Count(EventConventions.VillagerCreated));
    }

    [Fact]
    public void Arrancar_a_mitad_de_partida_cuenta_los_aldeanos_que_ya_habia()
    {
        var state = new PlayerState();
        state.Apply(At(600, 20));

        Assert.Equal(20 - PlayerState.StartingVillagers, state.Count(EventConventions.VillagerCreated));
    }
}

public class SnapshotBuilderTests
{
    private static ScreenReading Reading(double? clock, int? villagers = 3, double? food = 200, params string[] events) =>
        new(clock, food, 200, 100, 200, villagers, null, null, events);

    [Fact]
    public void Sin_reloj_no_hay_snapshot_y_los_eventos_pasan_al_siguiente()
    {
        var builder = new SnapshotBuilder(TestData.GameData);

        Assert.Null(builder.Build(Reading(null, events: "house_built")));
        var snapshot = builder.Build(Reading(30));

        var e = Assert.Single(snapshot!.Events);
        Assert.Equal(("house_built", 30.0), (e.EventId, e.Time));
    }

    [Fact]
    public void Un_recurso_que_no_se_lee_conserva_el_ultimo_valor()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        builder.Build(Reading(10, food: 340));

        var snapshot = builder.Build(Reading(11, food: null));

        Assert.Equal(340, snapshot!.Stock.Food);
    }

    [Fact]
    public void Sin_N_M_estima_la_poblacion_y_el_tope()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        builder.Build(Reading(100, villagers: 10, events: ["house_built", "house_built", "scout_cavalry_created"]));

        var snapshot = builder.Build(Reading(101, villagers: 10))!;

        Assert.Equal(10 + 1 + 1, snapshot.Population); // aldeanos + explorador nuevo + explorador inicial
        Assert.Equal(15, snapshot.PopulationCap);       // centro urbano + 2 casas
        Assert.True(builder.PopulationEstimated);
    }

    [Fact]
    public void El_tope_estimado_nunca_queda_debajo_de_la_poblacion()
    {
        // El log junta las casas seguidas: se vieron menos casas de las que hay.
        var builder = new SnapshotBuilder(TestData.GameData);

        var snapshot = builder.Build(Reading(400, villagers: 20))!;

        Assert.Equal(21, snapshot.Population);
        Assert.Equal(21, snapshot.PopulationCap);
    }
}

public class CoachTests
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    private static int Index(string stepId) => Scouts.Steps.Select(s => s.Id).ToList().IndexOf(stepId);

    private static GameSnapshot At(double time, double food, int villagers = 21, params string[] events) =>
        new(time, new ResourceAmounts(food, 200, 200, 100), villagers + 1, 25, events.Select(e => new GameEvent(e, time)).ToArray())
        {
            Villagers = villagers,
        };

    [Fact]
    public void El_clic_de_Feudal_se_reconoce_por_la_baja_de_comida_y_pasa_al_paso_siguiente()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(520, 523));

        var view = coach.Update(At(520.5, 33));

        var feudal = coach.Tracker.Steps[Index("s13")];
        Assert.Equal(StepState.InProgress, feudal.State);
        Assert.Equal(520.5, feudal.StartedAt);
        Assert.NotEqual("s13", view.Current?.Step.Id);
        var running = Assert.Single(view.Running);
        Assert.Equal(520.5 + TestData.GameData.Get("feudal_age").Time, running.EndsAt!.Value, 0);
        Assert.Contains(view.Deviations, d => d.What == "feudal_age" && d.Clicked && d.Estimated == 520.5);
    }

    [Fact]
    public void Los_aldeanos_de_un_paso_se_cuentan_como_el_HUD()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(10, 200, villagers: 3));

        // Los 3 iniciales hicieron las casas y están en las ovejas: "6 aldeanos a ovejas" son 9 en total.
        var view = coach.Update(At(40, 200, 3, "house_built", "house_built"));
        Assert.Equal("s2", view.Current?.Step.Id);
        Assert.Equal((3, 9), view.SourceVillagers);
        Assert.Equal("Aldeanos a ovejas: 3/9", CoachText.Lines(view, TestData.GameData).Current);

        view = coach.Update(At(65, 200, 4));
        Assert.Equal("Aldeanos a ovejas: 4/9", CoachText.Lines(view, TestData.GameData).Current);
    }

    [Fact]
    public void El_reparto_lleva_el_plan_al_total_de_aldeanos_del_HUD()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(10, 200, villagers: 3));
        coach.Update(At(40, 200, 3, "house_built", "house_built"));

        // La BO tiene los 3 iniciales en ovejas; el HUD dice 5: van todos a la comida.
        var view = coach.Update(At(65, 200, 5));
        Assert.Equal(new ResourceAmounts(5, 0, 0, 0), view.Distribution);
        Assert.Equal("Reparto: comida 5 · madera 0 · oro 0 · piedra 0", CoachText.Lines(view, TestData.GameData).Distribution);
    }

    [Fact]
    public void Un_aviso_del_log_se_muestra_unos_segundos()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(600, 100));

        var view = coach.Update(At(601, 100, events: ["sheep_found", "farm_exhausted"]));
        Assert.Equal("farm_exhausted", view.Alert);
        Assert.Equal("Se agotó una granja: resembrala", CoachText.Lines(view, TestData.GameData).Alert);

        Assert.Equal("farm_exhausted", coach.Update(At(608, 100)).Alert);
        Assert.Null(coach.Update(At(609.5, 100)).Alert);
        Assert.Null(CoachText.Lines(coach.View, TestData.GameData).Alert);
    }

    [Fact]
    public void Si_se_cancela_la_Feudal_vuelve_a_estar_pendiente()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(520, 523));
        coach.Update(At(520.5, 33));

        coach.Update(At(530, 533));

        Assert.NotEqual(StepState.InProgress, coach.Tracker.Steps[Index("s13")].State);
    }

    [Fact]
    public void Gastar_otra_cantidad_no_marca_la_Feudal()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(520, 523));

        coach.Update(At(520.5, 273)); // 5 aldeanos encolados de una

        Assert.Empty(coach.Tracker.InProgress);
    }

    [Fact]
    public void Retroceder_deshace_un_clic_mal_detectado()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(520, 523));
        coach.Update(At(520.5, 33));

        coach.Back();

        Assert.Empty(coach.Tracker.InProgress);
    }

    [Fact]
    public void Los_textos_del_overlay()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(520, 523));
        var lines = CoachText.Lines(coach.Update(At(520.5, 33)), TestData.GameData);

        Assert.Contains(lines.Running, l => l.StartsWith("En curso: Edad Feudal (termina en 2:"));
        Assert.Equal("Clic de Feudal a las 8:40, objetivo 9:00", lines.Deviation);
        Assert.False(lines.Behind);
    }
}

/// <summary>
/// La partida de scouts grabada (rec-20261015-214512, BO de scouts hasta 18:43): lecturas reales del HUD y del log,
/// ya suavizadas por Vision, pasadas por el coach completo. Los tiempos esperados salen del log de la partida.
/// </summary>
public class PartidaDeScoutsGrabadaTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    private static readonly string Readings = Path.Combine(AppContext.BaseDirectory, "fixtures", "scouts-20261015-214512.csv");

    private static double Clock(string text) => GameClock.TryParse(text, out var seconds) ? seconds : throw new ArgumentException(text);

    [Fact]
    public void Sigue_la_BO_con_los_tiempos_reales_de_la_partida()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        double? loomClick = null, feudalClick = null;
        string? shown = null;
        var changes = new List<(double Time, string? Text)>();

        foreach (var logged in ScreenReadingLog.Load(Readings))
        {
            if (builder.Build(logged.Reading) is not { } snapshot)
                continue;
            var view = coach.Update(snapshot);
            loomClick ??= coach.Tracker.Steps.First(s => s.Step.Id == "s12").StartedAt;
            feudalClick ??= coach.Tracker.Steps.First(s => s.Step.Id == "s13").StartedAt;
            var text = view.Suggestion?.ToString();
            if (text != shown)
                changes.Add((view.Time, shown = text));
        }

        var steps = coach.Tracker.Steps.ToDictionary(s => s.Step.Id);
        foreach (var s in steps.Values)
            output.WriteLine($"{s.Step.Id,-4} {s.State,-8} {(s.DoneAt is { } t ? GameClock.Format(t) : "")}");
        output.WriteLine($"sugerencias: {changes.Count} cambios");
        foreach (var (time, text) in changes)
            output.WriteLine($"  {GameClock.Format(time)} {text ?? "(nada)"}");

        // Clics vistos por la baja de recursos: telar (50 de oro) a las 8:04 y Feudal (500 de comida) a las 8:10.
        Assert.Equal(Clock("8:04"), loomClick!.Value, 1.5);
        Assert.Equal(Clock("8:10"), feudalClick!.Value, 1.5);

        // Momentos en que el log mostró cada cosa.
        Assert.Equal(Clock("8:53"), steps["s12"].DoneAt!.Value, 1.5);
        Assert.Equal(Clock("11:02"), steps["s13"].DoneAt!.Value, 1.5);
        Assert.Equal(Clock("11:54"), steps["s14"].DoneAt!.Value, 1.5);
        Assert.Equal(Clock("13:56"), steps["s15"].DoneAt!.Value, 1.5);
        Assert.Equal(Clock("15:42"), steps["s17"].DoneAt!.Value, 1.5);

        // Todo hecho menos las granjas: en la partida no se vio ningún "Farm Built".
        Assert.All(steps.Values.Where(s => s.Step.Id != "s16"), s => Assert.Equal(StepState.Done, s.State));
        Assert.Equal("s16", coach.View.Current?.Step.Id);
        Assert.Empty(coach.Tracker.Late);

        // La sugerencia de aldeanos no salta a cada captura (en esta partida cambia 17 veces en 18 minutos).
        Assert.InRange(changes.Count, 1, 25);
        Assert.All(changes.Zip(changes.Skip(1)), pair => Assert.True(pair.Second.Time - pair.First.Time >= 3,
            $"la sugerencia cambió a las {GameClock.Format(pair.Second.Time)}, apenas {pair.Second.Time - pair.First.Time:0.0} s después"));
    }

    [Fact]
    public void Avisa_cuando_el_centro_urbano_deja_de_sacar_aldeanos()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        var idle = new List<(double From, double To)>();
        double? since = null;

        foreach (var logged in ScreenReadingLog.Load(Readings))
        {
            if (builder.Build(logged.Reading) is not { } snapshot)
                continue;
            var view = coach.Update(snapshot);
            if (view.TownCenterIdle is not null)
                since ??= view.Time;
            else if (since is { } from)
            {
                idle.Add((from, view.Time));
                since = null;
            }
        }
        foreach (var (from, to) in idle)
            output.WriteLine($"parado {GameClock.Format(from)} a {GameClock.Format(to)}");

        // Según el total de aldeanos del HUD: de 4:14 a 4:50 y de 6:05 a 6:47 no salió ninguno (un aldeano tarda 25 s).
        // De 8:27 a 11:27 tampoco, pero el centro urbano estaba investigando telar y Feudal: no es estar parado.
        Assert.Equal(2, idle.Count);
        Assert.Equal(Clock("4:47"), idle[0].From, 1.5);
        Assert.Equal(Clock("4:50"), idle[0].To, 1.5);
        Assert.Equal(Clock("6:38"), idle[1].From, 1.5);
        Assert.Equal(Clock("6:47"), idle[1].To, 1.5);
    }
}

public class CentroUrbanoParadoTests
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    private static GameSnapshot At(double time, int villagers, double food = 100) =>
        new(time, new ResourceAmounts(food, 200, 200, 100), villagers + 1, 25, []) { Villagers = villagers };

    [Fact]
    public void Pasado_el_tiempo_de_un_aldeano_y_el_margen_avisa()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(100, 6));

        Assert.Null(coach.Update(At(130, 6)).TownCenterIdle);      // 25 s + 8 de margen todavía no
        var view = coach.Update(At(140, 6));
        Assert.Equal(15, view.TownCenterIdle!.Value, 1);            // 40 s sin aldeanos: 15 de más
        Assert.Equal("Centro urbano parado hace 15 s", CoachText.Lines(view, TestData.GameData).Idle);

        Assert.Null(coach.Update(At(142, 7)).TownCenterIdle);      // salió uno
    }

    [Fact]
    public void Si_la_BO_ya_no_pide_aldeanos_no_avisa()
    {
        var bo = Scouts with { Steps = Scouts.Steps.Where(s => s.What != "villager").ToList() };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        coach.Update(At(100, 6));

        Assert.Null(coach.Update(At(200, 6)).TownCenterIdle);
    }
}
