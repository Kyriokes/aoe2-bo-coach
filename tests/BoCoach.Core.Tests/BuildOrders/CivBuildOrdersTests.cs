using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.BuildOrders;

/// <summary>D7: al menos una BO por civ (build_orders/civs), importada de buildorderguide.com.</summary>
public class CivBuildOrdersTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "civs");

    private static List<BuildOrder> All() =>
        Directory.EnumerateFiles(Dir, "*.json").Select(BuildOrderSerializer.Load).ToList();

    [Fact]
    public void Cada_civ_tiene_su_BO_importada()
    {
        var all = All();
        foreach (var civ in TestData.GameData.Civs.Keys)
            Assert.Contains(all, bo => bo.Civ == civ && bo.Tags.Contains("importada"));
    }

    [Fact]
    public void Cada_civ_tiene_su_BO_mejorada_y_nunca_es_peor_que_la_importada()
    {
        var all = All();
        var optimizer = new BuildOrderOptimizer(TestData.GameData);
        foreach (var civ in TestData.GameData.Civs.Keys)
        {
            var imported = Assert.Single(all, bo => bo.Civ == civ && bo.Tags.Contains("importada"));
            var improved = Assert.Single(all, bo => bo.Civ == civ && bo.Tags.Contains("mejorada"));
            Assert.Equal($"{imported.Id}-mejorada", improved.Id);
            if (improved.Tags.Contains("castillos"))
            {
                AssertExtends(imported, improved);
                continue;
            }
            Assert.True(optimizer.Summarize(improved).Score <= optimizer.Summarize(imported).Score, civ);
            // La misma estrategia: los mismos edificios, unidades y tecnologías, y los mismos aldeanos en total.
            Assert.Equal(Strategy(imported), Strategy(improved));
            Assert.Equal(LivePlan.Units(imported).Where((_, i) => LivePlan.IsVillagerTarget(imported.Steps[i])).Sum(),
                LivePlan.Units(improved).Where((_, i) => LivePlan.IsVillagerTarget(improved.Steps[i])).Sum());
        }
    }

    // Las que siguen en Castillos con la producción (la de los hunos desde el 2026-10-31, pedido del usuario: "nunca
    // hace los CA") terminan más tarde que la importada: no pueden clicar Castillos más tarde (la mejorada de los hunos ya
    // clicaba Feudal 8 s después a cambio de un minuto en Castillos) y hacen todo lo que ella hace.
    private static void AssertExtends(BuildOrder imported, BuildOrder improved)
    {
        var planner = new Planner(TestData.GameData);
        var (before, after) = (planner.Simulate(imported), planner.Simulate(improved));
        Assert.True(after.ClickTime("castle_age") <= before.ClickTime("castle_age"), improved.Id);
        var made = Totals(improved);
        foreach (var (what, count) in Totals(imported))
            Assert.True(made.GetValueOrDefault(what) >= count, $"{improved.Id}: {what}");
        Assert.True(LivePlan.Units(improved).Where((_, i) => LivePlan.IsVillagerTarget(improved.Steps[i])).Sum()
                    >= LivePlan.Units(imported).Where((_, i) => LivePlan.IsVillagerTarget(imported.Steps[i])).Sum());
    }

    private static Dictionary<string, int> Totals(BuildOrder bo) => bo.Steps
        .Where(s => !LivePlan.IsVillagerTarget(s) && s.Do != StepAction.Move)
        .GroupBy(s => $"{s.Do} {s.What}").ToDictionary(g => g.Key, g => g.Sum(s => s.Count));

    private static string Strategy(BuildOrder bo) => string.Join(",", bo.Steps
        .Where(s => !LivePlan.IsVillagerTarget(s) && s.Do != StepAction.Move)
        .GroupBy(s => $"{s.Do} {s.What}").OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Sum(s => s.Count)}"));

    // Pedido del usuario (2026-10-31): "Scouts → CA" de los hunos nunca sacaba arqueros a caballo. Sube a Castillos y deja
    // la producción en 3 galerías con las mejoras de la galería, el establo, la herrería y la universidad.
    [Fact]
    public void Los_hunos_terminan_con_arqueros_a_caballo_en_3_galerias_y_sus_mejoras()
    {
        var bo = BuildOrderSerializer.Load(Path.Combine(AppContext.BaseDirectory, "civs", "huns-mejorada.json"));
        var plan = new Planner(TestData.GameData).Simulate(bo);

        Assert.True(plan.Completed);
        var ranges = plan.Jobs.Where(j => j.What == "cavalry_archer").GroupBy(j => j.Building).ToList();
        Assert.Equal(3, ranges.Count);
        Assert.All(ranges, r => Assert.True(r.Count() >= 9, r.Key));
        var castle = plan.For(bo.Steps.First(s => s.What == "castle_age").Id)!.End!.Value;
        Assert.True(ranges.Min(r => r.Min(j => j.Start)) <= castle + 1); // apenas llega a Castillos
        string[] upgrades = ["thumb_ring", "bloodlines", "husbandry", "fletching", "bodkin_arrow", "padded_archer_armor", "leather_archer_armor", "ballistics"];
        Assert.All(upgrades, u => Assert.Contains(bo.Steps, s => s is { Do: StepAction.Research } && s.What == u));
        Assert.Equal("scouts", BuildOrderStyle.Of(bo));
    }

    [Fact]
    public void Todas_validan_y_se_simulan_completas_con_su_civ()
    {
        var validator = new BuildOrderValidator(TestData.GameData);
        var planner = new Planner(TestData.GameData);
        foreach (var bo in All())
        {
            var errors = validator.Validate(bo).Errors.ToList();
            Assert.True(errors.Count == 0, $"{bo.Id}: {string.Join("; ", errors)}");
            Assert.True(planner.Simulate(bo).Completed, $"{bo.Id} no se completa");
        }
    }
}
