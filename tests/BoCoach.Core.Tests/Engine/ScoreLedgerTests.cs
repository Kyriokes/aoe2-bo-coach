using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>
/// Edificios repetidos deducidos por el panel de puntajes (partida 20261101-125353): el puntaje sube el 20 % del costo
/// de cada cosa que se termina, y el log no agrega renglón si "Archery Range Built" ya es el último.
/// </summary>
public class ScoreLedgerTests
{
    private static ScreenReading Score(double clock, int own, params string[] events) =>
        new(clock, 200, 200, 100, 200, 7, null, null, events, Scores: [new PlayerScore(2, 800 + (int)clock, false), new PlayerScore(1, own, false)]);

    private static int Count(GameSnapshot? snapshot, string id) => snapshot?.Events.Count(e => e.EventId == id) ?? 0;

    private static SnapshotBuilder Warm(int? slot = 1)
    {
        var builder = new SnapshotBuilder(TestData.GameData) { PlayerSlot = slot };
        // El puntaje sube 4 por lectura solo por la economía.
        for (var i = 0; i < 4; i++)
            builder.Build(Score(i * 3, 1000 + i * 4));
        return builder;
    }

    [Fact]
    public void Una_segunda_galeria_sin_aviso_del_log_se_deduce_por_el_puntaje()
    {
        // Galería a las 0:12 (+35 y el aviso) y otra a las 0:18 sin aviso (seguía siendo el último renglón).
        var builder = Warm();
        var first = builder.Build(Score(12, 1051, "archery_range_built"));
        var quiet = builder.Build(Score(15, 1055));
        var second = builder.Build(Score(18, 1094));
        var after = builder.Build(Score(21, 1098));

        Assert.Equal(1, Count(first, "archery_range_built"));
        Assert.Equal(0, Count(quiet, "archery_range_built"));
        Assert.Equal(1, Count(second, "archery_range_built") + Count(after, "archery_range_built"));
    }

    [Fact]
    public void Una_suba_que_explica_el_log_no_agrega_edificio()
    {
        var builder = Warm();
        builder.Build(Score(12, 1051, "archery_range_built"));
        builder.Build(Score(15, 1055));
        var research = builder.Build(Score(18, 1089, "gold_mining_researched"));
        var after = builder.Build(Score(21, 1093));

        Assert.Equal(0, Count(research, "archery_range_built") + Count(after, "archery_range_built"));
    }

    [Fact]
    public void Una_suba_que_no_calza_con_el_ultimo_edificio_no_cuenta()
    {
        var builder = Warm();
        builder.Build(Score(12, 1051, "archery_range_built"));
        builder.Build(Score(15, 1055));
        var odd = builder.Build(Score(18, 1079));
        var after = builder.Build(Score(21, 1083));

        Assert.Equal(0, Count(odd, "archery_range_built") + Count(after, "archery_range_built"));
    }

    [Fact]
    public void Si_el_aviso_del_log_llega_una_lectura_despues_de_la_suba_no_es_otro_edificio()
    {
        var builder = Warm();
        builder.Build(Score(12, 1051));
        var logged = builder.Build(Score(15, 1055, "archery_range_built"));
        var after = builder.Build(Score(18, 1059));

        Assert.Equal(1, Count(logged, "archery_range_built"));
        Assert.Equal(0, Count(after, "archery_range_built"));
    }

    [Fact]
    public void Pasados_20_s_del_aviso_la_suba_ya_no_se_atribuye_al_edificio()
    {
        var builder = Warm();
        builder.Build(Score(12, 1051, "archery_range_built"));
        for (var t = 15; t <= 33; t += 3)
            builder.Build(Score(t, 1051 + (t - 12) / 3 * 4));
        var late = builder.Build(Score(36, 1051 + 8 * 4 + 35));
        var after = builder.Build(Score(39, 1051 + 9 * 4 + 35));

        Assert.Equal(0, Count(late, "archery_range_built") + Count(after, "archery_range_built"));
    }

    [Fact]
    public void Sin_saber_cual_es_el_propio_se_deduce_por_las_subas_que_coinciden_con_el_log()
    {
        // El renglón que sube 20 con cada "Lumber Camp Built" es el nuestro; el otro sigue solo con su economía.
        var builder = new SnapshotBuilder(TestData.GameData);
        var score = 1000;
        var t = 0;
        for (var i = 0; i < 6; i++)
        {
            for (var k = 0; k < 3; k++, t += 3)
                builder.Build(Score(t, score += 4));
            builder.Build(Score(t, score += 24, "lumber_camp_built"));
            t += 3;
        }
        Assert.Equal(1, builder.PlayerSlot);

        for (var k = 0; k < 4; k++, t += 3)
            builder.Build(Score(t, score += 4));
        builder.Build(Score(t, score += 39, "archery_range_built"));
        builder.Build(Score(t + 3, score += 4));
        var second = builder.Build(Score(t + 6, score += 39));
        var after = builder.Build(Score(t + 9, score += 4));

        Assert.Equal(1, Count(second, "archery_range_built") + Count(after, "archery_range_built"));
    }

    [Fact]
    public void Un_aviso_posterior_explica_su_propia_suba_y_no_la_del_edificio_repetido()
    {
        // Partida 20261101-020049: dos galerías a las 17:22 y 17:23 (un aviso) y la herrería a las 17:28. La suba de la
        // segunda galería no la explica la herrería, y la de la herrería no es otra herrería.
        var builder = Warm();
        builder.Build(Score(12, 1051, "archery_range_built"));
        var second = builder.Build(Score(15, 1094));
        var smith = builder.Build(Score(18, 1128, "blacksmith_built"));
        var after = builder.Build(Score(21, 1132));
        var later = builder.Build(Score(24, 1136));

        var all = new[] { second, smith, after, later };
        Assert.Equal(1, all.Sum(s => Count(s, "archery_range_built")));
        Assert.Equal(1, all.Sum(s => Count(s, "blacksmith_built")));
    }

    [Fact]
    public void Despues_del_clic_de_una_edad_el_rebote_del_puntaje_no_es_un_edificio()
    {
        // Partidas 20261101-020049 y 125353: monasterio hecho, clic en Imperial (−180) y unos 10 s después +38 sin aviso.
        var builder = Warm();
        builder.Build(Score(12, 1051, "monastery_built"));
        builder.Build(Score(15, 1055));
        builder.Build(Score(18, 879));
        var rebound = builder.Build(Score(21, 921));
        var after = builder.Build(Score(24, 925));

        Assert.Equal(0, Count(rebound, "monastery_built") + Count(after, "monastery_built"));
    }

    [Fact]
    public void Una_casa_repetida_la_delata_el_tope_y_no_el_puntaje()
    {
        var builder = Warm();
        builder.Build(Score(12, 1021, "house_built"));
        builder.Build(Score(15, 1025));
        var second = builder.Build(Score(18, 1034));
        var after = builder.Build(Score(21, 1038));

        Assert.Equal(0, Count(second, "house_built") + Count(after, "house_built"));
    }
}
