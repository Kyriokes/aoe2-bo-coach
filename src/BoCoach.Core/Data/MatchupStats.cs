using System.Globalization;
using System.Text.Json;

namespace BoCoach.Core.Data;

/// <summary>
/// Un casillero de las estadísticas de cruces: la chance de ganar (a Elo parejo), el rango probable del 90 % y
/// cuántas partidas reales hay detrás.
/// </summary>
public sealed record MatchupCell(double P, double From, double To, int N)
{
    /// <summary>Parejo: el rango incluye el 50 % (spec D10, 4.5).</summary>
    public bool Even => From <= 0.5 && 0.5 <= To;

    /// <summary>El mismo casillero visto desde el rival.</summary>
    public MatchupCell Inverted => new(1 - P, 1 - To, 1 - From, N);
}

/// <summary>
/// Las estadísticas reales de cruces de data/matchup_stats.json, que escribe tools/stats (spec D10, 3.2 y 3.3).
/// <para>Cada entrada (una pareja "a|b" en 1v1, con la chance de la primera; una civ en equipos) tiene 180 casilleros
/// [p, desde, hasta, n] en milésimas y partidas, en el orden mapa (todos y los de <see cref="MapKeys"/>) × franja de
/// Elo (todas y 5) × franja de duración (todas y 5). En las consultas, 0 es "todos" o "todas" y 1..5 cada franja.</para>
/// <para>El archivo lo genera una herramienta, no el usuario: si falta o está roto, queda vacío y nadie muestra un
/// error (la app sigue sin el renglón del cruce).</para>
/// </summary>
public sealed class MatchupStats
{
    private const int Values = 5 * 6 * 6 * 4;
    private static readonly int[] DefaultEloLimits = [850, 1050, 1300, 1600];
    private static readonly int[] DefaultGameMinuteLimits = [20, 30, 40, 55];
    private static readonly string[] DefaultMapKeys = ["arabia", "arena", "black_forest", "nomad"];

    private readonly IReadOnlyDictionary<string, int[]> _oneVsOne;
    private readonly IReadOnlyDictionary<string, int[]> _team;

    private MatchupStats(IReadOnlyDictionary<string, int[]> oneVsOne, IReadOnlyDictionary<string, int[]> team,
        IReadOnlyList<int> eloLimits, IReadOnlyList<int> gameMinuteLimits, IReadOnlyList<string> mapKeys)
    {
        _oneVsOne = oneVsOne;
        _team = team;
        EloLimits = eloLimits;
        GameMinuteLimits = gameMinuteLimits;
        MapKeys = mapKeys;
    }

    public static MatchupStats Empty { get; } = new(new Dictionary<string, int[]>(), new Dictionary<string, int[]>(),
        DefaultEloLimits, DefaultGameMinuteLimits, DefaultMapKeys);

    public bool IsEmpty => _oneVsOne.Count == 0 && _team.Count == 0;

    public string? SourceName { get; private init; }
    public string? PatchLabel { get; private init; }
    public DateOnly? From { get; private init; }
    public DateOnly? To { get; private init; }

    /// <summary>El parche actual tenía menos de 4 semanas y se sumaron las partidas del anterior.</summary>
    public bool PreviousMixed { get; private init; }

    public IReadOnlyList<int> EloLimits { get; }
    public IReadOnlyList<int> GameMinuteLimits { get; }
    public IReadOnlyList<string> MapKeys { get; }

    /// <param name="onlyCiv">Si se da, solo se guarda lo de esa civ (la app no necesita las 1378 parejas en memoria).</param>
    public static MatchupStats Load(string path, string? onlyCiv = null)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path), onlyCiv) : Empty;
        }
        catch (IOException)
        {
            return Empty;
        }
    }

    public static MatchupStats Parse(string json, string? onlyCiv = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        try
        {
            var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
            using var document = JsonDocument.Parse(json, options);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Empty;
            var civ = Key(onlyCiv);
            var oneVsOne = Entries(root, "one_vs_one", key => civ is null || key.Split('|').Contains(civ), pairs: true);
            var team = Entries(root, "team", key => civ is null || key == civ, pairs: false);
            if (oneVsOne.Count == 0 && team.Count == 0)
                return Empty;
            var bands = Property(root, "bands");
            var source = Property(root, "source");
            var patch = Property(root, "patch");
            return new MatchupStats(oneVsOne, team,
                Limits(bands, "elo") ?? DefaultEloLimits,
                Limits(bands, "game_minutes") ?? DefaultGameMinuteLimits,
                MapList(bands) ?? DefaultMapKeys)
            {
                SourceName = Text(source, "name"),
                PatchLabel = Text(patch, "label"),
                From = Date(source, "from"),
                To = Date(source, "to"),
                PreviousMixed = Property(patch, "previous_mixed") is { ValueKind: JsonValueKind.True },
            };
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>0 sin Elo (todas las franjas); si no, 1 (menos de 850) a 5 (1600 o más). El borde va a la de arriba.</summary>
    public int EloBand(int? elo) => elo is null or <= 0 ? 0 : 1 + EloLimits.Count(limit => elo >= limit);

    /// <summary>1 (menos de 20 minutos de juego) a 5 (55 o más). El borde va a la de arriba.</summary>
    public int DurationBand(TimeSpan gameTime) => 1 + GameMinuteLimits.Count(limit => gameTime.TotalMinutes >= limit);

    /// <summary>1..4 para los mapas de <see cref="MapKeys"/>; 0 (todos los mapas) para los demás o sin mapa.</summary>
    public int MapIndex(string? map)
    {
        var key = Key(map);
        return key is null ? 0 : MapKeys.ToList().IndexOf(key) + 1;
    }

    /// <summary>"antes de los 20 min", "de 20 a 30 min", ..., "después de los 55 min".</summary>
    public string DurationLabel(int band)
    {
        var limits = GameMinuteLimits;
        if (band <= 1)
            return $"antes de los {limits[0]} min";
        if (band > limits.Count)
            return $"después de los {limits[^1]} min";
        return $"de {limits[band - 2]} a {limits[band - 1]} min";
    }

    /// <summary>Si hay datos de esa civ (en 1v1 o en equipos). Las civs de Crónicas no juegan clasificatorias.</summary>
    public bool HasCiv(string civ)
    {
        var key = Key(civ);
        return key is not null && (_team.ContainsKey(key) || _oneVsOne.Keys.Any(k => k.Split('|').Contains(key)));
    }

    /// <summary>
    /// La chance de que <paramref name="civ"/> le gane a <paramref name="rival"/> en 1v1, a Elo parejo; null en un
    /// espejo o si falta la pareja (una civ sin partidas clasificatorias).
    /// </summary>
    public MatchupCell? OneVsOne(string civ, string rival, int map, int eloBand, int durationBand)
    {
        var (a, b) = (Key(civ), Key(rival));
        if (a is null || b is null || a == b)
            return null;
        var first = string.CompareOrdinal(a, b) < 0;
        if (!_oneVsOne.TryGetValue(first ? $"{a}|{b}" : $"{b}|{a}", out var cells))
            return null;
        var cell = Cell(cells, map, eloBand, durationBand);
        return first ? cell : cell.Inverted;
    }

    /// <summary>
    /// La chance de que gane un equipo con esa civ en lugar de una civ promedio, a Elo parejo. En equipos, N no son
    /// partidas sino apariciones de la civ (cada jugador cuenta).
    /// </summary>
    public MatchupCell? Team(string civ, int map, int eloBand, int durationBand) =>
        Key(civ) is { } key && _team.TryGetValue(key, out var cells) ? Cell(cells, map, eloBand, durationBand) : null;

    private static MatchupCell Cell(int[] cells, int map, int eloBand, int durationBand)
    {
        var i = ((Math.Clamp(map, 0, 4) * 6 + Math.Clamp(eloBand, 0, 5)) * 6 + Math.Clamp(durationBand, 0, 5)) * 4;
        return new MatchupCell(cells[i] / 1000.0, cells[i + 1] / 1000.0, cells[i + 2] / 1000.0, cells[i + 3]);
    }

    private static string? Key(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim().ToLowerInvariant();

    private static Dictionary<string, int[]> Entries(JsonElement root, string name, Func<string, bool> keep, bool pairs)
    {
        var entries = new Dictionary<string, int[]>();
        if (Property(root, name) is not { ValueKind: JsonValueKind.Object } table)
            return entries;
        foreach (var entry in table.EnumerateObject())
        {
            var key = Key(entry.Name);
            if (key is null || (pairs && key.Split('|').Length != 2) || !keep(key))
                continue;
            if (entry.Value.ValueKind != JsonValueKind.Array || entry.Value.GetArrayLength() != Values)
                continue;
            var cells = new int[Values];
            var ok = true;
            var i = 0;
            foreach (var value in entry.Value.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out cells[i++]))
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
                entries[key] = cells;
        }
        return entries;
    }

    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var value) ? value : null;

    private static string? Text(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static DateOnly? Date(JsonElement? element, string name) =>
        DateOnly.TryParseExact(Text(element, name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : null;

    private static int[]? Limits(JsonElement? bands, string name)
    {
        if (Property(bands, name) is not { ValueKind: JsonValueKind.Array } list || list.GetArrayLength() != 4)
            return null;
        var limits = new int[4];
        var i = 0;
        foreach (var value in list.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out limits[i]) || (i > 0 && limits[i] <= limits[i - 1]))
                return null;
            i++;
        }
        return limits;
    }

    private static string[]? MapList(JsonElement? bands)
    {
        if (Property(bands, "maps") is not { ValueKind: JsonValueKind.Array } list || list.GetArrayLength() != 4)
            return null;
        var maps = list.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? Key(v.GetString()) : null).ToArray();
        return maps.All(m => m is not null) ? maps.Select(m => m!).ToArray() : null;
    }
}
