using System.Text.Json;
using System.Text.Json.Nodes;

namespace BoCoach.Core.Data;

/// <summary>Un mapa del juego: qué fuentes tiene y si hay agua para pescar y para el muelle.</summary>
public sealed record GameMap(string Key, string Name, bool Water, IReadOnlySet<GatherSource> Sources, string? Note)
{
    /// <summary>Lo que tiene cada jugador en Arabia: 8 ovejas, 2 jabalíes y 6 arbustos de bayas.</summary>
    public static IReadOnlyDictionary<GatherSource, int> StandardFood { get; } = new Dictionary<GatherSource, int>
    {
        [GatherSource.Sheep] = 8,
        [GatherSource.Hunt] = 2,
        [GatherSource.Berries] = 6,
    };

    /// <summary>D12: cuántas ovejas, jabalíes y arbustos de bayas tiene cada jugador (por defecto, los de Arabia).</summary>
    public IReadOnlyDictionary<GatherSource, int> Food { get; init; } = StandardFood;

    public bool Has(GatherSource source) => Sources.Contains(source);
}

/// <summary>
/// Los mapas que conoce el coach (data/maps.json, para que lo revise el usuario): por mapa, el nombre, si hay agua y
/// qué fuentes tiene (D8). "any" es cualquier mapa y no filtra nada. El archivo se edita a mano: un valor de otro tipo
/// se ignora y un error de sintaxis dice qué archivo es.
/// </summary>
public sealed class Maps
{
    public const string Any = "any";

    private static readonly IReadOnlySet<GatherSource> AllSources = Enum.GetValues<GatherSource>().ToHashSet();

    private readonly Dictionary<string, GameMap> _maps;

    public Maps(IEnumerable<GameMap> maps)
    {
        _maps = new Dictionary<string, GameMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
            _maps[map.Key] = map;
        _maps.TryAdd(Any, new GameMap(Any, "Cualquiera", true, AllSources, null));
    }

    public static Maps Empty { get; } = new([]);

    /// <summary>Los mapas en el orden del archivo, con "any" primero.</summary>
    public IReadOnlyList<GameMap> All => [_maps[Any], .. _maps.Values.Where(m => !IsAny(m.Key))];

    /// <summary>El mapa de esa clave, o null si no está en el archivo. Null o "any" es cualquier mapa.</summary>
    public GameMap? Get(string? key) => _maps.TryGetValue(key ?? Any, out var map) ? map : null;

    public bool IsKnown(string? key) => _maps.ContainsKey(key ?? Any);

    public static bool IsAny(string? key) => string.IsNullOrWhiteSpace(key) || string.Equals(key.Trim(), Any, StringComparison.OrdinalIgnoreCase);

    public static Maps Load(string path)
    {
        if (!File.Exists(path))
            return Empty;
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            // El usuario edita el archivo a mano: el error tiene que decir qué archivo es y dónde está el problema.
            throw new InvalidDataException($"{Path.GetFileName(path)}: {e.Message}", e);
        }
    }

    public static Maps Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var root = JsonNode.Parse(json, documentOptions: options) as JsonObject;
        if (root?["maps"] is not JsonObject maps)
            return Empty;
        return new Maps(maps.Select(kv => Read(kv.Key, kv.Value)).OfType<GameMap>());
    }

    private static GameMap? Read(string key, JsonNode? node)
    {
        if (node is not JsonObject map || string.IsNullOrWhiteSpace(key))
            return null;
        var sources = map["sources"] is JsonArray list
            ? list.Select(Text).OfType<string>().Select(ParseSource).OfType<GatherSource>().ToHashSet()
            : AllSources;
        var water = map["water"] is JsonValue w && w.GetValueKind() is JsonValueKind.True or JsonValueKind.False && w.GetValue<bool>();
        return new GameMap(key.Trim(), Text(map["name"]) ?? key, water, sources, Text(map["note"])) { Food = ReadFood(map["food"]) };
    }

    /// <summary>Las cantidades de food que valen (un entero de 0 en adelante de una fuente contada); lo demás queda en el estándar.</summary>
    private static IReadOnlyDictionary<GatherSource, int> ReadFood(JsonNode? node)
    {
        if (node is not JsonObject food)
            return GameMap.StandardFood;
        var result = new Dictionary<GatherSource, int>(GameMap.StandardFood);
        foreach (var (name, value) in food)
        {
            if (ParseSource(name) is { } source && result.ContainsKey(source)
                && value is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var count) && count >= 0)
                result[source] = count;
        }
        return result;
    }

    private static GatherSource? ParseSource(string text) =>
        Enum.TryParse<GatherSource>(text, ignoreCase: true, out var source) ? source : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
