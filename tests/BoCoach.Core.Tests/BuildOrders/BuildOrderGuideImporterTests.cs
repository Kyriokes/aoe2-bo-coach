using BoCoach.Core.Data;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.BuildOrders;

public class BuildOrderGuideImporterTests
{
    // Guías de buildorderguide.com en el formato de la página (colección pública "published-builds"), una por civ (D7).
    private static string Guide(string civ) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "buildorderguide", $"{civ}.json"));

    [Fact]
    public void Reconoce_el_formato()
    {
        Assert.True(BuildOrderGuideImporter.IsBuildOrderGuide(Guide("huns")));
        Assert.False(BuildOrderGuideImporter.IsBuildOrderGuide(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "rts-overlay", "23p-archers.json"))));
        Assert.False(BuildOrderGuideImporter.IsBuildOrderGuide("no es json"));
    }

    [Fact]
    public void Los_hunos_no_hacen_casas_y_los_aldeanos_iniciales_van_directo()
    {
        var result = BuildOrderGuideImporter.Convert(Guide("huns"), TestData.GameData);
        var bo = result.BuildOrder;

        Assert.Equal("huns", bo.Civ);
        Assert.Equal(new Step { Id = "s1", Do = StepAction.Move, Count = 3, Assign = GatherSource.Sheep }, bo.Steps[0] with { Note = null });
        Assert.DoesNotContain(bo.Steps, s => s.What == "house");
        Assert.True(new BuildOrderValidator(TestData.GameData).Validate(bo).IsValid);
        Assert.True(new Planner(TestData.GameData).Simulate(bo).Completed);
        Assert.Contains("buildorderguide.com/builds/", bo.Description);
    }

    [Fact]
    public void Una_guia_generica_se_adapta_a_una_civ_con_carro_de_mulas()
    {
        // Armenios: sin campamento maderero ni minero, los reemplaza el carro de mulas.
        var bo = BuildOrderGuideImporter.Convert(Guide("armenians"), TestData.GameData, "armenians").BuildOrder;

        Assert.Equal("armenians", bo.Civ);
        Assert.DoesNotContain(bo.Steps, s => s.What is "lumber_camp" or "mining_camp");
        Assert.Contains(bo.Steps, s => s.What == "mule_cart");
        Assert.True(new BuildOrderValidator(TestData.GameData).Validate(bo).IsValid);
        Assert.True(new Planner(TestData.GameData).Simulate(bo).Completed);

        // La misma guía para cualquier civ usa los campamentos.
        var any = BuildOrderGuideImporter.Convert(Guide("armenians"), TestData.GameData, "any").BuildOrder;
        Assert.Contains(any.Steps, s => s.What == "lumber_camp");
        Assert.DoesNotContain(any.Steps, s => s.What == "mule_cart");
    }

    [Fact]
    public void El_molino_de_la_guia_no_se_agrega_otra_vez_al_ir_a_las_bayas()
    {
        // Las tres guías hacen el molino con un constructor y después lo pasan a las bayas: es un solo molino.
        foreach (var civ in new[] { "britons", "byzantines", "chinese" })
        {
            var bo = BuildOrderGuideImporter.Convert(Guide(civ), TestData.GameData).BuildOrder;

            Assert.True(bo.Steps.Where(s => s is { Do: StepAction.Build, What: "mill" }).Sum(s => s.Count) == 1, civ);
        }
    }

    [Fact]
    public void Las_granjas_de_la_lista_no_se_cuentan_dos_veces()
    {
        // Cada aldeano que va a granjas hace la suya: las granjas que la página lista en el mismo paso son esas.
        foreach (var civ in new[] { "huns", "franks", "britons" })
        {
            var bo = BuildOrderGuideImporter.Convert(Guide(civ), TestData.GameData).BuildOrder;
            var farmers = LivePlan.Units(bo).Select((n, i) => (n, s: bo.Steps[i]))
                .Where(x => x.s is { Do: StepAction.Train, What: "villager", Assign: GatherSource.Farm }).Sum(x => x.n)
                + bo.Steps.Where(s => s is { Do: StepAction.Build, What: "farm" } && s.From != GatherSource.Farm).Sum(s => s.Count);
            var farms = bo.Steps.Where(s => s is { Do: StepAction.Build, What: "farm" }).Sum(s => s.Count);
            Assert.True(farms <= farmers, $"{civ}: {farms} granjas para {farmers} granjeros");
        }
    }

    [Fact]
    public void La_galeria_sin_cuartel_de_la_guia_lleva_el_cuartel_antes()
    {
        // La guía de polacos hace la galería de tiro sin cuartel, y el juego no la deja poner.
        var bo = BuildOrderGuideImporter.Convert(Guide("poles"), TestData.GameData, "poles").BuildOrder;

        var range = bo.Steps.ToList().FindIndex(s => s.What == "archery_range");
        Assert.InRange(bo.Steps.ToList().FindIndex(s => s.What == "barracks"), 0, range - 1);
        Assert.DoesNotContain(new BuildOrderValidator(TestData.GameData).Validate(bo).Warnings, w => w.Message.Contains("terminado"));
        Assert.True(new Planner(TestData.GameData).Simulate(bo).Completed);
    }
}
