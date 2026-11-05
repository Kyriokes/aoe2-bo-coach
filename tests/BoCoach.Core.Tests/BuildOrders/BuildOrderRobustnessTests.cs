using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Engine;
using BoCoach.Core.Web;

namespace BoCoach.Core.Tests.BuildOrders;

/// <summary>
/// Lo que puede venir en el JSON de una BO o de una guía. La web le pasa al Core lo que se pega en el editor y la app baja
/// lo que se publicó, y un null pasa zod y la base: nada de eso puede tirar una excepción que nadie espera.
/// </summary>
public class BuildOrderRobustnessTests
{
    private const string FullBuildOrder = """
        {
          "schema_version": 2, "id": "x", "name": "Completa", "civ": "britons", "map": "arabia", "author": "a", "description": "d", "tags": ["a", "b"],
          "steps": [
            { "id": "s1", "do": "build", "what": "house", "count": 2, "builders": 3, "assign": "sheep" },
            { "id": "s2", "do": "train", "what": "villager", "count": 6, "assign": "sheep", "confirm_by": "villager_created", "note": "n", "group": "g" },
            { "id": "s3", "do": "build", "what": "lumber_camp", "from": "sheep", "assign": "wood", "after": ["s1"], "group": "g" },
            { "id": "s4", "do": "move", "count": 1, "from": "sheep", "assign": "wood", "at_pop": 7, "at_time": 60 },
            { "id": "s5", "do": "research", "what": "loom" }
          ],
          "targets": { "feudal_click_time": 540, "castle_click_time": 900, "villagers_at_feudal": { "food": 14, "wood": 6, "gold": 0, "stone": 0 } },
          "posture": { "queue": ["villager", "archer"], "next_age": "feudal_age" },
          "branches": [ { "on": "under_attack", "before": 300, "note": "n", "steps": [ { "id": "b0", "do": "research", "what": "loom" }, { "id": "b1", "do": "train", "what": "archer", "count": 2, "after": ["b0"] } ] } ]
        }
        """;

    private const string AssistedBuildOrder = """
        {
          "schema_version": 2, "name": "Asistida", "civ": "franks",
          "assisted": {
            "opening": { "houses": true, "sheep": 6, "boar": true, "berries": "auto" },
            "villagers": { "until": 25 },
            "eco_upgrades": true,
            "goals": [ { "unit": "scout_cavalry", "count": 2, "buildings": 1 }, { "town_centers": 2 }, { "tech": "loom" }, { "age": "feudal_age", "at_pop": 20 } ]
          }
        }
        """;

    private const string RtsOverlayGuide = """
        {
          "name": "Guía", "civilization": "Franks", "author": "a", "source": "s", "description": "d",
          "build_order": [
            { "villager_count": 6, "resources": { "food": 6, "wood": 0, "gold": 0, "stone": 0 },
              "notes": [ "Create 6 @resource/MaleVillDE.jpg@ on @animal/Sheep_aoe2DE.png@", "Lure @animal/Boar_aoe2DE.png@", "(un consejo)", "-----", "Bailá la macarena" ] },
            { "villager_count": 8, "resources": { "food": 6, "wood": 2, "gold": 0, "stone": 0 },
              "notes": [ "Create 2 @resource/MaleVillDE.jpg@ on @resource/Aoe2de_wood.png@", "@resource/Aoe2de_hammer.png@ 2 barracks", "Research loom", "@age/FeudalAgeIconDE_alpha.png@ 50% --> @building/Barracks_aoe2DE.png@" ] },
            { "villager_count": 9, "resources": { "food": 6, "wood": 2, "gold": 1, "stone": 0 },
              "notes": [ "Move 1 @resource/MaleVillDE.jpg@ from @animal/Sheep_aoe2DE.png@ to @resource/Aoe2de_gold.png@", "Create 1 @resource/MaleVillDE.jpg@ on @mill/FarmDE.png@", "Click up to feudalAge", "Train 3 archer", "Before Feudal" ] }
          ]
        }
        """;

    private const string BuildOrderGuide = """
        {
          "title": "Guía", "civilization": "Franks", "author": "a", "id": "abc", "description": "d",
          "build": [
            { "type": "newVillagers", "task": "sheep", "count": 3, "buildings": [ { "type": "house", "count": 2 } ], "resources": { "food": 6, "wood": 0, "gold": 0, "stone": 0, "build": 0 } },
            { "type": "newVillagers", "task": "wood", "count": 2, "buildings": [ { "type": "lumberCamp", "count": 1 } ], "resources": { "food": 6, "wood": 2, "gold": 0, "stone": 0 } },
            { "type": "moveVillagers", "from": "sheep", "to": "gold", "count": 1, "buildings": [ { "type": "miningCamp", "count": 1 } ] },
            { "type": "build", "buildings": [ { "type": "barracks", "count": 1 }, { "type": "farm", "count": 2 } ] },
            { "type": "research", "tech": ["loom", "doubleBitAxe"] },
            { "type": "ageUp", "age": "feudalAge" },
            { "type": "newAge", "age": "feudalAge" },
            { "type": "trainUnit", "unit": "archer", "count": 3 },
            { "type": "lure", "animal": "boar", "count": 2 },
            { "type": "custom", "text": "algo" },
            { "type": "decision", "text": "otra cosa" },
            { "type": "trade", "action": "buy", "count": 100, "resource": "food" },
            { "type": "collectGold" }
          ]
        }
        """;

    private static readonly string[] WeirdValues =
    [
        "null", "true", "0", "-1", "1.5", "1e400", "2147483647", "2147483648", "-2147483648", "\"\"", "\"x\"", "\"\\ud800\"", "[]", "[null]", "[[]]", "[5]", "{}", """{"a":null}""",
    ];

    /// <summary>Cada lugar del JSON donde hay un valor, en orden: cómo cambiarlo.</summary>
    private static IEnumerable<Action<JsonNode?>> Slots(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                yield return value => obj[key] = value;
                foreach (var slot in Slots(obj[key]))
                    yield return slot;
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var index = i;
                yield return value => array[index] = value;
                foreach (var slot in Slots(array[index]))
                    yield return slot;
            }
        }
    }

    /// <summary>El JSON con cada valor (y cada elemento de cada lista) cambiado por cada valor raro, de a uno.</summary>
    private static IEnumerable<string> Mutations(string json)
    {
        // Por un texto que después se reemplaza: un "\ud800" no se puede volver a escribir con JsonNode.
        const string placeholder = "@@raro@@";
        var total = Slots(JsonNode.Parse(json)).Count();
        for (var target = 0; target < total; target++)
        {
            var copy = JsonNode.Parse(json)!;
            Slots(copy).ElementAt(target)(JsonValue.Create(placeholder));
            var text = copy.ToJsonString();
            foreach (var weird in WeirdValues)
                yield return text.Replace($"\"{placeholder}\"", weird);
        }
    }

    /// <summary>Todo lo que la web y la app hacen con el JSON de una BO: nada de esto puede tirar.</summary>
    private static void UseEverything(string json)
    {
        var read = JsonNode.Parse(BuildOrderImport.ReadJson(json, TestData.GameData))!;
        Assert.NotNull(read["ok"]);
        var analysis = JsonNode.Parse(BuildOrderAnalysis.Analyze(json, TestData.GameData, TestData.Events))!;
        Assert.NotNull(analysis["ok"]);

        BuildOrder bo;
        try
        {
            bo = BuildOrderSerializer.Parse(json);
        }
        catch (BuildOrderFormatException)
        {
            return; // lo que se entiende por el error
        }
        new BuildOrderValidator(TestData.GameData, TestData.Events).Validate(bo);
        BuildOrderSummary.Of(bo);
        BuildOrderPicker.Fits(bo, new MatchSetup("franks", null));
        Posture.Effective(bo, TestData.GameData);
        BuildOrderSerializer.Parse(BuildOrderSerializer.Serialize(bo));
    }

    /// <summary>Lo que hace la web con una guía: la importa, y lo que sale lo analiza.</summary>
    private static void ImportAndAnalyze(string json)
    {
        var read = JsonNode.Parse(BuildOrderImport.ReadJson(json, TestData.GameData))!;
        if (read["ok"]!.GetValue<bool>())
            Assert.NotNull(JsonNode.Parse(BuildOrderAnalysis.Analyze(read["build_order"]!.ToJsonString(), TestData.GameData, TestData.Events))!["ok"]);
    }

    private static void AssertNoneThrows(IEnumerable<string> jsons, Action<string> use)
    {
        var failures = new List<string>();
        var tried = 0;
        foreach (var json in jsons)
        {
            tried++;
            try
            {
                use(json);
            }
            catch (Exception e)
            {
                failures.Add($"{e.GetType().Name}: {e.Message}\n  en {e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}\n  con {json}");
            }
        }
        Assert.True(tried > 100, $"solo {tried} casos");
        Assert.True(failures.Count == 0, $"{failures.Count} de {tried} tiraron:\n{string.Join("\n", failures.Take(12))}");
    }

    [Fact]
    public void Cualquier_valor_raro_en_una_BO_no_tira() => AssertNoneThrows(Mutations(FullBuildOrder), UseEverything);

    [Fact]
    public void Cualquier_valor_raro_en_una_BO_asistida_no_tira() => AssertNoneThrows(Mutations(AssistedBuildOrder), UseEverything);

    [Fact]
    public void Cualquier_valor_raro_en_una_guia_de_RTS_Overlay_no_tira() => AssertNoneThrows(Mutations(RtsOverlayGuide), ImportAndAnalyze);

    [Fact]
    public void Cualquier_valor_raro_en_una_guia_de_buildorderguide_no_tira() => AssertNoneThrows(Mutations(BuildOrderGuide), ImportAndAnalyze);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("5")]
    [InlineData("\"x\"")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("/* nada */")]
    [InlineData("\uFEFF{}")]
    [InlineData("\0")]
    [InlineData("""{"schema_version":1,"steps":[null]}""")]
    [InlineData("""{"schema_version":2,"steps":[null]}""")]
    [InlineData("""{"schema_version":1,"name":"x","steps":[{"id":"a","do":"train","what":"villager","assign":"sheep","after":null}]}""")]
    [InlineData("""{"schema_version":1,"name":"x","steps":[{"id":"a","do":"train","what":"villager","assign":"sheep","after":[null]}]}""")]
    [InlineData("""{"schema_version":2,"name":"\ud800"}""")]
    [InlineData("""{"schema_version":2,"steps":[{"note":"\ud800"}]}""")]
    [InlineData("""{"build_order":[{"notes":["\ud800"]}],"name":"\ud800"}""")]
    [InlineData("""{"build":[{"type":"\ud800"}],"civilization":"\ud800"}""")]
    public void Un_texto_raro_no_tira(string json) => UseEverything(json);

    // Un carácter partido tal cual en el texto (no como "\ud800"): JavaScript lo puede mandar, y JsonNode tira ArgumentException.
    [Theory]
    [InlineData("""{"schema_version":2,"name":"{0}"}""")]
    [InlineData("""{"build_order":[],"name":"{0}"}""")]
    [InlineData("""{"build":[],"civilization":"{0}"}""")]
    public void Una_mitad_de_un_caracter_tal_cual_en_el_texto_no_tira(string template)
    {
        var json = template.Replace("{0}", ((char)0xD800).ToString());

        var read = JsonNode.Parse(BuildOrderImport.ReadJson(json, TestData.GameData))!;
        Assert.False(read["ok"]!.GetValue<bool>());
        var analysis = JsonNode.Parse(BuildOrderAnalysis.Analyze(json, TestData.GameData, TestData.Events))!;
        Assert.False(analysis["ok"]!.GetValue<bool>());
    }

    [Fact]
    public void Un_JSON_mas_profundo_que_el_tope_no_tira()
    {
        var json = """{"schema_version":2,"x":""" + new string('[', 200) + new string(']', 200) + "}";

        UseEverything(json);
    }

    private static readonly CivStyles Styles = CivStyles.Load(Path.Combine(AppContext.BaseDirectory, "civ_styles.json"));

    // La app del suscriptor: lo que corta el arranque es que la sugerencia o el coach toquen un null.
    [Fact]
    public void Con_nulls_en_la_BO_la_sugerencia_y_el_coach_andan()
    {
        var bo = BuildOrderSerializer.Parse("""
            { "schema_version": 2, "id": null, "name": "x", "civ": null, "tags": null,
              "steps": [ { "id": "a", "do": "train", "what": "villager", "count": 6, "assign": "sheep", "after": null } ],
              "posture": { "queue": null }, "branches": [ { "on": "under_attack", "steps": null } ] }
            """);

        Assert.True(BuildOrderPicker.Fits(bo, new MatchSetup("franks", null)));
        Assert.NotNull(BuildOrderSuggester.Suggest([bo with { Civ = "franks" }], new MatchSetup("franks", null), Styles));
        Assert.NotNull(new Coach(bo, TestData.GameData, TestData.Events));
    }
}
