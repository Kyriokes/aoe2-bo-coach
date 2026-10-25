using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.Data;

/// <summary>D10: las estadísticas reales de cruces de data/matchup_stats.json (las escribe tools/stats).</summary>
public class MatchupStatsTests
{
    private static readonly Dictionary<string, Func<int, int, int, int[]>> UnaPareja = new()
    {
        ["britons|franks"] = MatchupJson.Same(497, 469, 525, 93),
    };

    private static readonly Dictionary<string, Func<int, int, int, int[]>> UnaCiv = new()
    {
        ["franks"] = MatchupJson.Same(510, 500, 520, 900),
    };

    [Fact]
    public void Lee_la_cabecera_y_las_franjas()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(UnaPareja, UnaCiv, previousMixed: true));

        Assert.False(stats.IsEmpty);
        Assert.Equal("aoe2-guide", stats.SourceName);
        Assert.Equal("Update 185872", stats.PatchLabel);
        Assert.Equal(new DateOnly(2026, 6, 1), stats.From);
        Assert.Equal(new DateOnly(2026, 7, 3), stats.To);
        Assert.True(stats.PreviousMixed);
        Assert.Equal([850, 1050, 1300, 1600], stats.EloLimits);
        Assert.Equal([20, 30, 40, 55], stats.GameMinuteLimits);
        Assert.Equal(["arabia", "arena", "black_forest", "nomad"], stats.MapKeys);
    }

    [Fact]
    public void Franjas_con_los_bordes()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(UnaPareja, UnaCiv));

        Assert.Equal(0, stats.EloBand(null));
        Assert.Equal(0, stats.EloBand(0));
        Assert.Equal(1, stats.EloBand(849));
        Assert.Equal(2, stats.EloBand(850));
        Assert.Equal(5, stats.EloBand(1600));
        Assert.Equal(1, stats.DurationBand(new TimeSpan(0, 19, 59)));
        Assert.Equal(2, stats.DurationBand(TimeSpan.FromMinutes(20)));
        Assert.Equal(5, stats.DurationBand(TimeSpan.FromMinutes(55)));
        Assert.Equal(1, stats.MapIndex("arabia"));
        Assert.Equal(4, stats.MapIndex("nomad"));
        Assert.Equal(4, stats.MapIndex(" Nomad "));
        Assert.Equal(0, stats.MapIndex("megarandom"));
        Assert.Equal(0, stats.MapIndex(null));
        Assert.Equal("antes de los 20 min", stats.DurationLabel(1));
        Assert.Equal("de 30 a 40 min", stats.DurationLabel(3));
        Assert.Equal("después de los 55 min", stats.DurationLabel(5));
    }

    [Fact]
    public void Un_archivo_roto_o_que_falta_da_vacio()
    {
        Assert.True(MatchupStats.Load(Path.Combine(AppContext.BaseDirectory, "no_existe.json")).IsEmpty);
        Assert.True(MatchupStats.Parse("").IsEmpty);
        Assert.True(MatchupStats.Parse("{").IsEmpty);
        Assert.True(MatchupStats.Parse("""{ "one_vs_one": 3, "team": "x" }""").IsEmpty);
        Assert.True(MatchupStats.Parse("[1, 2]").IsEmpty);
        Assert.True(MatchupStats.Empty.IsEmpty);
    }

    private static Dictionary<string, Func<int, int, int, int[]>> TresCivs(Func<int, int, int, int[]> celda) => new()
    {
        ["britons|franks"] = celda,
        ["britons|mayan"] = celda,
        ["franks|mayan"] = celda,
    };

    private static Dictionary<string, Func<int, int, int, int[]>> TresCivsEnEquipos => new()
    {
        ["britons"] = MatchupJson.Same(480, 470, 490, 500),
        ["franks"] = MatchupJson.Same(510, 500, 520, 900),
        ["mayan"] = MatchupJson.Same(505, 495, 515, 700),
    };

    [Fact]
    public void Lee_cada_casillero_en_su_lugar()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(
            TresCivs((m, e, d) => MatchupJson.Cell(100 * m + 10 * e + d, 0, 999, 7)), TresCivsEnEquipos));

        var celda = stats.OneVsOne("britons", "franks", 2, 3, 4);
        Assert.NotNull(celda);
        Assert.Equal(0.234, celda.P, 3);
        Assert.Equal(7, celda.N);
        Assert.Equal(0.0, stats.OneVsOne("britons", "franks", 0, 0, 0)!.P, 3);
        Assert.Equal(0.455, stats.OneVsOne("BRITONS ", "franks", 4, 5, 5)!.P, 3);
        Assert.Equal(0.505, stats.Team("mayan", 3, 2, 1)!.P, 3);
        Assert.Equal(700, stats.Team("mayan", 3, 2, 1)!.N);
    }

    [Fact]
    public void Al_reves_invierte_la_chance_y_el_rango()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(TresCivs(MatchupJson.Same(600, 550, 650, 40)), TresCivsEnEquipos));

        var derecho = stats.OneVsOne("britons", "franks", 1, 0, 0)!;
        var reves = stats.OneVsOne("franks", "britons", 1, 0, 0)!;
        Assert.Equal(0.6, derecho.P, 3);
        Assert.Equal(0.4, reves.P, 3);
        Assert.Equal(0.35, reves.From, 3);
        Assert.Equal(0.45, reves.To, 3);
        Assert.Equal(40, reves.N);
        Assert.False(reves.Even);
    }

    [Fact]
    public void Espejo_y_civ_sin_datos_dan_null()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(TresCivs(MatchupJson.Same(600, 550, 650, 40)), TresCivsEnEquipos));

        Assert.Null(stats.OneVsOne("franks", "franks", 0, 0, 0));
        Assert.Null(stats.OneVsOne("franks", "spartans", 0, 0, 0));
        Assert.Null(stats.Team("spartans", 0, 0, 0));
        Assert.True(stats.HasCiv("franks"));
        Assert.False(stats.HasCiv("spartans"));
        Assert.Null(MatchupStats.Empty.OneVsOne("britons", "franks", 0, 0, 0));
    }

    [Fact]
    public void Con_una_civ_guarda_solo_lo_suyo()
    {
        var stats = MatchupStats.Parse(MatchupJson.Build(TresCivs(MatchupJson.Same(600, 550, 650, 40)), TresCivsEnEquipos),
            onlyCiv: "Franks");

        Assert.NotNull(stats.OneVsOne("britons", "franks", 0, 0, 0));
        Assert.NotNull(stats.OneVsOne("franks", "mayan", 0, 0, 0));
        Assert.Null(stats.OneVsOne("britons", "mayan", 0, 0, 0));
        Assert.NotNull(stats.Team("franks", 0, 0, 0));
        Assert.Null(stats.Team("britons", 0, 0, 0));
    }

    [Fact]
    public void Ignora_una_entrada_con_otro_largo()
    {
        var json = MatchupJson.Build(new Dictionary<string, Func<int, int, int, int[]>>
        {
            ["britons|franks"] = MatchupJson.Same(497, 469, 525, 93),
            ["britons|mayan"] = MatchupJson.Same(400, 350, 450, 10),
        }, TresCivsEnEquipos);
        // Le saca el último número a la entrada de britons|mayan: queda con 719.
        var stats = MatchupStats.Parse(json.Replace(",10]", "]", StringComparison.Ordinal));

        Assert.NotNull(stats.OneVsOne("britons", "franks", 0, 0, 0));
        Assert.Null(stats.OneVsOne("britons", "mayan", 0, 0, 0));
    }

    [Fact]
    public void Lee_el_archivo_real()
    {
        var stats = MatchupStats.Load(Path.Combine(AppContext.BaseDirectory, "matchup_stats.json"));

        var celda = stats.OneVsOne("britons", "franks", 0, 0, 0)!;
        Assert.Equal(0.497, celda.P, 3);
        Assert.Equal(0.469, celda.From, 3);
        Assert.Equal(0.525, celda.To, 3);
        Assert.Equal(93, celda.N);
        Assert.Equal(0.503, stats.OneVsOne("franks", "britons", 0, 0, 0)!.P, 3);
        Assert.Equal(0.523, stats.Team("georgians", 0, 0, 0)!.P, 3);
        var civs = new[] { "aztecs", "britons", "franks", "mayan", "mongols" };
        Assert.All(civs.SelectMany(a => civs.Where(b => b != a).Select(b => (a, b))),
            par => Assert.Equal(0, stats.OneVsOne(par.a, par.b, stats.MapIndex("nomad"), 0, 0)!.N));
    }

    /// <summary>britons|franks (y franks en equipos) con un casillero por franja de duración; el resto sin partidas.</summary>
    private static MatchupStats PorDuracion(params (int Band, int P, int From, int To, int N)[] franjas)
    {
        int[] Celda(int m, int e, int d) =>
            franjas.FirstOrDefault(f => f.Band == d) is { N: > 0 } f ? MatchupJson.Cell(f.P, f.From, f.To, f.N) : MatchupJson.Cell(480, 470, 490, 0);
        return MatchupStats.Parse(MatchupJson.Build(
            new Dictionary<string, Func<int, int, int, int[]>> { ["britons|franks"] = Celda },
            new Dictionary<string, Func<int, int, int, int[]>> { ["franks"] = Celda }));
    }

    [Fact]
    public void Tendencia_a_partidas_cortas()
    {
        var stats = PorDuracion((1, 540, 491, 587, 50), (5, 420, 380, 460, 40));

        var tendencia = stats.OneVsOneTrend("britons", "franks", 1, 0);
        Assert.NotNull(tendencia);
        Assert.Equal(1, tendencia.ShortBand);
        Assert.Equal(5, tendencia.LongBand);
        Assert.Equal(0.54, tendencia.Short.P, 3);
        Assert.Equal(0.42, tendencia.Long.P, 3);
        Assert.True(tendencia.FavorsShort);
        Assert.True(stats.TeamTrend("franks", 1, 0)!.FavorsShort);
    }

    [Fact]
    public void Sin_tendencia_si_los_rangos_se_tocan()
    {
        var stats = PorDuracion((1, 540, 491, 587, 50), (5, 450, 400, 495, 40));
        Assert.Null(stats.OneVsOneTrend("britons", "franks", 1, 0));
        Assert.Null(stats.TeamTrend("franks", 1, 0));
    }

    [Fact]
    public void Usa_las_franjas_con_partidas()
    {
        var stats = PorDuracion((2, 540, 491, 587, 50), (4, 420, 380, 460, 30));

        var tendencia = stats.OneVsOneTrend("britons", "franks", 1, 0)!;
        Assert.Equal(2, tendencia.ShortBand);
        Assert.Equal(4, tendencia.LongBand);
    }

    [Fact]
    public void Sin_partidas_no_hay_tendencia()
    {
        Assert.Null(PorDuracion().OneVsOneTrend("britons", "franks", 1, 0));
        Assert.Null(PorDuracion((3, 540, 491, 587, 50)).OneVsOneTrend("britons", "franks", 1, 0));
        Assert.Null(PorDuracion((1, 540, 491, 587, 50), (5, 420, 380, 460, 40)).OneVsOneTrend("britons", "mayan", 1, 0));
    }

    [Fact]
    public void Al_reves_la_tendencia_se_da_vuelta()
    {
        var tendencia = PorDuracion((1, 540, 491, 587, 50), (5, 420, 380, 460, 40)).OneVsOneTrend("franks", "britons", 1, 0)!;
        Assert.False(tendencia.FavorsShort);
        Assert.Equal(0.46, tendencia.Short.P, 3);
    }
}
