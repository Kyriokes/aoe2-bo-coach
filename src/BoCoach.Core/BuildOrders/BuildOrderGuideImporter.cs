using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.Data;

namespace BoCoach.Core.BuildOrders;

/// <summary>
/// Convierte una guía de buildorderguide.com en su formato propio (el documento de la colección pública
/// "published-builds"): <c>title</c>, <c>civilization</c>, <c>author</c> y <c>build</c>, una lista de pasos con
/// <c>type</c> (newVillagers, moveVillagers, build, research, ageUp, newAge, trainUnit, lure, custom, decision, trade,
/// collectGold) y en cada uno los aldeanos por recurso después del paso (<c>resources</c>). Es más fiel que el
/// exportable de RTS Overlay, que pasa todo a notas de texto. Lo que la guía da por sabido lo agrega el mismo
/// conversor de <see cref="RtsOverlayImporter"/>.
/// </summary>
public static class BuildOrderGuideImporter
{
    public const string Site = "https://www.buildorderguide.com";

    /// <summary>Si el JSON parece una guía de buildorderguide.com (tiene "build" como lista y "civilization").</summary>
    public static bool IsBuildOrderGuide(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: Options) is JsonObject obj && obj["build"] is JsonArray && obj["civilization"] is JsonValue;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <param name="civ">La civ para la que se convierte (clave de game_data); null = la de la guía. Con otra civ la
    /// conversión se adapta (sin casas para los hunos, carro de mulas o asentamiento en lugar de campamentos).</param>
    public static ImportResult Convert(string json, GameData data, string? civ = null)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: Options) as JsonObject
                ?? throw new BuildOrderFormatException("La build order tiene que ser un objeto JSON.");
        }
        catch (JsonException e)
        {
            throw new BuildOrderFormatException($"JSON inválido (línea {e.LineNumber + 1}): {e.Message}", e);
        }
        if (root["build"] is not JsonArray guide || guide.Count == 0)
            throw new BuildOrderFormatException("No tiene pasos ('build').");
        return Convert(root, guide, data, civ);
    }

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Los grupos de civs de la página ("Meso", "South American") no son una civ: quedan para cualquiera.</summary>
    private static readonly HashSet<string> CivGroups = new(StringComparer.OrdinalIgnoreCase) { "Generic", "Meso", "South American" };

    private static ImportResult Convert(JsonObject root, JsonArray guide, GameData data, string? civOverride)
    {
        var civText = RtsOverlayImporter.Text(root["civilization"]);
        var civ = civOverride ?? (civText is null || CivGroups.Contains(civText) ? "any" : RtsOverlayImporter.Civ(civText, data));
        var converter = new RtsOverlayImporter.Converter(data, civ);
        if (civOverride is null && civ == "any" && civText is not null && !CivGroups.Contains(civText))
            converter.Warn($"No conozco la civ \"{civText}\": quedó para cualquier civ.");
        var steps = guide.OfType<JsonObject>().ToList();
        converter.Begin(steps.Where(s => Type(s) == "newVillagers").Select(s => Source(Str(s["task"]), converter)).FirstOrDefault(s => s is not null) ?? GatherSource.Sheep);
        var initialHouses = 2; // las de los 3 aldeanos iniciales, que el conversor ya agregó
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var guideStep = i + 1;
            var resources = Resources(step["resources"] as JsonObject);
            var total = resources is null ? (int?)null : new[] { "food", "wood", "gold", "stone" }.Sum(k => RtsOverlayImporter.Number(resources[k]) ?? 0);
            switch (Type(step))
            {
                case "newVillagers":
                {
                    var to = Source(Str(step["task"]), converter);
                    var buildings = Buildings(step);
                    BuildFirst(buildings, converter, ref initialHouses);
                    var camps = Camps(converter);
                    // "6 en ovejas" cuenta a los 3 iniciales: lo nuevo es lo que la guía dice de más.
                    var count = RtsOverlayImporter.Number(step["count"]) ?? 1;
                    if (total is { } t)
                        count = Math.Min(count, Math.Max(0, t - converter.VillagerCount));
                    if (to is { } source)
                        converter.Villagers(count, source);
                    else
                        converter.Remark($"Aldeanos nuevos: {Str(step["task"])}", $"Paso {guideStep} de la guía: no conozco la tarea \"{Str(step["task"])}\".");
                    BuildCamps(buildings, camps, converter);
                    break;
                }
                case "moveVillagers":
                {
                    var buildings = Buildings(step);
                    BuildFirst(buildings, converter, ref initialHouses);
                    var camps = Camps(converter);
                    var from = Source(Str(step["from"]), converter);
                    var to = Source(Str(step["to"]), converter);
                    if (from is { } f && to is { } d && f != d)
                        converter.Move(RtsOverlayImporter.Number(step["count"]) ?? 1, f, d);
                    else if (from is null || to is null)
                        converter.Remark($"Mové aldeanos de {Str(step["from"])} a {Str(step["to"])}", $"Paso {guideStep} de la guía: no conozco \"{Str(step["from"])}\" o \"{Str(step["to"])}\".");
                    BuildCamps(buildings, camps, converter);
                    break;
                }
                case "build":
                {
                    var buildings = Buildings(step);
                    BuildFirst(buildings, converter, ref initialHouses);
                    BuildCamps(buildings, [], converter);
                    break;
                }
                case "research":
                    foreach (var tech in (step["tech"] as JsonArray ?? []).Select(Str))
                        Research(tech, converter, guideStep);
                    break;
                case "ageUp":
                    Research(Str(step["age"]), converter, guideStep);
                    break;
                case "trainUnit":
                    Train(Str(step["unit"]), RtsOverlayImporter.Number(step["count"]) ?? 1, converter, guideStep);
                    break;
                case "lure":
                    converter.Remark($"Atraé {(RtsOverlayImporter.Number(step["count"]) is { } n and > 1 ? $"{n} " : "")}{Animal(Str(step["animal"]))}");
                    break;
                case "custom" or "decision":
                    if (RtsOverlayImporter.Text(step["text"]) is { } text)
                        converter.Remark(text.Replace('\n', ' '));
                    break;
                case "trade":
                    converter.Remark($"Mercado: {(Str(step["action"]) == "buy" ? "comprá" : "vendé")} {RtsOverlayImporter.Number(step["count"]) ?? 100} de {Str(step["resource"])}");
                    break;
                case "collectGold":
                    converter.Remark("Juntá un poco de oro con un aldeano (lo justo para lo que sigue)");
                    break;
                case "newAge":
                    // La llegada a la edad: el coach la ve en el log. Algunas guías no anotan el clic: va acá.
                    if (RtsOverlayImporter.KeyOf(Str(step["age"]) ?? "") is { } age && converter.TryEntity(age, out _) && !converter.Has(StepAction.Research, age))
                        converter.Research(age);
                    break;
                default:
                    converter.Warn($"Paso {guideStep} de la guía: no conozco el tipo \"{Type(step)}\".");
                    break;
            }
            converter.EndStep(guideStep, total, resources);
        }

        var id = RtsOverlayImporter.Text(root["id"]);
        var description = string.Join(" ", new[]
        {
            RtsOverlayImporter.Text(root["description"])?.Replace('\n', ' '),
            id is null ? null : $"Fuente: {Site}/builds/{id}.",
        }.Where(t => t is not null));
        return converter.Finish(RtsOverlayImporter.Text(root["title"]) ?? "BO importada", civ, RtsOverlayImporter.Text(root["author"]), description);
    }

    private static string? Type(JsonObject step) => Str(step["type"]);

    private static string? Str(JsonNode? node) => RtsOverlayImporter.Text(node);

    /// <summary>
    /// Los aldeanos por recurso del paso, con los que construyen ("build") sumados a la madera: el conversor manda a
    /// los constructores desde la madera y los devuelve ahí.
    /// </summary>
    private static JsonObject? Resources(JsonObject? r)
    {
        if (r is null)
            return null;
        int N(string k) => RtsOverlayImporter.Number(r[k]) ?? 0;
        return new JsonObject { ["food"] = N("food"), ["wood"] = N("wood") + N("build"), ["gold"] = N("gold"), ["stone"] = N("stone") };
    }

    /// <summary>
    /// La fuente de una tarea de la página. Los constructores ("build") y los que van adelante ("forward") se cuentan en
    /// la madera; "food" a secas es la fuente de comida con más aldeanos.
    /// </summary>
    private static GatherSource? Source(string? task, RtsOverlayImporter.Converter converter) => task switch
    {
        null => null,
        "foodUnderTC" or "chicken" => GatherSource.Sheep,
        "fox" => GatherSource.Hunt,
        "stragglerTree" or "build" or "forward" => GatherSource.Wood,
        "farms" => GatherSource.Farm,
        "food" => new[] { GatherSource.Sheep, GatherSource.Hunt, GatherSource.Berries, GatherSource.Farm, GatherSource.Fish }.MaxBy(converter.On),
        _ => RtsOverlayImporter.SourceOf(task),
    };

    private static List<(string Key, int Count)> Buildings(JsonObject step) =>
        (step["buildings"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(b => (Key: RtsOverlayImporter.KeyOf(Str(b["type"]) ?? "") ?? "", Count: RtsOverlayImporter.Number(b["count"]) ?? 1))
            // Las granjas las hace cada aldeano que va a granjas (el conversor agrega una por aldeano): las de la lista
            // de la página son esas mismas.
            .Where(b => b.Key.Length > 0 && b.Key != "farm" && b.Count > 0)
            .ToList();

    private static bool IsCamp(string key) => key is "lumber_camp" or "mill" or "mining_camp";

    private static HashSet<string> Camps(RtsOverlayImporter.Converter converter) =>
        new[] { "lumber_camp", "mill", "mining_camp" }.Where(converter.HasCamp).ToHashSet();

    /// <summary>
    /// Los edificios que no son campamentos van antes que los aldeanos del paso: así las casas de la guía cuentan y el
    /// conversor no agrega otras. Las 2 primeras casas son las de los aldeanos iniciales, que ya están.
    /// </summary>
    private static void BuildFirst(List<(string Key, int Count)> buildings, RtsOverlayImporter.Converter converter, ref int initialHouses)
    {
        foreach (var (key, count) in buildings.Where(b => !IsCamp(b.Key)))
        {
            var n = count;
            if (key == "house")
            {
                var skip = Math.Min(n, initialHouses);
                (n, initialHouses) = (n - skip, initialHouses - skip);
            }
            if (n > 0)
                Build(key, n, converter);
        }
        initialHouses = 0; // solo cuentan las del primer paso que trae casas
    }

    /// <summary>Los campamentos van después: el primero lo agrega el conversor con el aldeano que llega al recurso.</summary>
    private static void BuildCamps(List<(string Key, int Count)> buildings, HashSet<string> before, RtsOverlayImporter.Converter converter)
    {
        foreach (var (key, count) in buildings.Where(b => IsCamp(b.Key)))
        {
            var n = count - (converter.HasCamp(key) && !before.Contains(key) ? 1 : 0);
            if (n > 0)
                Build(key, n, converter);
        }
    }

    private static void Build(string key, int count, RtsOverlayImporter.Converter converter)
    {
        if (converter.TryEntity(key, out var entity) && entity.Kind == EntityKind.Building)
            converter.Build(count, key);
        else
            converter.Remark($"Construí {(count > 1 ? $"{count} " : "")}{Spanish(key)}", $"La guía construye \"{key}\", que el coach no conoce (quedó como nota).");
    }

    private static void Research(string? name, RtsOverlayImporter.Converter converter, int guideStep)
    {
        var key = RtsOverlayImporter.KeyOf(name ?? "");
        if (key is not null && converter.TryEntity(key, out var tech) && tech.Kind == EntityKind.Tech)
            converter.Research(key);
        else
            converter.Remark($"Investigá {Spanish(key ?? name ?? "?")}", $"Paso {guideStep} de la guía: no conozco la tecnología \"{name}\" (quedó como nota).");
    }

    /// <summary>Los hombres de armas de la página son la milicia (el hombre de armas es su mejora).</summary>
    private static void Train(string? name, int count, RtsOverlayImporter.Converter converter, int guideStep)
    {
        var key = RtsOverlayImporter.KeyOf(name ?? "");
        if (key == "man_at_arms")
            key = "militia";
        if (key is not null && key != "villager" && converter.TryEntity(key, out var unit) && unit.Kind == EntityKind.Unit)
            converter.Train(unit, count);
        else
            converter.Remark($"Entrená {count} {Spanish(key ?? name ?? "?")}", $"Paso {guideStep} de la guía: no conozco la unidad \"{name}\" (quedó como nota).");
    }

    private static string Animal(string? animal) => animal switch
    {
        "boar" => "jabalí",
        "deer" => "ciervo",
        _ => animal ?? "",
    };

    private static string Spanish(string key) => StepText.HasName(key) ? StepText.Name(key) : key;
}
