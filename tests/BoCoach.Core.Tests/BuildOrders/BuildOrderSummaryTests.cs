using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Tests.BuildOrders;

/// <summary>La app elige la BO con la lista de la web, que no trae los pasos: el resumen tiene que puntuar igual.</summary>
public class BuildOrderSummaryTests
{
    private static readonly CivStyles Styles = CivStyles.Load(Path.Combine(AppContext.BaseDirectory, "civ_styles.json"));

    private static readonly BuildOrder[] Civs = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "civs"), "*.json")
        .Select(BuildOrderSerializer.Load).ToArray();

    [Fact]
    public void Of_copia_lo_que_hace_falta_para_puntuar()
    {
        var scouts = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_22p.json")) with { Map = "arabia", Tags = ["feudal"] };

        var summary = BuildOrderSummary.Of(scouts);

        Assert.Equal(new[] { scouts.Id, scouts.Name, scouts.Civ, "arabia", "scouts" }, new[] { summary.Key, summary.Name, summary.Civ, summary.Map, summary.Style });
        Assert.Equal(["feudal"], summary.Tags);
    }

    public static TheoryData<string> Matches => ["propia", "otra", "arabia", "enfrente", "flanco"];

    [Theory]
    [MemberData(nameof(Matches))]
    public void Por_resumen_da_lo_mismo_que_por_BO(string kind)
    {
        Assert.True(Civs.Length > 100);
        foreach (var civ in Civs.Select(b => b.Civ).Distinct().Take(20))
        {
            var match = kind switch
            {
                "propia" => new MatchSetup(civ),
                "otra" => new MatchSetup(civ == "franks" ? "britons" : "franks"),
                "arabia" => new MatchSetup(civ, Map: "Arabia"),
                "enfrente" => new MatchSetup(civ, "britons", ["franks", "mongols"]),
                _ => new MatchSetup(civ, "huns", Position: TeamPosition.Flank),
            };
            foreach (var bo in Civs)
                Assert.Equal(BuildOrderSuggester.Score(bo, match, Styles), BuildOrderSuggester.Score(BuildOrderSummary.Of(bo), match, Styles));

            var byBuildOrder = BuildOrderSuggester.Suggest(Civs, match, Styles);
            var bySummary = BuildOrderSuggester.Suggest(Civs.Select(BuildOrderSummary.Of), match, Styles);
            Assert.Equal(byBuildOrder?.BuildOrder.Id, bySummary?.Summary.Key);
            Assert.Equal(byBuildOrder?.Reason, bySummary?.Reason);
        }
    }
}
