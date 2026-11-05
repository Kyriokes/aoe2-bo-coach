using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>Pasos mixtos: varias metas que se hacen a la vez (<see cref="Step.Group"/>).</summary>
public class TrackerGroupTests
{
    private static readonly ResourceAmounts Stock = new(500, 500, 200, 200);

    private static BuildOrder Bo(params Step[] steps) => new() { Id = "t", Name = "Prueba", Steps = steps };

    private static (Tracker Tracker, PlayerState State) Start(BuildOrder bo)
    {
        var tracker = new Tracker(bo, TestData.GameData);
        var state = new PlayerState();
        tracker.Update(state);
        return (tracker, state);
    }

    private static void Play(Tracker tracker, PlayerState state, double time, params string[] events)
    {
        state.Apply(new GameSnapshot(time, Stock, 5, 15, events.Select(e => new GameEvent(e, time)).ToArray()));
        tracker.Update(state);
    }

    [Fact]
    public void El_paso_mixto_muestra_todas_sus_metas_y_despues_es_el_paso_siguiente()
    {
        var (tracker, _) = Start(Bo(
            new Step { Id = "a", Do = StepAction.Build, What = "barracks", Group = "g" },
            new Step { Id = "b", Do = StepAction.Build, What = "house", Group = "g" },
            new Step { Id = "c", Do = StepAction.Build, What = "mill" }));

        Assert.Equal("a", tracker.Current?.Step.Id);
        Assert.Equal(["a", "b"], tracker.CurrentGoals.Select(g => g.Step.Id));
        Assert.Equal("c", tracker.Next?.Step.Id);
    }

    [Fact]
    public void Una_meta_que_termina_antes_que_otra_del_mismo_paso_no_la_deja_atrasada()
    {
        // Las dos las hacen aldeanos: en pasos simples, la casa hecha antes que el cuartel lo dejaría atrasado.
        var (tracker, state) = Start(Bo(
            new Step { Id = "a", Do = StepAction.Build, What = "barracks", Group = "g" },
            new Step { Id = "b", Do = StepAction.Build, What = "house", Group = "g" },
            new Step { Id = "c", Do = StepAction.Build, What = "mill" }));

        Play(tracker, state, 40, "house_built");

        Assert.Equal(StepState.Done, tracker.Steps[1].State);
        Assert.Equal("a", tracker.Current?.Step.Id);
        Assert.Equal("c", tracker.Next?.Step.Id);
    }

    [Fact]
    public void El_paso_mixto_termina_cuando_terminan_todas_sus_metas()
    {
        var (tracker, state) = Start(Bo(
            new Step { Id = "a", Do = StepAction.Build, What = "barracks", Group = "g" },
            new Step { Id = "b", Do = StepAction.Build, What = "house", Group = "g" },
            new Step { Id = "c", Do = StepAction.Build, What = "mill" }));

        Play(tracker, state, 40, "house_built");
        Play(tracker, state, 60, "barracks_built");

        Assert.Equal("c", tracker.Current?.Step.Id);
        Assert.Empty(tracker.CurrentGoals);
    }

    [Fact]
    public void Las_metas_de_un_grupo_tienen_que_ir_seguidas()
    {
        var bo = Bo(
            new Step { Id = "a", Do = StepAction.Build, What = "house", Group = "g" },
            new Step { Id = "b", Do = StepAction.Build, What = "mill" },
            new Step { Id = "c", Do = StepAction.Build, What = "house", Group = "g" });

        var result = new BuildOrderValidator(TestData.GameData).Validate(bo);

        Assert.Contains(result.Errors, e => e.Message.Contains("seguidas"));
    }

    private static Step Farmers(string id, int count, string? group = "g") =>
        new() { Id = id, Do = StepAction.Train, What = "villager", Count = count, Assign = GatherSource.Farm, Group = group };

    private static Step Farms(string id, int count, string? group = "g") =>
        new() { Id = id, Do = StepAction.Build, What = "farm", Count = count, From = GatherSource.Farm, Assign = GatherSource.Farm, Group = group };

    // Auditoría del 2026-11-05: la granja de un paso mixto queda hecha con sus compañeras, y si la compañera se salteó o
    // no pedía nada, quedaba abierta para siempre.
    [Fact]
    public void Si_se_saltea_la_companera_la_granja_del_paso_mixto_no_queda_abierta()
    {
        var (tracker, state) = Start(Bo(Farmers("v", 2), Farms("f", 2)));
        Play(tracker, state, 100);

        tracker.Skip(Lane.Villagers);

        Assert.Equal(StepState.Skipped, tracker.Steps[0].State);
        Assert.Equal(StepState.Done, tracker.Steps[1].State);
        Assert.Equal(100, tracker.Steps[1].DoneAt);
        Assert.False(tracker.HasOpenSteps);

        // "Atrás" la vuelve a abrir.
        tracker.Back();
        Assert.NotEqual(StepState.Done, tracker.Steps[1].State);
    }

    [Fact]
    public void Si_la_companera_no_pide_nada_la_granja_es_como_una_suelta()
    {
        // Los dos granjeros ya estaban (paso "v0"): el paso mixto no pide ningún aldeano y nada confirmaría la granja.
        var bo = Bo(Farmers("v0", 2, group: null), Farmers("v", 2), Farms("f", 2), new Step { Id = "m", Do = StepAction.Build, What = "mill" });
        var (tracker, state) = Start(bo);
        Play(tracker, state, 60, "villager_created", "villager_created");

        Assert.Equal(StepState.Done, tracker.Steps[1].State); // no pide ninguno
        Assert.NotEqual(StepState.Done, tracker.Steps[2].State); // todavía no le tocó: no se da por hecha sola
        Assert.Equal("m", tracker.Current?.Step.Id); // no traba la BO: se muestra aparte
        Assert.Contains(tracker.Silent, s => s.Step.Id == "f");

        Play(tracker, state, 120, "mill_built"); // queda hecha cuando avanza un paso posterior

        Assert.Equal(StepState.Done, tracker.Steps[2].State);
    }

    [Fact]
    public void Si_es_lo_ultimo_y_la_companera_no_pide_nada_la_BO_termina_igual()
    {
        var (tracker, state) = Start(Bo(Farmers("v0", 2, group: null), Farmers("v", 2), Farms("f", 2)));

        Play(tracker, state, 60, "villager_created", "villager_created");

        Assert.Null(tracker.Current);
        Assert.False(tracker.HasOpenSteps);
    }

    [Fact]
    public void Dos_pasos_de_granjas_en_un_grupo_no_se_esperan_entre_si()
    {
        var (tracker, state) = Start(Bo(Farms("f1", 1), Farms("f2", 1), new Step { Id = "m", Do = StepAction.Build, What = "mill" }));

        Play(tracker, state, 60, "mill_built");

        Assert.Equal(StepState.Done, tracker.Steps[0].State);
        Assert.Equal(StepState.Done, tracker.Steps[1].State);
    }
}
