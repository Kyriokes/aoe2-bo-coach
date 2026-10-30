using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>La vista del coach con un renglón por línea (spec 2026-10-30, coach por líneas paralelas).</summary>
public class CoachLanesTests
{
    private static Step Villagers(string id, int count, GatherSource to) =>
        new() { Id = id, Do = StepAction.Train, What = "villager", Count = count, Assign = to };

    private static readonly BuildOrder Axe = new()
    {
        Name = "t",
        Steps =
        [
            new Step { Id = "camp", Do = StepAction.Build, What = "lumber_camp", Assign = GatherSource.Wood },
            Villagers("v1", 3, GatherSource.Wood),
            new Step { Id = "axe", Do = StepAction.Research, What = "double_bit_axe" },
            new Step { Id = "house", Do = StepAction.Build, What = "house", From = GatherSource.Wood, Assign = GatherSource.Wood },
            Villagers("v2", 9, GatherSource.Wood),
            Villagers("v3", 2, GatherSource.Gold),
        ],
    };

    private static GameSnapshot At(double time, ResourceAmounts stock, params string[] events) =>
        new(time, stock, 10, 15, events.Select(e => new GameEvent(e, time)).ToArray());

    [Fact]
    public void Hay_un_renglon_por_linea_en_orden()
    {
        var view = new Coach(Axe, TestData.GameData, TestData.Events).Update(At(10, PlanStart.Standard.Stock));

        Assert.Equal([Lane.Villagers, Lane.Build, Lane.Research, Lane.Produce], view.Lanes.Select(l => l.Lane));
    }

    // Partida 20261029-220928: el hacha sin madera trababa todo y no se sabía a dónde mandar los aldeanos que salían.
    [Fact]
    public void El_hacha_sin_madera_muestra_lo_que_falta_y_los_aldeanos_siguen()
    {
        var coach = new Coach(Axe, TestData.GameData, TestData.Events);
        var view = coach.Update(At(400, new ResourceAmounts(200, 0, 200, 200),
            "lumber_camp_built", "villager_created", "villager_created", "villager_created", "feudal_age_researched"));

        var research = view.Lanes.Single(l => l.Lane == Lane.Research);
        Assert.Equal("axe", research.Step?.Step.Id);
        Assert.True(research.Missing.Wood > 0);
        Assert.Equal("v2", view.Lanes.Single(l => l.Lane == Lane.Villagers).Step?.Step.Id);
        Assert.Equal("house", view.Lanes.Single(l => l.Lane == Lane.Build).Step?.Step.Id);
    }

    [Fact]
    public void Al_terminar_sale_del_paso_de_aldeanos()
    {
        var coach = new Coach(Axe, TestData.GameData, TestData.Events);
        var view = coach.Update(At(400, new ResourceAmounts(200, 0, 200, 200),
            "lumber_camp_built", "villager_created", "villager_created", "villager_created", "feudal_age_researched"));

        // El paso de aldeanos del renglón es v2 (9 en madera), aunque el hacha y la casa estén sin hacer.
        Assert.Equal(9, view.StepTarget?.Wood);
    }

    [Fact]
    public void Sin_pasos_de_aldeanos_la_linea_va_a_la_GD()
    {
        var bo = new BuildOrder { Name = "t", Steps = [Villagers("v1", 3, GatherSource.Wood)] };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        var events = Enumerable.Repeat("villager_created", 3).ToArray();
        coach.Update(At(100, new ResourceAmounts(0, 300, 0, 0), events) with { Villagers = 6, VillagersOn = new ResourceAmounts(3, 3, 0, 0) });
        var view = coach.Update(At(110, new ResourceAmounts(0, 300, 0, 0)) with { Villagers = 6, VillagersOn = new ResourceAmounts(3, 3, 0, 0) });

        Assert.Null(view.Lanes.Single(l => l.Lane == Lane.Villagers).Step);
        Assert.Null(view.StepTarget);
        Assert.NotNull(view.VillagerGuide);
    }

    // El usuario: "estoy haciendo el 26, el 27, y no sé a qué recursos mandarlos". Aunque no falte nada, el renglón de
    // Aldeanos siempre dice a dónde (el recurso que más se gasta).
    [Fact]
    public void Sin_pasos_de_aldeanos_y_sin_faltantes_igual_dice_a_donde()
    {
        var bo = new BuildOrder { Name = "t", Steps = [Villagers("v1", 3, GatherSource.Wood), new Step { Id = "x", Do = StepAction.Research, What = "loom" }] };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        var rich = new ResourceAmounts(5000, 5000, 5000, 5000);
        coach.Update(At(100, rich, "villager_created", "villager_created", "villager_created") with { Villagers = 6, VillagersOn = new ResourceAmounts(3, 3, 0, 0) });
        var view = coach.Update(At(110, rich) with { Villagers = 6, VillagersOn = new ResourceAmounts(3, 3, 0, 0) });

        Assert.Null(view.Lanes.Single(l => l.Lane == Lane.Villagers).Step);
        Assert.NotNull(view.VillagerGuide);
    }
}
