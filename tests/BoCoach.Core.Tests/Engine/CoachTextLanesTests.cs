using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>Los renglones fijos del overlay (spec 2026-10-30): uno por línea, blanco, amarillo o gris.</summary>
public class CoachTextLanesTests
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
            Villagers("v2", 9, GatherSource.Wood),
        ],
    };

    private static GameSnapshot At(double time, ResourceAmounts stock, params string[] events) =>
        new(time, stock, 10, 15, events.Select(e => new GameEvent(e, time)).ToArray());

    [Fact]
    public void LaneLines_con_el_hacha_esperando()
    {
        var view = new Coach(Axe, TestData.GameData, TestData.Events).Update(At(400, new ResourceAmounts(200, 0, 200, 200),
            "lumber_camp_built", "villager_created", "villager_created", "villager_created", "feudal_age_researched"));

        var lines = CoachText.LaneLines(view, TestData.GameData);

        var research = lines.Single(l => l.Lane == Lane.Research);
        Assert.Equal("Investigar", research.Label);
        Assert.Equal(LaneTone.Waiting, research.Tone);
        Assert.Contains("faltan", research.Text);
        Assert.Contains("madera", research.Text);
        Assert.Equal(LaneTone.Now, lines.Single(l => l.Lane == Lane.Villagers).Tone);
        Assert.Equal(lines.Select(l => l.Lane).Order(), lines.Select(l => l.Lane)); // en el orden de las líneas
    }

    [Fact]
    public void LaneLines_fallback_de_aldeanos()
    {
        var bo = new BuildOrder { Name = "t", Steps = [Villagers("v1", 3, GatherSource.Wood)] };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        coach.Update(At(100, new ResourceAmounts(0, 300, 0, 0), "villager_created", "villager_created", "villager_created")
            with { Villagers = 6, VillagersOn = new ResourceAmounts(3, 3, 0, 0) });
        var view = coach.Update(At(110, new ResourceAmounts(0, 300, 0, 0)) with { Villagers = 6, VillagersOn = new ResourceAmounts(3, 3, 0, 0) });

        var villagers = CoachText.LaneLines(view, TestData.GameData).Single(l => l.Lane == Lane.Villagers);

        Assert.Equal(LaneTone.Fallback, villagers.Tone);
        Assert.Contains(Suggestion.Name(view.VillagerGuide!.Value), villagers.Text);
    }

    [Fact]
    public void LaneLines_sin_nada_no_devuelve_renglones()
    {
        var view = new CoachView(0, 0, 200, null, null, [], [], ResourceAmounts.Zero, null, [], null, 1, 1)
        {
            Lanes = Lanes.All.Select(l => new LaneView(l, null, ResourceAmounts.Zero, null)).ToList(),
        };

        Assert.Empty(CoachText.LaneLines(view, TestData.GameData));
    }
}
