using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// Coach por líneas paralelas (spec 2026-10-30): cada paso cae en una línea (Aldeanos, Construir, Investigar,
/// Producir) y cada línea muestra su primer paso elegible (de la edad del jugador, con su edificio, con sus
/// dependencias). Casos de la partida 20261029-220928: el hacha "ya se puede" con la Feudal en curso y el emplumado sin
/// herrería.
/// </summary>
public class TrackerLanesTests
{
    private static Step Villagers(string id, int count, GatherSource to) =>
        new() { Id = id, Do = StepAction.Train, What = "villager", Count = count, Assign = to };

    private static Step Build(string id, string what) => new() { Id = id, Do = StepAction.Build, What = what, Assign = GatherSource.Wood };

    private static Step Research(string id, string what) => new() { Id = id, Do = StepAction.Research, What = what };

    private static BuildOrder Bo(params Step[] steps) => new() { Name = "t", Steps = steps };

    private static int Index(BuildOrder bo, string id) => bo.Steps.Select(s => s.Id).ToList().IndexOf(id);

    [Fact]
    public void Lanes_Of_reparte_cada_accion()
    {
        Assert.Equal(Lane.Villagers, Lanes.Of(Villagers("a", 1, GatherSource.Wood)));
        Assert.Equal(Lane.Villagers, Lanes.Of(new Step { Do = StepAction.Move, From = GatherSource.Sheep, Assign = GatherSource.Wood }));
        Assert.Equal(Lane.Build, Lanes.Of(Build("b", "house")));
        Assert.Equal(Lane.Research, Lanes.Of(Research("r", "loom")));
        Assert.Equal(Lane.Produce, Lanes.Of(new Step { Do = StepAction.Train, What = "archer", Count = 6 }));
        Assert.Equal([Lane.Villagers, Lane.Build, Lane.Research, Lane.Produce], Lanes.All);
    }

    private static readonly BuildOrder Axe = Bo(
        Build("c1", "lumber_camp"),
        Build("c2", "barracks"),
        Villagers("v1", 3, GatherSource.Wood),
        Research("feudal", "feudal_age"),
        Research("axe", "double_bit_axe"),
        Villagers("v2", 6, GatherSource.Wood));

    [Fact]
    public void Con_la_Feudal_en_curso_no_muestra_el_hacha()
    {
        var tracker = new Tracker(Axe, TestData.GameData);
        var partida = new Partida().Event("lumber_camp_built").Event("barracks_built").Events("villager_created", 3);
        tracker.Update(partida.State);
        tracker.MarkStarted(Index(Axe, "feudal"), partida.Time);

        Assert.Equal(Age.Dark, tracker.Age);
        Assert.False(tracker.IsEligible(Index(Axe, "axe")));
        Assert.Null(tracker.LaneStep(Lane.Research));
        Assert.Equal("v2", tracker.LaneStep(Lane.Villagers)?.Step.Id);
        Assert.NotEqual("axe", tracker.Current?.Step.Id);

        partida.Event("feudal_age_researched");
        tracker.Update(partida.State);

        Assert.Equal(Age.Feudal, tracker.Age);
        Assert.Equal("axe", tracker.LaneStep(Lane.Research)?.Step.Id);
    }

    private static readonly BuildOrder Fletching = Bo(
        Research("fletch", "fletching"),
        Build("smith", "blacksmith"));

    [Fact]
    public void Sin_herreria_no_muestra_el_emplumado()
    {
        var tracker = new Tracker(Fletching, TestData.GameData);
        var partida = new Partida().Event("feudal_age_researched");
        tracker.Update(partida.State);

        Assert.Null(tracker.LaneStep(Lane.Research));
        Assert.Equal("smith", tracker.LaneStep(Lane.Build)?.Step.Id);

        partida.Event("blacksmith_built");
        tracker.Update(partida.State);

        Assert.Equal("fletch", tracker.LaneStep(Lane.Research)?.Step.Id);
    }

    [Fact]
    public void Una_linea_que_arranca_con_algo_de_otra_edad_queda_vacia_hasta_llegar()
    {
        var bo = Bo(Villagers("v1", 3, GatherSource.Wood), Build("range", "archery_range"));
        var tracker = new Tracker(bo, TestData.GameData);
        var partida = new Partida().Event("barracks_built");
        tracker.Update(partida.State);

        Assert.Null(tracker.LaneStep(Lane.Build));

        partida.Event("feudal_age_researched");
        tracker.Update(partida.State);

        Assert.Equal("range", tracker.LaneStep(Lane.Build)?.Step.Id);
    }

    [Fact]
    public void Un_edificio_que_la_BO_dio_por_hecho_cuenta_como_construido()
    {
        var bo = Bo(Build("smith", "blacksmith"), Research("fletch", "fletching"));
        var tracker = new Tracker(bo, TestData.GameData);
        tracker.Update(new Partida().Event("feudal_age_researched").State);

        tracker.Advance();

        Assert.True(tracker.Built("blacksmith"));
        Assert.True(tracker.IsEligible(Index(bo, "fletch")));
        Assert.Equal("fletch", tracker.LaneStep(Lane.Research)?.Step.Id);
    }

    [Fact]
    public void El_asentamiento_cuenta_como_campamento()
    {
        // SnapshotBuilder emite un "X Built" por cada edificio del que vale el asentamiento de los incas.
        var bo = Bo(Research("axe", "double_bit_axe"));
        var tracker = new Tracker(bo, TestData.GameData);
        tracker.Update(new Partida().Event("feudal_age_researched").Event("settlement_built").Event("lumber_camp_built").State);

        Assert.True(tracker.Built("lumber_camp"));
        Assert.Equal("axe", tracker.LaneStep(Lane.Research)?.Step.Id);
    }

    [Fact]
    public void El_clic_de_edad_espera_los_dos_edificios()
    {
        var bo = Bo(Research("feudal", "feudal_age"), Build("c1", "lumber_camp"), Build("c2", "barracks"));
        var tracker = new Tracker(bo, TestData.GameData);
        var partida = new Partida().Event("lumber_camp_built");
        tracker.Update(partida.State);

        Assert.False(tracker.IsEligible(Index(bo, "feudal")));

        partida.Event("barracks_built");
        tracker.Update(partida.State);

        Assert.True(tracker.IsEligible(Index(bo, "feudal")));
    }
}
