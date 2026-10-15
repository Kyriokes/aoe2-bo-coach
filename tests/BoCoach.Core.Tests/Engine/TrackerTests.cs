using BoCoach.Core.BuildOrders;
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

    [Fact]
    public void Partida_perfecta_completa_todos_los_pasos_en_orden()
    {
        var tracker = NewTracker();
        var partida = new Partida();

        for (var i = 0; i < Scouts.Steps.Count; i++)
        {
            Assert.Equal(Scouts.Steps[i].Id, tracker.Current?.Step.Id);
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
    public void Paso_salteado_queda_atrasado_y_no_se_pierde()
    {
        var tracker = NewTracker();
        // Hace todo hasta s3 y la casa de s5, pero se olvida del campamento maderero (s4).
        var partida = new Partida().Play(Scouts, 0, 2).Event("house_built");

        tracker.Update(partida.State);

        Assert.Equal(StepState.Late, StateOf(tracker, "s4"));
        Assert.Equal(StepState.Done, StateOf(tracker, "s5"));
        Assert.Equal("s6", tracker.Current?.Step.Id);
        Assert.Contains(tracker.Late, s => s.Step.Id == "s4");

        partida.Event("lumber_camp_built");
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s4"));
        Assert.Empty(tracker.Late);
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
        // Con el catálogo real, "loom_researched" y "feudal_age_researched" no son reconocibles.
        var tracker = new Tracker(Scouts, TestData.GameData, TestData.Events);
        var partida = new Partida().Play(Scouts, 0, 10);
        tracker.Update(partida.State);
        Assert.Equal("s12", tracker.Current?.Step.Id);

        partida.Event("barracks_built");
        tracker.Update(partida.State);

        Assert.Equal(StepState.Done, StateOf(tracker, "s12"));
        Assert.Equal(StepState.Done, StateOf(tracker, "s13"));
        Assert.Equal("s15", tracker.Current?.Step.Id);
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
}
