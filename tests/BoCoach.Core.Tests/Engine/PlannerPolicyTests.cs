using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>D12 (b): una política que agrega pasos mientras el Planner simula, y la BO que sale da lo mismo.</summary>
public class PlannerPolicyTests
{
    private static readonly PlanStart Open = PlanStart.Standard with { PopulationCap = 200 };

    /// <summary>Una política de prueba: lo que diga la función, y anota cada foto que recibe.</summary>
    private sealed class Scripted(Func<PlanView, IEnumerable<Step>> next) : IStepPolicy
    {
        public List<PlanView> Views { get; } = [];

        public IReadOnlyList<Step> Next(PlanView view)
        {
            Views.Add(view);
            return next(view).ToList();
        }
    }

    /// <summary>
    /// Una política simple: los sin tarea a las ovejas; un aldeano a la vez hasta <paramref name="maxVillagers"/> (a
    /// las ovejas hasta tener 6 ahí, después a la madera); y una casa cuando quedan 2 lugares o menos.
    /// </summary>
    private sealed class Simple(bool houses, int maxVillagers = 15) : IStepPolicy
    {
        private int _ids;
        private bool _toBerries;

        public IReadOnlyList<Step> Next(PlanView v)
        {
            var steps = new List<Step>();
            if (v.IdleVillagers > 0)
                steps.Add(new Step { Id = Id("sin-tarea"), Do = StepAction.Move, Count = v.IdleVillagers, Assign = GatherSource.Sheep });
            // Con la comida finita: los de las ovejas pasan a las bayas cuando se acaban, y cuando se acaban las bayas
            // cada aldeano nuevo va a la granja con la suya. Los nuevos no van a ovejas que se están acabando.
            var sheepEnding = v.Food is { } food && food.Remaining[GatherSource.Sheep] < 200;
            if (sheepEnding && !_toBerries && v.Villagers[GatherSource.Sheep] > 0)
            {
                _toBerries = true;
                steps.Add(new Step { Id = Id("a-bayas"), Do = StepAction.Move, Count = v.Villagers[GatherSource.Sheep], From = GatherSource.Sheep, Assign = GatherSource.Berries });
            }
            var villagers = v.Villagers.Values.Sum() + v.IdleVillagers + v.Builders;
            var pending = v.Steps.Any(s => s.Step is { Do: StepAction.Train, What: "villager" } && s.End is null);
            if (villagers < maxVillagers && !pending && v.Food is { } f && f.Remaining[GatherSource.Berries] < 200)
            {
                steps.Add(new Step { Id = Id("granjero"), Do = StepAction.Train, What = "villager", Assign = GatherSource.Farm });
                steps.Add(new Step { Id = Id("granja"), Do = StepAction.Build, What = "farm", From = GatherSource.Farm, Assign = GatherSource.Farm });
            }
            else if (villagers < maxVillagers && !pending)
                steps.Add(new Step { Id = Id("aldeano"), Do = StepAction.Train, What = "villager", Assign = v.Villagers[GatherSource.Sheep] < 6 && !sheepEnding ? GatherSource.Sheep : GatherSource.Wood });
            var house = v.Steps.Any(s => s.Step is { Do: StepAction.Build, What: "house" } && s.End is null);
            if (houses && v.PopulationCap - v.Population - v.InProduction <= 2 && !house)
                steps.Add(new Step { Id = Id("casa"), Do = StepAction.Build, What = "house", Assign = GatherSource.Sheep });
            return steps;
        }

        private string Id(string what) => $"{what}-{++_ids}";
    }

    private static Step Villagers(string id, int count, GatherSource assign) =>
        new() { Id = id, Do = StepAction.Train, What = "villager", Count = count, Assign = assign };

    private static PlanResult Run(IStepPolicy policy, PlanStart? start = null, CivBonuses? civ = null) =>
        new Planner(TestData.GameData).Simulate([], start ?? Open, civ, policy);

    /// <summary>La BO que sale: los pasos con las condiciones que les puso el Planner, pasados a totales.</summary>
    private static BuildOrder Generated(PlanResult plan, string civ)
    {
        var bo = new BuildOrder { Name = "g", Civ = civ, Steps = plan.Steps.Select(s => s.Step with { }).ToList() };
        LivePlan.CountsToTotals(bo);
        return bo;
    }

    private static void AssertSame(PlanResult expected, PlanResult actual)
    {
        Assert.Equal(expected.Steps.Select(s => (s.Step.Id, s.Start, s.End)), actual.Steps.Select(s => (s.Step.Id, s.Start, s.End)));
        Assert.Equal(expected.Blocks, actual.Blocks);
        Assert.Equal(expected.Timeline, actual.Timeline);
        Assert.Equal(expected.Idle, actual.Idle);
    }

    [Fact]
    public void La_politica_se_consulta_al_quedarse_sin_pasos_y_en_cada_nacimiento()
    {
        var policy = new Scripted(v => v.Steps.Count == 0 ? [Villagers("v", 3, GatherSource.Sheep)] : []);

        Run(policy);

        // A los 0 s la BO está vacía; después de agregar los aldeanos vuelve a quedarse sin pasos y se consulta otra vez.
        Assert.Equal([0.0, 0, 25, 50, 75], policy.Views.Select(v => v.Time));
        Assert.Equal([false, false, true, true, true], policy.Views.Select(v => v.Birth));
        Assert.True(policy.Views[0].RanOut);
    }

    [Fact]
    public void Lo_que_agrega_arranca_en_el_mismo_segundo()
    {
        var house = new Step { Id = "casa", Do = StepAction.Build, What = "house", Builders = 3, Assign = GatherSource.Sheep };

        var plan = Run(new Scripted(v => v.Steps.Count == 0 ? [house] : []));

        Assert.Equal(0, plan.For("casa")!.Start);
    }

    [Theory]
    [InlineData("any")]
    [InlineData("franks")]
    [InlineData("huns")]
    public void La_BO_generada_da_lo_mismo_que_la_simulacion_con_la_politica(string civ)
    {
        var planner = new Planner(TestData.GameData);
        var houses = !TestData.GameData.Civ(civ).Start.NoHouses;

        var withPolicy = planner.Simulate(new BuildOrder { Name = "g", Civ = civ }, new Simple(houses));
        var bo = Generated(withPolicy, civ);

        Assert.True(withPolicy.Steps.Count > 10);
        AssertSame(withPolicy, planner.Simulate(bo));
        Assert.DoesNotContain(new BuildOrderValidator(TestData.GameData).Validate(bo).Issues, i => i.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("any")]
    [InlineData("tatars")]
    [InlineData("poles")]
    public void Con_comida_finita_la_BO_generada_da_lo_mismo(string civ)
    {
        var planner = new Planner(TestData.GameData, new PlannerSettings { Food = FoodSupply.For(TestData.GameData, null) });

        var withPolicy = planner.Simulate(new BuildOrder { Name = "g", Civ = civ }, new Simple(houses: true, maxVillagers: 40));
        var bo = Generated(withPolicy, civ);

        Assert.Contains(withPolicy.Steps, s => s.Step is { Do: StepAction.Build, What: "farm" });
        AssertSame(withPolicy, planner.Simulate(bo));
        Assert.DoesNotContain(new BuildOrderValidator(TestData.GameData).Validate(bo).Issues, i => i.Severity == Severity.Error);
    }

    [Fact]
    public void Un_paso_agregado_despues_queda_fijado_para_arrancar_en_el_mismo_segundo()
    {
        // Cada paso una sola vez: lo que se agrega puede volver a dejar la BO sin pasos y se pregunta otra vez en el mismo segundo.
        var policy = new Scripted(v =>
        {
            bool Has(string id) => v.Steps.Any(s => s.Step.Id == id);
            if (v.Steps.Count == 0)
                return [new Step { Id = "casas", Do = StepAction.Build, What = "house", Count = 2, Builders = 3, Assign = GatherSource.Sheep }, Villagers("v", 3, GatherSource.Sheep)];
            if (v.Birth && v.Time == 25 && !Has("campamento"))
                return [new Step { Id = "campamento", Do = StepAction.Build, What = "lumber_camp", From = GatherSource.Sheep, Assign = GatherSource.Wood }];
            if (v.Birth && v.Time == 50 && !Has("cuartel"))
                return [new Step { Id = "cuartel", Do = StepAction.Build, What = "barracks", From = GatherSource.Wood, Assign = GatherSource.Wood }];
            var barracks = v.Steps.FirstOrDefault(s => s.Step.Id == "cuartel");
            if (barracks is { Started: true } && !Has("v-madera"))
                return [Villagers("v-madera", 1, GatherSource.Wood)];
            return [];
        });

        // Desde el arranque estándar, como la BO generada (las 2 casas del principio dan el tope).
        var plan = new Planner(TestData.GameData).Simulate(new BuildOrder { Name = "g" }, policy);

        Assert.Equal(5, plan.For("campamento")!.Step.AtPop);
        Assert.Equal(6, plan.For("cuartel")!.Step.AtPop);
        var last = plan.For("v-madera")!.Step;
        Assert.True(last.AtTime is not null || last.AtPop is not null);
        AssertSame(plan, new Planner(TestData.GameData).Simulate(Generated(plan, "any")));
    }

    [Fact]
    public void Una_politica_que_no_para_de_agregar_da_error()
    {
        var n = 0;
        var policy = new Scripted(_ => [new Step { Id = $"m{++n}", Do = StepAction.Move, Count = 0, Assign = GatherSource.Wood }]);

        var e = Assert.Throws<InvalidOperationException>(() => Run(policy));

        Assert.Contains("política", e.Message);
    }

    [Fact]
    public void Un_id_repetido_da_error()
    {
        var policy = new Scripted(v => v.Birth || v.Steps.Count == 0 ? [Villagers("x", 1, GatherSource.Sheep)] : []);

        var e = Assert.Throws<InvalidOperationException>(() => Run(policy));

        Assert.Contains("'x'", e.Message);
    }

    [Fact]
    public void Sin_comida_finita_la_foto_no_trae_comida()
    {
        var policy = new Scripted(_ => []);

        Run(policy);

        Assert.Null(policy.Views[0].Food);
    }

    [Fact]
    public void La_foto_trae_lo_que_queda_de_cada_fuente_con_el_rendimiento_de_la_civ()
    {
        var policy = new Scripted(_ => []);
        var start = Open with { IdleVillagers = 0, Villagers = new Dictionary<GatherSource, int> { [GatherSource.Sheep] = 6 } };

        new Planner(TestData.GameData, new PlannerSettings { Food = FoodSupply.For(TestData.GameData, null) })
            .Simulate([], start, TestData.GameData.Civ("tatars"), policy);

        var food = policy.Views[0].Food!;
        Assert.Equal(800 * 1.57, food.Remaining[GatherSource.Sheep], 6);
        Assert.Equal(680, food.Remaining[GatherSource.Hunt], 6);
        Assert.False(food.Remaining.ContainsKey(GatherSource.Wood));
    }

    [Fact]
    public void La_foto_trae_las_granjas_y_los_granjeros_que_esperan()
    {
        var policy = new Scripted(_ => []);
        var start = Open with
        {
            IdleVillagers = 0,
            Villagers = new Dictionary<GatherSource, int> { [GatherSource.Farm] = 2 },
            Stock = new ResourceAmounts(200, 0, 0, 100),
        };
        var supply = FoodSupply.For(TestData.GameData, null) with { FarmFood = 50 };
        var late = Villagers("v", 1, GatherSource.Sheep) with { AtTime = 275 };

        new Planner(TestData.GameData, new PlannerSettings { Food = supply }).Simulate([new PlanItem(late, 1)], start, null, policy);

        var food = policy.Views.Single(v => v.Birth && v.Time == 300).Food!;
        Assert.Equal([0.0, 0.0], food.Farms);
        Assert.Equal(2, food.FarmersWaitingReseed);
        Assert.Equal(0, food.FarmersWaitingFarm);
    }

    [Fact]
    public void Cambiar_un_paso_ya_devuelto_no_cambia_la_simulacion()
    {
        Step? given = null;
        var changing = new Scripted(v =>
        {
            if (given is null)
                return [given = Villagers("v", 3, GatherSource.Sheep)];
            given.Count = 30;
            return [];
        });
        var steady = new Scripted(v => v.Steps.Count == 0 ? [Villagers("v", 3, GatherSource.Sheep)] : []);

        AssertSame(Run(steady), Run(changing));
    }
}
