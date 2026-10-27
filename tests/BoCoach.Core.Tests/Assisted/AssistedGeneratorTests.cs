using BoCoach.Core.Assisted;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using System.Diagnostics;
using System.Text.Json;
using BoCoach.Core.Engine;
using Xunit.Abstractions;

namespace BoCoach.Core.Tests.Assisted;

/// <summary>D12 (c): el generador del modo asistido de punta a punta.</summary>
public class AssistedGeneratorTests(ITestOutputHelper output)
{
    private static readonly Maps RealMaps = Maps.Load(Path.Combine(AppContext.BaseDirectory, "maps.json"));

    private static readonly AssistedGenerator Generator = new(TestData.GameData, RealMaps);

    private static BuildOrder Assisted(string civ, int until, params AssistedGoal[] goals) => new()
    {
        Name = "Asistida",
        Civ = civ,
        Assisted = new AssistedPlan { Villagers = new AssistedVillagers { Until = until }, Goals = goals },
    };

    /// <summary>La BO generada, simulada de nuevo sin la política, da lo mismo, y valida sin errores.</summary>
    private static void AssertConsistent(AssistedResult result, Maps? maps = null)
    {
        var again = new Planner(TestData.GameData, AssistedGenerator.Settings(TestData.GameData, maps ?? RealMaps, result.BuildOrder))
            .Simulate(result.BuildOrder);
        Assert.Equal(result.Plan.Steps.Select(s => (s.Step.Id, s.Start, s.End)), again.Steps.Select(s => (s.Step.Id, s.Start, s.End)));
        Assert.Equal(result.Plan.Blocks, again.Blocks);
        Assert.Equal(result.Plan.Timeline, again.Timeline);
        Assert.Equal(result.Plan.Idle, again.Idle);
        Assert.Empty(new BuildOrderValidator(TestData.GameData, TestData.Events).Validate(result.BuildOrder).Errors);
    }

    private static IEnumerable<Step> VillagerSteps(AssistedResult result) =>
        result.BuildOrder.Steps.Where(s => s is { Do: StepAction.Train, What: "villager" });

    [Theory]
    [InlineData("any")]
    [InlineData("franks")]
    public void Genera_aldeanos_hasta_N_con_casas(string civ)
    {
        var result = Generator.Generate(Assisted(civ, 18));

        Assert.Equal(18, result.Plan.Timeline[^1].Villagers);
        var villagerIds = VillagerSteps(result).Select(s => s.Id).ToHashSet();
        Assert.DoesNotContain(result.Plan.Blocks, b => villagerIds.Contains(b.StepId) && b.Reason == BlockReason.Population && b.Duration > 5);
        AssertConsistent(result);
    }

    [Fact]
    public void Los_pasos_de_aldeanos_salen_por_tramos()
    {
        var result = Generator.Generate(Assisted("any", 18));

        var units = LivePlan.Units(result.BuildOrder);
        var counts = result.BuildOrder.Steps.Select((s, i) => (s, n: units[i])).Where(x => LivePlan.IsVillagerTarget(x.s)).Select(x => x.n).ToList();
        Assert.True(counts.Count <= 9, $"{counts.Count} pasos de aldeanos para 15 aldeanos");
        Assert.All(counts, n => Assert.InRange(n, 1, 6));
    }

    [Fact]
    public void Until_chico_no_entrena_de_mas()
    {
        var result = Generator.Generate(Assisted("any", 5));

        Assert.Equal(5, result.Plan.Timeline[^1].Villagers);
        AssertConsistent(result);
    }

    [Fact]
    public void Sin_assisted_da_error()
    {
        Assert.Throws<ArgumentException>(() => Generator.Generate(new BuildOrder { Name = "manual" }));
    }

    [Theory]
    [InlineData("any")]
    [InlineData("tatars")]
    [InlineData("poles")]
    public void Con_la_comida_de_Arabia_nadie_queda_parado(string civ)
    {
        var result = Generator.Generate(Assisted(civ, 60));

        Assert.Equal(60, result.Plan.Timeline[^1].Villagers);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        Assert.Contains(result.BuildOrder.Steps, s => s is { Do: StepAction.Build, What: "farm" });
        AssertConsistent(result);
    }

    [Fact]
    public void Un_mapa_sin_jabali_ni_bayas_va_directo_a_las_granjas()
    {
        var maps = new Maps([new GameMap("pelado", "Pelado", false,
            new HashSet<GatherSource> { GatherSource.Sheep, GatherSource.Farm, GatherSource.Wood, GatherSource.Gold, GatherSource.Stone }, null)]);
        var bo = Assisted("any", 40) with { Map = "pelado" };

        var result = new AssistedGenerator(TestData.GameData, maps).Generate(bo);

        Assert.DoesNotContain(result.BuildOrder.Steps, s => s.Assign is GatherSource.Hunt or GatherSource.Berries);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result, maps);
    }

    private static AssistedGoal Age(string age, int? atPop = null) => new() { Age = age, AtPop = atPop };

    [Theory]
    [InlineData("any")]
    [InlineData("franks")]
    public void Llega_a_Imperial(string civ)
    {
        var result = Generator.Generate(Assisted(civ, 100, Age("feudal_age"), Age("castle_age"), Age("imperial_age")));

        Assert.Empty(result.Problems);
        Assert.All(result.Goals, g => Assert.NotNull(g.End));
        Assert.True(result.Goals[0].End < result.Goals[1].Start && result.Goals[1].End < result.Goals[2].Start);
        AssertConsistent(result);
    }

    private static AssistedGoal Units(string unit, int count, int buildings) => new() { Unit = unit, Count = count, Buildings = buildings };

    [Fact]
    public void Un_objetivo_que_la_civ_no_tiene_se_saltea()
    {
        var result = Generator.Generate(Assisted("incas", 60, Age("feudal_age"), Units("scout_cavalry", 5, 1), Age("castle_age")));

        Assert.Contains("Los incas no tienen", result.Goals[1].Problem);
        Assert.Contains("establo", result.Goals[1].Problem);
        Assert.NotNull(result.Goals[2].End);
        AssertConsistent(result);
    }

    [Fact]
    public void Una_unidad_sin_su_edad_antes_es_un_problema()
    {
        var result = Generator.Generate(Assisted("franks", 60, Age("feudal_age"), Units("knight", 5, 1)));

        Assert.Contains("Pedí antes la Edad de los Castillos", result.Goals[1].Problem);
        Assert.NotNull(result.Goals[0].End);
        AssertConsistent(result);
    }

    [Fact]
    public void La_postura_sale_de_la_edad_mas_alta_y_de_la_ultima_unidad()
    {
        var imperialFirst = Generator.Generate(Assisted("franks", 60, Age("imperial_age"), Age("feudal_age")));
        Assert.Null(imperialFirst.BuildOrder.Posture?.NextAge);

        var scouts = Generator.Generate(Assisted("franks", 60, Age("feudal_age"), Units("scout_cavalry", 10, 1), Age("castle_age")));
        Assert.Equal("scout_cavalry", scouts.BuildOrder.Posture!.Queue[^1]); // los aldeanos van solo si no se llegó a 'until'
        Assert.Equal("imperial_age", scouts.BuildOrder.Posture.NextAge);
    }

    [Fact]
    public void Veinte_exploradores_con_dos_establos()
    {
        var result = Generator.Generate(Assisted("franks", 60, Age("feudal_age"), Units("scout_cavalry", 20, 2)));

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Goals[1].End);
        Assert.Equal(2, result.BuildOrder.Steps.Count(s => s is { Do: StepAction.Build, What: "stable" }));
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
    }

    [Theory]
    [InlineData("franks", 60)]
    [InlineData("britons", 60)]
    [InlineData("britons", 100)]
    [InlineData("huns", 40)]
    [InlineData("huns", 100)]
    [InlineData("franks", 13)]
    public void Mas_establos_nunca_terminan_mas_tarde(string civ, int until)
    {
        var ends = new[] { 1, 2, 3 }
            .Select(n => Generator.Generate(Assisted(civ, until, Age("feudal_age"), Units("scout_cavalry", 20, n))).Goals[1].End!.Value)
            .ToList();

        Assert.True(ends[1] <= ends[0] && ends[2] <= ends[1], string.Join(", ", ends.Select(GameClock.Format)));
    }

    [Fact]
    public void Dos_objetivos_de_unidades_comparten_los_edificios()
    {
        var result = Generator.Generate(Assisted("franks", 60,
            Age("feudal_age"), Units("scout_cavalry", 10, 2), Age("castle_age"), Units("knight", 10, 2)));

        Assert.Empty(result.Problems);
        Assert.Equal(2, result.BuildOrder.Steps.Count(s => s is { Do: StepAction.Build, What: "stable" }));
        Assert.NotNull(result.Goals[3].End);
        AssertConsistent(result);
    }

    [Fact]
    public void Tres_centros_urbanos_llegan_antes_a_los_aldeanos()
    {
        var two = Generator.Generate(Assisted("franks", 100, Age("feudal_age"), Age("castle_age")));
        var three = Generator.Generate(Assisted("franks", 100, Age("feudal_age"), Age("castle_age"), new AssistedGoal { TownCenters = 3 }));

        Assert.Empty(three.Problems);
        Assert.Equal(2, three.BuildOrder.Steps.Count(s => s is { Do: StepAction.Build, What: "town_center" }));
        double Hundred(AssistedResult r) => r.Plan.Timeline.First(s => s.Villagers >= 100).Time;
        Assert.True(Hundred(three) < Hundred(two), $"{GameClock.Format(Hundred(three))} contra {GameClock.Format(Hundred(two))}");
        Assert.DoesNotContain(three.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(three);
    }

    private static BuildOrder WithEco(BuildOrder bo) => bo with { Assisted = bo.Assisted! with { EcoUpgrades = true } };

    [Fact]
    public void Con_mejoras_eco_compra_las_de_siempre()
    {
        var result = Generator.Generate(WithEco(Assisted("franks", 100, Age("feudal_age"), Age("castle_age"))));

        Assert.Empty(result.Problems);
        var researched = result.BuildOrder.Steps.Where(s => s.Do == StepAction.Research).Select(s => s.What).ToList();
        Assert.Contains("double_bit_axe", researched);
        Assert.Contains("gold_mining", researched);
        Assert.Contains("wheelbarrow", researched);
        Assert.DoesNotContain("horse_collar", researched); // a los francos les sale gratis
        Assert.True(researched.IndexOf("loom") is >= 0 and var loom && loom < researched.IndexOf("feudal_age"), string.Join(", ", researched));
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
    }

    [Fact]
    public void Con_pocos_aldeanos_no_compra_carretilla()
    {
        var result = Generator.Generate(WithEco(Assisted("any", 13, Age("feudal_age"), Age("castle_age"), Age("imperial_age"))));

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.BuildOrder.Steps, s => s is { Do: StepAction.Research, What: "wheelbarrow" or "hand_cart" });
        AssertConsistent(result);
    }

    [Fact]
    public void Las_mejoras_no_atrasan_las_edades_de_mas()
    {
        var plain = Generator.Generate(Assisted("franks", 100, Age("feudal_age"), Age("castle_age")));
        var eco = Generator.Generate(WithEco(Assisted("franks", 100, Age("feudal_age"), Age("castle_age"))));

        Assert.True(eco.Goals[1].Start <= plain.Goals[1].Start + 60,
            $"Castillos {GameClock.Format(eco.Goals[1].Start!.Value)} con mejoras, {GameClock.Format(plain.Goals[1].Start!.Value)} sin");
    }

    [Fact]
    public void Un_objetivo_que_ya_pidio_otra_regla_se_cumple_con_ese_paso()
    {
        // Revisión del (d): pedir a mano una mejora que las mejoras eco ya compran, o algo ya pedido más arriba, salía
        // como "No llega".
        var wheelbarrow = Generator.Generate(WithEco(Assisted("franks", 60, Age("feudal_age"), Age("castle_age"), new AssistedGoal { Tech = "wheelbarrow" })));
        var twice = Generator.Generate(Assisted("mongols", 60, Age("feudal_age"), new AssistedGoal { Tech = "bloodlines" }, new AssistedGoal { Tech = "bloodlines" }));
        var centers = Generator.Generate(Assisted("franks", 60, Age("feudal_age"), Age("castle_age"), new AssistedGoal { TownCenters = 3 }, new AssistedGoal { TownCenters = 2 }));

        Assert.Empty(wheelbarrow.Problems);
        Assert.Empty(twice.Problems);
        Assert.Equal(twice.Goals[1].End, twice.Goals[2].End);
        Assert.Empty(centers.Problems);
    }

    [Theory]
    [InlineData("britons")]
    [InlineData("achaemenids")]
    public void Una_milicia_antes_de_Feudal_no_frena_la_comida(string civ)
    {
        // Revisión del (d): el cuartel contaba como pagable sin la segunda casa de la apertura, que todavía no se pagó;
        // esperaba la madera y frenaba los campamentos y el "mover" de las ovejas.
        var result = Generator.Generate(Assisted(civ, 60, Units("militia", 6, 1), Age("feudal_age")));

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
    }

    [Theory]
    [InlineData("armenians", 25, "scouts-castle-scouts")]
    [InlineData("cumans", 25, "castle-tc")]
    [InlineData("aztecs", 13, "archers")]
    [InlineData("britons", 13, "scouts-castle")]
    public void Con_pocos_aldeanos_se_cumplen_los_objetivos_que_no_son_edades(string civ, int until, string list)
    {
        // Revisión del (d): con 'until' alcanzado nadie volvía a consultar ni movía aldeanos a lo que faltaba.
        AssistedGoal[] goals = list switch
        {
            "scouts-castle-scouts" => [Age("feudal_age"), Units("scout_cavalry", 20, 2), Age("castle_age"), Units("scout_cavalry", 10, 2)],
            "castle-tc" => [Age("feudal_age"), Age("castle_age"), new AssistedGoal { TownCenters = 3 }],
            "archers" => [Age("feudal_age"), Units("archer", 10, 1), Units("archer", 10, 3)],
            _ => [Age("feudal_age"), Units("scout_cavalry", 20, 2), Age("castle_age")],
        };

        var result = Generator.Generate(Assisted(civ, until, goals));

        Assert.Empty(result.Problems);
        AssertConsistent(result);
    }

    [Fact]
    public void El_telar_a_pedido_no_frena_al_centro_urbano()
    {
        var result = Generator.Generate(Assisted("any", 60, new AssistedGoal { Tech = "loom" }, Age("feudal_age")));

        Assert.Empty(result.Problems);
        Assert.Contains(result.BuildOrder.Steps, s => s is { Do: StepAction.Research, What: "loom" });
        Assert.NotNull(result.Goals[1].End);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
    }

    [Fact]
    public void Linea_de_sangre_despues_del_establo()
    {
        var result = Generator.Generate(Assisted("mongols", 60, Age("feudal_age"), new AssistedGoal { Tech = "bloodlines" }));

        Assert.Empty(result.Problems);
        var steps = result.BuildOrder.Steps.ToList();
        var stable = steps.FindIndex(s => s is { Do: StepAction.Build, What: "stable" });
        var research = steps.FindIndex(s => s is { Do: StepAction.Research, What: "bloodlines" });
        Assert.InRange(stable, 0, research - 1);
        AssertConsistent(result);
    }

    [Fact]
    public void La_tecnologia_espera_el_establo_libre()
    {
        var result = Generator.Generate(Assisted("mongols", 60,
            Age("feudal_age"), Units("scout_cavalry", 20, 1), new AssistedGoal { Tech = "bloodlines" }));

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
    }

    [Fact]
    public void Una_edad_a_una_poblacion_que_nunca_llega()
    {
        var result = Generator.Generate(Assisted("any", 20, Age("feudal_age", atPop: 60)));

        Assert.Contains("No llega", result.Goals[0].Problem);
        Assert.Null(result.Goals[0].End);
    }

    [Fact]
    public void La_postura_sale_de_los_objetivos()
    {
        var result = Generator.Generate(Assisted("any", 50, Age("feudal_age"), Age("castle_age")));

        Assert.Equal("imperial_age", result.BuildOrder.Posture?.NextAge);
    }

    [Fact]
    public void Llega_a_Castillos_aunque_ya_no_salgan_aldeanos()
    {
        // Con 20 aldeanos el centro urbano deja de producir antes de juntar Castillos: el clic tiene que salir igual.
        var result = Generator.Generate(Assisted("any", 20, Age("feudal_age"), Age("castle_age")));

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Goals[1].End);
        AssertConsistent(result);
    }

    [Theory]
    [InlineData("franks")]
    [InlineData("incas")]
    [InlineData("huns")]
    [InlineData("chinese")]
    [InlineData("armenians")]
    public void La_BO_generada_da_lo_mismo_en_cada_civ(string civ)
    {
        var result = Generator.Generate(Assisted(civ, 100, Age("feudal_age"), Age("castle_age"), Age("imperial_age")));

        AssertConsistent(result);
        Assert.Empty(result.Problems);
        var built = result.BuildOrder.Steps.Where(s => s.Do == StepAction.Build).Select(s => s.What).ToHashSet();
        Assert.All(built, b => Assert.True(TestData.GameData.CanMake(civ, b!), $"{civ} no puede hacer {b}"));
        if (civ == "huns")
            Assert.DoesNotContain("house", built);
    }

    [Fact]
    public void Sin_granjas_en_los_datos_no_las_pide_y_lo_avisa()
    {
        // Los kitanos usan pastos, que game_data no tiene: la BO no puede pedir granjas (no validaría).
        var result = Generator.Generate(Assisted("khitans", 60, Age("feudal_age")));

        AssertConsistent(result);
        Assert.DoesNotContain(result.BuildOrder.Steps, s => s.What == "farm");
        Assert.Contains(result.Problems, p => p.Contains("granjas"));
    }

    [Theory]
    [InlineData("aztecs", 20, 2)]
    [InlineData("any", 20, 3)]
    [InlineData("franks", 20, 3)]
    [InlineData("any", 13, 3)]
    [InlineData("britons", 13, 3)]
    [InlineData("mongols", 13, 3)]
    public void Con_pocos_aldeanos_llega_a_las_edades(string civ, int until, int ages)
    {
        // Ya no salen aldeanos: el reacomodo no puede pedir mover a los que no hay, y el clic no queda esperando en la
        // cola delante del "mover" de la comida que se acaba (las dos cosas trababan la BO para siempre).
        var goals = new[] { Age("feudal_age"), Age("castle_age"), Age("imperial_age") }.Take(ages).ToArray();

        var result = Generator.Generate(Assisted(civ, until, goals));

        Assert.Empty(result.Problems);
        AssertConsistent(result);
    }

    [Theory]
    [InlineData("muisca")]
    [InlineData("britons")]
    [InlineData("huns")]
    [InlineData("hindustanis")]
    [InlineData("japanese")]
    [InlineData("lithuanians")]
    [InlineData("mapuche")]
    public void Nadie_queda_parado_mientras_sube_de_edad(string civ)
    {
        // Los edificios de la edad siguiente no se piden antes de llegar a ella: esperarían y frenarían el "mover" de la
        // comida que se acaba mientras el centro urbano investiga.
        var result = Generator.Generate(Assisted(civ, 100, Age("feudal_age"), Age("castle_age"), Age("imperial_age")));

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
    }

    [Theory]
    [InlineData(GatherSource.Hunt)]
    [InlineData(GatherSource.Berries)]
    public void Sin_jabali_o_sin_bayas_nadie_queda_parado_subiendo_a_Feudal(GatherSource missing)
    {
        var sources = new HashSet<GatherSource> { GatherSource.Sheep, GatherSource.Hunt, GatherSource.Berries, GatherSource.Farm, GatherSource.Wood, GatherSource.Gold, GatherSource.Stone };
        sources.Remove(missing);
        var maps = new Maps([new GameMap("raro", "Raro", false, sources, null)]);
        var bo = Assisted("any", 60, Age("feudal_age")) with { Map = "raro" };

        var result = new AssistedGenerator(TestData.GameData, maps).Generate(bo);

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result, maps);
    }

    private static readonly Dictionary<string, AssistedGoal[]> Sweeps = new()
    {
        ["FC"] = [Age("feudal_age"), Age("castle_age")],
        ["FCI"] = [Age("feudal_age"), Age("castle_age"), Age("imperial_age")],
        ["F22C30"] = [Age("feudal_age", atPop: 22), Age("castle_age", atPop: 30)],
        // El ejemplo de la spec (sección 2) sin los objetivos del sub-paso (d).
        ["spec"] = [Age("feudal_age"), Age("castle_age", atPop: 32), Age("imperial_age")],
    };

    [Theory]
    [InlineData(13, "FCI")]
    [InlineData(20, "FC")]
    [InlineData(60, "F22C30")]
    [InlineData(120, "spec")]
    [InlineData(60, "FUC")]
    public void En_todas_las_civs_llega_sin_parados_y_da_lo_mismo(int until, string goals)
    {
        // Las civs sin granjas en los datos (kitanos) no pueden sostener la comida.
        var validator = new BuildOrderValidator(TestData.GameData, TestData.Events);
        var failed = new List<string>();
        foreach (var civ in TestData.GameData.Civs.Keys.Where(c => TestData.GameData.CanMake(c, "farm")).Order())
        {
            // "FUC": Feudal, 10 de la primera unidad militar de Feudal que la civ tenga, y Castillos.
            var list = goals != "FUC" ? Sweeps[goals]
                : new[] { "scout_cavalry", "archer", "spearman" }.FirstOrDefault(u => TestData.GameData.CanMake(civ, u)) is { } unit
                    ? [Age("feudal_age"), Units(unit, 10, 1), Age("castle_age")]
                    : null;
            if (list is null)
                continue;
            var result = Generator.Generate(Assisted(civ, until, list));
            var again = new Planner(TestData.GameData, AssistedGenerator.Settings(TestData.GameData, RealMaps, result.BuildOrder))
                .Simulate(result.BuildOrder);
            var idle = result.Plan.Idle.Where(i => i.Reason == IdleReason.Exhausted).Sum(i => i.Duration * i.Villagers);
            var errors = validator.Validate(result.BuildOrder).Errors.Select(e => e.Message).ToList();
            if (result.Problems.Count > 0)
                failed.Add($"{civ}: {string.Join("; ", result.Problems)}");
            if (idle > 0)
                failed.Add($"{civ}: {idle:0} segundos de aldeanos parados");
            if (errors.Count > 0)
                failed.Add($"{civ}: {errors[0]}");
            if (!again.Steps.Select(s => (s.Step.Id, s.Start, s.End)).SequenceEqual(result.Plan.Steps.Select(s => (s.Step.Id, s.Start, s.End)))
                || !again.Blocks.SequenceEqual(result.Plan.Blocks) || !again.Timeline.SequenceEqual(result.Plan.Timeline) || !again.Idle.SequenceEqual(result.Plan.Idle))
                failed.Add($"{civ}: simulada de nuevo da distinto");
        }

        Assert.Empty(failed);
    }

    [Theory]
    [InlineData("any")]
    [InlineData("mayan")]
    [InlineData("mongols")]
    [InlineData("britons")]
    [InlineData("franks")]
    public void Feudal_a_22_y_Castillos_a_N_de_poblacion_llegan(string civ)
    {
        // Los números de un fast castle: el centro urbano espera en N el costo de Castillos (con su oro).
        var failed = new List<int>();
        for (var n = 24; n <= 34; n++)
        {
            var result = Generator.Generate(Assisted(civ, 60, Age("feudal_age", atPop: 22), Age("castle_age", atPop: n)));
            if (result.Problems.Count > 0)
                failed.Add(n);
            else
                AssertConsistent(result);
        }

        Assert.Empty(failed);
    }

    [Fact]
    public void El_centro_urbano_no_para()
    {
        var result = Generator.Generate(Assisted("franks", 100, Age("feudal_age"), Age("castle_age")));

        var villagerIds = VillagerSteps(result).Select(s => s.Id).ToHashSet();
        var blocks = result.Plan.Blocks.Where(b => villagerIds.Contains(b.StepId)).ToList();
        Assert.Equal(0, blocks.Where(b => b.Reason == BlockReason.Population).Sum(b => b.Duration));
        Assert.True(blocks.Where(b => b.Reason == BlockReason.Resources).Sum(b => b.Duration) <= 25,
            string.Join(", ", blocks.Select(b => $"{b.StepId} {b.Reason} {b.From}-{b.To}")));
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
    }

    [Fact]
    public void Contra_las_guias()
    {
        var differences = new List<double>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "buildorderguide"), "*.json"))
        {
            var civ = Path.GetFileNameWithoutExtension(file);
            using var guide = JsonDocument.Parse(File.ReadAllText(file));
            var root = guide.RootElement;
            if (!TestData.GameData.IsKnownCiv(civ)
                || !root.TryGetProperty("attributes", out var attributes) || attributes.EnumerateArray().All(a => a.GetString() != "fastFeudal")
                || !root.TryGetProperty("uptime", out var uptime) || !uptime.TryGetProperty("feudalAge", out var feudal)
                || !GameClock.TryParse(feudal.GetString(), out var up)
                || !root.TryGetProperty("pop", out var pop) || !pop.TryGetProperty("feudalAge", out var feudalPop))
                continue;

            var result = Generator.Generate(Assisted(civ, 40, Age("feudal_age", atPop: feudalPop.GetInt32())));
            var reached = result.Goals[0].End;
            Assert.NotNull(reached);
            differences.Add(reached!.Value - up);
            output.WriteLine($"{civ,-14} pop {feudalPop.GetInt32()}  guía {GameClock.Format(up)}  generada {GameClock.Format(reached.Value)}  ({reached.Value - up:+0;-0} s)");
        }

        Assert.NotEmpty(differences);
        var sorted = differences.Select(Math.Abs).Order().ToList();
        var median = sorted[sorted.Count / 2];
        output.WriteLine($"{differences.Count} guías, mediana {median:0} s, peor {sorted[^1]:0} s");
        Assert.True(median <= 60, $"mediana {median:0} s");
        Assert.True(sorted[^1] <= 180, $"la peor {sorted[^1]:0} s");
    }

    /// <summary>El ejemplo de la spec (sección 2) entero, con mongoles: los francos no tienen línea de sangre.</summary>
    private static BuildOrder SpecExample() => WithEco(Assisted("mongols", 120,
        Age("feudal_age"), Units("scout_cavalry", 20, 2), Age("castle_age", atPop: 32),
        new AssistedGoal { TownCenters = 3 }, new AssistedGoal { Tech = "bloodlines" }, Age("imperial_age")));

    [Fact]
    public void El_ejemplo_de_la_spec_entero()
    {
        var result = Generator.Generate(SpecExample());

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
        output.WriteLine(string.Join(" · ", result.Goals.Select(g => $"{g.Index}: {(g.End is { } e ? GameClock.Format(e) : "-")}")));
    }

    [Fact]
    public void El_criterio_de_salida_con_francos()
    {
        // Spec, sección 10: francos, aldeanos hasta 120, Feudal, 20 exploradores con 2 establos, Castillos e Imperial.
        var result = Generator.Generate(Assisted("franks", 120,
            Age("feudal_age"), Units("scout_cavalry", 20, 2), Age("castle_age"), Age("imperial_age")));

        Assert.Empty(result.Problems);
        Assert.DoesNotContain(result.Plan.Idle, i => i.Reason == IdleReason.Exhausted);
        AssertConsistent(result);
    }

    [Fact]
    public void Agregar_un_objetivo_al_final_no_atrasa_a_los_de_arriba()
    {
        var goals = new[] { Age("feudal_age"), Units("scout_cavalry", 20, 2), Age("castle_age") };
        var before = Generator.Generate(Assisted("franks", 120, goals));
        var after = Generator.Generate(Assisted("franks", 120, [.. goals, Age("imperial_age")]));

        for (var i = 0; i < goals.Length; i++)
            Assert.True(after.Goals[i].End <= before.Goals[i].End + 10, $"objetivo {i}: {after.Goals[i].End} contra {before.Goals[i].End}");
    }

    [Fact]
    public void Genera_rapido()
    {
        var bo = SpecExample();
        Generator.Generate(bo);

        var watch = Stopwatch.StartNew();
        Generator.Generate(bo);
        watch.Stop();

        output.WriteLine($"{watch.ElapsedMilliseconds} ms");
        Assert.True(watch.ElapsedMilliseconds < 250, $"{watch.ElapsedMilliseconds} ms");
    }
}
