using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// Los pasos de mover aldeanos: el juego no los avisa. Partida 20261104-095625 (incas, "Archer (Inca)"): el telar temprano
/// daba por hechos los 3 "Mover" a madera, y el último paso ("Mover 5 de ovejas a bayas") se pedía desde las 8:22, antes de
/// tener el asentamiento, hasta el final: como nada lo cerraba, la BO quedó abandonada a las 19:13 en vez de terminada.
/// </summary>
public class TrackerMoveTests
{
    private static Step Villagers(string id, int count, GatherSource source) =>
        new() { Id = id, Do = StepAction.Train, What = "villager", Count = count, Assign = source };

    private static Step Move(string id, int count, GatherSource from, GatherSource to) =>
        new() { Id = id, Do = StepAction.Move, Count = count, From = from, Assign = to };

    private static Tracker NewTracker(params Step[] steps) => new(new BuildOrder { Name = "t", Steps = [.. steps] }, TestData.GameData);

    [Fact]
    public void Un_telar_temprano_no_da_por_hecho_un_Mover_de_antes()
    {
        var tracker = NewTracker(
            Villagers("s1", 6, GatherSource.Sheep),
            new Step { Id = "s2", Do = StepAction.Research, What = "feudal_age" },
            Move("s3", 3, GatherSource.Sheep, GatherSource.Wood),
            new Step { Id = "s4", Do = StepAction.Research, What = "loom" });
        var partida = new Partida().Events("villager_created", 6).Event("loom_researched");

        tracker.Update(partida.State);

        Assert.NotEqual(StepState.Done, tracker.Steps[2].State);
    }

    [Fact]
    public void Un_Mover_no_se_pide_hasta_que_la_BO_llega_a_el()
    {
        var tracker = NewTracker(
            Villagers("s1", 6, GatherSource.Sheep),
            new Step { Id = "s2", Do = StepAction.Build, What = "mill", From = GatherSource.Sheep, Assign = GatherSource.Berries },
            Move("s3", 3, GatherSource.Sheep, GatherSource.Berries));
        var partida = new Partida().Events("villager_created", 6);
        tracker.Update(partida.State);
        Assert.Null(tracker.LaneStep(Lane.Villagers));

        tracker.Update(partida.Event("mill_built").State);

        Assert.Equal("s3", tracker.LaneStep(Lane.Villagers)?.Step.Id);
    }

    [Fact]
    public void Un_Mover_espera_tambien_a_los_aldeanos_de_antes()
    {
        var tracker = NewTracker(
            Villagers("s1", 6, GatherSource.Sheep),
            Move("s2", 2, GatherSource.Sheep, GatherSource.Berries));
        var partida = new Partida().Wait(Tracker.MoveSeconds * 2);
        tracker.Update(partida.State);
        Assert.Equal("s1", tracker.LaneStep(Lane.Villagers)?.Step.Id);
        Assert.NotEqual(StepState.Done, tracker.Steps[1].State);

        tracker.Update(partida.Events("villager_created", 6).State);

        Assert.Equal("s2", tracker.LaneStep(Lane.Villagers)?.Step.Id);
    }

    [Fact]
    public void Un_clic_de_edad_pagado_deja_pasar_al_Mover_que_sigue()
    {
        var tracker = NewTracker(
            new Step { Id = "s1", Do = StepAction.Research, What = "feudal_age" },
            Move("s2", 3, GatherSource.Sheep, GatherSource.Wood));
        var partida = new Partida().Wait(1);
        tracker.Update(partida.State);
        Assert.Null(tracker.LaneStep(Lane.Villagers));

        tracker.MarkStarted(0, partida.Time);

        Assert.Equal("s2", tracker.LaneStep(Lane.Villagers)?.Step.Id);
    }

    [Fact]
    public void Un_Mover_que_nada_confirma_se_cierra_solo_y_la_BO_termina()
    {
        var tracker = NewTracker(Move("s1", 5, GatherSource.Sheep, GatherSource.Berries));
        var partida = new Partida();
        Assert.True(tracker.HasOpenSteps);

        tracker.Update(partida.Wait(Tracker.MoveSeconds - 1).State);
        Assert.NotEqual(StepState.Done, tracker.Steps[0].State);

        tracker.Update(partida.Wait(1).State);

        Assert.Equal(StepState.Done, tracker.Steps[0].State);
        Assert.False(tracker.HasOpenSteps);
        Assert.True(tracker.Finished);
    }

    // El aldeano que estaba en la cola sale a los 5 s del clic en Feudal: no dice nada de lo que hay que mover.
    [Fact]
    public void Un_aldeano_que_sale_no_cierra_un_Mover()
    {
        var tracker = NewTracker(
            Move("s1", 3, GatherSource.Sheep, GatherSource.Wood),
            Villagers("s2", 9, GatherSource.Sheep));
        var partida = new Partida().Wait(1);
        tracker.Update(partida.State);

        tracker.Update(partida.Event("villager_created", dt: 5).State);

        Assert.NotEqual(StepState.Done, tracker.Steps[0].State);
    }

    [Fact]
    public void Con_el_HUD_un_Mover_entre_recursos_se_cumple_cuando_se_ve_en_el_recurso()
    {
        var tracker = NewTracker(Villagers("s1", 6, GatherSource.Sheep), Move("s2", 3, GatherSource.Sheep, GatherSource.Wood));
        var partida = new Partida().Events("villager_created", 6).Hud(new ResourceAmounts(Food: 6, Wood: 0, Stone: 0, Gold: 0));
        tracker.Update(partida.State);

        // Más que lo que aguantaría sin el HUD: mientras no se ve, sigue pidiéndolo.
        tracker.Update(partida.Hud(new ResourceAmounts(Food: 6, Wood: 0, Stone: 0, Gold: 0), dt: Tracker.MoveSeconds + 1).State);
        Assert.NotEqual(StepState.Done, tracker.Steps[1].State);

        tracker.Update(partida.Hud(new ResourceAmounts(Food: 3, Wood: 3, Stone: 0, Gold: 0)).State);
        tracker.Update(partida.Hud(new ResourceAmounts(Food: 3, Wood: 3, Stone: 0, Gold: 0), dt: Tracker.HudConfirmSeconds).State);

        Assert.Equal(StepState.Done, tracker.Steps[1].State);
    }

    [Fact]
    public void Con_el_HUD_un_Mover_que_el_jugador_no_hace_tampoco_queda_para_siempre()
    {
        var tracker = NewTracker(Villagers("s1", 6, GatherSource.Sheep), Move("s2", 3, GatherSource.Sheep, GatherSource.Wood));
        var partida = new Partida().Events("villager_created", 6).Hud(new ResourceAmounts(Food: 6, Wood: 0, Stone: 0, Gold: 0));
        tracker.Update(partida.State);

        tracker.Update(partida.Hud(new ResourceAmounts(Food: 6, Wood: 0, Stone: 0, Gold: 0), dt: Tracker.MoveHudSeconds).State);

        Assert.Equal(StepState.Done, tracker.Steps[1].State);
    }

    [Fact]
    public void Dos_Mover_seguidos_se_piden_de_a_uno()
    {
        var tracker = NewTracker(
            Move("s1", 3, GatherSource.Sheep, GatherSource.Berries),
            Move("s2", 2, GatherSource.Sheep, GatherSource.Berries));
        var partida = new Partida().Wait(1);
        tracker.Update(partida.State);
        Assert.Equal("s1", tracker.LaneStep(Lane.Villagers)?.Step.Id);

        tracker.Update(partida.Wait(Tracker.MoveSeconds).State);

        Assert.Equal(StepState.Done, tracker.Steps[0].State);
        Assert.Equal("s2", tracker.LaneStep(Lane.Villagers)?.Step.Id);
        Assert.NotEqual(StepState.Done, tracker.Steps[1].State);
    }

    [Fact]
    public void A_la_poblacion_que_pide_arranca_la_cuenta_del_Mover()
    {
        var move = Move("s1", 3, GatherSource.Sheep, GatherSource.Berries);
        move.AtPop = 6;
        var tracker = NewTracker(move);
        var partida = new Partida().Wait(Tracker.MoveSeconds + 1);
        tracker.Update(partida.State);
        Assert.NotEqual(StepState.Done, tracker.Steps[0].State);

        tracker.Update(partida.PopulationGrows(2).State);
        Assert.NotEqual(StepState.Done, tracker.Steps[0].State);

        tracker.Update(partida.Wait(Tracker.MoveSeconds).State);

        Assert.Equal(StepState.Done, tracker.Steps[0].State);
    }

    [Fact]
    public void Atras_reabre_un_Mover_cerrado_solo_y_no_se_vuelve_a_cerrar()
    {
        var tracker = NewTracker(Move("s1", 5, GatherSource.Sheep, GatherSource.Berries));
        var partida = new Partida().Wait(1);
        tracker.Update(partida.State);
        tracker.Update(partida.Wait(Tracker.MoveSeconds).State);
        Assert.Equal(StepState.Done, tracker.Steps[0].State);

        tracker.Back();
        Assert.NotEqual(StepState.Done, tracker.Steps[0].State);

        tracker.Update(partida.Wait(Tracker.MoveSeconds * 2).State);

        Assert.NotEqual(StepState.Done, tracker.Steps[0].State);
        Assert.Equal("s1", tracker.LaneStep(Lane.Villagers)?.Step.Id);
    }

    // La partida 20261030-203917 ya enseñó que un segundo molino que nadie hace no puede dejar la BO sin terminar.
    [Fact]
    public void Un_Mover_que_espera_un_paso_que_quedo_atras_no_impide_terminar_la_BO()
    {
        var tracker = NewTracker(
            new Step { Id = "s1", Do = StepAction.Build, What = "mill", From = GatherSource.Sheep, Assign = GatherSource.Berries },
            Move("s2", 3, GatherSource.Sheep, GatherSource.Berries),
            new Step { Id = "s3", Do = StepAction.Build, What = "lumber_camp", From = GatherSource.Sheep, Assign = GatherSource.Wood });
        var partida = new Partida().Wait(1);
        tracker.Update(partida.State);

        tracker.Update(partida.Event("lumber_camp_built").State);

        Assert.True(tracker.IsLeftBehind(0));
        Assert.True(tracker.IsLeftBehind(1));
        Assert.False(tracker.HasOpenSteps);
    }

    // Las granjas no se avisan: un Mover no se queda esperando algo que nunca va a verse.
    [Fact]
    public void Un_Mover_no_espera_lo_que_el_juego_no_avisa()
    {
        var tracker = NewTracker(
            new Step { Id = "s1", Do = StepAction.Build, What = "farm", From = GatherSource.Sheep, Assign = GatherSource.Farm },
            Move("s2", 3, GatherSource.Sheep, GatherSource.Farm));
        var partida = new Partida().Wait(1);

        tracker.Update(partida.State);

        Assert.Equal("s2", tracker.LaneStep(Lane.Villagers)?.Step.Id);
    }

    [Fact]
    public void Una_BO_que_termina_con_un_Mover_llega_a_la_postura_y_no_queda_abandonada()
    {
        var bo = new BuildOrder { Name = "t", Steps = [Move("s1", 5, GatherSource.Sheep, GatherSource.Berries)] };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        var view = coach.View;

        for (var t = 0; t <= BuildOrderAbandonment.MaxStall + 60; t += 10)
            view = coach.Update(new GameSnapshot(t, new ResourceAmounts(200, 200, 100, 200), 5, 15));

        Assert.Null(view.Abandoned);
        Assert.NotNull(view.Posture);
    }

    [Fact]
    public void Un_Mover_cerrado_da_por_hechas_las_granjas_de_antes()
    {
        var tracker = NewTracker(
            new Step { Id = "s1", Do = StepAction.Build, What = "farm", From = GatherSource.Sheep, Assign = GatherSource.Farm },
            Move("s2", 3, GatherSource.Sheep, GatherSource.Farm));
        var partida = new Partida().Wait(1);
        tracker.Update(partida.State);

        tracker.Update(partida.Wait(Tracker.MoveSeconds).State);

        Assert.True(tracker.Finished);
    }
}
