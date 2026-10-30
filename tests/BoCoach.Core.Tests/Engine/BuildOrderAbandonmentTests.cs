using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// D1 (criterios del usuario, 2026-10-20): la BO se da por abandonada con más de 5 unidades ajenas y la de la BO
/// parada 60 s, o con 3 minutos sin avanzar ningún paso; produciendo las dos unidades a la par sigue la BO (el gris).
/// </summary>
public class BuildOrderAbandonmentTests
{
    private static readonly BuildOrder Archers = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "archers_feudal.json"));

    /// <summary>Los pasos de la BO de arqueros sin empezar.</summary>
    private static readonly IReadOnlyList<StepProgress> Steps = new Tracker(Archers, TestData.GameData).Steps;

    private static GameSnapshot At(double time, params GameEvent[] events) => new(time, PlanStart.Standard.Stock, 4, 50, events);

    /// <summary>Una partida en que salen esas unidades en esos segundos.</summary>
    private static PlayerState Game(double now, params (string Unit, double At)[] units)
    {
        var state = new PlayerState();
        foreach (var (unit, at) in units.OrderBy(u => u.At))
            state.Apply(At(at, new GameEvent($"{unit}_created", at)));
        state.Apply(At(now));
        return state;
    }

    private static (string, double)[] Units(string unit, int count, double from) =>
        Enumerable.Range(0, count).Select(i => (unit, from + i * 10.0)).ToArray();

    /// <summary>El detector con la partida vista desde <paramref name="start"/> (los pasos avanzan hasta ahí).</summary>
    private static BuildOrderAbandonment Watching(PlayerState state)
    {
        var detector = new BuildOrderAbandonment(Archers);
        detector.Update(state, Steps);
        return detector;
    }

    [Fact]
    public void Seis_lanceros_y_ningun_arquero_hace_un_minuto_es_otra_build()
    {
        var detector = Watching(Game(700, [("archer", 500), .. Units("spearman", 6, 600)]));

        Assert.True(detector.Abandoned);
        Assert.Contains("6 unidades que la BO no tiene (lanceros)", detector.Reason);
        Assert.Contains("arquero", detector.Reason);
    }

    [Fact]
    public void Hasta_cinco_unidades_de_reaccion_no_es_abandonarla()
    {
        var detector = Watching(Game(700, Units("spearman", 5, 600)));

        Assert.False(detector.Abandoned);
        Assert.False(detector.Gray);
    }

    [Fact]
    public void Si_la_unidad_de_la_BO_sigue_saliendo_es_el_gris_y_sigue_la_BO()
    {
        var detector = Watching(Game(700, [.. Units("spearman", 8, 600), ("archer", 680)]));

        Assert.False(detector.Abandoned);
        Assert.True(detector.Gray);
    }

    [Fact]
    public void Los_aldeanos_y_los_barcos_de_pesca_no_son_unidades_ajenas()
    {
        var detector = Watching(Game(700, [.. Units("villager", 10, 400), .. Units("fishing_ship", 6, 500)]));

        Assert.False(detector.Abandoned);
    }

    [Fact]
    public void Tres_minutos_sin_avanzar_ningun_paso_es_abandonarla()
    {
        var state = new PlayerState();
        state.Apply(At(600));
        var detector = Watching(state);

        state.Apply(At(779));
        detector.Update(state, Steps);
        Assert.False(detector.Abandoned);

        state.Apply(At(780));
        detector.Update(state, Steps);
        Assert.True(detector.Abandoned);
        Assert.Contains("hace 3:00 que no avanza ningún paso", detector.Reason);
    }

    [Fact]
    public void Cada_avance_de_un_paso_vuelve_a_contar_los_tres_minutos()
    {
        var state = new PlayerState();
        state.Apply(At(0));
        var detector = Watching(state);

        state.Apply(At(150));
        detector.Update(state, Steps.Select((s, i) => i == 0 ? s with { Completed = 1 } : s).ToList());
        state.Apply(At(300));
        detector.Update(state, Steps.Select((s, i) => i == 0 ? s with { Completed = 1 } : s).ToList());

        Assert.False(detector.Abandoned);
    }

    [Fact]
    public void Con_la_BO_terminada_no_se_abandona_por_tiempo()
    {
        var done = Steps.Select(s => s with { State = StepState.Done }).ToList();
        var state = new PlayerState();
        state.Apply(At(0));
        var detector = new BuildOrderAbandonment(Archers);
        detector.Update(state, done);
        state.Apply(At(900));
        detector.Update(state, done);

        Assert.False(detector.Abandoned);
    }

    [Fact]
    public void Al_retomarla_lo_ajeno_y_el_tiempo_se_cuentan_de_nuevo()
    {
        var state = Game(700, Units("spearman", 6, 600));
        var detector = Watching(state);
        Assert.True(detector.Abandoned);

        detector.Resume(state);
        detector.Update(state, Steps);
        Assert.False(detector.Abandoned);

        // Otros 6 lanceros sí la vuelven a dar por abandonada.
        foreach (var (unit, at) in Units("spearman", 6, 710))
            state.Apply(At(at, new GameEvent($"{unit}_created", at)));
        state.Apply(At(800));
        detector.Update(state, Steps);
        Assert.True(detector.Abandoned);
    }

    // Integración con el coach: abandonada, la vista no tiene pasos y el overlay lo dice; "Atrás" la retoma.
    [Fact]
    public void El_coach_con_la_BO_abandonada_deja_los_pasos_y_Atras_la_retoma()
    {
        var coach = new Coach(Archers, TestData.GameData, TestData.Events);
        coach.Update(At(600) with { Villagers = 20, VillagersOn = new ResourceAmounts(8, 10, 0, 2) });
        var view = coach.Update(At(700, Units("spearman", 6, 610).Select(u => new GameEvent("spearman_created", u.Item2)).ToArray())
            with { Villagers = 20, VillagersOn = new ResourceAmounts(8, 10, 0, 2) });

        Assert.NotNull(view.Abandoned);
        Assert.All(view.Lanes, l => Assert.Null(l.Step)); // cada línea a su fallback
        Assert.Null(view.Current);
        Assert.Null(view.Next);
        Assert.Empty(view.Late);
        Assert.Equal(new ResourceAmounts(8, 10, 0, 2), view.Distribution); // la GD es lo que hay
        var lines = CoachText.Lines(view, TestData.GameData);
        Assert.Equal("BO abandonada: seguí con lo básico", lines.Current);
        Assert.Contains("lanceros", lines.Status);

        view = coach.Back();
        Assert.Null(view.Abandoned);
        Assert.NotNull(view.Current);
    }

    [Fact]
    public void Con_la_BO_abandonada_la_GD_deja_la_comida_que_necesita_el_centro_urbano()
    {
        var coach = new Coach(Archers, TestData.GameData, TestData.Events);
        coach.Update(At(600) with { Villagers = 20, VillagersOn = new ResourceAmounts(0, 20, 0, 0) });
        var view = coach.Update(At(700, Units("spearman", 6, 610).Select(u => new GameEvent("spearman_created", u.Item2)).ToArray())
            with { Villagers = 20, VillagersOn = new ResourceAmounts(0, 20, 0, 0) });

        Assert.NotNull(view.Abandoned);
        Assert.True(view.Distribution!.Value.Food >= Math.Min(view.FoodFloor, 20));
        Assert.True(view.FoodFloor > 0);
        Assert.Equal(20, view.Distribution.Value.Food + view.Distribution.Value.Wood);
    }
}
