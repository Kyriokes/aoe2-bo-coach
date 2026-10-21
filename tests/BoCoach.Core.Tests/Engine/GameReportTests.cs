using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.Engine;

/// <summary>D5: el informe de después de la partida.</summary>
public class GameReportTests
{
    private static readonly BuildOrder Archers = BuildOrderSerializer.Load(Path.Combine(TestData.BuildOrdersDir, "archers_feudal.json"));

    private static GameSnapshot At(double time, ResourceAmounts? stock = null, int population = 10, int cap = 15, int idle = 0) =>
        new(time, stock ?? new ResourceAmounts(100, 100, 100, 100), population, cap) { Villagers = 10, IdleVillagers = idle };

    [Fact]
    public void Junta_inactivos_trabas_por_poblacion_y_recursos_acumulados()
    {
        var coach = new Coach(Archers, TestData.GameData, TestData.Events);
        for (var t = 100; t <= 300; t++)
        {
            var stock = t is >= 150 and < 240 ? new ResourceAmounts(650, 100, 100, 100) : (ResourceAmounts?)null;
            coach.Update(At(t, stock, population: t is >= 200 and < 210 ? 15 : 10, idle: t is >= 120 and < 140 ? 2 : 0));
        }
        var report = coach.Report();

        Assert.Equal(200, report.Length, 0);
        Assert.Equal(20, report.IdleVillagersTime, 0);
        Assert.Equal((10, 1), ((int)Math.Round(report.HousedTime), report.HousedTimes));
        var floating = Assert.Single(report.Floating);
        Assert.Equal((Resource.Food, 150, 90, 650), (floating.Resource, floating.Start, floating.Duration, floating.Peak));
    }

    [Fact]
    public void Cada_paso_dice_cuando_lo_pedia_la_BO_y_cuando_se_hizo()
    {
        // Tope 5 (el del arranque): con 15, el coach deduce que las 2 casas del primer paso ya están.
        var coach = new Coach(Archers, TestData.GameData, TestData.Events);
        coach.Update(At(10, population: 4, cap: 5));
        coach.Update(At(90, population: 4, cap: 5));
        coach.Advance();
        coach.Update(At(91, population: 4, cap: 5));
        var report = coach.Report();

        var first = report.Steps[0];
        Assert.Equal(1, first.Number);
        Assert.Equal(0, first.Planned);
        Assert.Equal(90, first.Done);
        Assert.Equal(90, first.Delay);
        Assert.Null(report.Steps[^1].Done); // Castillos, sin hacer
        Assert.Contains(report.AgeClicks, c => c.Age == "Feudal" && c.Click is null && c.Planned > 0);
    }

    [Fact]
    public void El_resumen_y_la_pagina_dicen_lo_principal()
    {
        var report = new GameReport("Arqueros", 1200,
            [new StepTiming(1, "Construir 2 casas", 0, 40), new StepTiming(2, "Construir 1 cuartel", 300, 500), new StepTiming(3, "Investigar Emplumado", 600, null)],
            [new AgeClick("Feudal", 487, 610, 500, 480), new AgeClick("Castillos", null, null, null, 1000)],
            31, [new GameInterval(150, 12)], 57, 20, 2, [new FloatingResource(Resource.Gold, 1029, 169, 910)]);

        var summary = GameReportText.Summary(report);
        Assert.Equal("Arqueros: 20:00 de partida.", summary[0]);
        Assert.Contains("Clic de Feudal a las 8:07, objetivo 8:20 (la BO bien jugada: 8:00).", summary);
        Assert.Contains("No hiciste clic en Castillos.", summary);
        Assert.Contains("Lo que más se atrasó: Construir 1 cuartel (3:20 tarde).", summary);
        Assert.Contains("Pasos sin hacer: 1 de 3.", summary);
        Assert.Contains("Centro urbano parado: 31 s en total; el hueco más largo, 12 s desde las 2:30.", summary);
        Assert.Contains("Acumulaste oro: 2:49 con más de 500 desde las 17:09 (hasta 910).", summary);

        var html = GameReportText.Html(report, new DateTime(2026, 10, 20, 21, 0, 0));
        Assert.Contains("<title>Informe: Arqueros</title>", html);
        Assert.Contains("sin hacer", html);
    }
}
