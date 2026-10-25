using System.Text.Json;
using System.Text.Json.Nodes;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.BuildOrders;

/// <summary>
/// Lo que sabe el coach de las aperturas (data/civ_styles.json, para que lo revise el usuario): las típicas de cada
/// civ en orden de preferencia, qué estilo le gana a cada estilo, un consejo contra cada estilo y excepciones por civ
/// rival, y qué estilos convienen en el flanco y en el bolsillo. El archivo viejo (solo las civs) se sigue leyendo.
/// </summary>
public sealed class CivStyles
{
    /// <summary>Qué estilo le gana a cada uno al principio, si el archivo no lo dice.</summary>
    private static readonly Dictionary<string, List<string>> DefaultCounters = new()
    {
        ["scouts"] = ["archers", "fast_castle"],
        ["archers"] = ["maa", "scouts"],
        ["maa"] = ["fast_castle"],
        ["fast_castle"] = [],
    };

    private readonly IReadOnlyDictionary<string, List<string>> _civs;
    private readonly IReadOnlyDictionary<string, List<string>> _counters;
    private readonly IReadOnlyDictionary<string, string> _advice;
    private readonly IReadOnlyDictionary<string, string> _civAdvice;
    private readonly IReadOnlyDictionary<string, List<string>> _positions;
    private readonly IReadOnlyDictionary<string, List<int>> _windows;

    /// <summary>
    /// D10: las franjas de duración típicas de cada estilo (1 = menos de 20 minutos de juego, 2 = de 20 a 30, 3 = de
    /// 30 a 40, 4 = de 40 a 55, 5 = 55 o más), si el archivo no las dice.
    /// </summary>
    private static readonly Dictionary<string, List<int>> DefaultWindows = new()
    {
        ["archers"] = [1, 2],
        ["scouts"] = [1, 2],
        ["maa"] = [1, 2],
        ["fast_castle"] = [3, 4],
    };

    public CivStyles(IReadOnlyDictionary<string, List<string>> civs,
        IReadOnlyDictionary<string, List<string>>? counters = null,
        IReadOnlyDictionary<string, string>? advice = null,
        IReadOnlyDictionary<string, string>? civAdvice = null,
        IReadOnlyDictionary<string, List<string>>? positions = null,
        IReadOnlyDictionary<string, List<int>>? windows = null)
    {
        _civs = civs;
        _counters = counters ?? DefaultCounters;
        _advice = advice ?? new Dictionary<string, string>();
        _civAdvice = civAdvice ?? new Dictionary<string, string>();
        _positions = positions ?? new Dictionary<string, List<string>>();
        _windows = windows ?? new Dictionary<string, List<int>>();
    }

    public static CivStyles Empty { get; } = new(new Dictionary<string, List<string>>());

    public static CivStyles Load(string path)
    {
        if (!File.Exists(path))
            return Empty;
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(File.ReadAllText(path), documentOptions: options);
        }
        catch (JsonException e)
        {
            // El usuario edita el archivo a mano: el error tiene que decir qué archivo es y dónde está el problema.
            throw new InvalidDataException($"{Path.GetFileName(path)}: {e.Message}", e);
        }
        if (parsed is not JsonObject root)
            return Empty;
        if (root["civs"] is not JsonObject civs)
            return new(Lists(root)); // formato viejo: solo las civs
        return new(Lists(civs), root["counters"] is JsonObject c ? Lists(c) : null, Texts(root["advice"]), Texts(root["civ_advice"]),
            root["positions"] is JsonObject p ? Lists(p) : null, root["windows"] is JsonObject w ? Windows(w) : null);
    }

    /// <summary>Las ventanas del archivo: listas de franjas de 1 a 5 (lo que no es un número de 1 a 5 se ignora).</summary>
    private static Dictionary<string, List<int>> Windows(JsonObject json) => json
        .Where(kv => kv.Value is JsonArray)
        .ToDictionary(kv => kv.Key, kv => kv.Value!.AsArray()
            .Select(n => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var band) ? band : 0)
            .Where(band => band is >= 1 and <= 5).ToList());

    private static Dictionary<string, List<string>> Lists(JsonObject json) => json
        .Where(kv => kv.Value is JsonArray)
        .ToDictionary(kv => kv.Key, kv => kv.Value!.AsArray().Select(Text).OfType<string>().ToList());

    private static Dictionary<string, string>? Texts(JsonNode? json) => json is JsonObject o
        ? o.Select(kv => (kv.Key, Value: Text(kv.Value))).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!)
        : null;

    /// <summary>El texto de un nodo, o null si no lo es (un número o un objeto puesto por error se ignora en vez de tirar abajo la carga).</summary>
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>Los estilos de una civ ("archers", "scouts", "maa", "fast_castle"), o ninguno si no está.</summary>
    public IReadOnlyList<string> Of(string? civ) => civ is not null && _civs.TryGetValue(civ, out var list) ? list : [];

    /// <summary>Si el estilo <paramref name="style"/> le gana a <paramref name="other"/> al principio.</summary>
    public bool Beats(string style, string other) => _counters.TryGetValue(style, out var beaten) && beaten.Contains(other);

    /// <summary>D10: las franjas de duración típicas del estilo (vacío si no se conoce).</summary>
    public IReadOnlyList<int> Window(string style) =>
        _windows.TryGetValue(style, out var bands) ? bands : DefaultWindows.TryGetValue(style, out var fallback) ? fallback : [];

    /// <summary>Los estilos que convienen en esa posición del equipo.</summary>
    public IReadOnlyList<string> ForPosition(TeamPosition position) =>
        _positions.TryGetValue(position == TeamPosition.Flank ? "flank" : "pocket", out var list) ? list : [];

    /// <summary>El consejo contra una civ rival: el de esa civ si lo hay, si no el de su estilo típico, o null.</summary>
    public string? AdviceAgainst(string? civ) =>
        civ is not null && _civAdvice.TryGetValue(civ, out var text) ? text
        : Of(civ).FirstOrDefault() is { } style && _advice.TryGetValue(style, out var general) ? general
        : null;
}

/// <summary>Dónde está el jugador en su equipo: en una punta (flanco) o en el medio (bolsillo).</summary>
public enum TeamPosition
{
    Flank,
    Pocket,
}

/// <summary>Lo que se sabe de la partida en la pantalla de carga (D4).</summary>
/// <param name="Civ">La civ del jugador.</param>
/// <param name="Facing">La civ del rival de enfrente (pesa doble).</param>
/// <param name="OtherRivals">Las civs de los demás rivales.</param>
/// <param name="Position">Flanco o bolsillo; null en 1 contra 1 o 2 contra 2.</param>
/// <param name="Map">El mapa, si se leyó.</param>
/// <param name="Elo">El Elo 1v1 del jugador (D10: las estadísticas de cruces son de 1v1), si lo puso.</param>
public sealed record MatchSetup(string? Civ, string? Facing = null, IReadOnlyList<string?>? OtherRivals = null, TeamPosition? Position = null,
    string? Map = null, int? Elo = null);

/// <summary>Una BO sugerida, por qué (para el overlay) y sus puntos.</summary>
public sealed record BuildOrderSuggestion(BuildOrder BuildOrder, string Reason, double Score = 0);

/// <summary>
/// D4 (diseño acordado con el usuario el 2026-10-20): qué BO de la biblioteca encaja con la partida de la pantalla de
/// carga. Puntos: la civ de la BO es la del jugador (+4; "any" vale 0; otra civ la descarta), el mapa está en sus
/// etiquetas (+2) y su estilo (deducido de los pasos) es el primero típico de la civ (+3) u otro típico (+2). Segunda
/// parte (2026-10-21): le gana al estilo del rival de enfrente (+2) y al de cada uno de los demás (+0,5), y conviene
/// en la posición del jugador en su equipo (+1). Entre una BO importada y su versión mejorada (D7), la mejorada (+0,5).
/// </summary>
public static class BuildOrderSuggester
{
    /// <summary>
    /// El estilo de una BO por sus pasos: la última unidad militar que entrena (arqueros, exploradores, hombres de
    /// armas) o, si no entrena ninguna y clica Castillos, "fast_castle". null si no se entiende.
    /// </summary>
    public static string? StyleOf(BuildOrder bo)
    {
        var military = bo.Steps.LastOrDefault(s => s.Do == StepAction.Train && s.What is { } w && w != "villager")?.What;
        return military switch
        {
            "archer" or "skirmisher" => "archers",
            "scout_cavalry" or "eagle_scout" => "scouts", // las águilas son los exploradores de las civs mesoamericanas
            "militia" or "spearman" => "maa",
            null when bo.Steps.Any(s => s is { Do: StepAction.Research, What: "castle_age" }) => "fast_castle",
            _ => null,
        };
    }

    private static readonly IReadOnlyDictionary<string, string> StyleNames = new Dictionary<string, string>
    {
        ["archers"] = "arqueros",
        ["scouts"] = "exploradores",
        ["maa"] = "hombres de armas",
        ["fast_castle"] = "fast castle",
    };

    /// <summary>Como <see cref="Suggest(IEnumerable{BuildOrder}, MatchSetup, CivStyles)"/>, con el primer rival enfrente.</summary>
    public static BuildOrderSuggestion? Suggest(IEnumerable<BuildOrder> library, string? civ, IEnumerable<string?> rivalCivs, string? map, CivStyles styles)
    {
        var rivals = rivalCivs.ToList();
        return Suggest(library, new MatchSetup(civ, rivals.FirstOrDefault(), rivals.Skip(1).ToList(), null, map), styles);
    }

    /// <summary>
    /// Puntos por la civ, el mapa y el estilo típico; contra el rival de enfrente (+2) y cada uno de los demás (+0,5);
    /// y por la posición en el equipo (+1). D10: con las estadísticas reales de cruces, de -2 a +2 según cuánto mejor se
    /// gana el cruce con el rival de enfrente en la ventana de duración del estilo que en general.
    /// </summary>
    public static BuildOrderSuggestion? Suggest(IEnumerable<BuildOrder> library, MatchSetup match, CivStyles styles,
        MatchupStats? matchups = null)
    {
        var context = Context.For(match, styles, matchups);
        (BuildOrder Bo, double Score, List<string> Why)? best = null;
        foreach (var bo in library)
        {
            if (Evaluate(bo, context, styles) is not { } result)
                continue;
            if (best is null || result.Score > best.Value.Score)
                best = (bo, result.Score, result.Why);
        }
        return best is { Score: > 0 } b
            ? new BuildOrderSuggestion(b.Bo, string.Join(", ", b.Why), b.Score)
            : null;
    }

    /// <summary>Los puntos de una BO para esa partida (0 si es de otra civ: no se puede sugerir). D6 compara con ellos la que corre.</summary>
    public static double Score(BuildOrder bo, MatchSetup match, CivStyles styles, MatchupStats? matchups = null) =>
        Evaluate(bo, Context.For(match, styles, matchups), styles)?.Score ?? 0;

    /// <summary>D10: el cruce con el rival de enfrente, si tiene tendencia por duración (la regla de honestidad).</summary>
    private sealed record DurationContext(MatchupStats Stats, string Civ, string Facing, int Map, int EloBand, MatchupCell Total,
        DurationTrend Trend)
    {
        public static DurationContext? For(MatchSetup match, MatchupStats? stats)
        {
            if (stats is null || stats.IsEmpty || match.Civ is not { } civ || match.Facing is not { } facing)
                return null;
            var (map, elo) = (stats.MapIndex(match.Map), stats.EloBand(match.Elo));
            return stats.OneVsOne(civ, facing, map, elo, 0) is { } total && stats.OneVsOneTrend(civ, facing, map, elo) is { } trend
                ? new DurationContext(stats, civ, facing, map, elo, total, trend)
                : null;
        }

        /// <summary>
        /// Los puntos de una ventana: la chance en sus franjas (promedio pesado por partidas: el archivo no trae el
        /// casillero de dos franjas juntas) contra la general, × 20 y entre -2 y 2.
        /// </summary>
        public double Points(IReadOnlyList<int> window)
        {
            var cells = window.Select(band => Stats.OneVsOne(Civ, Facing, Map, EloBand, band)).OfType<MatchupCell>().ToList();
            var games = cells.Sum(c => c.N);
            if (games == 0)
                return 0;
            var chance = cells.Sum(c => c.P * c.N) / games;
            return Math.Clamp((chance - Total.P) * 20, -2, 2);
        }
    }

    /// <summary>Lo que se deduce de la partida una sola vez, para puntuar cada BO.</summary>
    private sealed record Context(string? Civ, IReadOnlyList<string> Mine, string? FacingStyle, IReadOnlyList<string> OtherStyles,
        IReadOnlyList<string> PositionStyles, string MapKey, MatchSetup Match, DurationContext? Duration)
    {
        public static Context For(MatchSetup match, CivStyles styles, MatchupStats? matchups) => new(match.Civ, styles.Of(match.Civ),
            styles.Of(match.Facing).FirstOrDefault(),
            (match.OtherRivals ?? []).Select(c => styles.Of(c).FirstOrDefault()).OfType<string>().ToList(),
            match.Position is { } position ? styles.ForPosition(position) : [], Normalize(match.Map), match,
            DurationContext.For(match, matchups));
    }

    /// <summary>Los puntos de una BO y por qué, o null si es de otra civ.</summary>
    private static (double Score, List<string> Why)? Evaluate(BuildOrder bo, Context c, CivStyles styles)
    {
        var why = new List<string>();
        var score = 0.0;
        if (c.Civ is not null && string.Equals(bo.Civ, c.Civ, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
            why.Add("de tu civ");
        }
        else if (!string.Equals(bo.Civ, "any", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        // D8: el mapa de la BO manda (+2 si es el de la partida, -2 si es otro); sin mapa, vale la etiqueta como antes.
        if (c.MapKey.Length > 0)
        {
            if (!Maps.IsAny(bo.Map))
            {
                if (Normalize(bo.Map) == c.MapKey)
                {
                    score += 2;
                    why.Add($"para {c.Match.Map}");
                }
                else
                {
                    score -= 2;
                }
            }
            else if (bo.Tags.Any(t => Normalize(t) == c.MapKey))
            {
                score += 2;
                why.Add($"para {c.Match.Map}");
            }
        }
        var style = StyleOf(bo);
        if (style is not null && c.Mine.Count > 0 && c.Mine.Contains(style))
        {
            score += c.Mine[0] == style ? 3 : 2;
            why.Add($"{Name(style)}, típico de tu civ");
        }
        if (style is not null && c.Duration is { } duration && styles.Window(style) is { Count: > 0 } window)
        {
            var points = duration.Points(window);
            score += points;
            // El porqué solo si suma algo y la tendencia apunta al lado de la ventana del estilo.
            if (points >= 0.5 && duration.Trend.FavorsShort == window.Average() < 3)
                why.Add(MatchupText.DurationReason(duration.Trend));
        }
        if (style is not null && c.FacingStyle is not null && styles.Beats(style, c.FacingStyle))
        {
            score += 2;
            why.Add($"contra {Name(c.FacingStyle)}");
        }
        if (style is not null)
        {
            var others = c.OtherStyles.Count(o => styles.Beats(style, o));
            score += 0.5 * others;
            if (others > 0)
                why.Add(others == 1 ? "le gana a otro rival" : $"le gana a {others} rivales más");
        }
        if (style is not null && c.PositionStyles.Contains(style))
        {
            score += 1;
            why.Add(c.Match.Position == TeamPosition.Flank ? "para el flanco" : "para el bolsillo");
        }
        // D7: de cada civ hay una importada y su versión mejorada con el simulador; entre las dos, la mejorada.
        if (score > 0 && bo.Tags.Contains("mejorada", StringComparer.OrdinalIgnoreCase))
            score += 0.5;
        return (score, why);
    }

    /// <summary>El consejo contra el rival de enfrente (la excepción de su civ, o el de su estilo típico), o null.</summary>
    public static string? Advice(MatchSetup match, CivStyles styles) => styles.AdviceAgainst(match.Facing);

    private static string Name(string style) => StyleNames.TryGetValue(style, out var name) ? name : style;

    private static string Normalize(string? text) =>
        new string((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
