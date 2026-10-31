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

    [Fact]
    public void El_hacha_esperando_madera_no_frena_a_los_aldeanos()
    {
        var bo = Bo(Villagers("v1", 2, GatherSource.Wood), Research("axe", "double_bit_axe"), Villagers("v2", 2, GatherSource.Gold));
        var tracker = new Tracker(bo, TestData.GameData);
        var partida = new Partida { Stock = ResourceAmounts.Zero }.Event("feudal_age_researched").Event("lumber_camp_built").Events("villager_created", 4);
        tracker.Update(partida.State);

        Assert.Equal(StepState.Pending, tracker.Steps[Index(bo, "axe")].State);
        Assert.Equal(StepState.Done, tracker.Steps[Index(bo, "v1")].State);
        Assert.Equal(StepState.Done, tracker.Steps[Index(bo, "v2")].State);
        Assert.Null(tracker.LaneStep(Lane.Villagers));
        Assert.Equal("axe", tracker.LaneStep(Lane.Research)?.Step.Id);
    }

    [Fact]
    public void Un_edificio_hecho_antes_marca_su_paso_y_la_linea_sigue_en_el_primero()
    {
        var bo = Bo(Build("house", "house"), Build("camp", "lumber_camp"), Build("mill", "mill"));
        var tracker = new Tracker(bo, TestData.GameData);
        tracker.Update(new Partida().Event("mill_built").State);

        Assert.Equal(StepState.Done, tracker.Steps[Index(bo, "mill")].State);
        Assert.Contains(tracker.Steps[Index(bo, "house")].State, new[] { StepState.Pending, StepState.Ready });
        Assert.Contains(tracker.Steps[Index(bo, "camp")].State, new[] { StepState.Pending, StepState.Ready });
        Assert.Equal("house", tracker.LaneStep(Lane.Build)?.Step.Id);
    }

    [Fact]
    public void Paso_mixto_partido()
    {
        var bo = Bo(
            Villagers("v1", 3, GatherSource.Sheep),
            Villagers("v2", 2, GatherSource.Berries) with { Group = "g1" },
            Build("mill", "mill") with { Group = "g1" });
        var tracker = new Tracker(bo, TestData.GameData);
        var partida = new Partida().Events("villager_created", 3);
        tracker.Update(partida.State);

        Assert.Equal("v2", tracker.LaneStep(Lane.Villagers)?.Step.Id);
        Assert.Equal("mill", tracker.LaneStep(Lane.Build)?.Step.Id);

        tracker.Update(partida.Event("mill_built").State);

        Assert.Equal(StepState.Done, tracker.Steps[Index(bo, "mill")].State);
        Assert.Equal("v2", tracker.LaneStep(Lane.Villagers)?.Step.Id);

        tracker.Update(partida.Events("villager_created", 2).State);

        Assert.Null(tracker.LaneStep(Lane.Villagers));
    }

    [Fact]
    public void Advance_de_Investigar_da_por_hecho_el_paso_de_esa_linea()
    {
        var bo = Bo(Villagers("v1", 3, GatherSource.Wood), Research("loom", "loom"), Build("house", "house"));
        var tracker = new Tracker(bo, TestData.GameData);

        tracker.Advance(Lane.Research);

        Assert.Equal(StepState.Done, tracker.Steps[Index(bo, "loom")].State);
        Assert.NotEqual(StepState.Done, tracker.Steps[Index(bo, "v1")].State);
        Assert.NotEqual(StepState.Done, tracker.Steps[Index(bo, "house")].State);

        tracker.Skip(Lane.Build);

        Assert.Equal(StepState.Skipped, tracker.Steps[Index(bo, "house")].State);
        Assert.Equal("v1", tracker.LaneStep(Lane.Villagers)?.Step.Id);
    }

    [Fact]
    public void Advance_sin_linea_da_por_hecho_lo_primero_que_falta()
    {
        var bo = Bo(Build("house", "house"), Research("loom", "loom"));
        var tracker = new Tracker(bo, TestData.GameData);

        tracker.Advance();

        Assert.Equal(StepState.Done, tracker.Steps[Index(bo, "house")].State);
        Assert.NotEqual(StepState.Done, tracker.Steps[Index(bo, "loom")].State);
    }

    // Partida 20261029-220928 con la copia vieja de la BO (dos molinos): el segundo molino nunca se hizo y tapaba el
    // renglón de Construir toda la partida. Si ya se hizo un paso posterior de la línea, el renglón sigue desde ahí y
    // lo de antes queda aparte ("Quedó sin hacer").
    [Fact]
    public void Lo_que_quedo_atras_en_la_linea_no_la_traba()
    {
        var bo = Bo(Build("mill1", "mill"), Build("mill2", "mill"), Build("house", "house"), Build("camp", "lumber_camp"));
        var tracker = new Tracker(bo, TestData.GameData);
        tracker.Update(new Partida().Event("mill_built").Event("house_built").State);

        Assert.Equal("camp", tracker.LaneStep(Lane.Build)?.Step.Id);
        Assert.Equal(["mill2"], tracker.LaneBehind(Lane.Build).Select(s => s.Step.Id));
    }

    // Revisión de la rama: los jemeres suben de edad sin edificios.
    [Fact]
    public void Los_jemeres_hacen_clic_de_edad_sin_edificios()
    {
        var bo = Bo(Research("feudal", "feudal_age")) with { Civ = "khmer" };
        var tracker = new Tracker(bo, TestData.GameData);

        Assert.True(tracker.IsEligible(0));
    }

    [Fact]
    public void Un_paso_salteado_cuenta_para_los_que_dependen_de_el()
    {
        var bo = Bo(Build("h", "house"), Research("loom", "loom") with { After = ["h"] });
        var tracker = new Tracker(bo, TestData.GameData);

        tracker.Skip(Lane.Build);

        Assert.True(tracker.IsEligible(Index(bo, "loom")));
    }

    [Fact]
    public void La_edad_se_deduce_de_lo_que_ya_se_hizo_aunque_el_log_no_lea_el_aviso()
    {
        var bo = Bo(Research("feudal", "feudal_age"), Research("axe", "double_bit_axe"));
        var tracker = new Tracker(bo, TestData.GameData);
        tracker.Update(new Partida().Event("lumber_camp_built").Event("archery_range_built").State);

        Assert.Equal(Age.Feudal, tracker.Age);
        Assert.True(tracker.IsEligible(Index(bo, "axe")));
    }

    [Fact]
    public void Saltear_el_clic_de_edad_pasa_a_la_edad_siguiente()
    {
        var bo = Bo(Research("feudal", "feudal_age"), Research("axe", "double_bit_axe"));
        var tracker = new Tracker(bo, TestData.GameData);
        tracker.Update(new Partida().Event("lumber_camp_built").Event("barracks_built").State);

        tracker.Skip(Lane.Research);

        Assert.Equal(Age.Feudal, tracker.Age);
    }

    [Fact]
    public void El_asentamiento_hecho_sin_aviso_cuenta_como_sus_edificios()
    {
        var bo = Bo(Build("s", "settlement")) with { Civ = "incas" };
        var tracker = new Tracker(bo, TestData.GameData);

        tracker.Advance(Lane.Build);

        Assert.True(tracker.Built("mill"));
        Assert.True(tracker.Built("lumber_camp"));
    }

    // Partida 20261031-125929 (británicos, Fast Archers mejorada): Investigar pedía el telar desde el 0:00 (la BO lo
    // pone a 19 de población) y Construir el campamento minero a 15 (la BO lo pone después del clic de Feudal a 20).
    // Un paso de Construir, Investigar o Producir espera a la población que tiene la BO cuando llega a él.
    private static readonly BuildOrder ByPopulation = Bo(
        new Step { Id = "h0", Do = StepAction.Build, What = "house", Count = 2, Builders = 3, Assign = GatherSource.Sheep },
        Villagers("v1", 6, GatherSource.Sheep),
        Research("loom", "loom"),
        Build("camp", "lumber_camp"),
        Villagers("v2", 3, GatherSource.Wood),
        Villagers("berries", 2, GatherSource.Berries) with { Group = "g" },
        Build("mill", "mill") with { Group = "g" },
        Build("house", "house") with { AtPop = 9 });

    [Fact]
    public void Un_paso_que_no_es_de_aldeanos_espera_a_la_poblacion_de_la_BO()
    {
        var tracker = new Tracker(ByPopulation, TestData.GameData);
        var partida = new Partida();
        tracker.Update(partida.State);

        // La población con que la simulación de la BO empieza cada paso: 4 al arrancar (3 aldeanos y el explorador) y 3
        // aldeanos nuevos en ovejas; el telar se encola detrás del último (6 hechos y 1 en el centro urbano).
        var loom = tracker.LaneStep(Lane.Research)!;
        Assert.Equal("loom", loom.Step.Id);
        Assert.InRange(loom.AtPop!.Value, 6, 7);
        Assert.Equal(StepState.Pending, loom.State);
        // El molino va con los aldeanos de su paso mixto, no después de ellos (serían 12).
        Assert.InRange(tracker.Steps[Index(ByPopulation, "mill")].AtPop!.Value, 7, 10);
        // La de la BO manda.
        Assert.Equal(9, tracker.Steps[Index(ByPopulation, "house")].AtPop);
        // Los de aldeanos no esperan: hacen la población.
        Assert.Null(tracker.Steps[Index(ByPopulation, "v2")].AtPop);

        partida.Events("villager_created", 3);
        tracker.Update(partida.State);
        Assert.Equal(StepState.Ready, tracker.LaneStep(Lane.Research)!.State);
    }

    [Fact]
    public void Antes_de_su_poblacion_la_linea_no_habla_de_recursos()
    {
        // Partida 20261031-145925: de las 5:40 a las 6:21 el cuartel (a 20 de población) decía "faltan 65 de madera"
        // con 16 de población.
        var coach = new Coach(ByPopulation, TestData.GameData, TestData.Events);
        var view = coach.Update(new GameSnapshot(5, ResourceAmounts.Zero, 4, 5, []) { Villagers = 3 });

        var research = Assert.Single(CoachText.LaneLines(view, TestData.GameData), l => l.Lane == Lane.Research);
        Assert.Contains("de población (vas 4)", research.Text);
        Assert.DoesNotContain("faltan", research.Text);
    }

    [Fact]
    public void Con_la_poblacion_en_el_paso_no_la_repite()
    {
        // Partida 20261031-145925: "Investigar Edad Feudal a 20 de población · a 20 de población (vas 17)".
        var bo = Bo(Villagers("v1", 3, GatherSource.Sheep), Research("feudal", "feudal_age") with { AtPop = 20 });
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        // La Feudal pide 2 edificios de la Edad Oscura.
        var view = coach.Update(new GameSnapshot(5, PlanStart.Standard.Stock, 17, 20,
            [new GameEvent("lumber_camp_built", 4), new GameEvent("mill_built", 4)]) { Villagers = 16 });

        var research = Assert.Single(CoachText.LaneLines(view, TestData.GameData), l => l.Lane == Lane.Research);
        Assert.Equal(1, research.Text.Split("a 20 de población").Length - 1);
        Assert.EndsWith("· vas 17", research.Text);
    }

    [Fact]
    public void La_linea_dice_a_que_poblacion_espera()
    {
        var coach = new Coach(ByPopulation, TestData.GameData, TestData.Events);
        var view = coach.Update(new GameSnapshot(5, PlanStart.Standard.Stock, 4, 5, []) { Villagers = 3 });

        var research = Assert.Single(CoachText.LaneLines(view, TestData.GameData), l => l.Lane == Lane.Research);
        Assert.Contains($"a {coach.Tracker.LaneStep(Lane.Research)!.AtPop} de población (vas 4)", research.Text);
        Assert.Equal(LaneTone.Waiting, research.Tone);
    }
}
