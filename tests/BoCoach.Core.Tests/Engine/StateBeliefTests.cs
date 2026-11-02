using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// La creencia del estado (spec 2026-11-02): una sola capa que fusiona el HUD, el log, la cola y el puntaje con una
/// fuerza por fuente, sin saber nada de la BO.
/// </summary>
public class StateBeliefTests
{
    private static ScreenReading Reading(double clock, params string[] events) =>
        new(clock, 200, 200, 100, 200, 3, null, null, events);

    private static ScreenReading Queue(double clock, params (string Key, bool Waiting)[] items) =>
        Reading(clock) with { Queue = items.Select(i => new QueueItem(i.Key, i.Waiting)).ToList() };

    [Fact]
    public void El_log_suma_un_edificio_por_aviso_y_el_centro_urbano_arranca_en_uno()
    {
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(Reading(10, "archery_range_built"), 10);

        Assert.Equal(1, belief.View.Count("town_center"));
        Assert.Equal(1, belief.View.Count("archery_range"));
        Assert.Equal(Age.Feudal, belief.View.Age); // una galería es de Feudal
        var completion = Assert.Single(belief.TakeNew());
        Assert.Equal(("archery_range_built", EvidenceSource.Log, false), (completion.EventId, completion.Source, completion.Synthetic));
        Assert.Empty(belief.TakeNew());
    }

    [Fact]
    public void La_vista_trae_lo_leido_del_hud_y_lo_investigado()
    {
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(new ScreenReading(20, 200, 200, 100, 200, 7, 9, 15, ["loom_researched", "scout_cavalry_created"]) { VillagersOn = new ResourceAmounts(4, 3, 0, 0) }, 20);

        Assert.Equal((7, 9, 15), (belief.View.Villagers, belief.View.Population, belief.View.PopulationCap));
        Assert.Equal(new ResourceAmounts(4, 3, 0, 0), belief.View.VillagersOn);
        Assert.True(belief.View.Done("loom"));
        Assert.Equal(1, belief.View.Units["scout_cavalry"]);
        Assert.Equal(1, belief.View.Military);
    }

    [Fact]
    public void Lo_que_el_juego_no_avisa_no_se_puede_observar()
    {
        Assert.False(StateBelief.CanObserve("farm_built"));
        Assert.True(StateBelief.CanObserve("house_built"));
    }

    [Fact]
    public void El_log_aviso_una_galeria_y_la_cola_muestra_dos_arqueros_a_la_vez_entonces_hay_dos()
    {
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(Reading(717, "archery_range_built"), 717);
        belief.Observe(Queue(734, ("villager", false), ("archer", false), ("archer", false), ("archer", true)), 734);
        Assert.Equal(1, belief.View.Count("archery_range")); // una sola lectura no alcanza

        belief.Observe(Queue(734.5, ("villager", false), ("archer", false), ("archer", false), ("archer", true)), 734.5);

        Assert.Equal(2, belief.View.Count("archery_range"));
        Assert.Equal(1, belief.View.Count("town_center"));
        var added = belief.TakeNew().Where(c => c.EventId == "archery_range_built").ToList();
        Assert.Equal(2, added.Count);
        Assert.Equal([EvidenceSource.Log, EvidenceSource.Queue], added.Select(c => c.Source));
    }

    [Fact]
    public void Un_icono_de_una_unidad_de_una_edad_que_no_se_alcanzo_no_cuenta()
    {
        // Partida 20261101-230201: una milicia leída como arquero daba una galería a las 9:37, en la Edad Oscura.
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(Queue(577, ("archer", false)), 577);
        belief.Observe(Queue(577.5, ("archer", false)), 577.5);
        Assert.Equal(0, belief.View.Count("archery_range"));

        belief.Observe(Reading(600, "feudal_age_researched"), 600);
        belief.Observe(Queue(601, ("archer", false)), 601);
        belief.Observe(Queue(601.5, ("archer", false)), 601.5);

        Assert.Equal(1, belief.View.Count("archery_range"));
    }

    [Fact]
    public void Un_icono_desconocido_o_sin_poblacion_no_dice_nada()
    {
        var belief = new StateBelief(TestData.GameData);
        for (var t = 0; t < 2; t++)
            belief.Observe(Queue(100 + t, ("?", false), ("housed", false)), 100 + t);

        Assert.Single(belief.View.Buildings);
        Assert.Empty(belief.View.Research);
    }

    [Fact]
    public void Una_tecnologia_en_la_cola_esta_en_curso_y_al_irse_pasado_su_tiempo_queda_hecha()
    {
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(Queue(200, ("loom", false)), 200);
        Assert.False(belief.View.InProgress("loom"));
        belief.Observe(Queue(201, ("loom", false)), 201);
        Assert.True(belief.View.InProgress("loom"));

        // El telar tarda 25 s: a los 10 s que falte es una lectura mala.
        belief.Observe(Queue(210, ("villager", false)), 210);
        belief.Observe(Queue(211, ("villager", false)), 211);
        Assert.True(belief.View.InProgress("loom"));
        Assert.False(belief.View.Done("loom"));

        belief.Observe(Queue(226, ("villager", false)), 226);
        belief.Observe(Queue(227, ("villager", false)), 227);

        Assert.True(belief.View.Done("loom"));
        var done = Assert.Single(belief.TakeNew(), c => c.EventId == "loom_researched");
        Assert.True(done.Synthetic);
    }

    [Fact]
    public void El_aviso_tardio_del_log_de_una_tecnologia_hecha_por_la_cola_no_cuenta_dos_veces()
    {
        var belief = new StateBelief(TestData.GameData);
        belief.Observe(Queue(200, ("loom", false)), 200);
        belief.Observe(Queue(201, ("loom", false)), 201);
        belief.Observe(Queue(226, ("villager", false)), 226);
        belief.Observe(Queue(227, ("villager", false)), 227);
        belief.Observe(Reading(230, "loom_researched"), 230);

        Assert.Equal(1, belief.TakeNew().Count(c => c.EventId == "loom_researched"));
    }
}
