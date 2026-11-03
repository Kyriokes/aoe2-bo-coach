using System.Text.RegularExpressions;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests;

public partial class LangTests
{
    /// <summary>Lo que delata un texto que quedó en castellano: tildes, signos de apertura y palabras comunes del overlay.</summary>
    [GeneratedRegex(@"[áéíóúñ¿¡]|\b(de|los|las|aldeanos?|madera|comida|oro|piedra|hacé|faltan|curso|paso|parado|casa|lugares|ahora|después|edad|terminad[oa]|objetivo)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Spanish();

    [Fact]
    public void Sin_elegir_nada_es_el_castellano()
    {
        Assert.Equal(Lang.Spanish, Lang.Current);
        Assert.Equal("aldeano", StepText.Name("villager"));
    }

    [Fact]
    public async Task Use_vale_para_este_flujo_y_se_deshace()
    {
        using (Lang.Use(Lang.English))
        {
            Assert.Equal("villager", StepText.Name("villager"));
            await Task.Yield();
            Assert.Equal("villager", StepText.Name("villager"));
            await Task.Run(() => Assert.True(Lang.IsEnglish));
        }
        Assert.Equal(Lang.Spanish, Lang.Current);
    }

    [Fact]
    public void Un_idioma_que_no_se_conoce_es_el_castellano()
    {
        using (Lang.Use("xx"))
            Assert.Equal(Lang.Spanish, Lang.Current);
    }

    /// <summary>Una partida grabada entera en inglés: ni un renglón del overlay ni del informe en castellano.</summary>
    [Fact]
    public void Una_partida_entera_en_ingles_no_muestra_nada_en_castellano()
    {
        using var english = Lang.Use(Lang.English);
        var bo = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"));
        // Los pasos y las notas de la BO los escribe su autor: acá solo se mira lo que arma el coach.
        var builder = new SnapshotBuilder(TestData.GameData);
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        var seen = new HashSet<string>();
        var readings = Path.Combine(AppContext.BaseDirectory, "fixtures", "scouts-20261015-214512.csv");
        foreach (var logged in ScreenReadingLog.Load(readings))
        {
            if (builder.Build(logged.Reading) is not { } snapshot)
                continue;
            var lines = CoachText.Lines(coach.Update(snapshot), TestData.GameData);
            seen.UnionWith(new[] { lines.Current, lines.Status, lines.Next, lines.Deviation, lines.Suggestion, lines.Alert, lines.Idle, lines.Distribution, lines.DistributionNow, lines.Critical }.OfType<string>());
            seen.UnionWith(lines.Running);
            seen.UnionWith(lines.Goals);
            seen.UnionWith(lines.Lanes.Select(l => $"{l.Label} {l.Text}"));
            seen.UnionWith(lines.Card.Select(c => c.Text));
        }
        var report = coach.Report();
        seen.UnionWith(GameReportText.Summary(report));
        seen.Add(Regex.Replace(GameReportText.Html(report, DateTime.UnixEpoch), "<style>.*?</style>|<[^>]+>", " ", RegexOptions.Singleline));

        Assert.True(seen.Count > 50, $"Se vieron pocos textos: {seen.Count}");
        var spanish = seen.Where(line => Spanish().IsMatch(line.Replace(bo.Name, ""))).ToList();
        Assert.Empty(spanish);
    }

    [Fact]
    public void Los_avisos_del_validador_salen_en_el_idioma_pedido()
    {
        var bo = new BuildOrder { Name = "", Steps = [] };
        var validator = new BuildOrderValidator(TestData.GameData, TestData.Events);
        Assert.Contains(validator.Validate(bo).Errors, e => e.Message == "La build order no tiene nombre.");
        using (Lang.Use(Lang.English))
            Assert.Contains(validator.Validate(bo).Errors, e => e.Message == "The build order has no name.");
    }
}
