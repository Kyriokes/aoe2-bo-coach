using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Tests.BuildOrders;

public class RtsOverlayImporterTests
{
    // Exportada de buildorderguide.com (formato de RTS Overlay), la que pegó el usuario.
    private static readonly string Archers = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "rts-overlay", "23p-archers.json"));

    private static ImportResult Import(string json) => RtsOverlayImporter.Convert(json, TestData.GameData);

    [Fact]
    public void Reconoce_el_formato()
    {
        Assert.True(RtsOverlayImporter.IsRtsOverlay(Archers));
        Assert.False(RtsOverlayImporter.IsRtsOverlay(File.ReadAllText(Path.Combine(TestData.BuildOrdersDir, "scouts_feudal.json"))));
        Assert.False(RtsOverlayImporter.IsRtsOverlay("no es json"));
    }

    private static string Guide(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "rts-overlay", name));

    [Fact]
    public void Scouts_de_Cicero_el_establo_mal_escrito_y_el_separador()
    {
        // "hammer 1 staple with 2 aldeanos" es el establo; "-------" separa y no es nota.
        var result = Import(Guide("22p-scouts.json"));
        var bo = result.BuildOrder;

        Assert.Empty(result.Warnings);
        Assert.Contains(bo.Steps, s => s is { Do: StepAction.Build, What: "stable" });
        var steps = bo.Steps.ToList();
        Assert.True(steps.FindIndex(s => s.What == "stable") < steps.FindIndex(s => s.What == "scout_cavalry"));
        Assert.DoesNotContain(bo.Steps, s => s.Note?.Contains("---") == true);
    }

    [Fact]
    public void Los_consejos_entre_parentesis_quedan_como_nota_y_no_repiten_edificios()
    {
        // "(2 aldeanos hacen el mercado, 1 la herrería)" y "(no te olvides de 1-2 casas ... Castillos)" no son pasos.
        var result = Import(Guide("absolute-beginner-fc.json"));
        var bo = result.BuildOrder;

        Assert.Empty(result.Warnings);
        Assert.Equal(1, bo.Steps.Where(s => s.What == "market").Sum(s => s.Count));
        Assert.Equal(1, bo.Steps.Where(s => s.What == "blacksmith").Sum(s => s.Count));
        Assert.DoesNotContain(bo.Steps, s => s.What == "castle");
        Assert.Contains(bo.Steps, s => s.Note?.Contains("Market") == true);
    }

    [Fact]
    public void Los_aldeanos_de_cada_paso_coinciden_con_la_guia()
    {
        // Check compara, después de cada paso de la guía, el total y cada recurso: si no cierra, avisa.
        var result = Import(Archers);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Los_3_iniciales_hacen_las_casas_y_las_ovejas_son_6_en_total()
    {
        var bo = Import(Archers).BuildOrder;

        var houses = bo.Steps[0];
        Assert.Equal((StepAction.Build, "house", 2, (int?)3, (GatherSource?)null, GatherSource.Sheep), (houses.Do, houses.What, houses.Count, houses.Builders, houses.From, houses.Assign!.Value));
        var sheep = bo.Steps[1];
        Assert.Equal((StepAction.Train, "villager", 6, GatherSource.Sheep), (sheep.Do, sheep.What, sheep.Count, sheep.Assign));
    }

    [Fact]
    public void Arma_los_pasos_de_las_notas()
    {
        var bo = Import(Archers).BuildOrder;
        var steps = bo.Steps.Select(s => s.Do == StepAction.Move ? $"move {s.Count} {s.From}>{s.Assign}" : $"{s.Do.ToString().ToLowerInvariant()} {s.What} {s.Count}").ToList();

        Assert.Equal("23p Archers", bo.Name);
        Assert.Equal("Cicero7800", bo.Author);
        Assert.Contains("research loom 1", steps);
        Assert.Contains("research double_bit_axe 1", steps);
        Assert.Contains("research fletching 1", steps);
        Assert.Contains("research wheelbarrow 1", steps);
        Assert.Contains("build archery_range 2", steps);
        Assert.Contains("build blacksmith 1", steps);
        Assert.Contains("build barracks 1", steps);
        Assert.Equal(2, steps.Count(s => s == "train archer 12"));
        Assert.Contains("move 2 Sheep>Wood", steps);
        // El campamento minero lo hace uno de los 3 que van al oro.
        Assert.Contains("build mining_camp 1", steps);
        Assert.Contains("move 2 Sheep>Gold", steps);
        // Lo que las guías dan por sabido.
        Assert.Contains("build lumber_camp 1", steps);
        Assert.Contains("build mill 1", steps);
        Assert.True(steps.IndexOf("build lumber_camp 1") < steps.IndexOf("build mill 1"));
    }

    [Fact]
    public void Feudal_a_23_de_poblacion_y_el_reparto_de_la_guia_como_objetivo()
    {
        var bo = Import(Archers).BuildOrder;

        var feudal = Assert.Single(bo.Steps, s => s.What == "feudal_age");
        Assert.Equal(23, feudal.AtPop);
        Assert.Equal(new VillagerDistribution(Food: 13, Wood: 9, Gold: 0, Stone: 0), bo.Targets?.VillagersAtFeudal);
        // Castillos se pide después de los arqueros: no se sabe con cuánta población.
        Assert.Null(Assert.Single(bo.Steps, s => s.What == "castle_age").AtPop);
    }

    [Fact]
    public void Es_una_BO_valida_que_el_planner_completa_sin_trabarse_por_poblacion()
    {
        var bo = Import(Archers).BuildOrder;

        var validation = new BuildOrderValidator(TestData.GameData, TestData.Events).Validate(bo);
        Assert.True(validation.IsValid, string.Join("\n", validation.Errors));

        var plan = new Planner(TestData.GameData).Simulate(bo);
        var housed = plan.Blocks.Where(b => b.Reason == BlockReason.Population && b.Duration > 30).ToList();
        Assert.True(housed.Count == 0, string.Join("\n", housed.Select(b => $"{b.StepId} {bo.Steps.First(s => s.Id == b.StepId).What} desde {b.From} por {b.Duration}")));
        Assert.InRange(plan.ClickTime("feudal_age")!.Value, 8 * 60, 11 * 60);
    }

    [Fact]
    public void Lo_que_no_entiende_queda_como_nota_y_avisa()
    {
        const string json = """
            { "name": "x", "build_order": [
              { "villager_count": 6, "resources": { "food": 6, "wood": 0, "gold": 0, "stone": 0 },
                "notes": [ "Create 6 @resource/MaleVillDE.jpg@ on @animal/Sheep_aoe2DE.png@", "Bailá la macarena" ] } ] }
            """;

        var result = Import(json);

        Assert.Contains(result.Warnings, w => w.Contains("Bailá la macarena"));
        Assert.Contains("Bailá la macarena", result.BuildOrder.Steps[^1].Note);
    }
}
