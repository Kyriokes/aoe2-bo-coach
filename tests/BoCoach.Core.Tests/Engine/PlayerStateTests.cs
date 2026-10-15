using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

public class PlayerStateTests
{
    private static GameSnapshot Snap(double time, int pop, params GameEvent[] events) =>
        new(time, ResourceAmounts.Zero, pop, 10, events);

    [Fact]
    public void Poblacion_que_sube_sin_evento_cuenta_como_aldeano()
    {
        var state = new PlayerState();

        state.Apply(Snap(25, 6));

        Assert.Equal(2, state.Count(EventConventions.VillagerCreated));
        Assert.All(state.Events, e => Assert.True(e.Synthetic));
    }

    [Fact]
    public void Evento_que_llega_tarde_reemplaza_al_sintetico()
    {
        var state = new PlayerState();
        state.Apply(Snap(25, 5));

        state.Apply(Snap(26, 5, new GameEvent("villager_created", 26)));

        var e = Assert.Single(state.Events);
        Assert.False(e.Synthetic);
        Assert.Equal(25, e.Time);
    }

    [Fact]
    public void Unidad_militar_atrasada_corrige_al_aldeano_sintetico()
    {
        var state = new PlayerState();
        state.Apply(Snap(25, 5));

        state.Apply(Snap(27, 5, new GameEvent("scout_cavalry_created", 27)));

        Assert.Equal(0, state.Count(EventConventions.VillagerCreated));
        Assert.Equal(1, state.Count("scout_cavalry_created"));
    }

    [Fact]
    public void Suba_y_evento_de_otro_aldeano_en_el_mismo_snapshot_no_se_pierden()
    {
        var state = new PlayerState();
        state.Apply(Snap(25, 5));

        state.Apply(Snap(50, 6, new GameEvent("villager_created", 50)));

        Assert.Equal(2, state.Count(EventConventions.VillagerCreated));
    }

    [Fact]
    public void Evento_sin_suba_de_poblacion_se_cuenta_igual()
    {
        // Murió un aldeano y salió otro: la población no cambia, pero el log lo muestra.
        var state = new PlayerState();

        state.Apply(Snap(25, 4, new GameEvent("villager_created", 25)));

        Assert.Equal(1, state.Count(EventConventions.VillagerCreated));
    }

    [Fact]
    public void Evento_viejo_no_reemplaza_sinteticos_fuera_de_la_ventana()
    {
        var state = new PlayerState();
        state.Apply(Snap(25, 5));

        state.Apply(Snap(25 + PlayerState.MatchWindowSeconds + 5, 5, new GameEvent("villager_created", 38)));

        Assert.Equal(2, state.Count(EventConventions.VillagerCreated));
    }
}
