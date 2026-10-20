using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// D3: señales de la partida. Los puntajes imitan la grabación 20261019-202635: el clic de Feudal del rival bajó su
/// puntaje de 831 a 797 y quedó abajo; el del usuario, de 779 a 730.
/// </summary>
public class GameSignalsTests
{
    private static GameSnapshot At(double time, int? villagers = null, params PlayerScore[] scores) =>
        new(time, PlanStart.Standard.Stock, 20, 25) { Scores = scores.Length > 0 ? scores : null, Villagers = villagers };

    private static (GameSignals Signals, PlayerState State) New() => (new GameSignals(TestData.GameData), new PlayerState());

    private static IReadOnlyList<GameSignal> Feed(GameSignals signals, PlayerState state, params GameSnapshot[] snapshots)
    {
        var all = new List<GameSignal>();
        foreach (var s in snapshots)
        {
            state.Apply(s);
            all.AddRange(signals.Update(s, state));
        }
        return all;
    }

    [Fact]
    public void El_puntaje_del_rival_que_cae_y_queda_abajo_es_su_clic_de_Feudal()
    {
        var (signals, state) = New();
        var found = Feed(signals, state,
            At(498, null, new PlayerScore(2, 828, true)),
            At(500, null, new PlayerScore(2, 831, true)),
            At(502, null, new PlayerScore(2, 797, true)),
            At(504, null, new PlayerScore(2, 799, true)));

        var signal = Assert.Single(found);
        Assert.Equal("rival_feudal_click", signal.Kind);
        Assert.Equal("El rival hizo clic en Feudal (8:22): llega a las 10:32", signal.Text);
    }

    [Fact]
    public void Una_lectura_mala_que_vuelve_enseguida_no_es_un_clic()
    {
        var (signals, state) = New();
        Assert.Empty(Feed(signals, state,
            At(500, null, new PlayerScore(2, 831, true)),
            At(502, null, new PlayerScore(2, 781, true)),
            At(504, null, new PlayerScore(2, 833, true))));
    }

    [Fact]
    public void Un_renglon_tomado_una_vez_por_otro_jugador_no_hace_un_clic()
    {
        var (signals, state) = New();
        Assert.Empty(Feed(signals, state,
            At(190, null, new PlayerScore(2, 300, true)),
            At(192, null, new PlayerScore(2, 340, true)), // el renglón del usuario leído con el color del rival
            At(194, null, new PlayerScore(2, 301, true)),
            At(196, null, new PlayerScore(2, 302, true))));
    }

    [Fact]
    public void Gastar_poco_no_es_un_clic()
    {
        var (signals, state) = New();
        Assert.Empty(Feed(signals, state,
            At(500, null, new PlayerScore(2, 831, true)),
            At(502, null, new PlayerScore(2, 811, true)),
            At(504, null, new PlayerScore(2, 812, true))));
    }

    [Fact]
    public void La_caida_propia_junto_al_clic_propio_no_es_del_rival_y_dice_cual_es_el_jugador()
    {
        var (signals, state) = New();
        signals.OwnAgeClick(503);
        Assert.Empty(Feed(signals, state,
            At(498, null, new PlayerScore(1, 778, true)),
            At(500, null, new PlayerScore(1, 779, true)),
            At(502, null, new PlayerScore(1, 730, true)),
            At(504, null, new PlayerScore(1, 732, true))));
        Assert.Equal(1, signals.PlayerSlot);
    }

    [Fact]
    public void Con_el_jugador_conocido_su_renglon_no_cuenta()
    {
        var (signals, state) = New();
        signals.PlayerSlot = 1;
        Assert.Empty(Feed(signals, state,
            At(498, null, new PlayerScore(1, 778, true)),
            At(500, null, new PlayerScore(1, 779, true)),
            At(502, null, new PlayerScore(1, 730, true)),
            At(504, null, new PlayerScore(1, 732, true))));
    }

    [Fact]
    public void Despues_de_Feudal_vienen_Castillos_e_Imperial_con_caidas_mas_grandes()
    {
        var (signals, state) = New();
        var found = Feed(signals, state,
            At(898, null, new PlayerScore(2, 1995, false)),
            At(900, null, new PlayerScore(2, 2000, false)),
            At(902, null, new PlayerScore(2, 1950, false)), // 50 en Feudal: no es Castillos (pide unos 100)
            At(904, null, new PlayerScore(2, 1952, false)),
            At(998, null, new PlayerScore(2, 2098, false)),
            At(1000, null, new PlayerScore(2, 2100, false)),
            At(1002, null, new PlayerScore(2, 1998, false)),
            At(1004, null, new PlayerScore(2, 2000, false)),
            At(1498, null, new PlayerScore(2, 2995, false)),
            At(1500, null, new PlayerScore(2, 3000, false)),
            At(1502, null, new PlayerScore(2, 2815, false)),
            At(1504, null, new PlayerScore(2, 2816, false)));

        Assert.Equal(["rival_castle_click", "rival_imperial_click"], found.Select(s => s.Kind));
    }

    [Fact]
    public void Si_el_total_de_aldeanos_baja_y_se_sostiene_se_perdieron()
    {
        var (signals, state) = New();
        var found = Feed(signals, state, At(600, 20), At(601, 18), At(602, 18), At(604, 18), At(606, 18));

        var signal = Assert.Single(found);
        Assert.Equal(("villagers_lost", "Perdiste 2 aldeanos: reponelos"), (signal.Kind, signal.Text));
    }

    [Fact]
    public void Una_baja_de_aldeanos_que_vuelve_no_es_perderlos()
    {
        var (signals, state) = New();
        Assert.Empty(Feed(signals, state, At(600, 20), At(601, 18), At(602, 20), At(606, 20)));
    }

    [Fact]
    public void El_aviso_de_ataque_es_una_senal_y_no_se_repite_en_30_s()
    {
        var (signals, state) = New();
        var attack = (double t) => new GameSnapshot(t, PlanStart.Standard.Stock, 20, 25, new GameEvent("under_attack", t));
        var found = Feed(signals, state, attack(600), attack(610), attack(650));

        Assert.Equal([600.0, 650.0], found.Select(s => s.Time));
    }

    [Fact]
    public void El_overlay_muestra_la_senal_en_el_renglon_de_avisos()
    {
        var bo = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        coach.Update(At(498, null, new PlayerScore(2, 828, true)));
        coach.Update(At(500, null, new PlayerScore(2, 831, true)));
        coach.Update(At(502, null, new PlayerScore(2, 797, true)));
        var view = coach.Update(At(504, null, new PlayerScore(2, 799, true)));

        Assert.Equal("El rival hizo clic en Feudal (8:22): llega a las 10:32", CoachText.Lines(view, TestData.GameData).Alert);
        view = coach.Update(At(520));
        Assert.Empty(view.Signals); // a los 10 s se deja de mostrar
    }
}
