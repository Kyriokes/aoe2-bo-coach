using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Web;

namespace BoCoach.Core.Tests.Web;

/// <summary>Qué BO usa el coach con la lista de la web y lo guardado (parte 4).</summary>
public class BuildOrderPickerTests
{
    private static readonly CivStyles Styles = CivStyles.Load(Path.Combine(AppContext.BaseDirectory, "civ_styles.json"));

    private static BuildOrder Civ(string file, string code) =>
        BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "civs", file)) with { Id = code };

    private static readonly BuildOrder Huns = Civ("huns-mejorada.json", "hunshu");
    private static readonly BuildOrder Franks = Civ("franks-mejorada.json", "frnkss");

    private sealed class FakeSource(params BuildOrder[] web) : IBuildOrderSource
    {
        public List<BuildOrder> Stored { get; } = [];
        public bool Offline { get; set; }
        public List<string> Downloads { get; } = [];

        public WebBuildOrderList List { get; } = new("Ana", web.Select(bo =>
        {
            var s = BuildOrderSummary.Of(bo);
            return new WebBuildOrderEntry(bo.Id, s.Name, s.Civ, s.Map, s.Style, s.Tags, "Ana", "Ana", true, "public", DateTimeOffset.UnixEpoch, null, null);
        }).ToList());

        public BuildOrder? Saved(string code) => Stored.FirstOrDefault(b => b.Id == code);

        public IEnumerable<BuildOrder> AllSaved() => Stored;

        public Task<BuildOrder> GetAsync(string code, CancellationToken cancel = default)
        {
            Downloads.Add(code);
            if (Saved(code) is { } saved)
                return Task.FromResult(saved);
            if (Offline)
                throw new WebLibraryException("sin conexión");
            var bo = web.Single(b => b.Id == code);
            Stored.Add(bo);
            return Task.FromResult(bo);
        }
    }

    [Fact]
    public void Elige_la_de_mas_puntaje_de_la_lista()
    {
        var picker = new BuildOrderPicker(new FakeSource(Huns, Franks), Styles);

        Assert.Equal("hunshu", picker.Best(new MatchSetup("huns"), null)?.Summary.Key);
        Assert.Equal("frnkss", picker.Best(new MatchSetup("franks"), null)?.Summary.Key);
    }

    [Fact]
    public async Task La_baja_si_no_esta_guardada()
    {
        var source = new FakeSource(Huns, Franks);

        var bo = await new BuildOrderPicker(source, Styles).ForMatchAsync(new MatchSetup("franks"), null, default);

        Assert.Equal("frnkss", bo.Id);
        Assert.Equal(["frnkss"], source.Downloads);
    }

    [Fact]
    public async Task Sin_internet_usa_la_mejor_guardada_y_sin_nada_ninguna()
    {
        var source = new FakeSource(Huns, Franks) { Offline = true };
        source.Stored.Add(Huns);
        var picker = new BuildOrderPicker(source, Styles);

        // La mejor de la lista (francos) no se puede bajar: queda la guardada, aunque sea de otra civ que la de la
        // partida no se puede usar, así que sin ninguna que sirva va sin BO.
        Assert.True((await picker.ForMatchAsync(new MatchSetup("franks"), null, default)).IsNone);
        Assert.Equal("hunshu", (await picker.ForMatchAsync(new MatchSetup("huns"), null, default)).Id);

        source.Stored.Clear();
        Assert.True((await picker.ForMatchAsync(new MatchSetup("huns"), null, default)).IsNone);
    }

    [Fact]
    public void La_ultima_usada_solo_si_esta_guardada()
    {
        var source = new FakeSource(Huns);
        source.Stored.Add(Huns);
        var picker = new BuildOrderPicker(source, Styles);

        Assert.Equal("hunshu", picker.Current("hunshu")?.Id);
        Assert.Null(picker.Current("zzzzzz"));
        Assert.Null(picker.Current(null));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Cambia_de_BO_solo_en_la_carga_o_el_primer_minuto(double gameTime, bool allowed)
    {
        Assert.Equal(allowed, BuildOrderPicker.CanSwitchAt(gameTime));
    }
}
