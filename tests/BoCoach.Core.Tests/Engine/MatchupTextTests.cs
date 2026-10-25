using BoCoach.Core.Data;
using BoCoach.Core.Engine;
using BoCoach.Core.Tests.Data;

namespace BoCoach.Core.Tests.Engine;

/// <summary>D10: los textos de las estadísticas de cruces (pantalla de carga, informe y sugerencia).</summary>
public class MatchupTextTests
{
    private static readonly Dictionary<string, string> Nombres = new()
    {
        ["britons"] = "britanos", ["franks"] = "francos", ["mayan"] = "mayas", ["spartans"] = "espartanos",
    };

    private static readonly Dictionary<string, string> Mapas = new()
    {
        ["arabia"] = "Arabia", ["arena"] = "Arena", ["black_forest"] = "Bosque Negro", ["nomad"] = "Nómada",
    };

    private static string Civ(string clave) => Nombres[clave];

    private static string? Mapa(string clave) => Mapas.GetValueOrDefault(clave);

    /// <summary>
    /// britons|franks y franks en equipos: en cada mapa, el casillero general (duración "todas") y por franja de
    /// duración los que se pasan; las demás franjas sin partidas.
    /// </summary>
    private static MatchupStats Datos(int[] general, Dictionary<int, int[]>? franjas = null, int[]? nomad = null,
        bool previousMixed = false)
    {
        int[] Celda(int m, int e, int d)
        {
            if (m == 4 && nomad is not null)
                return d == 0 ? nomad : MatchupJson.Cell(nomad[0], nomad[1], nomad[2], 0);
            if (d == 0)
                return general;
            return franjas is not null && franjas.TryGetValue(d, out var franja) ? franja : MatchupJson.Cell(480, 470, 490, 0);
        }
        var celdas = new Dictionary<string, Func<int, int, int, int[]>> { ["britons|franks"] = Celda };
        var equipos = new Dictionary<string, Func<int, int, int, int[]>> { ["franks"] = Celda, ["britons"] = Celda };
        return MatchupStats.Parse(MatchupJson.Build(celdas, equipos, previousMixed));
    }

    private static readonly Dictionary<int, int[]> CortasMejor = new()
    {
        [1] = MatchupJson.Cell(540, 491, 587, 50), [5] = MatchupJson.Cell(420, 380, 460, 40),
    };

    private static readonly MatchupQuery BritanosEnArabia = new("britons", "franks", "arabia", 1050, Team: false);

    [Fact]
    public void Uno_contra_uno_con_tendencia()
    {
        var texto = MatchupText.Loading(Datos(MatchupJson.Cell(470, 430, 490, 80), CortasMejor), BritanosEnArabia, Civ, Mapa);
        Assert.Equal("Britanos contra francos en Arabia (Elo 1050): ganás 47 % (43 a 49). "
                     + "Mejor si dura poco: 54 % antes de los 20 min, 42 % después de los 55 min.", texto);
    }

    [Fact]
    public void Al_reves_la_tendencia_es_la_del_rival()
    {
        var texto = MatchupText.Loading(Datos(MatchupJson.Cell(470, 430, 490, 80), CortasMejor),
            BritanosEnArabia with { Civ = "franks", Rival = "britons" }, Civ, Mapa);
        Assert.Equal("Francos contra britanos en Arabia (Elo 1050): ganás 53 % (51 a 57). "
                     + "Mejor si dura mucho: 46 % antes de los 20 min, 58 % después de los 55 min.", texto);
    }

    [Fact]
    public void Parejo_y_sin_tendencia()
    {
        var franjas = new Dictionary<int, int[]>
        {
            [1] = MatchupJson.Cell(500, 450, 550, 50), [5] = MatchupJson.Cell(480, 430, 530, 40),
        };
        var texto = MatchupText.Loading(Datos(MatchupJson.Cell(490, 460, 520, 80), franjas), BritanosEnArabia, Civ, Mapa);
        Assert.Equal("Britanos contra francos en Arabia (Elo 1050): parejo, ganás 49 % (46 a 52). "
                     + "Parejo en cualquier duración.", texto);
    }

    [Fact]
    public void En_todos_los_mapas()
    {
        var texto = MatchupText.Loading(Datos(MatchupJson.Cell(470, 430, 490, 80)),
            BritanosEnArabia with { Map = "megarandom" }, Civ, Mapa);
        Assert.Equal("Britanos contra francos en todos los mapas (Elo 1050): ganás 47 % (43 a 49).", texto);
    }

    [Fact]
    public void Sin_tu_Elo_lo_aclara()
    {
        var texto = MatchupText.Loading(Datos(MatchupJson.Cell(470, 430, 490, 80)), BritanosEnArabia with { Elo = null }, Civ, Mapa);
        Assert.Equal("Britanos contra francos en Arabia (sin tu Elo): ganás 47 % (43 a 49).", texto);
    }

    [Fact]
    public void Sin_partidas_en_el_mapa_dice_que_es_estimado()
    {
        var stats = Datos(MatchupJson.Cell(470, 430, 490, 80), CortasMejor, nomad: MatchupJson.Cell(470, 400, 540, 0));
        var texto = MatchupText.Loading(stats, BritanosEnArabia with { Map = "nomad" }, Civ, Mapa);
        Assert.Equal("Britanos contra francos en Nómada (Elo 1050): parejo, ganás 47 % (40 a 54). "
                     + "Estimado: no hay partidas de este cruce en Nómada.", texto);
    }

    [Fact]
    public void Por_equipos()
    {
        var franjas = new Dictionary<int, int[]>
        {
            [1] = MatchupJson.Cell(500, 480, 520, 300), [5] = MatchupJson.Cell(560, 540, 580, 200),
        };
        var texto = MatchupText.Loading(Datos(MatchupJson.Cell(520, 506, 540, 900), franjas),
            new MatchupQuery("franks", null, "nomad", 1200, Team: true), Civ, Mapa);
        Assert.Equal("Francos en Nómada por equipos (Elo 1200): 52 % (51 a 54). Mejor en partidas largas.", texto);
    }

    [Fact]
    public void Civ_sin_datos()
    {
        var stats = Datos(MatchupJson.Cell(470, 430, 490, 80));
        Assert.Equal("Sin datos de partidas clasificatorias de espartanos.",
            MatchupText.Loading(stats, BritanosEnArabia with { Civ = "spartans" }, Civ, Mapa));
        Assert.Equal("Sin datos de partidas clasificatorias de espartanos.",
            MatchupText.Loading(stats, BritanosEnArabia with { Rival = "spartans" }, Civ, Mapa));
        Assert.Equal("Sin datos de partidas clasificatorias de espartanos.",
            MatchupText.Loading(stats, new MatchupQuery("spartans", null, "arabia", 1200, Team: true), Civ, Mapa));
    }

    [Fact]
    public void Sin_rival_o_sin_datos_no_hay_renglon()
    {
        Assert.Null(MatchupText.Loading(Datos(MatchupJson.Cell(470, 430, 490, 80)), BritanosEnArabia with { Rival = null }, Civ, Mapa));
        Assert.Null(MatchupText.Loading(MatchupStats.Empty, BritanosEnArabia, Civ, Mapa));
    }

    [Fact]
    public void Con_el_parche_anterior()
    {
        var texto = MatchupText.Loading(Datos(MatchupJson.Cell(470, 430, 490, 80), previousMixed: true), BritanosEnArabia, Civ, Mapa);
        Assert.Equal("Britanos contra francos en Arabia (Elo 1050): ganás 47 % (43 a 49). (Con datos del parche anterior.)", texto);
    }

    private static readonly Dictionary<int, int[]> ConFranjaDelMedio = new()
    {
        [1] = MatchupJson.Cell(540, 491, 587, 50), [3] = MatchupJson.Cell(450, 410, 490, 30), [5] = MatchupJson.Cell(560, 540, 580, 200),
    };

    [Fact]
    public void Informe_uno_contra_uno()
    {
        var stats = Datos(MatchupJson.Cell(470, 430, 490, 80), ConFranjaDelMedio, previousMixed: true);

        Assert.Equal(["Britanos contra francos en Arabia (Elo 1050): ganás 47 % (43 a 49).",
                      "Duró 34 min: a esa duración, en este cruce ganás el 45 % (41 a 49)."],
            MatchupText.Report(stats, BritanosEnArabia, new TimeSpan(0, 34, 50), Civ, Mapa));
        Assert.Equal("Duró 25 min: a esa duración, en este cruce ganás el 48 % (47 a 49). Estimado: pocas partidas así.",
            MatchupText.Report(stats, BritanosEnArabia, TimeSpan.FromMinutes(25), Civ, Mapa)[1]);
    }

    [Fact]
    public void Informe_por_equipos()
    {
        var stats = Datos(MatchupJson.Cell(520, 506, 540, 900), ConFranjaDelMedio);

        Assert.Equal(["Francos en Nómada por equipos (Elo 1200): 52 % (51 a 54).",
                      "Duró 60 min: a esa duración, los equipos con francos ganan el 56 % (54 a 58)."],
            MatchupText.Report(stats, new MatchupQuery("franks", null, "nomad", 1200, Team: true), TimeSpan.FromMinutes(60), Civ, Mapa));
    }

    [Fact]
    public void Informe_sin_datos_vacio()
    {
        var stats = Datos(MatchupJson.Cell(470, 430, 490, 80));
        Assert.Empty(MatchupText.Report(stats, BritanosEnArabia with { Civ = "spartans" }, TimeSpan.FromMinutes(30), Civ, Mapa));
        Assert.Empty(MatchupText.Report(stats, BritanosEnArabia with { Rival = null }, TimeSpan.FromMinutes(30), Civ, Mapa));
        Assert.Empty(MatchupText.Report(MatchupStats.Empty, BritanosEnArabia, TimeSpan.FromMinutes(30), Civ, Mapa));
    }

    [Fact]
    public void Porque_de_la_sugerencia()
    {
        var corta = new MatchupCell(0.54, 0.49, 0.59, 50);
        var larga = new MatchupCell(0.42, 0.38, 0.46, 40);
        Assert.Equal("el cruce se gana más en partidas cortas", MatchupText.DurationReason(new DurationTrend(1, corta, 5, larga)));
        Assert.Equal("el cruce se gana más en partidas largas", MatchupText.DurationReason(new DurationTrend(1, larga, 5, corta)));
    }

    [Fact]
    public void Con_solo_tu_civ_cargada_y_sin_datos_lo_dice()
    {
        // La app carga solo lo de tu civ: si no tiene partidas clasificatorias, el archivo igual se leyó.
        var json = MatchupJson.Build(
            new Dictionary<string, Func<int, int, int, int[]>> { ["britons|franks"] = MatchupJson.Same(470, 430, 490, 80) },
            new Dictionary<string, Func<int, int, int, int[]>> { ["franks"] = MatchupJson.Same(510, 500, 520, 900) });
        var stats = MatchupStats.Parse(json, onlyCiv: "spartans");

        Assert.False(stats.IsEmpty);
        Assert.False(stats.HasCiv("spartans"));
        Assert.Equal("Sin datos de partidas clasificatorias de espartanos.",
            MatchupText.Loading(stats, BritanosEnArabia with { Civ = "spartans" }, Civ, Mapa));
        Assert.Equal("Sin datos de partidas clasificatorias de espartanos.",
            MatchupText.Loading(stats, new MatchupQuery("spartans", null, "arabia", 1200, Team: true), Civ, Mapa));
    }

    [Fact]
    public void Sin_partidas_con_tu_Elo_lo_dice_asi()
    {
        // Con 1600 o más no hay partidas de este cruce en Arabia, pero sí con otros Elo: no es que no haya en el mapa.
        int[] Celda(int m, int e, int d) => MatchupJson.Cell(470, 430, 490, e == 5 || d > 0 ? 0 : 80);
        var stats = MatchupStats.Parse(MatchupJson.Build(
            new Dictionary<string, Func<int, int, int, int[]>> { ["britons|franks"] = Celda },
            new Dictionary<string, Func<int, int, int, int[]>> { ["franks"] = Celda }));

        Assert.Equal("Britanos contra francos en Arabia (Elo 1700): ganás 47 % (43 a 49). "
                     + "Estimado: no hay partidas de este cruce en Arabia con tu Elo.",
            MatchupText.Loading(stats, BritanosEnArabia with { Elo = 1700 }, Civ, Mapa));
        Assert.Equal("Britanos contra francos en todos los mapas (Elo 1700): ganás 47 % (43 a 49). "
                     + "Estimado: no hay partidas de este cruce con tu Elo.",
            MatchupText.Loading(stats, BritanosEnArabia with { Elo = 1700, Map = null }, Civ, Mapa));
        Assert.Equal("Britanos contra francos en Arabia (Elo 1050): ganás 47 % (43 a 49).",
            MatchupText.Loading(stats, BritanosEnArabia, Civ, Mapa));
    }
}
