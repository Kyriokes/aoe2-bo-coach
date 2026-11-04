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
        var planned = new Planner(TestData.GameData).Simulate(Archers).For(Archers.Steps[0].Id)!.End;
        Assert.Equal(1, first.Number);
        Assert.True(planned > 0);
        Assert.Equal(planned, first.Planned);
        Assert.Equal(90, first.Done);
        Assert.Equal(90 - planned, first.Delay);
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

    // Partida 20261104-095625: "6 aldeanos en ovejas" salía 1:16 tarde hecho justo cuando lo terminaba la BO, porque se
    // comparaba cuándo lo arrancaba la BO con cuándo se terminó.
    [Fact]
    public void Un_paso_hecho_cuando_lo_termina_la_BO_no_tiene_atraso()
    {
        var bo = new BuildOrder { Name = "t", Steps = [new Step { Id = "s1", Do = StepAction.Train, What = "villager", Count = 1, Assign = GatherSource.Sheep }] };
        var end = new Planner(TestData.GameData).Simulate(bo).For("s1")!.End!.Value;
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        coach.Update(new GameSnapshot(end, new ResourceAmounts(200, 200, 100, 200), 5, 15, [new GameEvent("villager_created", end)]));

        Assert.Equal(0, coach.Report().Steps[0].Delay);
    }

    // Lo que no se puede ver se cierra solo a su tiempo: su hora no dice cuándo lo hizo el jugador.
    [Fact]
    public void Un_Mover_no_tiene_atraso()
    {
        var bo = new BuildOrder { Name = "t", Steps = [new Step { Id = "s1", Do = StepAction.Move, Count = 3, Assign = GatherSource.Sheep }] };
        var coach = new Coach(bo, TestData.GameData, TestData.Events);
        coach.Update(new GameSnapshot(1000, new ResourceAmounts(200, 200, 100, 200), 4, 15));
        coach.Update(new GameSnapshot(1000 + Tracker.MoveSeconds, new ResourceAmounts(200, 200, 100, 200), 4, 15));

        var step = coach.Report().Steps[0];

        Assert.NotNull(step.Done);
        Assert.Null(step.Delay);
    }

    // Partida 20261031-163414: las granjas (que el juego no avisa) se dieron por hechas recién con Castillos, y el
    // informe las ponía primeras en "lo que más se atrasó" (14:30 tarde) aunque estaban hechas desde mucho antes.
    [Fact]
    public void Lo_que_el_juego_no_avisa_no_tiene_atraso()
    {
        var report = new GameReport("Arqueros", 1500,
            [new StepTiming(1, "Construir 1 cuartel", 300, 500), new StepTiming(2, "Construir 3 granjas", 500, 1400) { Unannounced = true }],
            [], 0, [], 0, 0, 0, []);

        Assert.Null(report.Steps[1].Delay);
        Assert.Contains("Lo que más se atrasó: Construir 1 cuartel (3:20 tarde).", GameReportText.Summary(report));
    }

    private static GameReport Simple => new("Arqueros", 1200, [new StepTiming(1, "Construir 2 casas", 0, 40)],
        [new AgeClick("Feudal", 487, 610, 500, 480)], 31, [new GameInterval(150, 12)], 57, 20, 2, []);

    [Fact]
    public void El_informe_suma_el_cruce()
    {
        var cruce = new[] { "Britanos contra francos en Arabia (Elo 1050): ganás 47 % (43 a 49).",
                            "Duró 34 min: a esa duración, en este cruce ganás el 45 % (41 a 49)." };
        var report = Simple with { Matchup = cruce };

        var summary = GameReportText.Summary(report);
        Assert.Equal(["El cruce: " + cruce[0], cruce[1]], summary.TakeLast(2));
        // En la página, su propia sección "El cruce" (spec D10, 5.4), no repetido en el resumen.
        var html = GameReportText.Html(report, new DateTime(2026, 10, 25, 21, 0, 0));
        Assert.Contains("<h2>El cruce</h2>", html);
        Assert.Contains($"<li>{System.Net.WebUtility.HtmlEncode(cruce[0])}</li>", html); // la "á" va como entidad
        Assert.DoesNotContain(System.Net.WebUtility.HtmlEncode("El cruce: "), html);
    }

    [Fact]
    public void Sin_cruce_el_informe_no_cambia()
    {
        Assert.Empty(Simple.Matchup);
        Assert.DoesNotContain(GameReportText.Summary(Simple), line => line.Contains("contra", StringComparison.Ordinal));
        Assert.Equal(GameReportText.Summary(Simple), GameReportText.Summary(Simple with { Matchup = [] }));
    }
}
