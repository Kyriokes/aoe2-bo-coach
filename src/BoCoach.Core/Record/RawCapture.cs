using System.Text.Json.Nodes;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Record;

/// <summary>Los tipos de línea del registro de partida (la clave <c>t</c>).</summary>
public static class GameRecordKeys
{
    public const string Meta = "meta";
    public const string Capture = "cap";
    public const string Done = "done";
    public const string Belief = "belief";
    public const string Show = "show";
    public const string Forecast = "forecast";
    public const string Outcome = "outcome";
    public const string Flag = "flag";
    public const string Note = "note";
    public const string End = "end";

    /// <summary>Versión del formato que escribe esta app.</summary>
    public const int Schema = 1;
}

/// <summary>
/// Lo crudo de una captura, antes de suavizar (spec 2026-11-02, registro de partida): lo que leyó cada región, con la
/// confianza de las letras, los renglones del log tal cual salieron del OCR, el texto de la edad, la cola y los
/// puntajes. Es el DTO del registro: no sabe nada de imágenes, y con él se vuelve a correr el suavizado sin PNG.
/// </summary>
/// <param name="VillagersOn">Aldeanos por recurso, solo los leídos (null si ninguno).</param>
/// <param name="Confidence">Confianza 0 a 1 de cada campo leído por plantillas, por clave ("clock", "food", "food_vil", "vil", "pop", "idle"...).</param>
/// <param name="Clipped">Regiones con el número cortado por la caja.</param>
/// <param name="LogLines">Renglones del log de arriba hacia abajo (vacío = sin texto), o null si no se lee el log. Lo que no es un mensaje del juego va tapado: ahí puede haber chat con nombres.</param>
/// <param name="AgeText">El texto crudo de la edad, solo en las capturas en que se leyó.</param>
/// <param name="Queue">La cola de producción, o null si no se lee.</param>
/// <param name="Scores">El panel de puntajes (sin nombres), solo en las capturas en que se leyó.</param>
public sealed record RawCapture(
    double? Clock,
    int? Food,
    int? Wood,
    int? Gold,
    int? Stone,
    int? Villagers,
    int? Idle,
    int? Population,
    int? PopulationCap,
    IReadOnlyDictionary<Resource, int>? VillagersOn,
    IReadOnlyDictionary<string, float> Confidence,
    IReadOnlyList<string> Clipped,
    IReadOnlyList<string>? LogLines,
    string? AgeText,
    IReadOnlyList<QueueItem>? Queue,
    IReadOnlyList<PlayerScore>? Scores)
{
    private static readonly Resource[] Resources = [Resource.Food, Resource.Wood, Resource.Gold, Resource.Stone];

    /// <summary>La línea <c>cap</c>. <paramref name="logChanged"/> false = los renglones son los de la captura anterior y no se repiten.</summary>
    public JsonObject ToJson(bool logChanged)
    {
        var json = new JsonObject { ["t"] = GameRecordKeys.Capture };
        Put(json, "clock", Clock);
        Put(json, "food", Food);
        Put(json, "wood", Wood);
        Put(json, "gold", Gold);
        Put(json, "stone", Stone);
        Put(json, "vil", Villagers);
        Put(json, "idle", Idle);
        Put(json, "pop", Population);
        Put(json, "cap", PopulationCap);
        if (VillagersOn is { Count: > 0 } on)
        {
            var o = new JsonObject();
            foreach (var r in Resources)
                if (on.TryGetValue(r, out var n))
                    o[Key(r)] = n;
            json["on"] = o;
        }
        if (Confidence.Count > 0)
        {
            var c = new JsonObject();
            foreach (var (key, value) in Confidence.OrderBy(p => p.Key, StringComparer.Ordinal))
                c[key] = Math.Round(value, 3);
            json["conf"] = c;
        }
        if (Clipped.Count > 0)
            json["clipped"] = new JsonArray(Clipped.Select(c => (JsonNode?)c).ToArray());
        if (logChanged)
            json["log"] = LogLines is { } lines ? new JsonArray(lines.Select(l => (JsonNode?)l).ToArray()) : null;
        if (AgeText is { } age)
            json["age"] = age;
        if (Queue is { } queue)
            json["queue"] = new JsonArray(queue.Select(q => (JsonNode?)new JsonArray(q.Key, q.Waiting)).ToArray());
        if (Scores is { } scores)
            json["scores"] = new JsonArray(scores.Select(s => (JsonNode?)new JsonArray(s.Slot, s.Score, s.DarkAge)).ToArray());
        return json;
    }

    /// <summary>Lee una línea <c>cap</c>. Sin <c>log</c>, los renglones son <paramref name="previousLog"/> (los de la captura anterior).</summary>
    public static RawCapture FromJson(JsonObject json, IReadOnlyList<string>? previousLog)
    {
        Dictionary<Resource, int>? on = null;
        if (json["on"] is JsonObject o)
        {
            on = [];
            foreach (var r in Resources)
                if (Int(o[Key(r)]) is { } n)
                    on[r] = n;
        }
        var confidence = new Dictionary<string, float>();
        if (json["conf"] is JsonObject c)
            foreach (var (key, value) in c)
                if (Double(value) is { } d)
                    confidence[key] = (float)d;
        var clipped = Strings(json["clipped"]) ?? [];
        var log = json.ContainsKey("log") ? Strings(json["log"]) : previousLog;
        IReadOnlyList<QueueItem>? queue = json["queue"] is JsonArray q
            ? q.OfType<JsonArray>().Where(i => i.Count >= 1 && i[0] is JsonValue).Select(i => new QueueItem(i[0]!.GetValue<string>(), i.Count > 1 && Bool(i[1]))).ToList()
            : null;
        IReadOnlyList<PlayerScore>? scores = json["scores"] is JsonArray s
            ? s.OfType<JsonArray>().Where(i => i.Count >= 2 && Int(i[0]) is not null && Int(i[1]) is not null)
                .Select(i => new PlayerScore(Int(i[0])!.Value, Int(i[1])!.Value, i.Count > 2 && Bool(i[2]))).ToList()
            : null;
        return new RawCapture(
            Double(json["clock"]), Int(json["food"]), Int(json["wood"]), Int(json["gold"]), Int(json["stone"]),
            Int(json["vil"]), Int(json["idle"]), Int(json["pop"]), Int(json["cap"]),
            on, confidence, clipped, log, json["age"] is JsonValue a && a.TryGetValue<string>(out var age) ? age : null, queue, scores);
    }

    private static string Key(Resource r) => r switch
    {
        Resource.Food => "food",
        Resource.Wood => "wood",
        Resource.Gold => "gold",
        _ => "stone",
    };

    private static void Put(JsonObject json, string key, double? value)
    {
        if (value is { } v)
            json[key] = v;
    }

    private static void Put(JsonObject json, string key, int? value)
    {
        if (value is { } v)
            json[key] = v;
    }

    internal static int? Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : Double(node) is { } d ? (int)d : null;

    internal static double? Double(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static List<string>? Strings(JsonNode? node) =>
        node is JsonArray a ? a.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList() : null;
}
