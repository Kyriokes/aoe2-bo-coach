namespace BoCoach.Core.BuildOrders;

/// <summary>El estilo de una BO (lo usan la sugerencia, la web y la base, que tiene una copia en SQL).</summary>
public static class BuildOrderStyle
{
    /// <summary>
    /// El estilo de una BO por sus pasos: la última unidad militar que entrena (arqueros, exploradores, hombres de
    /// armas) o, si no entrena ninguna y clica Castillos, "fast_castle". null si no se entiende.
    /// </summary>
    public static string? Of(BuildOrder bo)
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
}
