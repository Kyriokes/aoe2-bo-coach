using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// D2, postura de cierre (diseño acordado con el usuario el 2026-10-20): al terminar la BO, siempre en cola las
/// unidades de la postura en cada edificio que las produce, y lo que sobra a la próxima edad.
/// </summary>
public class PostureTests
{
    private static readonly GameData Data = TestData.GameData;
    private static readonly BuildOrder Scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));
    private static readonly BuildOrder Scouts22 = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_22p.json"));

    [Fact]
    public void Sin_postura_se_deduce_aldeanos_la_ultima_unidad_militar_y_la_edad_siguiente()
    {
        var (posture, derived) = Posture.Effective(Scouts, Data);
        Assert.True(derived);
        Assert.Equal(["villager", "scout_cavalry"], posture.Queue);
        Assert.Equal("castle_age", posture.NextAge);

        // La de 22 de población ya clica Castillos: apunta a Imperial.
        Assert.Equal("imperial_age", Posture.Effective(Scouts22, Data).Posture.NextAge);
    }

    // La guía original de "Scouts → CA" (la mejorada saca arqueros a caballo desde el 2026-10-31).
    private static readonly BuildOrder HunsCa = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "civs", "huns.json"));

    private static BuildOrder Then(BuildOrder bo, string building) =>
        bo with { Steps = [.. bo.Steps, new Step { Id = "fin", Do = StepAction.Build, What = building, Count = 1 }] };

    // Partida 20261031-215314 (hunos, "Scouts → CA (mejorada)", la de entonces): la BO entrena exploradores y después
    // construye 3 galerías; con la BO terminada el overlay pedía "Establo parado: poné exploradores en cola" hasta el final,
    // mientras salían arqueros a caballo.
    [Fact]
    public void Se_suman_los_edificios_de_produccion_que_la_BO_construye_despues_de_su_ultima_unidad()
    {
        var (posture, derived) = Posture.Effective(HunsCa, Data);

        Assert.True(derived);
        Assert.Equal(["villager", "scout_cavalry", "archer"], posture.Queue);
        Assert.Equal("imperial_age", posture.NextAge);

        // La mejorada de ahora termina sacando arqueros a caballo: la postura es esa.
        var mejorada = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "civs", "huns-mejorada.json"));
        Assert.Equal(["villager", "cavalry_archer"], Posture.Effective(mejorada, Data).Posture.Queue);
    }

    [Fact]
    public void Otro_edificio_igual_al_de_la_ultima_unidad_o_uno_que_no_produce_no_cambian_la_postura()
    {
        Assert.Equal(["villager", "scout_cavalry"], Posture.Effective(Then(Scouts, "stable"), Data).Posture.Queue);
        Assert.Equal(["villager", "scout_cavalry"], Posture.Effective(Then(Scouts, "blacksmith"), Data).Posture.Queue);
        Assert.Equal(["villager", "scout_cavalry"], Posture.Effective(Then(Scouts, "monastery"), Data).Posture.Queue);
        Assert.Equal(["villager", "scout_cavalry", "archer"], Posture.Effective(Then(Scouts, "archery_range"), Data).Posture.Queue);
    }

    private static PlayerState Timeline(params (double Time, string Event)[] events)
    {
        var state = new PlayerState();
        foreach (var (time, e) in events)
            state.Apply(new GameSnapshot(time, Poor, 25, 30, [new GameEvent(e, time)]));
        return state;
    }

    [Fact]
    public void En_vivo_queda_el_edificio_que_el_jugador_usa_con_la_unidad_que_saca()
    {
        var coach = new PostureCoach(HunsCa, Data) { IdleGrace = 0 };
        var huns = Data.Civ("huns");
        var state = Timeline((100, "stable_built"), (200, "scout_cavalry_created"),
            (300, "archery_range_built"), (310, "archery_range_built"), (320, "archery_range_built"));

        // Recién hechas las galerías, se piden las dos cosas.
        var view = coach.View(state, [new QueueItem("villager")], Age.Castle, huns);
        Assert.Equal(["villager", "scout_cavalry", "archer"], view.Queue);
        Assert.Equal(["stable", "archery_range"], view.Gaps.Select(g => g.Building));

        // Con 3 arqueros a caballo (uno de élite) desde el último explorador, el establo queda afuera.
        foreach (var (time, e) in new[] { (400.0, "cavalry_archer_created"), (430, "cavalry_archer_created"), (460, "heavy_cavalry_archer_created") })
            state.Apply(new GameSnapshot(time, Poor, 25, 30, [new GameEvent(e, time)]));
        view = coach.View(state, [new QueueItem("villager")], Age.Castle, huns);
        Assert.Equal(["villager", "cavalry_archer"], view.Queue);
        var gap = Assert.Single(view.Gaps);
        Assert.Equal(("archery_range", "cavalry_archer", 3), (gap.Building, gap.Unit, gap.Count));
        Assert.Equal("Galerías de tiro (3) paradas: poné arqueros a caballo en cola", CoachText.GapText(gap));
        Assert.Equal("Postura: aldeanos y arqueros a caballo siempre en cola", CoachText.PostureTitle(view));

        // Con dos en cola, falta una galería; la demanda es la de los arqueros a caballo en las 3.
        view = coach.View(state, [new QueueItem("cavalry_archer"), new QueueItem("cavalry_archer")], Age.Castle, huns);
        Assert.Equal(1, Assert.Single(view.Gaps).Count);
        var ca = Data.Get("cavalry_archer");
        var demand = coach.Demand(view, state, Age.Castle, huns, 0, villagers: false);
        Assert.Equal(huns.Cost(ca, Age.Castle).Gold * 3 / (huns.Time(ca, Age.Castle) / huns.WorkRate("archery_range", Age.Castle)), demand.Gold, 6);

        // Un explorador suelto vuelve a pedir el establo; si vuelve a los exploradores, las galerías quedan afuera.
        state.Apply(new GameSnapshot(500, Poor, 25, 30, [new GameEvent("scout_cavalry_created", 500)]));
        Assert.Equal(["villager", "scout_cavalry", "cavalry_archer"], coach.View(state, [], Age.Castle, huns).Queue);
        foreach (var time in new[] { 530.0, 560 })
            state.Apply(new GameSnapshot(time, Poor, 25, 30, [new GameEvent("scout_cavalry_created", time)]));
        Assert.Equal(["villager", "scout_cavalry"], coach.View(state, [], Age.Castle, huns).Queue);
    }

    [Fact]
    public void La_galeria_hecha_solo_para_Castillos_deja_de_pedirse_si_el_jugador_sigue_con_su_unidad()
    {
        // Como "Romans Infantry": milicias, y la galería como segundo edificio de Feudal.
        var coach = new PostureCoach(Then(Scouts, "archery_range"), Data) { IdleGrace = 0 };
        var state = Timeline((100, "stable_built"), (300, "archery_range_built"),
            (330, "scout_cavalry_created"), (360, "scout_cavalry_created"), (390, "scout_cavalry_created"));

        var view = coach.View(state, [], Age.Feudal, Data.Civ("any"));

        Assert.Equal(["villager", "scout_cavalry"], view.Queue);
        Assert.Equal("stable", Assert.Single(view.Gaps).Building);
    }

    [Fact]
    public void La_postura_escrita_manda()
    {
        var written = new ClosingPosture { Queue = ["villager", "knight"], NextAge = "castle_age" };
        var (posture, derived) = Posture.Effective(Scouts with { Posture = written }, Data);

        Assert.False(derived);
        Assert.Same(written, posture);
    }

    [Fact]
    public void La_postura_se_guarda_en_el_json_y_vuelve_igual()
    {
        var bo = Scouts with { Posture = new ClosingPosture { Queue = ["villager", "scout_cavalry"], NextAge = "castle_age" } };
        var json = BuildOrderSerializer.Serialize(bo);

        Assert.Contains("\"posture\"", json);
        Assert.Contains("\"next_age\": \"castle_age\"", json);
        var back = BuildOrderSerializer.Parse(json).Posture!;
        Assert.Equal(["villager", "scout_cavalry"], back.Queue);
        Assert.Equal("castle_age", back.NextAge);
        Assert.Equal(2, BuildOrderSerializer.Parse(json).SchemaVersion); // sin subir el formato
    }

    [Fact]
    public void El_validador_avisa_una_unidad_que_no_es_tal_y_una_edad_que_ya_paso()
    {
        var bo = Scouts with { Posture = new ClosingPosture { Queue = ["stable"], NextAge = "feudal_age" } };
        var warnings = new BuildOrderValidator(Data).Validate(bo).Issues.Where(i => i.Severity == Severity.Warning).Select(i => i.Message).ToList();

        Assert.Contains(warnings, w => w.Contains("'stable' no es una unidad"));
        Assert.Contains(warnings, w => w.Contains("'feudal_age' no es una edad posterior"));
    }

    private static PlayerState State(double time, ResourceAmounts stock, params string[] events)
    {
        var state = new PlayerState();
        state.Apply(new GameSnapshot(time, stock, 25, 30, events.Select(e => new GameEvent(e, time)).ToArray()));
        return state;
    }

    private static readonly ResourceAmounts Poor = new(100, 100, 0, 0);

    [Fact]
    public void Un_establo_sin_nada_en_cola_mas_de_5_s_esta_parado()
    {
        var coach = new PostureCoach(Scouts, Data);
        var state = State(700, Poor, "stable_built");
        Assert.Empty(coach.View(state, [new QueueItem("villager")], Age.Feudal, Data.Civ("any")).Gaps);

        state.Apply(new GameSnapshot(706, Poor, 25, 30));
        var gap = Assert.Single(coach.View(state, [new QueueItem("villager")], Age.Feudal, Data.Civ("any")).Gaps);
        Assert.Equal(("stable", "scout_cavalry", 1), (gap.Building, gap.Unit, gap.Count));
        Assert.Equal("Establo parado: poné exploradores en cola", CoachText.GapText(gap));

        // Con un explorador en cola, ya no.
        Assert.Empty(coach.View(state, [new QueueItem("villager"), new QueueItem("scout_cavalry")], Age.Feudal, Data.Civ("any")).Gaps);
    }

    [Fact]
    public void Con_dos_establos_cada_uno_tiene_que_producir()
    {
        var coach = new PostureCoach(Scouts, Data) { IdleGrace = 0 };
        var state = State(700, Poor, "stable_built", "stable_built");
        var gap = Assert.Single(coach.View(state, [new QueueItem("scout_cavalry")], Age.Feudal, Data.Civ("any")).Gaps);

        Assert.Equal(1, gap.Count);
    }

    [Fact]
    public void Sin_establo_avisa_que_falta_y_sin_leer_la_cola_no_inventa_edificios_parados()
    {
        var coach = new PostureCoach(Scouts, Data) { IdleGrace = 0 };
        var gap = Assert.Single(coach.View(State(700, Poor), [], Age.Feudal, Data.Civ("any")).Gaps);
        Assert.True(gap.Missing);
        Assert.Equal("Sin establo: los exploradores no tienen dónde salir", CoachText.GapText(gap));

        Assert.Empty(coach.View(State(700, Poor, "stable_built"), null, Age.Feudal, Data.Civ("any")).Gaps);
    }

    [Fact]
    public void Para_Imperial_el_castillo_solo_alcanza()
    {
        // En el juego un castillo vale por los 2 edificios de Castillos (el validador lo cuenta así desde D12).
        var coach = new PostureCoach(Scouts22, Data);
        var view = coach.View(State(1500, new ResourceAmounts(500, 100, 0, 100), "stable_built", "castle_built"), [], Age.Castle, Data.Civ("any"));

        Assert.Equal("imperial_age", view.NextAge);
        Assert.Equal(0, view.NextAgeBuildingsMissing);
    }

    [Fact]
    public void Para_Castillos_faltan_dos_edificios_de_Feudal_y_lo_que_cuesta_guardando_para_la_produccion()
    {
        var coach = new PostureCoach(Scouts, Data);
        var view = coach.View(State(700, new ResourceAmounts(500, 100, 0, 100), "stable_built"), [], Age.Feudal, Data.Civ("any"));

        Assert.Equal(1, view.NextAgeBuildingsMissing);
        Assert.Equal("Para Edad de los Castillos: te falta 1 edificio de Edad Feudal", CoachText.PostureStatus(view));

        // Con la galería: Castillos (800 de comida y 200 de oro) más un aldeano y un explorador (50 + 80 de comida).
        view = coach.View(State(700, new ResourceAmounts(500, 100, 0, 100), "stable_built", "archery_range_built"), [], Age.Feudal, Data.Civ("any"));
        Assert.Equal(0, view.NextAgeBuildingsMissing);
        Assert.Equal(new ResourceAmounts(430, 0, 0, 100), view.NextAgeMissing);
        Assert.Equal("Edad de los Castillos: faltan 430 de comida y 100 de oro (guardando para la producción)", CoachText.PostureStatus(view));
    }

    [Fact]
    public void Con_todo_listo_pide_el_clic_y_despues_la_muestra_en_curso()
    {
        var coach = new PostureCoach(Scouts, Data);
        var rich = new ResourceAmounts(2000, 500, 0, 500);
        var view = coach.View(State(700, rich, "stable_built", "archery_range_built"), [], Age.Feudal, Data.Civ("any"));
        Assert.True(view.NextAgeReady);
        Assert.Equal("Hacé clic en Edad de los Castillos", CoachText.PostureStatus(view));

        view = coach.View(State(700, rich, "stable_built", "archery_range_built"), [new QueueItem("castle_age")], Age.Feudal, Data.Civ("any"));
        Assert.Equal("Edad de los Castillos: en curso", CoachText.PostureStatus(view));
        Assert.Null(coach.View(State(900, rich, "castle_age_researched"), [], Age.Castle, Data.Civ("any")).NextAge);
    }

    [Fact]
    public void El_coach_muestra_la_postura_al_terminar_la_BO()
    {
        var coach = new Coach(Scouts, Data, TestData.Events);
        Assert.Null(coach.View.Posture);
        for (var i = 0; i < Scouts.Steps.Count && coach.Tracker.Current is not null; i++)
            coach.Skip();
        var view = coach.Update(new GameSnapshot(900, Poor, 25, 30));

        Assert.NotNull(view.Posture);
        var lines = CoachText.Lines(view, Data);
        Assert.Equal("BO terminada", lines.Current);
        Assert.Equal("Postura: aldeanos y exploradores siempre en cola", lines.Status);
        Assert.Contains("Sin establo: los exploradores no tienen dónde salir", lines.Running);
    }

    // Con la BO terminada (pedido del usuario, 2026-10-26: "debería pasar a ser mínima u ocultarse", opción A): 10 s el
    // panel completo con "BO terminada"; después, una tarjeta chica con lo que pide hacer algo, y nada si no hay nada.
    [Fact]
    public void Con_la_BO_terminada_el_panel_avisa_y_despues_queda_solo_lo_que_hay_que_hacer()
    {
        var coach = new Coach(Scouts, Data, TestData.Events);
        for (var i = 0; i < Scouts.Steps.Count && coach.Tracker.Current is not null; i++)
            coach.Advance();
        var view = coach.Update(new GameSnapshot(900, Poor, 25, 30));
        Assert.False(view.Compact);
        Assert.False(CoachText.Lines(view, Data).Compact);

        view = coach.Update(new GameSnapshot(909, Poor, 25, 30));
        Assert.False(view.Compact);
        view = coach.Update(new GameSnapshot(910, Poor, 25, 30));
        var lines = CoachText.Lines(view, Data);
        Assert.True(view.Compact);
        Assert.True(lines.Compact);
        // Lo que pide hacer algo, lo más urgente primero; lo que solo informa (juntar para la edad) no va.
        Assert.Equal(new CardLine("Para Edad de los Castillos: te faltan 2 edificios de Edad Feudal", CardKind.Missing), lines.Card[0]);
        Assert.Equal(new CardLine("Sin establo: los exploradores no tienen dónde salir", CardKind.Building), lines.Card[1]);
        Assert.DoesNotContain(lines.Card, c => c.Text.Contains("guardando") || c.Text.Contains("siempre en cola"));

        // Con el establo y la galería hechos (sin leer la cola), ya no faltan edificios ni hay establo parado.
        coach.Update(new GameSnapshot(911, Poor, 25, 30, [new GameEvent("stable_built", 911)]));
        view = coach.Update(new GameSnapshot(912, Poor, 25, 30, [new GameEvent("archery_range_built", 912)]));
        Assert.DoesNotContain(CoachText.Lines(view, Data).Card, c => c.Kind is CardKind.Missing or CardKind.Building);

        // "Atrás" vuelve a la BO: el panel completo otra vez.
        Assert.False(coach.Back().Compact);
    }

    [Fact]
    public void Con_la_BO_terminada_pide_las_tecnologias_que_faltan_y_Ya_esta_descarta_la_primera()
    {
        var coach = new Coach(Scouts, Data, TestData.Events) { FinishedNoticeSeconds = 0 };
        for (var i = 0; i < Scouts.Steps.Count && coach.Tracker.Current is not null; i++)
            coach.Advance();
        var rich = new ResourceAmounts(2000, 2000, 0, 2000);
        // La BO hizo el telar, el campamento maderero, el molino y el establo; no hay campamento minero.
        var view = coach.Update(new GameSnapshot(900, rich, 25, 30, [new GameEvent("stable_built", 900), new GameEvent("archery_range_built", 900)])
        {
            VillagersOn = new ResourceAmounts(10, 8, 0, 4),
        });

        Assert.Equal(["wheelbarrow", "double_bit_axe"], view.Techs.Select(t => t.Tech));
        var card = CoachText.Lines(view, Data).Card;
        Assert.Contains(card, c => c is { Text: "Hacé clic en Edad de los Castillos", Kind: CardKind.Ready });
        Assert.Equal(["Investigá Carretilla (centro urbano)", "Investigá Hacha de doble filo (campamento maderero)"],
            card.Where(c => c.Kind == CardKind.Tech).Select(c => c.Text));

        // "Ya está" saca la carretilla; la que sigue entra con la próxima captura.
        Assert.Equal(["double_bit_axe"], coach.Advance().Techs.Select(t => t.Tech));
        view = coach.Update(new GameSnapshot(901, rich, 25, 30) { VillagersOn = new ResourceAmounts(10, 8, 0, 4) });
        Assert.Equal(["double_bit_axe", "horse_collar"], view.Techs.Select(t => t.Tech));
    }

    // Auditoría del 2026-11-05: la edad salía solo de los pasos, así que sin BO (o con una que termina antes) las
    // tecnologías se quedaban en las de la edad que dejó la BO: sin ninguna, solo el telar.
    [Fact]
    public void Sin_BO_las_tecnologias_son_las_de_la_edad_que_avisa_el_log()
    {
        var coach = new Coach(BuildOrder.None, Data, TestData.Events) { FinishedNoticeSeconds = 0 };
        var rich = new ResourceAmounts(2000, 2000, 0, 2000);

        var view = coach.Update(new GameSnapshot(600, rich, 25, 30, [new GameEvent("feudal_age_researched", 600)]));

        Assert.Equal(["loom", "wheelbarrow"], view.Techs.Select(t => t.Tech));
    }

    [Fact]
    public void Una_BO_que_termina_en_Feudal_pide_lo_de_Castillos_al_llegar()
    {
        var bo = new BuildOrder { Name = "t", Steps = [new Step { Id = "feudal", Do = StepAction.Research, What = "feudal_age" }] };
        var coach = new Coach(bo, Data, TestData.Events) { FinishedNoticeSeconds = 0 };
        var rich = new ResourceAmounts(2000, 2000, 0, 2000);
        // Todo lo de Dark y Feudal ya está: lo único que puede faltar es de Castillos.
        coach.Update(new GameSnapshot(600, rich, 25, 30, [new GameEvent("feudal_age_researched", 600)]));
        foreach (var tech in new[] { "loom", "wheelbarrow", "fletching", "bloodlines", "forging", "man_at_arms", "crossbowman", "light_cavalry", "padded_archer_armor", "scale_barding_armor", "scale_mail_armor", "husbandry" })
            coach.Update(new GameSnapshot(601, rich, 25, 30, [new GameEvent($"{tech}_researched", 601)]));
        coach.Update(new GameSnapshot(1200, rich, 25, 30, [new GameEvent("castle_age_researched", 1200), new GameEvent("town_center_built", 1200)]));

        var view = coach.Update(new GameSnapshot(1201, rich, 25, 30));

        Assert.Contains("hand_cart", view.Techs.Select(t => t.Tech));
    }

    // La GD y la economía usan la demanda de la postura (lo que faltaba de D2): producir sin parar cada unidad de la
    // postura en cada edificio que la hace, y juntar lo que falta para la próxima edad.
    [Fact]
    public void La_demanda_de_la_postura_es_la_produccion_sin_parar_y_lo_que_falta_para_la_edad()
    {
        var coach = new PostureCoach(Scouts, Data);
        var state = State(700, new ResourceAmounts(500, 100, 0, 100), "stable_built", "stable_built", "archery_range_built");
        var view = coach.View(state, [], Age.Feudal, Data.Civ("any"));
        var villager = Data.Get("villager");
        var scout = Data.Get("scout_cavalry");

        var demand = coach.Demand(view, state, Age.Feudal, Data.Civ("any"), savingSeconds: 120, villagers: true);

        // Un centro urbano con aldeanos, dos establos con exploradores, y lo que falta de Castillos en 120 s.
        var expected = villager.Cost * (1.0 / villager.Time) + scout.Cost * (2.0 / scout.Time) + view.NextAgeNeed * (1 / 120.0);
        Assert.Equal(expected.Food, demand.Food, 6);
        Assert.Equal(expected.Gold, demand.Gold, 6);
        // Castillos más un aldeano y un explorador por establo, sin restar el stock (la economía lo resta).
        Assert.Equal(Data.Get("castle_age").Cost + villager.Cost + scout.Cost * 2, view.NextAgeNeed);

        // Con el tope de aldeanos, solo los exploradores y la edad.
        var capped = coach.Demand(view, state, Age.Feudal, Data.Civ("any"), savingSeconds: 120, villagers: false);
        Assert.Equal(expected.Food - villager.Cost.Food / (double)villager.Time, capped.Food, 6);
    }

    [Fact]
    public void Con_la_edad_en_curso_no_se_junta_para_ella()
    {
        var coach = new PostureCoach(Scouts, Data);
        var state = State(700, Poor, "stable_built", "archery_range_built");
        var view = coach.View(state, [new QueueItem("castle_age")], Age.Feudal, Data.Civ("any"));
        var scout = Data.Get("scout_cavalry");

        var demand = coach.Demand(view, state, Age.Feudal, Data.Civ("any"), savingSeconds: 120, villagers: false);

        Assert.Equal(scout.Cost.Food / (double)scout.Time, demand.Food, 6);
    }

    [Fact]
    public void No_junta_para_una_edad_que_no_es_la_siguiente()
    {
        // Arqueros que clican Castillos: la postura apunta a Imperial, pero mientras no se llegue a Castillos solo produce.
        var archers = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "archers_feudal.json"))
            with { Posture = new ClosingPosture { Queue = ["archer"], NextAge = "imperial_age" } };
        var coach = new PostureCoach(archers, Data);
        var state = State(700, Poor, "archery_range_built");
        var archer = Data.Get("archer");

        var feudal = coach.Demand(coach.View(state, [], Age.Feudal, Data.Civ("any")), state, Age.Feudal, Data.Civ("any"), 120, villagers: false);
        var castle = coach.Demand(coach.View(state, [], Age.Castle, Data.Civ("any")), state, Age.Castle, Data.Civ("any"), 120, villagers: false);

        Assert.Equal(archer.Cost * (1.0 / archer.Time), feudal);
        Assert.True(castle.Food > 0); // en Castillos ya junta para Imperial
    }

    [Fact]
    public void Con_la_BO_terminada_la_economia_pide_la_comida_de_la_postura()
    {
        var coach = new Coach(Scouts, Data, TestData.Events);
        for (var i = 0; i < Scouts.Steps.Count && coach.Tracker.Current is not null; i++)
            coach.Advance(); // hechos: la edad sale de los pasos hechos (Feudal)
        coach.Update(new GameSnapshot(900, Poor, 25, 30, [new GameEvent("stable_built", 900)]));

        var villager = Data.Get("villager");
        var scout = Data.Get("scout_cavalry");
        Assert.True(coach.Economy!.Demand.Food >= villager.Cost.Food / (double)villager.Time + scout.Cost.Food / (double)scout.Time - 1e-9);
        Assert.True(coach.Economy.Demand.Gold > 0); // Castillos pide oro
    }
}
