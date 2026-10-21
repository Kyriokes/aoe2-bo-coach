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
}
