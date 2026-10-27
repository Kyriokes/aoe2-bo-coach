using BoCoach.Core.Assisted;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Assisted;

/// <summary>D12 (c): cada regla del generador con fotos armadas a mano.</summary>
public class RuleTests
{
    private static GoalState State(string civ = "any", AssistedPlan? plan = null) =>
        new(TestData.GameData, TestData.GameData.Civ(civ), plan ?? new AssistedPlan());

    private static readonly IReadOnlyDictionary<GatherSource, double> Rates =
        new GatherModel(TestData.GameData, PlannerSettings.DefaultEfficiency).Rates(CivBonuses.None, Age.Dark, []);

    private static Dictionary<GatherSource, int> On(params (GatherSource Source, int Count)[] villagers)
    {
        var all = Enum.GetValues<GatherSource>().ToDictionary(s => s, _ => 0);
        foreach (var (source, count) in villagers)
            all[source] = count;
        return all;
    }

    [Fact]
    public void La_casa_se_pide_con_tres_lugares()
    {
        var view = new PlanView { Population = 12, InProduction = 1, PopulationCap = 15, Villagers = On((GatherSource.Wood, 4)), Rates = Rates, Stock = new ResourceAmounts(0, 100, 0, 0) };

        var house = Assert.Single(new HouseRule().Next(view, State()));

        Assert.Equal((StepAction.Build, "house", GatherSource.Wood, GatherSource.Wood), (house.Do, house.What, house.From, house.Assign));
    }

    [Fact]
    public void Sin_casas_para_los_hunos()
    {
        var view = new PlanView { Population = 12, InProduction = 1, PopulationCap = 15, Villagers = On((GatherSource.Wood, 4)), Rates = Rates };

        Assert.Empty(new HouseRule().Next(view, State("huns")));
    }

    [Fact]
    public void El_tramo_llena_las_ovejas_y_despues_reparte()
    {
        var view = new PlanView
        {
            Population = 4,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 3)),
            Stock = new ResourceAmounts(200, 0, 0, 100),
            Rates = Rates,
        };

        var steps = new VillagerRule().Next(view, State()).ToList();

        Assert.Equal((StepAction.Train, "villager", 3, GatherSource.Sheep), (steps[0].Do, steps[0].What, steps[0].Count, steps[0].Assign));
        Assert.Equal(6, steps.Where(s => s.Do == StepAction.Train).Sum(s => s.Count));
    }

    [Fact]
    public void El_primer_aldeano_a_la_madera_hace_el_campamento()
    {
        var view = new PlanView
        {
            Population = 7,
            InProduction = 1,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 6)),
            Stock = new ResourceAmounts(300, 100, 0, 100), // alcanza para el campamento
            Rates = Rates,
        };

        var steps = new VillagerRule().Next(view, State()).ToList();

        var builder = steps[0];
        var camp = steps[1];
        Assert.Equal((StepAction.Train, 1, GatherSource.Wood), (builder.Do, builder.Count, builder.Assign));
        Assert.Equal((StepAction.Build, "lumber_camp", GatherSource.Wood, GatherSource.Wood), (camp.Do, camp.What, camp.From, camp.Assign));
        Assert.Equal(9, camp.AtPop); // 7 de población, 1 en producción, y este es el siguiente
        Assert.NotNull(builder.Group);
        Assert.Equal(builder.Group, camp.Group);
    }

    [Fact]
    public void El_campamento_no_frena_la_comida_que_se_acaba()
    {
        // El campamento del aldeano nuevo espera a que nazca; lo que se agregue después (pasar a los de la caza que se
        // acaba) esperaría detrás. Entonces el campamento lo hace después CampRule, desde los que ya están en la madera.
        var view = new PlanView
        {
            Population = 10,
            InProduction = 1,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 6), (GatherSource.Hunt, 3)),
            Stock = new ResourceAmounts(300, 100, 0, 100),
            Rates = Rates,
            Food = Food(600, 50, 750),
        };

        var steps = new VillagerRule().Next(view, State()).ToList();

        Assert.Contains(steps, s => s is { Do: StepAction.Train, Assign: GatherSource.Wood });
        Assert.DoesNotContain(steps, s => s.Do == StepAction.Build);
    }

    private static PlanFoodView Food(double sheep, double hunt, double berries) => new(
        new Dictionary<GatherSource, double> { [GatherSource.Sheep] = sheep, [GatherSource.Hunt] = hunt, [GatherSource.Berries] = berries }, [], 0, 0);

    [Fact]
    public void Las_ovejas_que_se_acaban_pasan_al_jabali()
    {
        var state = State();
        var view = new PlanView { Villagers = On((GatherSource.Sheep, 6)), Rates = Rates, Food = Food(30, 680, 750), PopulationCap = 200 };

        var move = Assert.Single(new FoodRule().Next(view, state));

        Assert.Equal((StepAction.Move, 6, GatherSource.Sheep, GatherSource.Hunt), (move.Do, move.Count, move.From, move.Assign));
        Assert.Equal(GatherSource.Hunt, state.FoodSource);
    }

    [Fact]
    public void Antes_de_las_bayas_el_molino()
    {
        var state = State();
        state.FoodSource = GatherSource.Hunt;
        var view = new PlanView { Villagers = On((GatherSource.Hunt, 6)), Rates = Rates, Food = Food(0, 30, 750), PopulationCap = 200, Stock = new ResourceAmounts(0, 100, 0, 0) };

        var steps = new FoodRule().Next(view, state);

        // Uno de los cazadores hace el molino y después va a las bayas; los otros 5 pasan directo.
        Assert.Equal((StepAction.Build, "mill", GatherSource.Hunt, GatherSource.Berries), (steps[0].Do, steps[0].What, steps[0].From, steps[0].Assign));
        Assert.Equal((StepAction.Move, 5, GatherSource.Hunt, GatherSource.Berries), (steps[1].Do, steps[1].Count, steps[1].From, steps[1].Assign));
        Assert.Equal(GatherSource.Berries, state.FoodSource);
    }

    [Fact]
    public void Sin_bayas_en_el_mapa_va_a_las_granjas()
    {
        var state = State();
        state.FoodSource = GatherSource.Hunt;
        var view = new PlanView { Villagers = On((GatherSource.Hunt, 6)), Rates = Rates, Food = Food(0, 50, 0), PopulationCap = 200, Stock = new ResourceAmounts(0, 500, 0, 0) };

        var steps = new FoodRule().Next(view, state);

        Assert.Contains(steps, s => s is { Do: StepAction.Build, What: "mill" });
        // Uno de los cazadores hace el molino: los otros 5, una granja cada uno (el del molino, en la consulta siguiente).
        Assert.Contains(steps, s => s is { Do: StepAction.Build, What: "farm", Count: 5, From: GatherSource.Hunt, Assign: GatherSource.Farm });
        Assert.Equal(GatherSource.Farm, state.FoodSource);
    }

    [Theory]
    [InlineData(300, 750, 17 * 60)] // la comida salvaje se acaba en menos de 4 minutos: hace falta una granja por aldeano
    [InlineData(3000, 3000, 0)] // falta mucho: todavía no
    public void La_madera_de_las_granjas_se_prevé(double hunt, double berries, double wood)
    {
        var state = State();
        state.FoodSource = GatherSource.Hunt;
        var view = new PlanView { Villagers = On((GatherSource.Hunt, 17), (GatherSource.Wood, 4)), Rates = Rates, Food = Food(0, hunt, berries), PopulationCap = 200 };

        new FoodRule().Next(view, state);

        Assert.Equal(wood, state.Foreseen.Wood);
        Assert.Equal(0, state.Upcoming.Wood); // no aparta madera: solo guía el reparto
    }

    [Fact]
    public void Sin_madera_para_todas_las_granjas_los_demas_van_a_juntarla()
    {
        var state = State();
        state.FoodSource = GatherSource.Farm;
        var view = new PlanView
        {
            Villagers = On((GatherSource.Berries, 10), (GatherSource.Wood, 4)),
            Buildings = new Dictionary<string, int> { ["mill"] = 1 },
            Rates = Rates,
            Food = Food(0, 0, 60), // las bayas se acaban en unos segundos
            PopulationCap = 200,
            Stock = new ResourceAmounts(0, 120, 0, 0), // alcanza para 2 granjas
        };

        var steps = new FoodRule().Next(view, state);

        Assert.Contains(steps, s => s is { Do: StepAction.Build, What: "farm", Count: 2, From: GatherSource.Berries });
        Assert.Contains(steps, s => s is { Do: StepAction.Move, Count: 8, From: GatherSource.Berries, Assign: GatherSource.Wood });
    }

    [Fact]
    public void Para_Castillos_prefiere_el_establo_que_piden_los_objetivos()
    {
        var plan = new AssistedPlan
        {
            Goals = [new AssistedGoal { Age = "castle_age" }, new AssistedGoal { Unit = "knight", Count = 10, Buildings = 1 }],
        };
        var view = new PlanView
        {
            Age = Age.Feudal,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 6), (GatherSource.Wood, 10)),
            Buildings = new Dictionary<string, int> { ["town_center"] = 1, ["blacksmith"] = 1 },
            Researched = ["feudal_age"],
            Stock = new ResourceAmounts(750, 400, 0, 200),
            Rates = Rates,
        };

        var steps = new AgeRule().Next(view, State("franks", plan));

        Assert.True(steps.Any(s => s is { Do: StepAction.Build, What: "stable" }), string.Join(", ", steps.Select(s => $"{s.Do} {s.What}")));
    }

    private static AssistedPlan ToFeudal => new() { Goals = [new AssistedGoal { Age = "feudal_age" }] };

    [Theory]
    [InlineData(350, true)] // faltan 150 de comida: con 6 en ovejas, menos de 90 s
    [InlineData(0, false)] // todavía falta mucho: los edificios que salen solos suelen alcanzar
    public void Para_Feudal_faltan_dos_edificios(double food, bool asks)
    {
        var view = new PlanView
        {
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 6), (GatherSource.Wood, 4)),
            Buildings = new Dictionary<string, int> { ["town_center"] = 1, ["lumber_camp"] = 1 },
            Stock = new ResourceAmounts(food, 100, 0, 0),
            Rates = Rates,
        };

        var steps = new AgeRule().Next(view, State(plan: ToFeudal));

        Assert.Equal(asks, steps.Any(s => s is { Do: StepAction.Build, What: "mill" }));
        Assert.DoesNotContain(steps, s => s.Do == StepAction.Research);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(480, false)] // falta poco, pero el clic no espera en la cola: va cuando se puede pagar
    public void El_clic_va_cuando_alcanza(double food, bool clicks)
    {
        var view = new PlanView
        {
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 6)),
            Buildings = new Dictionary<string, int> { ["town_center"] = 1, ["lumber_camp"] = 1, ["mill"] = 1 },
            Stock = new ResourceAmounts(food, 0, 0, 0),
            Rates = Rates,
        };

        var steps = new AgeRule().Next(view, State(plan: ToFeudal));

        Assert.Equal(clicks, steps.Any(s => s is { Do: StepAction.Research, What: "feudal_age" }));
    }

    [Fact]
    public void Mientras_el_centro_urbano_investiga_no_arma_tramo()
    {
        var feudal = new Step { Id = "s9", Do = StepAction.Research, What = "feudal_age" };
        var view = new PlanView
        {
            Time = 400,
            Population = 20,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, 10), (GatherSource.Wood, 9)),
            Stock = new ResourceAmounts(0, 300, 0, 100),
            Rates = Rates,
            Steps = [new PlanViewStep(feudal, 0, true, true, 380, null)],
        };
        var state = State();

        Assert.Empty(new VillagerRule().Next(view, state));
        Assert.Equal(380 + TestData.GameData.Get("feudal_age").Time, state.WakeAt);
    }

    [Fact]
    public void El_clic_a_N_de_poblacion_va_cuando_se_llega()
    {
        var plan = new AssistedPlan { Goals = [new AssistedGoal { Age = "feudal_age", AtPop = 22 }] };
        PlanView View(int population, int inProduction) => new()
        {
            Population = population,
            InProduction = inProduction,
            PopulationCap = 200,
            Villagers = On((GatherSource.Sheep, population - 5), (GatherSource.Wood, 4)),
            Buildings = new Dictionary<string, int> { ["town_center"] = 1, ["lumber_camp"] = 1, ["mill"] = 1 },
            Stock = new ResourceAmounts(500, 100, 0, 0),
            Rates = Rates,
        };

        var early = State(plan: plan);
        Assert.DoesNotContain(new AgeRule().Next(View(18, 1), early), s => s.Do == StepAction.Research);
        Assert.Equal(3, early.VillagersBeforeClick); // 18 + 1 en producción + 3 = 22

        var waiting = State(plan: plan);
        Assert.DoesNotContain(new AgeRule().Next(View(21, 1), waiting), s => s.Do == StepAction.Research);
        Assert.Equal(0, waiting.VillagersBeforeClick);
        Assert.Empty(new VillagerRule().Next(View(21, 1), waiting));

        var click = Assert.Single(new AgeRule().Next(View(22, 0), State(plan: plan)), s => s.Do == StepAction.Research);
        Assert.Equal(22, click.AtPop);

        // Sin el costo, el clic no queda esperando en la cola (frenaría el "mover" de la comida que se acaba).
        var poor = View(22, 0) with { Stock = new ResourceAmounts(300, 100, 0, 0) };
        var later = State(plan: plan);
        Assert.DoesNotContain(new AgeRule().Next(poor, later), s => s.Do == StepAction.Research);
        Assert.NotNull(later.WakeAt);
    }

    [Fact]
    public void El_reacomodo_no_saca_de_donde_ya_se_fueron_todos()
    {
        var plan = new AssistedPlan { Villagers = new AssistedVillagers { Until = 18 }, Goals = [new AssistedGoal { Age = "castle_age" }] };
        var state = State(plan: plan);
        // En la misma consulta, la regla de la comida ya pasó a los 14 cazadores a las bayas.
        state.Add(new Step { Id = "m", Do = StepAction.Move, Count = 14, From = GatherSource.Hunt, Assign = GatherSource.Berries }, "a las bayas");
        var view = new PlanView
        {
            Time = 600,
            Age = Age.Feudal,
            Population = 19,
            PopulationCap = 200,
            Villagers = On((GatherSource.Hunt, 14), (GatherSource.Wood, 4)),
            Buildings = new Dictionary<string, int> { ["town_center"] = 1, ["blacksmith"] = 1, ["archery_range"] = 1 },
            Researched = ["feudal_age"],
            Stock = new ResourceAmounts(900, 0, 0, 0),
            Rates = Rates,
        };

        var steps = new AgeRule().Next(view, state);

        Assert.DoesNotContain(steps, s => s.Do == StepAction.Move && s.From == GatherSource.Hunt);
        Assert.Contains(steps, s => s is { Do: StepAction.Move, From: GatherSource.Wood, Assign: GatherSource.Gold });
    }
}
