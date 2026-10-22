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
    public void Todas_validan_y_se_simulan_completas_con_su_civ()
    {
        var validator = new BuildOrderValidator(TestData.GameData);
        var planner = new Planner(TestData.GameData);
        foreach (var bo in All())
        {
            var errors = validator.Validate(bo).Errors.ToList();
            Assert.True(errors.Count == 0, $"{bo.Id}: {string.Join("; ", errors)}");
            Assert.True(planner.Simulate(bo).Completed, $"{bo.Id} no se completa");
            Assert.True(BuildOrderLibrary.IsFileNameId(bo.Id), bo.Id);
        }
    }
}
