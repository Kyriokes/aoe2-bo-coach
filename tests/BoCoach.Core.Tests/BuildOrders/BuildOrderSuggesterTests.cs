using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Tests.Data;

namespace BoCoach.Core.Tests.BuildOrders;

/// <summary>D4: qué BO de la biblioteca sugerir con la partida de la pantalla de carga.</summary>
public class BuildOrderSuggesterTests
{
    private static BuildOrder Load(string file) => BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, file));

    private static readonly BuildOrder Archers = Load("archers_feudal.json");
    private static readonly BuildOrder Scouts = Load("scouts_22p.json");
    private static readonly BuildOrder FastCastle = Load("fast_castle_principiante.json");
    private static readonly BuildOrder[] Library = [Archers, Scouts, FastCastle];

    private static readonly CivStyles Styles = CivStyles.Load(Path.Combine(AppContext.BaseDirectory, "civ_styles.json"));

    [Fact]
    public void Entre_la_importada_y_la_mejorada_de_la_civ_sugiere_la_mejorada()
    {
        // D7: build_orders/civs tiene las dos de cada civ.
        var civs = Path.Combine(AppContext.BaseDirectory, "civs");
        var library = new[] { "huns.json", "huns-mejorada.json" }.Select(f => BuildOrderSerializer.Load(Path.Combine(civs, f))).Concat(Library);

        var suggestion = BuildOrderSuggester.Suggest(library, new MatchSetup("huns"), Styles);

        Assert.Equal("bog-huns-mejorada", suggestion?.BuildOrder.Id);
        Assert.Contains("de tu civ", suggestion!.Reason);
    }

    // D8: el campo map de la BO manda; sin él, vale la etiqueta como antes.
    [Fact]
    public void El_mapa_de_la_BO_suma_si_es_el_de_la_partida_y_resta_si_es_otro()
    {
        var forArabia = Scouts with { Id = "arabia", Map = "arabia", Tags = [] };
        var forArena = Scouts with { Id = "arena", Map = "arena", Tags = ["arabia"] };
        var tagged = Scouts with { Id = "tag", Map = null, Tags = ["arabia"] };
        var plain = Scouts with { Id = "plain", Map = null, Tags = [] };

        double Score(BuildOrder bo) => BuildOrderSuggester.Score(bo, new MatchSetup("franks", Map: "Arabia"), Styles);

        Assert.Equal(Score(plain) + 2, Score(forArabia));
        Assert.Equal(Score(plain) + 2, Score(tagged));
        Assert.Equal(Score(plain) - 2, Score(forArena));
        Assert.Contains("para Arabia", BuildOrderSuggester.Suggest([forArabia, forArena], new MatchSetup("franks", Map: "Arabia"), Styles)!.Reason);
    }

    [Fact]
    public void El_estilo_de_una_BO_sale_de_sus_pasos()
    {
        Assert.Equal("archers", BuildOrderStyle.Of(Archers));
        Assert.Equal("scouts", BuildOrderStyle.Of(Scouts));
        Assert.Equal("fast_castle", BuildOrderStyle.Of(FastCastle));
    }

    [Fact]
    public void Los_exploradores_aguila_son_el_estilo_de_exploradores()
    {
        // Las civs mesoamericanas no tienen establo: abren con águilas desde el cuartel.
        var eagles = BuildOrderSerializer.Parse(BuildOrderSerializer.Serialize(Scouts).Replace("\"scout_cavalry\"", "\"eagle_scout\""));

        Assert.Equal("scouts", BuildOrderStyle.Of(eagles));
    }

    [Fact]
    public void La_tabla_tiene_todas_las_civs_con_estilos_conocidos()
    {
        Assert.All(TestData.GameData.Civs.Keys, civ =>
        {
            var styles = Styles.Of(civ);
            Assert.NotEmpty(styles);
            Assert.All(styles, s => Assert.Contains(s, new[] { "archers", "scouts", "maa", "fast_castle" }));
        });
    }

    [Theory]
    [InlineData("britons", "archers")]
    [InlineData("franks", "scouts")]
    [InlineData("teutons", "fast_castle")]
    public void Sugiere_el_estilo_tipico_de_la_civ(string civ, string style)
    {
        var suggestion = BuildOrderSuggester.Suggest(Library, civ, [], null, Styles);

        Assert.Equal(style, BuildOrderStyle.Of(suggestion!.BuildOrder));
        Assert.Contains("típico de tu civ", suggestion.Reason);
    }

    [Fact]
    public void Una_BO_de_la_civ_gana_y_una_de_otra_civ_no_se_sugiere()
    {
        var mine = Archers with { Id = "mia", Name = "Mis mayas", Civ = "mayan" };
        var other = Scouts with { Id = "otra", Civ = "franks" };

        Assert.Same(mine, BuildOrderSuggester.Suggest([.. Library, mine, other], "mayan", [], null, Styles)!.BuildOrder);
        Assert.NotSame(other, BuildOrderSuggester.Suggest([other, Archers], "britons", [], null, Styles)!.BuildOrder);
    }

    [Fact]
    public void A_igual_estilo_desempata_el_mapa_y_el_rival()
    {
        // Hindustanos: exploradores o arqueros. Contra una civ de arqueros, mejor exploradores.
        var suggestion = BuildOrderSuggester.Suggest(Library, "hindustanis", ["britons"], "Arabia", Styles);

        Assert.Same(Scouts, suggestion!.BuildOrder);
        Assert.Contains("para Arabia", suggestion.Reason);
        Assert.Contains("contra arqueros", suggestion.Reason);
    }

    [Fact]
    public void Sin_nada_que_encaje_no_sugiere()
    {
        Assert.Null(BuildOrderSuggester.Suggest([Archers with { Civ = "franks" }], "britons", [], null, Styles));
    }

    // Segunda parte de D4 (diseño del 2026-10-21): el rival de enfrente pesa más, la posición suma y hay un consejo.

    private static readonly CivStyles Small = new(
        new Dictionary<string, List<string>>
        {
            ["yo"] = ["scouts", "archers"],
            ["arqueros"] = ["archers"],
            ["exploradores"] = ["scouts"],
        },
        advice: new Dictionary<string, string> { ["archers"] = "hostigadores" },
        civAdvice: new Dictionary<string, string> { ["exploradores"] = "lanceros temprano" },
        positions: new Dictionary<string, List<string>> { ["flank"] = ["archers"], ["pocket"] = ["scouts"] });

    private static readonly BuildOrder[] Two = [Archers, Scouts];

    [Fact]
    public void El_rival_de_enfrente_pesa_mas_que_los_demas()
    {
        // Exploradores le ganan a arqueros y arqueros a exploradores: decide el de enfrente.
        var vsArchers = BuildOrderSuggester.Suggest(Two, new MatchSetup("yo", "arqueros", ["exploradores"]), Small)!;
        Assert.Same(Scouts, vsArchers.BuildOrder);
        Assert.Contains("contra arqueros", vsArchers.Reason);

        var vsScouts = BuildOrderSuggester.Suggest(Two, new MatchSetup("yo", "exploradores", ["arqueros"]), Small)!;
        Assert.Same(Archers, vsScouts.BuildOrder);
        Assert.Contains("contra exploradores", vsScouts.Reason);
    }

    [Fact]
    public void Los_demas_rivales_suman_menos()
    {
        var suggestion = BuildOrderSuggester.Suggest(Two, new MatchSetup("yo", null, ["arqueros", "arqueros"]), Small)!;

        Assert.Same(Scouts, suggestion.BuildOrder);
        Assert.Contains("le gana a 2 rivales más", suggestion.Reason);
    }

    [Fact]
    public void En_el_flanco_arqueros_y_en_el_bolsillo_exploradores()
    {
        // Sin civ conocida, solo decide la posición.
        var flank = BuildOrderSuggester.Suggest(Two, new MatchSetup("nadie", Position: TeamPosition.Flank), Small)!;
        var pocket = BuildOrderSuggester.Suggest(Two, new MatchSetup("nadie", Position: TeamPosition.Pocket), Small)!;

        Assert.Same(Archers, flank.BuildOrder);
        Assert.Contains("para el flanco", flank.Reason);
        Assert.Same(Scouts, pocket.BuildOrder);
        Assert.Contains("para el bolsillo", pocket.Reason);
        Assert.Null(BuildOrderSuggester.Suggest(Two, new MatchSetup("nadie"), Small));
    }

    [Fact]
    public void El_consejo_de_la_civ_manda_sobre_el_de_su_estilo()
    {
        Assert.Equal("lanceros temprano", BuildOrderSuggester.Advice(new MatchSetup("yo", "exploradores"), Small));
        Assert.Equal("hostigadores", BuildOrderSuggester.Advice(new MatchSetup("yo", "arqueros"), Small));
        Assert.Null(BuildOrderSuggester.Advice(new MatchSetup("yo", "nadie"), Small));
        Assert.Null(BuildOrderSuggester.Advice(new MatchSetup("yo"), Small));
    }

    [Fact]
    public void El_archivo_viejo_con_solo_las_civs_se_sigue_leyendo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"civ_styles-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "// viejo\n{ \"britons\": [\"archers\"] }\n");
        try
        {
            var old = CivStyles.Load(path);
            Assert.Equal(["archers"], old.Of("britons"));
            Assert.True(old.Beats("scouts", "archers")); // los counters de siempre
            Assert.Null(old.AdviceAgainst("britons"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void La_tabla_solo_nombra_civs_y_estilos_que_existen()
    {
        string[] known = ["archers", "scouts", "maa", "fast_castle"];
        Assert.All(Styles.ForPosition(TeamPosition.Flank).Concat(Styles.ForPosition(TeamPosition.Pocket)), s => Assert.Contains(s, known));
        Assert.NotEmpty(Styles.ForPosition(TeamPosition.Pocket));
        // Cada civ tiene un consejo (el de su civ o el de su estilo típico).
        Assert.All(TestData.GameData.Civs.Keys, civ => Assert.NotNull(Styles.AdviceAgainst(civ)));
        Assert.Contains("lanceros", Styles.AdviceAgainst("mongols"));
    }

    [Fact]
    public void Un_valor_de_otro_tipo_se_ignora_y_un_error_de_sintaxis_dice_el_archivo()
    {
        // El usuario revisa civ_styles.json a mano: un número donde va un texto no tiene que tirar abajo al coach,
        // y un error de sintaxis tiene que decir en qué archivo está.
        var path = Path.Combine(Path.GetTempPath(), $"civ_styles-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"civs\": { \"britons\": [\"archers\", 7] }, \"advice\": { \"archers\": 3, \"scouts\": \"lanceros\" } }");
            var styles = CivStyles.Load(path);
            Assert.Equal(["archers"], styles.Of("britons"));
            Assert.Null(styles.AdviceAgainst("britons"));

            File.WriteAllText(path, "{ \"civs\": [ }");
            var error = Assert.Throws<InvalidDataException>(() => CivStyles.Load(path));
            Assert.Contains(Path.GetFileName(path), error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Los_puntos_de_una_BO_son_los_de_la_sugerencia_y_otra_civ_vale_cero()
    {
        // D6: con el coach arrancado solo, se cambia a la sugerida solo si puntúa más que la que corre.
        var match = new MatchSetup("yo", "exploradores", Position: TeamPosition.Flank);
        var suggestion = BuildOrderSuggester.Suggest(Two, match, Small)!;

        Assert.Same(Archers, suggestion.BuildOrder);
        Assert.Equal(suggestion.Score, BuildOrderSuggester.Score(Archers, match, Small));
        Assert.True(suggestion.Score > BuildOrderSuggester.Score(Scouts, match, Small));
        Assert.Equal(0, BuildOrderSuggester.Score(Archers with { Civ = "otra" }, match, Small));
    }

    [Fact]
    public void Ventanas_por_defecto_y_del_archivo()
    {
        Assert.Equal([1, 2], CivStyles.Empty.Window("archers"));
        Assert.Equal([1, 2], CivStyles.Empty.Window("maa"));
        Assert.Equal([3, 4], CivStyles.Empty.Window("fast_castle"));
        Assert.Empty(CivStyles.Empty.Window("dragones"));

        var path = Path.Combine(Path.GetTempPath(), $"civ_styles_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "civs": { "franks": ["scouts"] }, "windows": { "archers": [2, 9, "x"], "scouts": "mal" } }""");
        try
        {
            var styles = CivStyles.Load(path);
            Assert.Equal([2], styles.Window("archers"));
            Assert.Equal([1, 2], styles.Window("scouts"));
            Assert.Equal([3, 4], styles.Window("fast_castle"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void El_archivo_del_repo_tiene_las_ventanas()
    {
        Assert.Equal([1, 2], Styles.Window("scouts"));
        Assert.Equal([1, 2], Styles.Window("archers"));
        Assert.Equal([3, 4], Styles.Window("fast_castle"));
    }

    /// <summary>
    /// britons|franks con, para los britanos, franja 1 = 44 %, 2 = 45 %, 3 y 4 = 50 %, 5 = <paramref name="larga"/> y
    /// general 50 %: a los francos les va mejor corto. Igual en todos los mapas y franjas de Elo.
    /// </summary>
    private static MatchupStats CortoParaFrancos(int[]? larga = null)
    {
        int[] Celda(int m, int e, int d) => d switch
        {
            0 => MatchupJson.Cell(500, 480, 520, 600),
            1 => MatchupJson.Cell(440, 400, 480, 60),
            2 => MatchupJson.Cell(450, 410, 490, 60),
            5 => larga ?? MatchupJson.Cell(560, 520, 600, 60),
            _ => MatchupJson.Cell(500, 460, 540, 60),
        };
        return MatchupStats.Parse(MatchupJson.Build(
            new Dictionary<string, Func<int, int, int, int[]>> { ["britons|franks"] = Celda },
            new Dictionary<string, Func<int, int, int, int[]>> { ["franks"] = Celda }));
    }

    private static readonly MatchSetup FrancosContraBritanos = new("franks", "britons", Elo: 1100);

    [Fact]
    public void La_ventana_del_cruce_suma_a_la_BO_que_la_aprovecha()
    {
        var stats = CortoParaFrancos();

        var exploradores = BuildOrderSuggester.Score(Scouts, FrancosContraBritanos, CivStyles.Empty, stats)
                           - BuildOrderSuggester.Score(Scouts, FrancosContraBritanos, CivStyles.Empty);
        var castillo = BuildOrderSuggester.Score(FastCastle, FrancosContraBritanos, CivStyles.Empty, stats)
                       - BuildOrderSuggester.Score(FastCastle, FrancosContraBritanos, CivStyles.Empty);
        Assert.Equal(1.1, exploradores, 2);
        Assert.Equal(0, castillo, 2);

        var sugerida = BuildOrderSuggester.Suggest([Scouts, FastCastle], FrancosContraBritanos, CivStyles.Empty, stats);
        Assert.NotNull(sugerida);
        Assert.Same(Scouts, sugerida.BuildOrder);
        Assert.Contains("el cruce se gana más en partidas cortas", sugerida.Reason);
    }

    [Fact]
    public void Sin_tendencia_no_suma()
    {
        // La franja larga (para los francos, 52 % de 48 a 56) toca a la corta (56 % de 52 a 60): no se distingue del ruido.
        var stats = CortoParaFrancos(larga: MatchupJson.Cell(480, 440, 520, 60));
        Assert.Equal(BuildOrderSuggester.Score(Scouts, FrancosContraBritanos, CivStyles.Empty),
            BuildOrderSuggester.Score(Scouts, FrancosContraBritanos, CivStyles.Empty, stats), 6);
    }

    [Fact]
    public void Sin_datos_de_cruces_la_sugerencia_no_cambia()
    {
        var sinDatos = BuildOrderSuggester.Suggest(Library, new MatchSetup("britons", "franks", Elo: 1100), Styles);
        var vacio = BuildOrderSuggester.Suggest(Library, new MatchSetup("britons", "franks", Elo: 1100), Styles, MatchupStats.Empty);
        Assert.Equal(sinDatos?.BuildOrder, vacio?.BuildOrder);
        Assert.Equal(sinDatos?.Reason, vacio?.Reason);
        Assert.Equal(sinDatos?.Score, vacio?.Score);
    }
}
