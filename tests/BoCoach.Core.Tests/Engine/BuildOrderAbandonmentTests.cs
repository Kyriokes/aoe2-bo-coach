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

    // Partida 20261030-203917 (británicos): investigados los ballesteros, la galería saca ballesteros y no arqueros, y a
    // las 27:16 la BO quedaba "abandonada" por 6 unidades que no tiene.
    [Fact]
    public void Los_ballesteros_son_los_arqueros_de_la_BO()
    {
        var state = Game(700, [("archer", 500), .. Units("crossbowman", 6, 600)]);

        Assert.False(Watching(state).Abandoned);
        Assert.Equal(7, state.Count("archer_created"));
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

    // Partida 20261031-163414 (británicos, Fast Archers mejorada): de 15:05 a 20:53 el jugador hizo granjas y juntó para
    // Castillos, y a las 18:05 la BO quedaba "abandonada" con "faltan 584 de comida".
    [Fact]
    public void Juntar_lo_que_le_falta_al_paso_actual_es_avanzar()
    {
        var state = new PlayerState();
        state.Apply(At(900));
        var detector = Watching(state);
        detector.Update(state, Steps, missing: 900);

        for (var t = 960; t <= 1260; t += 60)
        {
            state.Apply(At(t));
            detector.Update(state, Steps, missing: 900 - (t - 900));
        }

        Assert.False(detector.Abandoned);
    }

    [Fact]
    public void Si_lo_que_falta_no_baja_los_tres_minutos_siguen_contando()
    {
        var state = new PlayerState();
        state.Apply(At(900));
        var detector = Watching(state);
        detector.Update(state, Steps, missing: 500);

        // Junta y lo gasta en otra cosa: lo que falta sube y baja sin llegar nunca más abajo.
        for (var t = 930; t <= 1080; t += 30)
        {
            state.Apply(At(t));
            detector.Update(state, Steps, missing: t % 60 == 0 ? 520 : 500);
        }

        Assert.True(detector.Abandoned);
    }

    /// <summary>La BO abandonada por 3 minutos sin avanzar, en el segundo 780.</summary>
    private static (BuildOrderAbandonment Detector, PlayerState State) AbandonedAt780()
    {
        var state = new PlayerState();
        state.Apply(At(600));
        var detector = Watching(state);
        state.Apply(At(780));
        detector.Update(state, Steps);
        Assert.True(detector.Abandoned);
        return (detector, state);
    }

    private static IReadOnlyList<StepProgress> WithStep(string id, Func<StepProgress, StepProgress> change) =>
        Steps.Select(s => s.Step.Id == id ? change(s) : s).ToList();

    // Partida 20261031-171509: abandonada a las 16:44 (la herrería pagable desde las 16:29 y sin hacer), quedó así hasta
    // el final aunque a las 19:48 hizo la herrería y después el emplumado, Castillos y los centros urbanos.
    [Fact]
    public void Si_avanza_un_paso_de_la_BO_abandonada_se_retoma_sola()
    {
        var (detector, state) = AbandonedAt780();

        state.Apply(At(900));
        detector.Update(state, WithStep("s25", s => s with { State = StepState.Done, Completed = 1 })); // la herrería

        Assert.False(detector.Abandoned);
    }

    // Auditoría del 2026-11-05: el centro urbano saca aldeanos igual, y con cada uno la BO se retomaba sola.
    [Fact]
    public void Con_la_BO_abandonada_un_aldeano_no_la_retoma()
    {
        var (detector, state) = AbandonedAt780();

        state.Apply(At(805));
        detector.Update(state, WithStep("s2", s => s with { Completed = 1 }));

        Assert.True(detector.Abandoned);
    }

    [Fact]
    public void Con_la_BO_abandonada_una_casa_no_la_retoma()
    {
        // Una casa se hace cuando la pide la población, siga o no la BO (por lo mismo no deja atrás a nadie en su línea).
        var (detector, state) = AbandonedAt780();

        state.Apply(At(805));
        detector.Update(state, WithStep("s1", s => s with { State = StepState.Done, Completed = 2 }));

        Assert.True(detector.Abandoned);
    }

    [Fact]
    public void Con_la_BO_abandonada_una_granja_no_la_retoma()
    {
        // El juego no las avisa: quedan hechas cuando avanza cualquier paso posterior, un aldeano por ejemplo.
        var (detector, state) = AbandonedAt780();

        state.Apply(At(805));
        detector.Update(state, WithStep("s33", s => s with { State = StepState.Done, Completed = 2 }));

        Assert.True(detector.Abandoned);
    }

    [Fact]
    public void Con_la_BO_abandonada_un_clic_en_curso_no_la_retoma_hasta_que_termina()
    {
        // El clic se ve en el stock y puede ser otra cosa (dos aldeanos encolados cuestan lo mismo que la armadura acolchada).
        var (detector, state) = AbandonedAt780();

        state.Apply(At(805));
        detector.Update(state, WithStep("s24", s => s with { State = StepState.InProgress }));
        Assert.True(detector.Abandoned);

        state.Apply(At(850));
        detector.Update(state, WithStep("s24", s => s with { State = StepState.Done, Completed = 1 }));
        Assert.False(detector.Abandoned);
    }

    [Fact]
    public void Si_la_BO_termina_estando_abandonada_se_retoma()
    {
        var (detector, state) = AbandonedAt780();

        state.Apply(At(805));
        detector.Update(state, Steps.Select(s => Lanes.Of(s.Step) == Lane.Villagers ? s with { State = StepState.Done } : s).ToList(), finished: true);

        Assert.False(detector.Abandoned);
    }

    // Con el mod, lo hecho de un paso de aldeanos sale del HUD: un leñador que va a hacer una casa baja la cuenta 1 y al
    // volver la sube. Eso no es volver a la BO ni avanzar.
    [Fact]
    public void Un_vaiven_de_lo_hecho_no_retoma_la_BO()
    {
        var (detector, state) = AbandonedAt780();

        foreach (var (time, wood) in new[] { (800, 3), (820, 2), (840, 3), (860, 2), (880, 3) })
        {
            state.Apply(At(time));
            detector.Update(state, WithStep("s3", s => s with { Completed = wood }));
            Assert.True(detector.Abandoned, $"a los {time}");
        }
    }

    [Fact]
    public void Un_vaiven_de_lo_hecho_no_reinicia_los_tres_minutos()
    {
        var state = new PlayerState();
        state.Apply(At(600));
        var detector = new BuildOrderAbandonment(Archers);
        detector.Update(state, WithStep("s3", s => s with { Completed = 3 }));

        foreach (var (time, wood) in new[] { (660, 2), (720, 3), (779, 2) })
        {
            state.Apply(At(time));
            detector.Update(state, WithStep("s3", s => s with { Completed = wood }));
            Assert.False(detector.Abandoned, $"a los {time}");
        }

        state.Apply(At(780));
        detector.Update(state, WithStep("s3", s => s with { Completed = 3 }));
        Assert.True(detector.Abandoned);
    }

    [Fact]
    public void Atras_vuelve_a_contar_los_tres_minutos()
    {
        // "Atrás" deshace un paso: rehacerlo no supera lo que ya se había hecho, pero el jugador sigue en la BO.
        var state = new PlayerState();
        state.Apply(At(600));
        var detector = new BuildOrderAbandonment(Archers);
        detector.Update(state, WithStep("s3", s => s with { Completed = 3 }));

        state.Apply(At(700));
        detector.Rewound();
        detector.Update(state, WithStep("s3", s => s with { Completed = 2 }));

        state.Apply(At(850));
        detector.Update(state, WithStep("s3", s => s with { Completed = 2 }));
        Assert.False(detector.Abandoned);

        state.Apply(At(880));
        detector.Update(state, WithStep("s3", s => s with { Completed = 2 }));
        Assert.True(detector.Abandoned);
    }

    // Partida 20261031-215314 (hunos, "Scouts → CA"): terminada la BO, los arqueros a caballo que no entrena la daban
    // por abandonada.
    [Fact]
    public void Con_la_BO_terminada_las_unidades_ajenas_no_la_abandonan()
    {
        var done = Steps.Select(s => s with { State = StepState.Done }).ToList();
        var state = Game(700, Units("spearman", 8, 600));
        var detector = new BuildOrderAbandonment(Archers);

        detector.Update(state, done);

        Assert.False(detector.Abandoned);
        Assert.False(detector.Gray);
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
        Assert.Equal(new ResourceAmounts(8, 10, 0, 2), view.Distribution); // la GD es lo que hay
        var lines = CoachText.Lines(view, TestData.GameData);
        Assert.Equal("BO abandonada: seguí con lo básico", lines.Current);
        Assert.Contains("lanceros", lines.Status);
        Assert.Contains("Ctrl+Alt+← vuelve a la BO", lines.Status);
        // Con el atajo cambiado por el jugador, el texto dice el suyo.
        Assert.Contains("Ctrl+Alt+F9 vuelve a la BO", CoachText.Lines(view, TestData.GameData, backHotkey: "Ctrl+Alt+F9").Status);

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
