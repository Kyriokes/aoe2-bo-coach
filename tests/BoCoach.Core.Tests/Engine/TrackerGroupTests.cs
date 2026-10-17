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
        Assert.NotEqual(StepState.Late, tracker.Steps[0].State);
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
}
