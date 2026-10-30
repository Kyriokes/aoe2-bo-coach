using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

public class TrackerTests
{
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));

    private static int Index(string stepId) => Scouts.Steps.Select(s => s.Id).ToList().IndexOf(stepId);

    /// <summary>Tracker que reconoce todos los eventos (como si Vision leyera todo).</summary>
    private static Tracker NewTracker() => new(Scouts, TestData.GameData);

    private static StepState StateOf(Tracker t, string stepId) => t.Steps[Index(stepId)].State;

    private static readonly StepState[] Open = [StepState.Pending, StepState.Ready];

    private static bool Silent(Step s) =>
        s.Group is null && EventConventions.EventFor(s) is { } e && EventConventions.Unannounced.Contains(e);

    [Fact]
    public void Partida_perfecta_completa_todos_los_pasos_en_orden()
    {
        var tracker = NewTracker();
        var partida = new Partida();

        for (var i = 0; i < Scouts.Steps.Count; i++)
        {
            // Las granjas no se avisan en el log: no son el paso actual (nada las confirmaría), se muestran aparte y
            // quedan hechas cuando avanza el siguiente.
            var expected = Enumerable.Range(i, Scouts.Steps.Count - i).Select(j => Scouts.Steps[j]).First(s => !Silent(s));
            Assert.Equal(expected.Id, tracker.Current?.Step.Id);
            if (Silent(Scouts.Steps[i]))
                Assert.Contains(tracker.Silent, s => s.Index == i);
            partida.Play(Scouts, i, i);
            tracker.Update(partida.State);
        }

        Assert.True(tracker.Finished);
        Assert.Null(tracker.Current);
        Assert.Empty(tracker.Late);
        Assert.Empty(tracker.Extras);
        Assert.All(tracker.Steps, s => Assert.False(s.Manual));
    }

    [Fact]
    public void Progreso_parcial_de_un_paso_de_aldeanos()
    {
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2).Events("villager_created", 3);

        tracker.Update(partida.State);

        var s2 = tracker.Steps[Index("s2")];
        Assert.Contains(s2.State, Open);
        Assert.Equal(3, s2.Completed);
        Assert.Equal(3, s2.Remaining);
        Assert.Equal("s2", tracker.Current?.Step.Id);
        Assert.Equal("s3", tracker.Next?.Step.Id);
    }

    [Fact]
    public void Un_paso_de_aldeanos_cuenta_los_que_ya_hay_en_el_recurso_segun_el_HUD()
    {
        // s3: "4 aldeanos en madera". El jugador ya mandó 2 a madera por su cuenta: faltan 2, no 4.
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2).Events("villager_created", 6)
            .Hud(new ResourceAmounts(Food: 7, Wood: 2, Stone: 0, Gold: 0));
        tracker.Update(partida.State);

        var s3 = tracker.Steps[Index("s3")];
        Assert.Equal((2, 2), (s3.Completed, s3.Remaining));
    }

    [Fact]
    public void Un_paso_de_aldeanos_se_cumple_cuando_el_HUD_tiene_el_total_aunque_no_se_vieron_salir()
    {
        // Partida 20261018-014649: había 8 en oro (el total de la BO) y el coach seguía pidiendo aldeanos al oro.
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2).Events("villager_created", 6)
            .Hud(new ResourceAmounts(Food: 9, Wood: 4, Stone: 0, Gold: 0));
        tracker.Update(partida.State);
        Assert.NotEqual(StepState.Done, StateOf(tracker, "s3")); // una sola lectura no alcanza

        partida.Hud(new ResourceAmounts(Food: 9, Wood: 4, Stone: 0, Gold: 0), dt: 3);
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s3"));
        // Y si después mueve gente de la madera, el paso no se deshace.
        partida.Hud(new ResourceAmounts(Food: 12, Wood: 1, Stone: 0, Gold: 0), dt: 5);
        tracker.Update(partida.State);
        Assert.Equal(StepState.Done, StateOf(tracker, "s3"));
    }

    [Fact]
    public void Los_aldeanos_que_ya_conto_un_paso_no_pasan_al_siguiente_cuando_el_HUD_lo_completa()
    {
        // s2 (ovejas) ya tenía 3 de sus 6 aldeanos nuevos; el HUD lo completa: esos 3 no son de la madera (s3).
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2).Events("villager_created", 3)
            .Hud(new ResourceAmounts(Food: 9, Wood: 0, Stone: 0, Gold: 0));
        tracker.Update(partida.State);
        partida.Hud(new ResourceAmounts(Food: 9, Wood: 0, Stone: 0, Gold: 0), dt: 3);
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s2"));
        Assert.Equal(0, tracker.Steps[Index("s3")].Completed);

        partida.Event("villager_created");
        tracker.Update(partida.State);
        Assert.Equal(1, tracker.Steps[Index("s3")].Completed);
    }

    [Fact]
    public void El_HUD_no_cumple_un_paso_cuyo_total_ya_se_paso_antes_en_la_BO()
    {
        // Partida 20261028-232628 (británicos mejorada): "5 en bayas" va después de sacar 6 de las ovejas y la caza, y
        // su total de comida (6) es menos que el de pasos anteriores. A las 0:52, con 5 o 6 en comida y nadie movido,
        // el HUD lo daba por hecho, y con él a todos los "Mover" de antes.
        var bo = new BuildOrder
        {
            Name = "t",
            Steps =
            [
                new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 6, Assign = GatherSource.Sheep },
                new Step { Id = "s2", Do = StepAction.Move, Count = 3, From = GatherSource.Sheep, Assign = GatherSource.Wood },
                new Step { Id = "s3", Do = StepAction.Train, What = "villager", Count = 2, Assign = GatherSource.Berries },
            ],
        };
        var tracker = new Tracker(bo, TestData.GameData);
        var partida = new Partida().Events("villager_created", 3).Hud(new ResourceAmounts(Food: 6, Wood: 0, Stone: 0, Gold: 0));
        tracker.Update(partida.State);
        partida.Hud(new ResourceAmounts(Food: 6, Wood: 0, Stone: 0, Gold: 0), dt: 3);
        tracker.Update(partida.State);

        Assert.Equal(0, tracker.Steps[2].Completed);
        Assert.NotEqual(StepState.Done, tracker.Steps[1].State);
    }

    [Fact]
    public void La_produccion_militar_empezada_no_frena_la_BO()
    {
        // Como la guía de arqueros: 12 + 12 arqueros y después la economía. Partida 20261018-021924: el coach quedaba
        // 7 minutos en "Entrenar 12 arqueros" sin mostrar las granjas ni lo demás.
        var bo = new BuildOrder
        {
            Name = "t",
            Steps =
            [
                new Step { Id = "s1", Do = StepAction.Train, What = "archer", Count = 3 },
                new Step { Id = "s2", Do = StepAction.Train, What = "archer", Count = 3 },
                new Step { Id = "s3", Do = StepAction.Build, What = "house", From = GatherSource.Wood, Assign = GatherSource.Wood },
            ],
        };
        var tracker = new Tracker(bo, TestData.GameData);
        // En Feudal y con la galería (sin eso los arqueros no se muestran: spec 2026-10-30).
        var partida = new Partida().Event("feudal_age_researched").Event("archery_range_built");
        tracker.Update(partida.State);
        Assert.Equal("s1", tracker.Current?.Step.Id); // todavía no salió ninguno: hay que empezar

        tracker.Update(partida.Event("archer_created").State);

        Assert.Equal("s3", tracker.Current?.Step.Id);
        Assert.Equal(["s1", "s2"], tracker.Producing.Select(s => s.Step.Id));
        var view = new CoachView(0, 0, 0, tracker.Current, null, [], tracker.Producing.Select(s => new RunningStep(s, null)).ToList(), ResourceAmounts.Zero, null, [], null, 0, 3);
        Assert.Equal("Seguí produciendo arqueros: 1/6", CoachText.Lines(view, TestData.GameData).Running.Single());
    }

    [Fact]
    public void Paso_salteado_queda_primero_en_su_linea_y_no_se_pierde()
    {
        var tracker = NewTracker();
        // Hace todo hasta s3 y la casa de s5, pero se olvida del campamento maderero (s4). Sin "atrasados" (spec
        // 2026-10-30): el campamento queda primero en Construir y los aldeanos siguen en su línea.
        var partida = new Partida().Play(Scouts, 0, 2).Event("house_built");

        tracker.Update(partida.State);

        Assert.Contains(StateOf(tracker, "s4"), Open);
        Assert.Equal(StepState.Done, StateOf(tracker, "s5"));
        Assert.Equal("s4", tracker.LaneStep(Lane.Build)?.Step.Id);
        Assert.Equal("s6", tracker.LaneStep(Lane.Villagers)?.Step.Id);

        partida.Event("lumber_camp_built");
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s4"));
        Assert.Equal("s8", tracker.LaneStep(Lane.Build)?.Step.Id);
    }

    [Fact]
    public void Accion_fuera_del_plan_se_registra_como_extra_y_no_mueve_el_puntero()
    {
        var tracker = NewTracker();
        var partida = new Partida().Play(Scouts, 0, 0).Event("dock_built");

        tracker.Update(partida.State);

        var extra = Assert.Single(tracker.Extras);
        Assert.Equal("dock_built", extra.Event.EventId);
        Assert.Equal("s2", tracker.Current?.Step.Id);
    }

    [Fact]
    public void Los_avisos_del_log_no_son_acciones_extra()
    {
        var tracker = NewTracker();
        var partida = new Partida().Play(Scouts, 0, 0).Event("sheep_found").Event("farm_exhausted").Event("under_attack");

        tracker.Update(partida.State);

        Assert.Empty(tracker.Extras);
        Assert.Equal("s2", tracker.Current?.Step.Id);
    }

    [Fact]
    public void Mas_casas_de_las_que_pide_la_BO_son_extras()
    {
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2 + 1 + 1 + 1); // la BO pide 4

        tracker.Update(partida.State);

        Assert.Single(tracker.Extras);
    }

    [Fact]
    public void Avanzar_a_mano_da_por_hecho_el_paso_actual()
    {
        var tracker = NewTracker();
        tracker.Update(new Partida().Wait(30).State);

        tracker.Advance();

        var s1 = tracker.Steps[0];
        Assert.Equal(StepState.Done, s1.State);
        Assert.True(s1.Manual);
        Assert.Equal(30, s1.DoneAt);
        Assert.Equal("s2", tracker.Current?.Step.Id);
    }

    [Fact]
    public void El_paso_hecho_a_mano_no_consume_eventos_de_los_siguientes()
    {
        var tracker = NewTracker();
        tracker.Advance(); // s1 (2 casas) a mano: el log no las mostró
        var partida = new Partida().Play(Scouts, 1, 3).Event("house_built");

        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s5"));
        Assert.Empty(tracker.Extras);
    }

    [Fact]
    public void Retroceder_deshace_el_ultimo_paso_y_descarta_sus_eventos()
    {
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2);
        tracker.Update(partida.State);
        Assert.Equal(StepState.Done, tracker.Steps[0].State);

        tracker.Back(); // las 2 casas eran una lectura falsa

        Assert.Contains(tracker.Steps[0].State, Open);
        Assert.Equal(0, tracker.Steps[0].Completed);

        // La corrección se respeta en los Update siguientes, y casas nuevas lo vuelven a completar.
        tracker.Update(partida.State);
        Assert.Contains(tracker.Steps[0].State, Open);
        partida.Events("house_built", 2);
        tracker.Update(partida.State);
        Assert.Equal(StepState.Done, tracker.Steps[0].State);
        Assert.Empty(tracker.Extras);
    }

    [Fact]
    public void Retroceder_despues_de_avanzar_a_mano_vuelve_atras()
    {
        var tracker = NewTracker();
        tracker.Advance();
        tracker.Back();

        Assert.Equal("s1", tracker.Current?.Step.Id);
        Assert.All(tracker.Steps, s => Assert.NotEqual(StepState.Done, s.State));
    }

    [Fact]
    public void Saltear_a_mano()
    {
        var tracker = NewTracker();

        tracker.Skip();

        Assert.Equal(StepState.Skipped, tracker.Steps[0].State);
        Assert.Equal("s2", tracker.Current?.Step.Id);

        tracker.Back();
        Assert.Equal("s1", tracker.Current?.Step.Id);
    }

    [Fact]
    public void Pasos_que_el_log_no_confirma_se_dan_por_hechos_cuando_avanza_uno_posterior()
    {
        // Un catálogo sin los mensajes de investigación: "loom_researched" y "feudal_age_researched" no son
        // reconocibles (el log real sí los muestra, pero el Tracker no puede depender de eso).
        var withoutResearch = new EventCatalog(TestData.Events.Phrases
            .Where(e => !e.Key.EndsWith("_researched", StringComparison.Ordinal))
            .ToDictionary(e => e.Key, e => e.Value));
        var tracker = new Tracker(Scouts, TestData.GameData, withoutResearch);
        var partida = new Partida().Play(Scouts, 0, 10);
        tracker.Update(partida.State);
        Assert.Equal("s12", tracker.Current?.Step.Id);

        partida.Event("barracks_built");
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s12"));
        // Feudal (s13) y el cuartel (s14) son metas del mismo paso mixto: se hacen a la vez, así que el cuartel
        // terminado no da por hecha la Feudal. Sí la da por hecha un paso de después del grupo.
        Assert.Equal("s13", tracker.Current?.Step.Id);

        partida.Event("stable_built");
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s13"));
        // s16 son granjas, que el juego no avisa: se muestran aparte y el paso actual es el siguiente.
        Assert.Equal("s17", tracker.Current?.Step.Id);
        Assert.Contains(tracker.Silent, s => s.Step.Id == "s16");
    }

    [Fact]
    public void Aldeanos_contados_por_la_poblacion_cuando_el_log_no_los_muestra()
    {
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2).PopulationGrows(4);

        tracker.Update(partida.State);

        Assert.Equal(4, tracker.Steps[Index("s2")].Completed);
    }

    [Fact]
    public void Listo_cuando_alcanzan_los_recursos_y_hay_poblacion()
    {
        var tracker = NewTracker();
        var partida = new Partida().Events("house_built", 2);
        partida.Stock = partida.Stock with { Food = 40 };
        partida.Wait(1);
        tracker.Update(partida.State);
        Assert.Equal(StepState.Pending, tracker.Current?.State);

        partida.Stock = partida.Stock with { Food = 60 };
        partida.Wait(1);
        tracker.Update(partida.State);

        Assert.Equal(StepState.Ready, tracker.Current?.State);
    }

    [Fact]
    public void Construir_antes_de_tiempo_no_atrasa_los_aldeanos_del_centro_urbano()
    {
        var tracker = NewTracker();
        // Hace las casas iniciales y el campamento maderero (s4) antes de entrenar los aldeanos de s2 y s3.
        var partida = new Partida().Play(Scouts, 0, 0).Event("lumber_camp_built");

        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s4"));
        Assert.Contains(StateOf(tracker, "s2"), Open);
        Assert.Contains(StateOf(tracker, "s3"), Open);
        Assert.Empty(tracker.Late);
        Assert.Equal("s2", tracker.Current?.Step.Id);
    }

    [Fact]
    public void Un_paso_en_curso_deja_de_ser_el_actual_y_retroceder_lo_deshace()
    {
        var tracker = NewTracker();
        tracker.Update(new Partida().Play(Scouts, 0, 10).State);
        Assert.Equal("s12", tracker.Current?.Step.Id);

        tracker.MarkStarted(Index("s12"), 500);

        Assert.Equal(StepState.InProgress, StateOf(tracker, "s12"));
        Assert.Equal(500, tracker.Steps[Index("s12")].StartedAt);
        Assert.Equal("s13", tracker.Current?.Step.Id);

        tracker.Back();

        Assert.Equal("s12", tracker.Current?.Step.Id);
        Assert.Empty(tracker.InProgress);
    }

    [Fact]
    public void Empezar_una_investigacion_deja_la_anterior_primera_en_su_linea()
    {
        var tracker = NewTracker();
        tracker.Update(new Partida().Play(Scouts, 0, 10).State);

        // Hace clic en Feudal sin haber investigado el telar (los dos se hacen en el centro urbano): el telar sigue
        // primero en Investigar (spec 2026-10-30, sin "atrasados").
        tracker.MarkStarted(Index("s13"), 520);

        Assert.Contains(StateOf(tracker, "s12"), Open);
        Assert.Equal("s12", tracker.LaneStep(Lane.Research)?.Step.Id);
    }

    [Fact]
    public void Pagar_el_telar_temprano_no_atrasa_los_aldeanos_ni_hace_la_granja()
    {
        // Partida 20261018-132830: telar a las 3:14, antes de los aldeanos de caza y de bayas (todo del centro urbano).
        var archers = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "archers_feudal.json"));
        var ids = archers.Steps.Select(s => s.Id).ToList();
        var tracker = new Tracker(archers, TestData.GameData);
        tracker.Update(new Partida().Play(archers, 0, ids.IndexOf("s4")).State);

        tracker.MarkStarted(ids.IndexOf("s15"), 194);

        Assert.Equal("s5", tracker.Current?.Step.Id);
        Assert.Empty(tracker.Late);
        Assert.NotEqual(StepState.Done, tracker.Steps[ids.IndexOf("s11")].State);   // la granja del paso mixto
    }

    [Fact]
    public void Una_casa_queda_hecha_si_el_tope_ya_llego_a_lo_que_espera_la_BO()
    {
        // El usuario tenía 35/55 y el coach seguía con "Construí una casa": las había hecho antes y el log no las avisó.
        var archers = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "archers_feudal.json"));
        var ids = archers.Steps.Select(s => s.Id).ToList();
        var tracker = new Tracker(archers, TestData.GameData);
        var partida = new Partida().Play(archers, 0, ids.IndexOf("s5"));
        tracker.Update(partida.State);
        Assert.Equal("s6", tracker.Current?.Step.Id);   // la casa de los 15 de población

        partida.CapGrows(10).Wait(1);                    // de 15 a 25
        tracker.Update(partida.State);
        Assert.Equal("s6", tracker.Current?.Step.Id);   // tiene que sostenerse 2 s
        partida.Wait(2);
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, tracker.Steps[ids.IndexOf("s6")].State);
        Assert.Equal(StepState.Done, tracker.Steps[ids.IndexOf("s13")].State);  // con 25 también estaba la siguiente
        Assert.NotEqual(StepState.Done, tracker.Steps[ids.IndexOf("s20")].State);

        // "Atrás" lo deshace y el tope no lo vuelve a dar.
        tracker.Back();
        partida.Wait(3);
        tracker.Update(partida.State);
        Assert.NotEqual(StepState.Done, tracker.Steps[ids.IndexOf("s13")].State);
    }

    [Fact]
    public void Las_granjas_no_traban_la_BO_y_quedan_hechas_al_empezar_un_paso_posterior()
    {
        // La partida 20261018-132830: después de la casa del paso 39 la BO pide 4 granjas, carretilla y Castillos.
        // El juego no avisa las granjas, y el coach mostró "Construir 4 granjas" desde las 18:00 hasta el final.
        var archers = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "archers_feudal.json"));
        var farms = archers.Steps.Select(s => s.Id).ToList().IndexOf("s40");
        var tracker = new Tracker(archers, TestData.GameData);
        tracker.Update(new Partida().Play(archers, 0, farms).State);

        Assert.Equal("s41", tracker.Current?.Step.Id);
        Assert.Contains(tracker.Silent, s => s.Step.Id == "s40");

        // Hace clic en Castillos sin la carretilla: las granjas ya están, la carretilla queda primera en Investigar.
        tracker.MarkStarted(archers.Steps.Select(s => s.Id).ToList().IndexOf("s42"), 1226);

        Assert.Equal(StepState.Done, tracker.Steps[farms].State);
        Assert.Empty(tracker.Silent);
        Assert.Contains(tracker.Steps[farms + 1].State, Open);
        Assert.Equal(tracker.Steps[farms + 1].Step.Id, tracker.LaneStep(Lane.Research)?.Step.Id);
    }
}
