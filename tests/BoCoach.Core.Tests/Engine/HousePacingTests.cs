using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>Casas por ritmo de producción (spec 2026-11-02, escalón 1).</summary>
public class HousePacingTests
{
    private static readonly GameData Data = TestData.GameData;
    private static readonly CivBonuses Civ = CivBonuses.None;

    [Fact]
    public void El_centro_urbano_solo_consume_un_aldeano_cada_25_s_y_deja_el_margen_de_siempre()
    {
        var rate = HousePacing.ConsumptionRate([new("villager")], Data, Civ, Age.Dark);

        Assert.Equal(1 / 25.0, rate, 6);
        Assert.Equal(40, HousePacing.ReactionSeconds(Data, Civ, Age.Dark)); // la casa 25 s + caminar 5 + el jugador 10
        Assert.Equal(3, HousePacing.Margin(rate, 40)); // 1,6 lugares: nunca menos que 3
    }

    [Fact]
    public void Tres_galerias_y_un_establo_piden_unos_7_lugares()
    {
        QueueItem[] queue = [new("villager"), new("archer"), new("archer"), new("archer"), new("scout_cavalry")];

        var rate = HousePacing.ConsumptionRate(queue, Data, Civ, Age.Feudal);

        Assert.Equal(1 / 25.0 + 3 / 35.0 + 1 / 30.0, rate, 6); // 0,159 por segundo
        Assert.Equal(7, HousePacing.Margin(rate, 40));
    }

    [Fact]
    public void Lo_que_espera_y_las_tecnologias_no_consumen()
    {
        QueueItem[] queue = [new("villager"), new("archer", Waiting: true), new("loom"), new("feudal_age"), new("housed")];

        Assert.Equal(1 / 25.0, HousePacing.ConsumptionRate(queue, Data, Civ, Age.Dark), 6);
    }

    // Auditoría del 2026-11-05: el catálogo de la cola reconoce las mejoras ("pikeman", "crossbowman"), que para game_data
    // no existen o son la tecnología, y el margen de la casa no contaba lo que estaban produciendo.
    [Fact]
    public void Las_mejoras_de_una_unidad_consumen_como_su_unidad_base()
    {
        QueueItem[] queue = [new("pikeman"), new("crossbowman")];

        var rate = HousePacing.ConsumptionRate(queue, Data, Civ, Age.Feudal);

        Assert.Equal(1.0 / Data.Get("spearman").Time + 1.0 / Data.Get("archer").Time, rate, 6);
    }

    [Fact]
    public void Sin_cola_se_cuenta_solo_el_centro_urbano()
    {
        Assert.Equal(1 / 25.0, HousePacing.ConsumptionRate(null, Data, Civ, Age.Dark), 6);
        Assert.Equal(0, HousePacing.ConsumptionRate([], Data, Civ, Age.Dark));
    }
}
