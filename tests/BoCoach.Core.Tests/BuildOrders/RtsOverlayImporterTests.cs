using System.Text.Json.Nodes;
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

    private static string GuideWith(params string[] notes) => new JsonObject
    {
        ["name"] = "x",
        ["build_order"] = new JsonArray(new JsonObject { ["notes"] = new JsonArray(notes.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()) }),
    }.ToJsonString();

    /// <summary>Si tarda de más, el test falla en vez de colgarse (el hilo sigue hasta que termine el proceso).</summary>
    private static Task<ImportResult> ImportInTime(string json) =>
        Task.Run(() => Import(json)).WaitAsync(TimeSpan.FromSeconds(20));

    private const string OneOnWood = "Create 1 @resource/MaleVillDE.jpg@ on @resource/Aoe2de_wood.png@";

    // "500000000 house" daba vuelta el tope de población, y el aldeano que seguía agregaba casas hasta agotar la memoria.
    [Theory]
    [InlineData("@resource/Aoe2de_hammer.png@ 500000000 house")]
    [InlineData("@resource/Aoe2de_hammer.png@ 2147483647 house")]
    [InlineData("Build 429496730 house")]
    public async Task Una_cantidad_enorme_de_casas_no_cuelga(string note)
    {
        var bo = (await ImportInTime(GuideWith(note, OneOnWood))).BuildOrder;

        Assert.InRange(bo.Steps.Count, 1, 60);
        Assert.Equal(BuildOrderValidator.MaxCount, bo.Steps.Single(s => s is { What: "house", Count: > 2 }).Count);
    }

    [Theory]
    [InlineData("@resource/Aoe2de_hammer.png@ 2147483647 barracks")]
    [InlineData("@resource/Aoe2de_hammer.png@ 2147483647 farm")]
    [InlineData("Train 2147483647 archer")]
    [InlineData("Create 2147483647 @resource/MaleVillDE.jpg@ on @animal/Sheep_aoe2DE.png@")]
    [InlineData("Move 2147483647 @resource/MaleVillDE.jpg@ from @animal/Sheep_aoe2DE.png@ to @resource/Aoe2de_wood.png@")]
    public async Task Las_cantidades_de_una_nota_tienen_el_tope_del_validador(string note)
    {
        var bo = (await ImportInTime(GuideWith(note, OneOnWood))).BuildOrder;

        Assert.InRange(bo.Steps.Count, 1, 60);
        Assert.All(bo.Steps.Where(s => !LivePlan.IsVillagerTarget(s)), s => Assert.InRange(s.Count, 1, BuildOrderValidator.MaxCount));
    }

    [Theory]
    [InlineData("""{ "type": "build", "buildings": [ { "type": "house", "count": 500000000 } ] }""")]
    [InlineData("""{ "type": "newVillagers", "task": "sheep", "count": 2147483647 }""")]
    [InlineData("""{ "type": "trainUnit", "unit": "archer", "count": 2147483647 }""")]
    [InlineData("""{ "type": "moveVillagers", "from": "sheep", "to": "wood", "count": 2147483647 }""")]
    [InlineData("""{ "type": "newVillagers", "task": "wood", "count": 1, "resources": { "food": 2147483647, "wood": 2147483647, "gold": 2147483647, "stone": 2147483647 } }""")]
    [InlineData("""{ "type": "newVillagers", "task": "wood", "count": 1, "resources": { "food": -2147483648, "wood": -2147483648 } }""")]
    public async Task Las_cantidades_de_una_guia_de_buildorderguide_tienen_el_tope(string step)
    {
        var json = $$"""
            { "title": "x", "civilization": "Franks", "build": [ {{step}}, { "type": "newVillagers", "task": "wood", "count": 1 } ] }
            """;

        var result = await Task.Run(() => BuildOrderGuideImporter.Convert(json, TestData.GameData)).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.InRange(result.BuildOrder.Steps.Count, 1, 60);
    }

    // Expresiones que tardaban el cuadrado del largo de la nota: una sola nota de 200 KB colgaba la pestaña.
    public static TheoryData<string> LongNotes()
    {
        const int n = 200_000;
        return new TheoryData<string>
        {
            "a" + new string(' ', n) + "x", // jabalí 50% --> ...
            "50% a" + new string(' ', n) + "x", // 50% Feudal --> ...
            new string('a', n) + " 50%" + new string(' ', n) + "x",
            "50% " + string.Concat(Enumerable.Repeat("--> ", n / 4)) + "\nz",
            "a " + string.Concat(Enumerable.Repeat("1 ", n / 2)),
            "@" + string.Concat(Enumerable.Repeat("DE", n / 2)) + "x@", // el nombre de un ícono
            "@" + string.Concat(Enumerable.Repeat("_aoe2", n / 5)) + "x@",
            new string('1', n),
            new string('@', n),
            string.Concat(Enumerable.Repeat("-->", n / 3)),
            "(" + new string(' ', n),
        };
    }

    [Theory]
    [MemberData(nameof(LongNotes))]
    public async Task Una_nota_enorme_no_cuelga(string note)
    {
        var result = await ImportInTime(GuideWith(note, OneOnWood));

        Assert.NotEmpty(result.BuildOrder.Steps);
    }

    // Juntar las notas de un paso de a una también tardaba el cuadrado del total: 100.000 notas chicas, 40 s.
    [Fact]
    public async Task Muchas_notas_chicas_no_cuelgan()
    {
        var result = await ImportInTime(GuideWith([.. Enumerable.Repeat("bailá", 100_000), OneOnWood]));

        Assert.InRange(result.BuildOrder.Steps.Max(s => s.Note?.Length ?? 0), 1, 2100);
    }

    [Fact]
    public async Task Muchos_pasos_con_una_nota_no_cuelgan()
    {
        var step = new JsonObject { ["notes"] = new JsonArray(JsonValue.Create("bailá")) };
        var json = new JsonObject { ["name"] = "x", ["build_order"] = new JsonArray(Enumerable.Range(0, 100_000).Select(_ => (JsonNode?)step.DeepClone()).ToArray()) }.ToJsonString();

        var result = await ImportInTime(json);

        Assert.InRange(result.BuildOrder.Steps.Max(s => s.Note?.Length ?? 0), 1, 2100);
    }

    // Lo que leen esas expresiones sigue igual.
    [Theory]
    [InlineData("@animal/Boar_aoe2DE.png@ 50% --> Lure @animal/Boar_aoe2DE.png@", "Al 50% de jabalí: atraé jabalí")]
    [InlineData("Alta Edad 50%   -->   algo", "Al 50% de Alta Edad: algo")]
    [InlineData("50% Feudal --> un consejo", "Al 50% de Feudal: un consejo")]
    [InlineData("50%   Edad Feudal   -->   otro consejo", "Al 50% de Edad Feudal: otro consejo")]
    [InlineData("Ninguna flecha 50%", "Ninguna flecha 50%")]
    public void Las_notas_al_medio_de_algo_quedan_en_castellano(string note, string expected)
    {
        var result = Import(GuideWith(note, OneOnWood));

        Assert.Contains(expected, result.BuildOrder.Steps.Select(s => s.Note ?? "").First(n => n.Contains("50%")));
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
