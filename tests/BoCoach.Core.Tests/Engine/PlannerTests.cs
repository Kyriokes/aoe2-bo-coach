using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

public class PlannerTests
{
    private static readonly Planner Planner = new(TestData.GameData);

    private static BuildOrder Bo(params Step[] steps) => new() { Name = "t", Steps = steps };

    private static Step Villagers(string id, int count, GatherSource assign = GatherSource.Sheep) =>
        new() { Id = id, Do = StepAction.Train, What = "villager", Count = count, Assign = assign };

    private static BuildOrder Load(string file) => BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, file));

    [Theory]
    [InlineData("scouts_feudal.json", 7 * 60, 10 * 60)]
    [InlineData("archers_feudal.json", 7 * 60, 10 * 60)]
    [InlineData("fast_castle.json", 9 * 60, 12 * 60)]
    public void Las_BO_de_ejemplo_se_completan_sin_bloqueos_de_poblacion(string file, int feudalMin, int feudalMax)
    {
        var plan = Planner.Simulate(Load(file));

        Assert.True(plan.Completed);
        Assert.DoesNotContain(plan.Blocks, b => b.Reason == BlockReason.Population);
        Assert.InRange(plan.ClickTime("feudal_age")!.Value, feudalMin, feudalMax);
    }

    [Fact]
    public void Marca_el_bloqueo_de_poblacion_si_no_se_hacen_casas()
    {
        var plan = Planner.Simulate(Bo(Villagers("s1", 6)));

        Assert.False(plan.Completed);
        var block = Assert.Single(plan.Blocks, b => b.Reason == BlockReason.Population);
        Assert.Equal("s1", block.StepId);
        Assert.Equal(25, block.From); // pop 4/5: el primer aldeano sale a los 25 s y ahí la cola se traba en 5/5
    }

    [Fact]
    public void La_casa_destraba_la_poblacion()
    {
        var plan = Planner.Simulate(Bo(
            new Step { Id = "s1", Do = StepAction.Build, What = "house", Builders = 3, Assign = GatherSource.Sheep },
            Villagers("s2", 3)));

        Assert.True(plan.Completed);
        Assert.DoesNotContain(plan.Blocks, b => b.Reason == BlockReason.Population && b.Duration > 20);
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(3, 15)] // 25 × 3 / (3 + 2)
    public void Tiempo_de_construccion_segun_constructores(int builders, double seconds)
    {
        var plan = Planner.Simulate(Bo(new Step { Id = "s1", Do = StepAction.Build, What = "house", Builders = builders }));

        var s1 = plan.For("s1")!;
        Assert.Equal(seconds, s1.End - s1.Start);
    }

    [Fact]
    public void Las_granjas_se_construyen_en_paralelo()
    {
        var start = PlanStart.Standard with { Villagers = new Dictionary<GatherSource, int> { [GatherSource.Sheep] = 3 }, IdleVillagers = 0 };
        var farms = new Step { Id = "s1", Do = StepAction.Build, What = "farm", Count = 3, From = GatherSource.Sheep, Assign = GatherSource.Farm };

        var plan = Planner.Simulate([new PlanItem(farms, 3)], start);

        Assert.Equal(15, plan.For("s1")!.End);
    }

    [Fact]
    public void Espera_recursos_y_dice_cual_falta()
    {
        var plan = Planner.Simulate(Bo(Villagers("s1", 1), new Step { Id = "s2", Do = StepAction.Research, What = "loom" }, new Step { Id = "s3", Do = StepAction.Research, What = "feudal_age" }));

        Assert.True(plan.Completed);
        Assert.Contains(plan.Blocks, b => b.StepId == "s3" && b.Reason == BlockReason.Resources && b.Missing == Resource.Food);
        Assert.True(plan.ClickTime("feudal_age") > plan.For("s2")!.End);
    }

    [Fact]
    public void Un_edificio_de_otra_edad_espera_la_edad()
    {
        var plan = Planner.Simulate(Bo(new Step { Id = "s1", Do = StepAction.Build, What = "archery_range" }));

        Assert.Contains(plan.Blocks, b => b.StepId == "s1" && b.Reason == BlockReason.Age);
    }

    [Fact]
    public void Una_unidad_sin_su_edificio_espera_el_edificio()
    {
        var plan = Planner.Simulate(Bo(new Step { Id = "s1", Do = StepAction.Train, What = "militia" }));

        Assert.Contains(plan.Blocks, b => b.StepId == "s1" && b.Reason == BlockReason.Producer);
    }

    [Fact]
    public void Mover_aldeanos_cambia_la_recoleccion()
    {
        var start = PlanStart.Standard with { Villagers = new Dictionary<GatherSource, int> { [GatherSource.Wood] = 5 }, IdleVillagers = 0, Stock = ResourceAmounts.Zero };
        var move = new Step { Id = "s1", Do = StepAction.Move, Count = 5, From = GatherSource.Wood, Assign = GatherSource.Gold };
        var wait = new Step { Id = "s2", Do = StepAction.Research, What = "loom" }; // 50 de oro

        var plan = Planner.Simulate([new PlanItem(move, 1), new PlanItem(wait, 1)], start);

        Assert.True(plan.Completed);
        Assert.InRange(plan.ClickTime("loom")!.Value, 10, 40);
    }

    [Fact]
    public void Jugador_atrasado_da_desvio_positivo_contra_el_objetivo()
    {
        var bo = Load("scouts_feudal.json");

        PlanResult PlanAt(double dt)
        {
            var partida = new Partida().Play(bo, 0, 4, dt);
            partida.Stock = new ResourceAmounts(100, 50, 200, 100);
            partida.Wait(1);
            var tracker = new Tracker(bo, TestData.GameData);
            tracker.Update(partida.State);
            return LivePlan.Replan(tracker, Planner);
        }

        var onTime = PlanAt(dt: 15);  // 14 eventos (s1 a s5) en 3:30, cerca de lo que estima el Planner
        var late = PlanAt(dt: 25);    // el mismo progreso, 2:20 más tarde

        var onTimeDev = Assert.Single(LivePlan.Deviations(bo, onTime));
        var lateDev = Assert.Single(LivePlan.Deviations(bo, late));
        Assert.InRange(lateDev.Delta - onTimeDev.Delta, 135, 145);
        Assert.True(lateDev.Delta > 0);
        Assert.StartsWith("Feudal estimada en", lateDev.ToString());
    }

    [Fact]
    public void Re_planificar_desde_el_tracker_respeta_lo_ya_hecho()
    {
        var bo = Load("scouts_feudal.json");
        var partida = new Partida().Play(bo, 0, 1);
        var tracker = new Tracker(bo, TestData.GameData);
        tracker.Update(partida.State);

        var start = LivePlan.Start(tracker);
        var items = LivePlan.Items(tracker);

        Assert.Equal(9, start.Villagers[GatherSource.Sheep]); // 3 iniciales + 6 nuevos
        Assert.Equal(0, start.IdleVillagers);
        Assert.Equal(2, start.Buildings["house"]);
        Assert.Equal("s3", items[0].Step.Id);
        Assert.Equal(partida.Time, start.Time);
    }
}
