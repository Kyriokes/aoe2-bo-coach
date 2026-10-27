using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

/// <summary>
/// D7: una BO por civ desde buildorderguide.com. <c>fetch</c> baja la colección pública de guías; <c>civs</c> elige
/// una por civ, guarda la guía original en los fixtures y la convertida en build_orders/civs.
/// </summary>
internal static class CivGuides
{
    private const string Collection =
        "https://firestore.googleapis.com/v1/projects/build-order-guide/databases/(default)/documents/published-builds?pageSize=300";

    /// <summary>Baja todas las guías publicadas (la misma colección que lee la página sin iniciar sesión) como JSON plano.</summary>
    public static async Task<int> Fetch(string output)
    {
        using var http = new HttpClient();
        var all = new JsonArray();
        string? token = null;
        do
        {
            var page = JsonNode.Parse(await http.GetStringAsync(Collection + (token is null ? "" : $"&pageToken={Uri.EscapeDataString(token)}")))!.AsObject();
            foreach (var doc in page["documents"]?.AsArray() ?? [])
                all.Add(Plain(new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = doc!["fields"]!.DeepClone() } }));
            token = page["nextPageToken"]?.GetValue<string>();
        }
        while (token is not null);
        File.WriteAllText(output, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"{all.Count} guías → {output}");
        return 0;
    }

    /// <summary>Un valor de Firestore ({"stringValue": "x"}, {"mapValue": {"fields": ...}}) como JSON común.</summary>
    private static JsonNode? Plain(JsonNode? typed)
    {
        if (typed is not JsonObject o || o.Count == 0)
            return null;
        var (kind, value) = o.First();
        return kind switch
        {
            "mapValue" => new JsonObject((value?["fields"]?.AsObject() ?? []).Select(kv => KeyValuePair.Create(kv.Key, Plain(kv.Value)))),
            "arrayValue" => new JsonArray((value?["values"]?.AsArray() ?? []).Select(Plain).ToArray()),
            "integerValue" => JsonValue.Create(long.Parse(value!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)),
            "nullValue" => null,
            _ => value?.DeepClone(),
        };
    }

    /// <summary>Lo que se sabe de una guía para elegirla.</summary>
    private sealed record Candidate(JsonObject Guide, string Id, string Civ, ImportResult Import, string? Style, double Feudal, double Score, string Why);

    /// <summary>Los grupos de la página y las civs que cubren.</summary>
    private static readonly Dictionary<string, string[]> Groups = new()
    {
        ["Meso"] = ["aztecs", "mayan", "incas"],
        ["South American"] = ["mapuche", "muisca", "tupi"],
    };

    public static int Civs(string guidesPath, string[] args)
    {
        var data = GameDataLoader.LoadLatest(Text(args, "--game-data", Path.Combine("data", "game_data")));
        var styles = CivStyles.Load(Path.Combine("data", "civ_styles.json"));
        var outDir = Text(args, "--out", Path.Combine("build_orders", "civs"));
        var fixtures = Text(args, "--fixtures", Path.Combine("tests", "fixtures", "buildorderguide"));
        var survey = args.Contains("--survey");
        var guides = JsonNode.Parse(File.ReadAllText(guidesPath))!.AsArray().OfType<JsonObject>().ToList();
        var planner = new Planner(data);
        var validator = new BuildOrderValidator(data);

        var candidates = new List<Candidate>();
        var rejected = new Dictionary<string, int>();
        var dump = args.Contains("--rejected") ? Text(args, "--rejected", "") : null;
        JsonObject? current = null;
        void Reject(string why)
        {
            rejected[why] = rejected.GetValueOrDefault(why) + 1;
            if (dump is { Length: > 0 } && rejected[why] <= 2 && current is not null)
            {
                Directory.CreateDirectory(dump);
                File.WriteAllText(Path.Combine(dump, $"{string.Concat(why.Where(char.IsLetterOrDigit))}-{rejected[why]}.json"), current.ToJsonString());
            }
        }
        foreach (var guide in guides)
        {
            current = guide;
            var attributes = (guide["attributes"] as JsonArray ?? []).Select(Str).ToHashSet();
            if (attributes.Contains("meme") || attributes.Contains("water"))
            {
                Reject("broma o agua");
                continue;
            }
            ImportResult import;
            try
            {
                import = BuildOrderGuideImporter.Convert(guide.ToJsonString(), data);
            }
            catch (BuildOrderFormatException)
            {
                Reject("formato");
                continue;
            }
            var bo = import.BuildOrder;
            if (validator.Validate(bo).Errors.FirstOrDefault() is { } error)
            {
                Reject($"inválida: {Generic(error.Message)}");
                continue;
            }
            var plan = planner.Simulate(bo);
            if (!plan.Completed || plan.ClickTime("feudal_age") is not { } feudal)
            {
                var block = plan.Blocks.LastOrDefault();
                var what = block is null ? null : bo.Steps.FirstOrDefault(st => st.Id == block.StepId)?.What;
                Reject(plan.Completed ? "sin Feudal" : $"no se completa ({block?.Reason} {block?.Missing} en {what})");
                continue;
            }
            var civText = Str(guide["civilization"]) ?? "Generic";
            var style = Opening(bo, attributes);
            var rating = Num(guide["avg_rating"]) ?? 0;
            var votes = Num(guide["number_of_ratings"]) ?? 0;
            // Las estrellas pesan según los votos (una sola de 5 vale 2,5); cada aviso del importador es algo que el coach no
            // va a seguir; arena y torres no son la apertura de todos los días.
            var score = rating * votes / (votes + 1) - 0.4 * Math.Min(import.Warnings.Count, 10)
                - (attributes.Contains("arena") ? 3 : 0) - (bo.Name.Contains("tower", StringComparison.OrdinalIgnoreCase) ? 3 : 0)
                // Simulada con Feudal muy tarde: casi siempre es algo que no se convirtió bien (un recurso sin aldeanos).
                - Math.Max(0, feudal - (style == "fast_castle" ? 660 : 570)) / 60;
            candidates.Add(new Candidate(guide, Str(guide["id"]) ?? "", civText, import, style, feudal, score,
                $"{rating:0.#}★ ({votes}), {import.Warnings.Count} avisos"));
        }
        Console.WriteLine($"{guides.Count} guías, {candidates.Count} se importan, validan y se simulan completas.");
        foreach (var (why, count) in rejected.OrderByDescending(r => r.Value))
            Console.WriteLine($"  {count,4} descartadas: {why}");

        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(fixtures);
        foreach (var civ in data.Civs.Keys.Order())
        {
            var typical = styles.Of(civ);
            double Fit(Candidate c) => c.Score + (c.Style is null ? -4 : typical.Count == 0 ? 0 : typical[0] == c.Style ? 4 : typical.Contains(c.Style) ? 2.5 : -2);
            // La propia gana si encaja; si no, la del grupo y después la genérica, que cargan con un poco de desventaja
            // porque no aprovechan la civ.
            var options = candidates.Where(c => c.Import.BuildOrder.Civ == civ).Select(c => (c, Fit: Fit(c), Kind: "propia"))
                .Concat(candidates.Where(c => Groups.TryGetValue(c.Civ, out var members) && members.Contains(civ)).Select(c => (c, Fit: Fit(c) - 2.5, Kind: "del grupo")))
                .Concat(candidates.Where(c => c.Civ == "Generic").Select(c => (c, Fit: Fit(c) - 5, Kind: "genérica")))
                .ToList();
            // Con la civ puesta: que no pida nada que la civ no tenga (exploradores a los mesoamericanos, casas a los
            // hunos) y que se siga simulando completa con sus bonificaciones.
            // La guía se vuelve a convertir para la civ: sin casas para los hunos, carro de mulas o asentamiento en lugar
            // de campamentos.
            Candidate? best = null;
            var kind = "";
            foreach (var option in options.OrderByDescending(o => o.Fit))
            {
                var import = BuildOrderGuideImporter.Convert(option.c.Guide.ToJsonString(), data, civ);
                if (!validator.Validate(import.BuildOrder).IsValid || planner.Simulate(import.BuildOrder) is not { Completed: true } plan)
                    continue;
                best = option.c with { Import = import, Feudal = plan.ClickTime("feudal_age") ?? option.c.Feudal };
                kind = option.Kind;
                break;
            }
            if (best is null)
            {
                Console.WriteLine($"{civ,-12} sin guía");
                continue;
            }
            Console.WriteLine($"{civ,-12} {kind,-9} {best.Import.BuildOrder.Name} ({best.Style ?? "?"}, típica {string.Join("/", typical)}) {best.Why}, Feudal {BoCoach.Core.GameClock.Format(best.Feudal)}");
            if (survey)
            {
                foreach (var warning in best.Import.Warnings)
                    Console.WriteLine($"    aviso: {warning}");
                continue;
            }
            Save(best, civ, kind, data, outDir, fixtures);
        }
        return 0;
    }

    private static void Save(Candidate best, string civ, string kind, GameData data, string outDir, string fixtures)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(Path.Combine(fixtures, $"{civ}.json"), best.Guide.ToJsonString(options));
        var bo = best.Import.BuildOrder;
        var civName = data.Civ(civ).Names.TryGetValue("es", out var es) ? es : civ;
        var tags = new List<string> { "importada", "buildorderguide" };
        if (BuildOrderSuggester.StyleOf(bo) is { } style)
            tags.Add(style);
        if (kind != "propia")
            tags.Add("genérica");
        bo = bo with
        {
            Id = $"bog-{civ}",
            Civ = civ,
            Name = kind == "propia" ? bo.Name : $"{bo.Name} ({civName})",
            Tags = tags,
            Description = kind == "propia" ? bo.Description : $"Guía {(kind == "genérica" ? "genérica" : $"de {best.Civ}")} con la civ puesta: la página no tiene una para {civName}. {bo.Description}",
        };
        BuildOrderSerializer.Save(bo, Path.Combine(outDir, $"{civ}.json"));
    }

    /// <summary>
    /// Vuelve a convertir la guía de <paramref name="civ"/> que quedó en los fixtures, con el importador de ahora, y la
    /// guarda con el id, el nombre, la descripción, el autor y las etiquetas de la que ya estaba (cuando cambia el
    /// importador, sin volver a elegir entre todas las guías). La mejorada se arma después con <c>improve --civ</c>.
    /// </summary>
    public static int Reimport(string civ, string[] args)
    {
        var data = GameDataLoader.LoadLatest(Text(args, "--game-data", Path.Combine("data", "game_data")));
        var outDir = Text(args, "--out", Path.Combine("build_orders", "civs"));
        var fixtures = Text(args, "--fixtures", Path.Combine("tests", "fixtures", "buildorderguide"));
        var path = Path.Combine(outDir, $"{civ}.json");
        var old = BuildOrderSerializer.Load(path);
        var import = BuildOrderGuideImporter.Convert(File.ReadAllText(Path.Combine(fixtures, $"{civ}.json")), data, civ);
        var bo = import.BuildOrder with { Id = old.Id, Civ = civ, Name = old.Name, Author = old.Author, Description = old.Description, Tags = old.Tags };
        var issues = new BuildOrderValidator(data).Validate(bo);
        var plan = new Planner(data).Simulate(bo);
        Console.WriteLine($"{civ}: {bo.Steps.Count} pasos, {(issues.IsValid ? "valida" : "no valida")}, {(plan.Completed ? "se completa" : "no se completa")}");
        foreach (var warning in issues.Warnings)
            Console.WriteLine($"    aviso: {warning.Message}");
        if (!issues.IsValid || !plan.Completed)
            return 1;
        BuildOrderSerializer.Save(bo, path);
        return 0;
    }

    /// <summary>
    /// La versión mejorada de cada BO importada de la carpeta (<c>&lt;civ&gt;-mejorada.json</c>): el optimizador busca
    /// cambios chicos con el simulador sin cambiar la estrategia. Si no encuentra nada que valga, la mejorada es igual
    /// a la importada y lo dice.
    /// </summary>
    public static int Improve(string folder, string[] args)
    {
        var data = GameDataLoader.LoadLatest(Text(args, "--game-data", Path.Combine("data", "game_data")));
        var only = Text(args, "--civ", "");
        var files = Directory.EnumerateFiles(folder, "*.json")
            .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("-mejorada", StringComparison.Ordinal))
            .Where(f => only.Length == 0 || Path.GetFileNameWithoutExtension(f) == only)
            .Order().ToList();
        var lines = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, file =>
        {
            var original = BuildOrderSerializer.Load(file);
            var result = new BuildOrderOptimizer(data).Improve(original);
            var civ = Path.GetFileNameWithoutExtension(file);
            var summary = result.Improved
                ? $"Mejorada con el simulador del coach desde \"{original.Name}\", con la misma estrategia: {string.Join("; ", result.Changes)}. " +
                  $"Simulada antes: {result.Before.Describe()}. Después: {result.After.Describe()}."
                : $"El simulador del coach no encontró cambios que la mejoren (antes y después: {result.Before.Describe()}).";
            var improved = result.BuildOrder with
            {
                Id = $"{original.Id}-mejorada",
                Name = $"{original.Name} (mejorada)",
                Tags = original.Tags.Select(t => t == "importada" ? "mejorada" : t).ToList(),
                Description = $"{summary} {original.Description}",
            };
            BuildOrderSerializer.Save(improved, Path.Combine(folder, $"{civ}-mejorada.json"));
            lines[civ] = $"{civ,-12} {(result.Improved ? $"{result.Before.Score - result.After.Score,6:0} s" : "     -  ")} {result.Before.Describe()} → {result.After.Describe()}" +
                         (result.Improved ? Environment.NewLine + string.Join(Environment.NewLine, result.Changes.Select(c => $"    {c}")) : "");
        });
        foreach (var line in lines.OrderBy(l => l.Key))
            Console.WriteLine(line.Value);
        return 0;
    }

    /// <summary>
    /// La apertura de una guía: la primera unidad militar antes del clic en Castillos (las guías siguen con otras
    /// unidades después), o fast castle si no hay ninguna y clica Castillos. null si no se entiende.
    /// </summary>
    private static string? Opening(BuildOrder bo, HashSet<string?> attributes)
    {
        foreach (var step in bo.Steps)
        {
            if (step is { Do: StepAction.Research, What: "castle_age" })
                return "fast_castle";
            if (step.Do == StepAction.Train && step.What is { } unit && BuildOrderSuggester.StyleOf(new BuildOrder { Steps = [step] }) is { } style)
                return style;
        }
        return attributes.Contains("fastCastle") ? "fast_castle" : null;
    }

    /// <summary>El mensaje sin los números, para agrupar los motivos.</summary>
    private static string Generic(string message) => System.Text.RegularExpressions.Regex.Replace(message, @"\d+|""[^""]*""", "#");

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static double? Num(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : null;

    private static string Text(string[] args, string name, string fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
}
