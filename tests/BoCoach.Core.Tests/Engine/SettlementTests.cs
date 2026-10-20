using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// El "Settlement" de los incas es campamento maderero, campamento minero, molino y casa en un solo edificio (en el
/// log, "Settlement Built"). Partida 20261017-163738: el coach seguía pidiendo el campamento y el molino, que ya
/// estaban en el Settlement.
/// </summary>
public class SettlementTests
{
    private static readonly CivBonuses Incas = TestData.GameData.Civ("incas");

    private static ScreenReading Reading(double clock, params string[] events) =>
        new(clock, 200, 200, 100, 200, 3, Population: null, PopulationCap: null, events);

    private static Step Build(string id, string what) => new() { Id = id, Do = StepAction.Build, What = what };

    private static Step Research(string id, string what) => new() { Id = id, Do = StepAction.Research, What = what };

    [Fact]
    public void Un_Settlement_cuenta_como_los_cuatro_edificios_y_da_su_poblacion()
    {
        var builder = new SnapshotBuilder(TestData.GameData, Incas);
        builder.Build(Reading(10));

        var snapshot = builder.Build(Reading(40, "settlement_built"))!;

        var ids = snapshot.Events.Select(e => e.EventId).ToList();
        Assert.Contains("settlement_built", ids);
        Assert.Contains("lumber_camp_built", ids);
        Assert.Contains("mining_camp_built", ids);
        Assert.Contains("mill_built", ids);
        Assert.Contains("house_built", ids);
        Assert.Equal(5, ids.Count); // una vez cada uno: la población no se suma dos veces
        Assert.Equal(5 + Incas.PopBonus(TestData.GameData.Get("settlement"), Age.Dark), snapshot.PopulationCap);
    }

    [Fact]
    public void Los_pasos_de_campamento_molino_y_casa_quedan_hechos_con_un_Settlement()
    {
        var bo = new BuildOrder { Id = "t", Name = "Incas", Civ = "incas", Steps = [Build("s1", "lumber_camp"), Build("s2", "mill"), Build("s3", "house")] };
        var builder = new SnapshotBuilder(TestData.GameData, Incas);
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        coach.Update(builder.Build(Reading(10))!);

        coach.Update(builder.Build(Reading(40, "settlement_built"))!);

        Assert.All(coach.Tracker.Steps, s => Assert.Equal(StepState.Done, s.State));
    }

    [Fact]
    public void El_Planner_sabe_que_en_el_Settlement_se_investigan_las_mejoras_del_campamento()
    {
        var bo = new BuildOrder
        {
            Id = "t", Name = "Incas", Civ = "incas",
            Steps =
            [
                new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 6, Assign = GatherSource.Sheep },
                new Step { Id = "s2", Do = StepAction.Train, What = "villager", Count = 2, Assign = GatherSource.Wood },
                Build("s3", "settlement"),
                Research("s4", "feudal_age"),
                Research("s5", "double_bit_axe"), // se investiga en el campamento maderero
            ],
        };

        var plan = new Planner(TestData.GameData).Simulate(bo);

        // Mientras el Settlement se construye el bloqueo es legítimo; desde que está, el campamento maderero existe.
        var settlementDone = plan.For("s3")!.End!.Value;
        Assert.DoesNotContain(plan.Blocks, b => b.StepId == "s5" && b.Reason == BlockReason.Producer && b.To > settlementDone + 1);
        Assert.NotNull(plan.For("s5")?.End);
    }
}
