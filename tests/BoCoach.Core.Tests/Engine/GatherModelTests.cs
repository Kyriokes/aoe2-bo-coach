using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>Tasas con viajes (carga, trabajo y caminata) y las mejoras económicas del .dat.</summary>
public class GatherModelTests
{
    private static readonly GameData Data = TestData.GameData;
    private static readonly GatherModel Model = new(Data, PlannerSettings.DefaultEfficiency);

    private static double Rate(GatherSource source, params string[] researched) => Model.Rate(source, CivBonuses.None, Age.Feudal, researched);

    [Fact]
    public void Sin_mejoras_es_la_tasa_del_dat_por_la_eficiencia()
    {
        foreach (var source in Enum.GetValues<GatherSource>())
            Assert.Equal(Data.GatherRates[source] * PlannerSettings.DefaultEfficiency[source], Rate(source), 6);
    }

    [Fact]
    public void Las_mejoras_salen_del_dat()
    {
        Assert.Equal(new GatherEffect(Work: 1.2), Data.Get("double_bit_axe").Gather[GatherSource.Wood]);
        Assert.Equal(new GatherEffect(CarryMultiplier: 1.2695, Speed: 1.1), Data.Get("wheelbarrow").Gather[GatherSource.Farm]);
        Assert.Equal(new GatherEffect(CarryAdd: 1), Data.Get("heavy_plow").Gather[GatherSource.Farm]);
        Assert.Equal(new GatherEffect(Work: 1.15), Data.Get("gold_mining").Gather[GatherSource.Gold]);
        Assert.Empty(Data.Get("loom").Gather);
        Assert.Equal(35, Data.Carry(GatherSource.Hunt));
        Assert.Equal(10, Data.Carry(GatherSource.Wood));
    }

    [Fact]
    public void Una_mejora_de_trabajo_rinde_menos_por_la_caminata()
    {
        var gain = Rate(GatherSource.Wood, "double_bit_axe") / Rate(GatherSource.Wood);

        Assert.InRange(gain, 1.1, 1.2); // +20 % de trabajo, pero el viaje al campamento no se acorta
    }

    [Fact]
    public void La_carretilla_rinde_mas_donde_se_camina_mas()
    {
        var farm = Rate(GatherSource.Farm, "wheelbarrow") / Rate(GatherSource.Farm);    // eficiencia 0,6
        var sheep = Rate(GatherSource.Sheep, "wheelbarrow") / Rate(GatherSource.Sheep);  // eficiencia 0,95

        Assert.True(farm > sheep, $"granja ×{farm:0.000}, ovejas ×{sheep:0.000}");
        Assert.True(sheep > 1);
    }

    [Fact]
    public void Las_mejoras_se_acumulan()
    {
        var wood = Rate(GatherSource.Wood);
        var axe = Rate(GatherSource.Wood, "double_bit_axe");
        var both = Rate(GatherSource.Wood, "double_bit_axe", "bow_saw");
        var carts = Rate(GatherSource.Wood, "double_bit_axe", "bow_saw", "wheelbarrow", "hand_cart");

        Assert.True(wood < axe && axe < both && both < carts);
    }

    [Fact]
    public void Bonificaciones_de_civ_de_carga_y_velocidad()
    {
        var aztecs = Data.Civ("aztecs");
        Assert.Equal(13, aztecs.Carry(GatherSource.Wood, Age.Dark));          // aztecas: +3
        Assert.Equal(25, Data.Civ("dravidians").Carry(GatherSource.Fish, Age.Dark)); // pesca +15
        Assert.Null(Data.Civ("britons").Carry(GatherSource.Wood, Age.Dark));
        Assert.Equal([1.05, 1.1, 1.1, 1.1], Enum.GetValues<Age>().Select(a => Data.Civ("berbers").VillagerSpeed(a)));

        Assert.True(Model.Rate(GatherSource.Wood, aztecs, Age.Dark, []) > Model.Rate(GatherSource.Wood, CivBonuses.None, Age.Dark, []));
        Assert.True(Model.Rate(GatherSource.Farm, Data.Civ("berbers"), Age.Feudal, []) > Model.Rate(GatherSource.Farm, CivBonuses.None, Age.Feudal, []));
    }

    [Fact]
    public void Recursos_al_subir_de_edad_y_al_arrancar()
    {
        Assert.Equal(new ResourceAmounts(0, 200, 0, 0), Data.Civ("dravidians").AgeResources(Age.Feudal));
        Assert.Equal(new ResourceAmounts(100, 0, 0, 100), Data.Civ("ethiopians").AgeResources(Age.Castle));
        Assert.Equal(ResourceAmounts.Zero, Data.Civ("dravidians").AgeResources(Age.Dark));
        Assert.Equal(100, Data.Civ("lithuanians").Start.Resources.Food); // un edificio escondido del centro urbano con 100
    }

    private static BuildOrder WoodBo(bool axe)
    {
        var steps = new List<Step> { new() { Id = "v", Do = StepAction.Train, What = "villager", Count = 10, Assign = GatherSource.Wood } };
        if (axe)
            steps.Add(new Step { Id = "axe", Do = StepAction.Research, What = "double_bit_axe" });
        steps.Add(new Step { Id = "v2", Do = StepAction.Train, What = "villager", Count = 4, Assign = GatherSource.Wood });
        return new BuildOrder { Name = "Madera", Steps = steps };
    }

    [Fact]
    public void El_Planner_aplica_la_mejora_cuando_termina()
    {
        var planner = new Planner(Data);
        static double WoodAt(PlanResult plan, double time) => plan.Timeline.Last(s => s.Time <= time).Stock.Wood;

        // Las hachas se investigan en el campamento maderero... que esta BO no construye: no se investigan nunca.
        // Así que se arranca con el campamento hecho.
        var start = PlanStart.Standard with { Buildings = new Dictionary<string, int> { ["town_center"] = 1, ["lumber_camp"] = 1 }, Age = Age.Feudal };
        PlanResult Run(bool axe) => planner.Simulate(WoodBo(axe).Steps.Select(s => new PlanItem(s, s.Do == StepAction.Train ? s.Count : 1)).ToList(), start);

        var without = Run(axe: false);
        var with = Run(axe: true);

        var axeDone = with.For("axe")!.End!.Value;
        // Hasta pagar las hachas (100 de madera y 50 de comida) se junta igual; después, más rápido.
        Assert.True(WoodAt(with, axeDone + 200) - WoodAt(with, axeDone) > WoodAt(without, axeDone + 200) - WoodAt(without, axeDone));
    }

    [Fact]
    public void En_vivo_cuentan_las_mejoras_que_mostro_el_log()
    {
        var tracker = new Tracker(WoodBo(axe: false), Data);
        var partida = new Partida().Event("wheelbarrow_researched");
        tracker.Update(partida.State);

        Assert.Contains("wheelbarrow", LivePlan.Start(tracker).Researched);
    }
}
