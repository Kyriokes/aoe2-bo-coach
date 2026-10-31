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
            Assert.True(optimizer.Summarize(improved).Score <= optimizer.Summarize(imported).Score, civ);
            // La misma estrategia: los mismos edificios, unidades y tecnologías, y los mismos aldeanos en total.
            Assert.Equal(Strategy(imported), Strategy(improved));
            Assert.Equal(LivePlan.Units(imported).Where((_, i) => LivePlan.IsVillagerTarget(imported.Steps[i])).Sum(),
                LivePlan.Units(improved).Where((_, i) => LivePlan.IsVillagerTarget(improved.Steps[i])).Sum());
        }
    }

    private static string Strategy(BuildOrder bo) => string.Join(",", bo.Steps
        .Where(s => !LivePlan.IsVillagerTarget(s) && s.Do != StepAction.Move)
        .GroupBy(s => $"{s.Do} {s.What}").OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Sum(s => s.Count)}"));

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
