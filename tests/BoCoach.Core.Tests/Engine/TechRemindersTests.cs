using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// Las tecnologías que faltan con la BO terminada (pedido del usuario, 2026-10-26): las que importan, de tu edad, con
/// su edificio hecho, que tu civ tiene y no le salen gratis, que no se hicieron y que te alcanza para pagar.
/// </summary>
public class TechRemindersTests
{
    private static readonly GameData Data = TestData.GameData;
    private static readonly ResourceAmounts Rich = new(2000, 2000, 2000, 2000);
    private static readonly ResourceAmounts Eco = new(10, 8, 0, 4); // comida, madera, piedra, oro
    private static readonly string[] Camps = ["lumber_camp", "mill", "mining_camp"];
    // Hechas todas las económicas de Feudal menos la carretilla.
    private static readonly string[] AllButWheelbarrow = ["loom", "double_bit_axe", "horse_collar", "gold_mining"];

    private static PlayerState State(double time, ResourceAmounts stock, params string[] events)
    {
        var state = new PlayerState();
        state.Apply(new GameSnapshot(time, stock, 25, 30, events.Select(e => new GameEvent(e, time)).ToArray()));
        return state;
    }

    private static List<string> Update(TechReminders reminders, PlayerState state, Age age = Age.Feudal, ResourceAmounts? villagers = null,
        IReadOnlyList<QueueItem>? queue = null, string[]? built = null, string[]? byBo = null) =>
        reminders.Update(state, queue, age, villagers ?? Eco, b => (built ?? Camps).Contains(b), t => (byBo ?? []).Contains(t))
            .Select(r => r.Tech).ToList();

    [Fact]
    public void Con_plata_aldeanos_y_edificios_pide_primero_las_economicas_de_siempre()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);

        var shown = reminders.Update(State(900, Rich), null, Age.Feudal, Eco, b => Camps.Contains(b), _ => false);

        Assert.Equal(["loom", "wheelbarrow"], shown.Select(r => r.Tech));
        Assert.Equal("Investigá Carretilla (centro urbano)", CoachText.TechText(shown[1]));
    }

    [Fact]
    public void Lo_que_avisó_el_log_o_hizo_la_BO_no_se_pide()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);

        Assert.Equal(["double_bit_axe", "horse_collar"], Update(reminders, State(900, Rich, "loom_researched"), byBo: ["wheelbarrow"]));
    }

    [Fact]
    public void Sin_el_edificio_sin_aldeanos_en_el_recurso_o_de_otra_edad_no_se_pide()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);
        var done = new[] { "loom", "wheelbarrow" };

        // Sin campamento maderero no hay hachas; con 4 en comida no hay collera; con 2 en oro no hay minería; la carreta
        // de mano es de Castillos.
        Assert.Empty(Update(reminders, State(900, Rich), villagers: new ResourceAmounts(4, 8, 0, 2), built: ["mill", "mining_camp"], byBo: done));
        // Sin saber cuántos hay en cada recurso, solo las de siempre.
        Assert.Empty(reminders.Update(State(900, Rich), null, Age.Feudal, null, b => Camps.Contains(b), done.Contains));
    }

    [Fact]
    public void Sin_plata_no_aparece_pero_una_vez_que_aparece_queda_aunque_baje_el_stock()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);
        Assert.Equal(["loom"], Update(reminders, State(900, new ResourceAmounts(100, 20, 0, 60))));

        // La carretilla no alcanza (175 de comida y 50 de madera) y el telar queda aunque el oro baje a 30.
        Assert.Equal(["loom"], Update(reminders, State(901, new ResourceAmounts(100, 20, 0, 30))));
    }

    [Fact]
    public void Las_gratis_de_la_civ_no_se_piden()
    {
        Assert.DoesNotContain("wheelbarrow", Update(new TechReminders(Data, "vikings", ["villager"]), State(900, Rich), byBo: ["loom"]));
        Assert.DoesNotContain("horse_collar", Update(new TechReminders(Data, "franks", ["villager"]), State(900, Rich), byBo: ["loom", "wheelbarrow", "double_bit_axe"]));
    }

    [Fact]
    public void Las_militares_salen_de_las_unidades_de_la_postura()
    {
        var eco = new[] { "loom", "wheelbarrow", "double_bit_axe", "horse_collar", "gold_mining" };
        var built = new[] { "lumber_camp", "mill", "mining_camp", "stable", "archery_range", "blacksmith" };

        Assert.Equal(["bloodlines", "forging"], Update(new TechReminders(Data, "any", ["villager", "scout_cavalry"]), State(900, Rich), built: built, byBo: eco));
        Assert.Equal(["fletching", "padded_archer_armor"], Update(new TechReminders(Data, "any", ["villager", "archer"]), State(900, Rich), built: built, byBo: eco));
    }

    [Fact]
    public void El_clic_la_saca_y_si_el_log_no_la_confirma_vuelve()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);
        var stock = new ResourceAmounts(300, 100, 0, 0);
        Assert.Equal(["wheelbarrow"], Update(reminders, State(900, stock), byBo: AllButWheelbarrow));

        // El clic en la carretilla: bajan justo 175 de comida y 50 de madera.
        Assert.Empty(Update(reminders, State(902, stock - new ResourceAmounts(175, 50, 0, 0)), byBo: AllButWheelbarrow));
        Assert.Empty(Update(reminders, State(950, new ResourceAmounts(280, 90, 0, 0)), byBo: AllButWheelbarrow));

        // Pasado 1,5 veces lo que tarda (75 s) más 15 s sin que el log la confirme, vuelve.
        Assert.Equal(["wheelbarrow"], Update(reminders, State(902 + 75 * 1.5 + 15, stock), byBo: AllButWheelbarrow));
    }

    [Fact]
    public void Con_el_log_que_la_confirma_no_vuelve_y_si_el_stock_vuelve_se_cancelo()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);
        var stock = new ResourceAmounts(300, 100, 0, 0);
        var paid = stock - new ResourceAmounts(175, 50, 0, 0);
        Assert.Equal(["wheelbarrow"], Update(reminders, State(900, stock), byBo: AllButWheelbarrow));
        Update(reminders, State(902, paid), byBo: AllButWheelbarrow);
        Assert.Empty(Update(reminders, State(1100, new ResourceAmounts(250, 80, 0, 0), "wheelbarrow_researched"), byBo: AllButWheelbarrow));

        // Otra partida: clic y el stock vuelve justo lo mismo (se canceló).
        reminders = new TechReminders(Data, "any", ["villager"]);
        Update(reminders, State(900, stock), byBo: AllButWheelbarrow);
        Assert.Empty(Update(reminders, State(902, paid), byBo: AllButWheelbarrow));
        Assert.Equal(["wheelbarrow"], Update(reminders, State(904, stock), byBo: AllButWheelbarrow));
    }

    [Fact]
    public void En_la_cola_de_produccion_dos_veces_seguidas_cuenta_como_en_curso()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);
        Assert.Equal(["wheelbarrow", "double_bit_axe"], Update(reminders, State(900, Rich), byBo: ["loom"]));

        Assert.Equal(["wheelbarrow", "double_bit_axe"], Update(reminders, State(901, Rich), queue: [new QueueItem("wheelbarrow")], byBo: ["loom"]));
        Assert.Equal(["double_bit_axe", "horse_collar"], Update(reminders, State(902, Rich), queue: [new QueueItem("wheelbarrow")], byBo: ["loom"]));
    }

    [Fact]
    public void Ya_esta_descarta_la_primera_por_el_resto_de_la_partida()
    {
        var reminders = new TechReminders(Data, "any", ["villager"]);
        Update(reminders, State(900, Rich));

        Assert.True(reminders.Dismiss());
        Assert.Equal(["wheelbarrow", "double_bit_axe"], Update(reminders, State(901, Rich)));
        Assert.Equal(["wheelbarrow", "double_bit_axe"], Update(reminders, State(2000, Rich)));
    }
}
