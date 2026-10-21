using System.Text.Json;
using BoCoach.Core.Data;

namespace BoCoach.Core.BuildOrders;

/// <summary>Las aperturas típicas de cada civ (data/civ_styles.json), en orden de preferencia.</summary>
public sealed class CivStyles(IReadOnlyDictionary<string, List<string>> styles)
{
    public static CivStyles Empty { get; } = new(new Dictionary<string, List<string>>());

    public static CivStyles Load(string path) =>
        File.Exists(path) ? new(JsonSerializer.Deserialize(File.ReadAllText(path), CoreJson.Info<Dictionary<string, List<string>>>()) ?? []) : Empty;

    /// <summary>Los estilos de una civ ("archers", "scouts", "maa", "fast_castle"), o ninguno si no está.</summary>
    public IReadOnlyList<string> Of(string? civ) => civ is not null && styles.TryGetValue(civ, out var list) ? list : [];
}

/// <summary>Una BO sugerida y por qué (para el overlay).</summary>
public sealed record BuildOrderSuggestion(BuildOrder BuildOrder, string Reason);

/// <summary>
/// D4 (diseño acordado con el usuario el 2026-10-20): qué BO de la biblioteca encaja con la partida de la pantalla de
/// carga. Puntos: la civ de la BO es la del jugador (+4; "any" vale 0; otra civ la descarta), el mapa está en sus
/// etiquetas (+2), su estilo (deducido de los pasos) es el primero típico de la civ (+3) u otro típico (+2), y le gana
/// al estilo del rival (+1).
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
            "scout_cavalry" => "scouts",
            "militia" or "spearman" => "maa",
            null when bo.Steps.Any(s => s is { Do: StepAction.Research, What: "castle_age" }) => "fast_castle",
            _ => null,
        };
    }

    /// <summary>Qué estilo le gana a cada uno al principio: exploradores a los arqueros y al fast castle, arqueros a los hombres de armas y a los exploradores.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> Beats = new Dictionary<string, string[]>
    {
        ["scouts"] = ["archers", "fast_castle"],
        ["archers"] = ["maa", "scouts"],
        ["maa"] = ["fast_castle"],
        ["fast_castle"] = [],
    };

    private static readonly IReadOnlyDictionary<string, string> StyleNames = new Dictionary<string, string>
    {
        ["archers"] = "arqueros",
        ["scouts"] = "exploradores",
        ["maa"] = "hombres de armas",
        ["fast_castle"] = "fast castle",
    };

    public static BuildOrderSuggestion? Suggest(IEnumerable<BuildOrder> library, string? civ, IEnumerable<string?> rivalCivs, string? map, CivStyles styles)
    {
        var mine = styles.Of(civ);
        var rivalStyle = rivalCivs.Select(c => styles.Of(c).FirstOrDefault()).FirstOrDefault(s => s is not null);
        var mapKey = Normalize(map);

        (BuildOrder Bo, int Score, List<string> Why)? best = null;
        foreach (var bo in library)
        {
            var why = new List<string>();
            var score = 0;
            if (civ is not null && string.Equals(bo.Civ, civ, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
                why.Add("de tu civ");
            }
            else if (!string.Equals(bo.Civ, "any", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (mapKey.Length > 0 && bo.Tags.Any(t => Normalize(t) == mapKey))
            {
                score += 2;
                why.Add($"para {map}");
            }
            var style = StyleOf(bo);
            if (style is not null && mine.Count > 0 && mine.Contains(style))
            {
                score += mine[0] == style ? 3 : 2;
                why.Add($"{StyleNames[style]}, típico de tu civ");
            }
            if (style is not null && rivalStyle is not null && Beats[style].Contains(rivalStyle))
            {
                score += 1;
                why.Add($"contra {StyleNames[rivalStyle]}");
            }
            if (best is null || score > best.Value.Score)
                best = (bo, score, why);
        }
        return best is { Score: > 0 } b
            ? new BuildOrderSuggestion(b.Bo, string.Join(", ", b.Why))
            : null;
    }

    private static string Normalize(string? text) =>
        new string((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
