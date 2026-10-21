using BoCoach.Core.BuildOrders;

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
    public void El_estilo_de_una_BO_sale_de_sus_pasos()
    {
        Assert.Equal("archers", BuildOrderSuggester.StyleOf(Archers));
        Assert.Equal("scouts", BuildOrderSuggester.StyleOf(Scouts));
        Assert.Equal("fast_castle", BuildOrderSuggester.StyleOf(FastCastle));
    }

    [Fact]
    public void Los_exploradores_aguila_son_el_estilo_de_exploradores()
    {
        // Las civs mesoamericanas no tienen establo: abren con águilas desde el cuartel.
        var eagles = BuildOrderSerializer.Parse(BuildOrderSerializer.Serialize(Scouts).Replace("\"scout_cavalry\"", "\"eagle_scout\""));

        Assert.Equal("scouts", BuildOrderSuggester.StyleOf(eagles));
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

        Assert.Equal(style, BuildOrderSuggester.StyleOf(suggestion!.BuildOrder));
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
}
