using BoCoach.Core.Economy;
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

    private static ScreenReading WithCap(double clock, int cap, params string[] events) =>
        new(clock, 200, 200, 100, 200, 7, 8, cap, events);

    private static int Houses(GameSnapshot? snapshot) => snapshot!.Events.Count(e => e.EventId == "house_built");

    [Fact]
    public void Si_el_tope_sube_sin_aviso_del_log_es_una_casa_terminada()
    {
        // La segunda "House Built" no cambia el log si la primera sigue siendo el último renglón.
        var builder = new SnapshotBuilder(TestData.GameData);
        builder.Build(WithCap(10, 15));

        Assert.Equal(1, Houses(builder.Build(WithCap(11, 20))));
    }

    [Fact]
    public void Con_incas_una_casa_da_10_de_poblacion_y_la_suba_del_tope_no_cuenta_doble()
    {
        // Partida 20261017-163738: el coach tomaba la civ de la BO ("any") y cada casa de incas se contaba como dos.
        var builder = new SnapshotBuilder(TestData.GameData, TestData.GameData.Civ("incas"));
        builder.Build(WithCap(10, 15));

        Assert.Equal(1, Houses(builder.Build(WithCap(11, 25))));
    }

    [Fact]
    public void Una_casa_avisada_por_el_log_no_se_cuenta_otra_vez_cuando_sube_el_tope()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        builder.Build(WithCap(10, 15));

        Assert.Equal(1, Houses(builder.Build(WithCap(11, 15, "house_built"))));
        Assert.Equal(0, Houses(builder.Build(WithCap(12, 20))));
    }

    [Fact]
    public void Si_el_aviso_del_log_llega_despues_de_la_suba_no_es_otra_casa()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        builder.Build(WithCap(10, 15));

        Assert.Equal(1, Houses(builder.Build(WithCap(11, 20))));
        Assert.Equal(0, Houses(builder.Build(WithCap(12, 20, "house_built"))));
    }

    [Fact]
    public void Despues_de_un_rato_sin_leer_el_tope_no_se_deducen_casas()
    {
        // Mientras no se leía, el log pudo avisar esas casas: la suba no se puede atribuir.
        var builder = new SnapshotBuilder(TestData.GameData);
        builder.Build(WithCap(10, 15));

        Assert.Equal(0, Houses(builder.Build(WithCap(40, 25))));
    }

    [Fact]
    public void Un_total_de_aldeanos_imposible_se_descarta_sin_cortar_la_lectura()
    {
        // En vivo se leyó "8889" y Math.Clamp tiraba una excepción que cortaba toda la lectura.
        var builder = new SnapshotBuilder(TestData.GameData);
        builder.Build(Reading(10, villagers: 7));

        var snapshot = builder.Build(Reading(11, villagers: 8889))!;

        Assert.Null(snapshot.Villagers);
        Assert.Equal(7 + 1, snapshot.Population);
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

    private static ScreenReading WithPop(double clock, int villagers, int population, params string[] events) =>
        new(clock, 200, 200, 100, 200, villagers, population, 50, events);

    private static int Archers(SnapshotBuilder builder, params ScreenReading[] readings) =>
        readings.Sum(r => builder.Build(r)?.Events.Count(e => e.EventId == "archer_created") ?? 0);

    [Fact]
    public void Dos_arqueros_a_la_vez_son_dos_aunque_el_log_muestre_uno()
    {
        // Dos galerías sacan un arquero cada una: el log no agrega un renglón si el mensaje ya es el último.
        var builder = new SnapshotBuilder(TestData.GameData);

        var archers = Archers(builder,
            WithPop(10, 26, 30), WithPop(10.5, 26, 30), WithPop(11, 26, 30),
            WithPop(12, 26, 32, "archer_created"), WithPop(12.5, 26, 32), WithPop(13, 26, 32), WithPop(14, 26, 32),
            WithPop(15, 26, 32), WithPop(16, 26, 32));

        Assert.Equal(2, archers);
    }

    [Fact]
    public void Si_la_poblacion_sube_antes_que_el_log_las_unidades_toman_el_tipo_del_aviso()
    {
        var builder = new SnapshotBuilder(TestData.GameData);

        var archers = Archers(builder,
            WithPop(10, 26, 30), WithPop(10.5, 26, 30), WithPop(11, 26, 30),
            WithPop(12, 26, 32), WithPop(12.5, 26, 32), WithPop(13, 26, 32), WithPop(14, 26, 32, "archer_created"),
            WithPop(15, 26, 32), WithPop(16, 26, 32));

        Assert.Equal(2, archers);
    }

    [Fact]
    public void Un_aviso_del_log_que_llega_tarde_no_es_otra_unidad()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        Archers(builder,
            WithPop(10, 26, 30), WithPop(10.5, 26, 30), WithPop(11, 26, 30),
            WithPop(12, 26, 31, "archer_created"), WithPop(12.5, 26, 31), WithPop(13, 26, 31));

        // La población delata otro arquero antes que el log, que lo avisa después.
        var archers = Archers(builder,
            WithPop(15, 26, 32), WithPop(15.5, 26, 32), WithPop(16, 26, 32), WithPop(18, 26, 32, "archer_created"));

        Assert.Equal(1, archers);
    }

    [Fact]
    public void Aldeanos_y_poblacion_leidos_en_capturas_distintas_no_inventan_unidades()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        Archers(builder, WithPop(5, 26, 30, "archer_created"));

        // Sale un aldeano: el total azul cambia una captura antes que el "N/M".
        var archers = Archers(builder,
            WithPop(10, 26, 30), WithPop(10.5, 26, 30), WithPop(11, 26, 30),
            WithPop(12, 27, 30), WithPop(12.5, 27, 30), WithPop(13, 27, 30), WithPop(15, 27, 30), WithPop(17, 27, 30),
            WithPop(17.5, 27, 31), WithPop(18, 27, 31), WithPop(19, 27, 31), WithPop(21, 27, 31), WithPop(23, 27, 31));
        // Otro aldeano, al revés: el "N/M" cambia antes que el total azul.
        archers += Archers(builder,
            WithPop(24, 27, 32), WithPop(24.5, 27, 32), WithPop(25.5, 27, 32), WithPop(26, 28, 32), WithPop(28, 28, 32),
            WithPop(30, 28, 32));

        Assert.Equal(0, archers);
    }

    private static int Built(SnapshotBuilder builder, string building, params ScreenReading[] readings) =>
        readings.Sum(r => builder.Build(r)?.Events.Count(e => e.EventId == building) ?? 0);

    [Fact]
    public void Dos_arqueros_mas_seguidos_de_lo_que_saca_una_galeria_son_dos_galerias()
    {
        // Partida 20261018-011721: las dos galerías terminaron juntas y el log mostró un solo "Archery Range Built";
        // después salieron arqueros a las 12:40 y 12:45 (un arquero tarda 35 s).
        var builder = new SnapshotBuilder(TestData.GameData);

        var ranges = Built(builder, "archery_range_built",
            Reading(722, events: "archery_range_built"), Reading(760, events: "archer_created"), Reading(765, events: "archer_created"));

        Assert.Equal(2, ranges);
    }

    [Fact]
    public void Dos_arqueros_avanzando_en_la_cola_son_dos_galerias()
    {
        // Partida 20261018-132830: las galerías terminaron juntas a las 11:57 (un solo aviso) y a las 12:14 la cola
        // muestra un arquero en cada una. Lo que espera detrás (teñido) no es otro edificio.
        var builder = new SnapshotBuilder(TestData.GameData);
        static QueueItem[] Q(params (string Key, bool Waiting)[] items) => items.Select(i => new QueueItem(i.Key, i.Waiting)).ToArray();
        var waiting = Q(("villager", false), ("villager", true), ("loom", true));
        var twoRanges = Q(("villager", false), ("archer", false), ("archer", false));

        Assert.Equal(0, Built(builder, "town_center_built", Reading(280) with { Queue = waiting }, Reading(281) with { Queue = waiting }));
        var ranges = Built(builder, "archery_range_built",
            Reading(717, events: "archery_range_built"),
            Reading(734) with { Queue = twoRanges }, Reading(734.5) with { Queue = twoRanges }, Reading(735) with { Queue = twoRanges });

        Assert.Equal(2, ranges);
    }

    [Fact]
    public void Una_lectura_suelta_de_la_cola_no_suma_edificios()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        QueueItem[] twoRanges = [new("archer"), new("archer")];

        var ranges = Built(builder, "archery_range_built",
            Reading(717, events: "archery_range_built"), Reading(734) with { Queue = twoRanges }, Reading(735) with { Queue = [new("archer")] });

        Assert.Equal(1, ranges);
    }

    [Fact]
    public void Arqueros_al_ritmo_de_una_galeria_no_suman_galerias()
    {
        var builder = new SnapshotBuilder(TestData.GameData);

        var ranges = Built(builder, "archery_range_built",
            Reading(722, events: "archery_range_built"), Reading(760, events: "archer_created"), Reading(796, events: "archer_created"),
            Reading(832, events: "archer_created"));

        Assert.Equal(1, ranges);
    }

    [Fact]
    public void Una_unidad_que_muere_no_tapa_a_la_siguiente()
    {
        var builder = new SnapshotBuilder(TestData.GameData);
        Archers(builder, WithPop(5, 26, 30, "archer_created"));

        // Muere un arquero (las unidades bajan de 4 a 3 y quedan así) y después sale otro.
        var archers = Archers(builder,
            WithPop(10, 26, 30), WithPop(12, 26, 29), WithPop(15, 26, 29), WithPop(18, 26, 29), WithPop(21, 26, 29),
            WithPop(23, 26, 29), WithPop(25, 26, 30), WithPop(27, 26, 30), WithPop(29, 26, 30));

        Assert.Equal(1, archers);
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
    }

    [Fact]
    public void Recuerda_la_casa_cuando_quedan_3_lugares_de_poblacion()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);

        Assert.False(coach.Update(At(300, 200, villagers: 20)).NeedsHouse); // 21/25
        Assert.True(coach.Update(At(301, 200, villagers: 21)).NeedsHouse);  // 22/25
    }

    [Fact]
    public void Recuerda_el_centro_urbano_parado_solo_debajo_del_maximo_de_aldeanos()
    {
        var bo = new BuildOrder { Name = "t", Steps = [new Step { Id = "s1", Do = StepAction.Research, What = "loom" }] };
        var below = new Coach(bo, TestData.GameData, TestData.Events, economy: new EconomySettings { MaxVillagers = 30 });
        var reached = new Coach(bo, TestData.GameData, TestData.Events, economy: new EconomySettings { MaxVillagers = 20 });
        foreach (var coach in new[] { below, reached })
            coach.Update(At(300, 200, villagers: 20));

        // Un minuto sin aldeanos nuevos: el centro urbano está parado.
        Assert.True(below.Update(At(360, 200, villagers: 20)).TownCenterReminder);
        Assert.False(reached.Update(At(360, 200, villagers: 20)).TownCenterReminder);
    }

    [Fact]
    public void Con_la_cola_leida_el_centro_urbano_investigando_no_esta_parado()
    {
        // Partida 20261018-021924: se quejaba de centro urbano parado mientras investigaba la carretilla (no estaba
        // como paso en curso de la BO).
        var bo = new BuildOrder { Name = "t", Steps = [new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 40, Assign = GatherSource.Wood }] };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        GameSnapshot Queue(double time, params string[] queue) => At(time, 200, villagers: 30) with { Queue = queue.Select(k => new QueueItem(k)).ToList() };

        coach.Update(Queue(1000, "villager"));
        Assert.Null(coach.Update(Queue(1060, "wheelbarrow", "archer")).TownCenterIdle);  // un minuto sin aldeanos, investigando
        Assert.Null(coach.Update(Queue(1063, "archer")).TownCenterIdle);                 // recién terminó: margen
        Assert.NotNull(coach.Update(Queue(1066, "archer")).TownCenterIdle);              // 6 s sin nada del centro urbano
    }

    [Fact]
    public void Una_baja_del_stock_no_es_el_clic_de_algo_que_todavia_no_se_puede_investigar()
    {
        // Partida 20261025-230041 (vikingos): a las 3:53, en la Edad Oscura, dos aldeanos encolados bajaron 100 de
        // comida y el coach dio por empezada la armadura acolchada (100 de comida, en la herrería, de Feudal). Quedó "en
        // curso" toda la partida y dejó atrasado el emplumado.
        var bo = new BuildOrder
        {
            Name = "t",
            Steps =
            [
                new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 30, Assign = GatherSource.Wood },
                new Step { Id = "s2", Do = StepAction.Research, What = "padded_archer_armor" },
            ],
        };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        StepState Padded() => coach.Tracker.Steps[1].State;

        coach.Update(At(233, 300, 12));
        coach.Update(At(233.5, 200, 12));
        Assert.NotEqual(StepState.InProgress, Padded()); // Edad Oscura

        coach.Update(At(600, 300, 20, "feudal_age_researched"));
        coach.Update(At(600.5, 200, 20));
        Assert.NotEqual(StepState.InProgress, Padded()); // Feudal, pero sin herrería

        coach.Update(At(700, 300, 22, "blacksmith_built"));
        coach.Update(At(700.5, 200, 22));
        Assert.Equal(StepState.InProgress, Padded());
    }

    [Fact]
    public void La_cola_vacia_no_cuenta_hasta_que_la_cola_muestra_algo()
    {
        // Partida 20261025-230041: la caja de la cola había quedado corrida sobre el mapa (la calibración de la prueba
        // anterior) y se leía vacía toda la partida: el overlay y el informe dieron el centro urbano parado los 29:53
        // mientras salían aldeanos sin parar.
        var bo = new BuildOrder { Name = "t", Steps = [new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 40, Assign = GatherSource.Wood }] };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        GameSnapshot Queue(double time, int villagers, params string[] queue) =>
            At(time, 200, villagers) with { Queue = queue.Select(k => new QueueItem(k)).ToList() };

        // Un aldeano cada 25 s y la cola vacía: se deduce por los aldeanos (no está parado).
        for (var t = 0; t <= 100; t++)
            Assert.Null(coach.Update(Queue(1000 + t, 10 + t / 25)).TownCenterIdle);

        // La cola muestra un aldeano: desde ahí vale, y vacía 6 s es el centro urbano parado.
        coach.Update(Queue(1101, 14, "villager"));
        Assert.Null(coach.Update(Queue(1104, 14)).TownCenterIdle);
        Assert.NotNull(coach.Update(Queue(1107, 14)).TownCenterIdle);
    }

    [Fact]
    public void Una_investigacion_en_la_cola_queda_en_curso()
    {
        // Partida 20261018-132830: el clic en Castillos (20:26) no se vio en el stock, pero la cola lo mostraba.
        var bo = new BuildOrder
        {
            Name = "t",
            Steps =
            [
                new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 4, Assign = GatherSource.Wood },
                new Step { Id = "s2", Do = StepAction.Research, What = "loom" },
            ],
        };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        GameSnapshot Queue(double time, params QueueItem[] queue) => At(time, 200, villagers: 4) with { Queue = queue };

        coach.Update(Queue(100, new QueueItem("villager"), new QueueItem("loom", Waiting: true)));
        Assert.NotEqual(StepState.InProgress, coach.Tracker.Steps[1].State);   // una lectura sola no alcanza
        coach.Update(Queue(100.5, new QueueItem("villager"), new QueueItem("loom", Waiting: true)));

        Assert.Equal(StepState.InProgress, coach.Tracker.Steps[1].State);
    }

    [Fact]
    public void El_overlay_muestra_como_quedan_los_aldeanos_al_terminar_el_paso()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(10, 200, villagers: 3));
        var view = coach.Update(At(40, 200, 3, "house_built", "house_built"));

        // Paso "aldeanos a ovejas": al terminarlo, todos los de la BO en ovejas.
        Assert.Equal("s2", view.Current?.Step.Id);
        var target = LivePlan.PlannedVillagers(Scouts, Index("s2"))[GatherSource.Sheep];
        Assert.Equal(new ResourceAmounts(target, 0, 0, 0), view.StepTarget);
        Assert.Equal($"Al terminar el paso: madera 0 · comida {target} · oro 0 · piedra 0", CoachText.Lines(view, TestData.GameData).Distribution);
    }

    [Fact]
    public void En_un_paso_largo_el_overlay_muestra_tambien_como_repartir_los_de_ahora()
    {
        // Pedido del usuario: partir los pasos largos en "ahora" y "al terminar". A "aldeanos a ovejas" le faltan más
        // de 3: además de cómo quedan al terminar, cómo quiere la BO a los 3 que hay.
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(10, 200, villagers: 3));
        var view = coach.Update(At(40, 200, 3, "house_built", "house_built"));

        Assert.Equal(new ResourceAmounts(3, 0, 0, 0), view.NowTarget);
        var lines = CoachText.Lines(view, TestData.GameData);
        Assert.Equal("Ahora: madera 0 · comida 3 · oro 0 · piedra 0", lines.DistributionNow);
        Assert.StartsWith("Al terminar el paso:", lines.Distribution);
    }

    [Fact]
    public void En_un_paso_corto_no_hay_renglon_de_ahora()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(10, 200, villagers: 3));
        coach.Update(At(40, 200, 3, "house_built", "house_built"));

        // A "aldeanos a ovejas" le faltan 2: alcanza con cómo quedan al terminar.
        var view = coach.Update(At(150, 200, villagers: Scouts.Steps[Index("s2")].Count - 2));
        Assert.Equal("s2", view.Current?.Step.Id);
        Assert.Null(view.NowTarget);
    }

    [Fact]
    public void Con_los_aldeanos_por_recurso_del_HUD_la_GD_parte_de_lo_que_hay()
    {
        var coach = new Coach(Scouts, TestData.GameData, TestData.Events);
        coach.Update(At(10, 200, villagers: 3));
        coach.Update(At(40, 200, 3, "house_built", "house_built"));

        // La BO los quiere a todos en ovejas, pero hay 3 en madera y nada va a faltar: la GD no pide moverlos.
        var view = coach.Update(At(65, 600, 5) with { VillagersOn = new ResourceAmounts(2, 3, 0, 0) });

        Assert.Equal(new ResourceAmounts(2, 3, 0, 0), view.Distribution);
        Assert.Equal(new ResourceAmounts(5, 0, 0, 0), view.BoDistribution);
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

        // Todo hecho. Las granjas, sin "Farm Built" (el juego no lo avisa): se dan por hechas cuando avanzan los
        // exploradores, el paso siguiente.
        Assert.All(steps.Values, s => Assert.Equal(StepState.Done, s.State));
        Assert.Null(coach.View.Current);

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
        // Terminada la BO el centro urbano tiene que seguir hasta el máximo de aldeanos: en Feudal paró de 11:52 a
        // 13:48 y de 13:56 a 14:39 (con 25 s + 8 de margen, avisa desde 12:25 y 14:21).
        Assert.Equal(4, idle.Count);
        Assert.Equal(Clock("4:47"), idle[0].From, 1.5);
        Assert.Equal(Clock("4:50"), idle[0].To, 1.5);
        Assert.Equal(Clock("6:38"), idle[1].From, 1.5);
        Assert.Equal(Clock("6:47"), idle[1].To, 1.5);
        Assert.Equal(Clock("12:25"), idle[2].From, 1.5);
        Assert.Equal(Clock("13:48"), idle[2].To, 1.5);
        Assert.Equal(Clock("14:21"), idle[3].From, 1.5);
        Assert.Equal(Clock("14:39"), idle[3].To, 1.5);
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
    public void Sin_aldeanos_en_la_BO_avisa_igual_hasta_el_maximo()
    {
        var bo = Scouts with { Steps = Scouts.Steps.Where(s => s.What != "villager").ToList() };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        coach.Update(At(100, 6));

        Assert.NotNull(coach.Update(At(200, 6)).TownCenterIdle);
    }

    [Fact]
    public void Con_el_maximo_de_aldeanos_y_la_BO_terminada_no_avisa()
    {
        var bo = Scouts with { Steps = Scouts.Steps.Where(s => s.What != "villager").ToList() };
        var coach = new Coach(bo, TestData.GameData, TestData.Events, economy: new EconomySettings { MaxVillagers = 6 });
        coach.Update(At(100, 6));

        Assert.Null(coach.Update(At(200, 6)).TownCenterIdle);
    }
}
