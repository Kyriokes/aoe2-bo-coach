namespace BoCoach.Core.BuildOrders;

/// <summary>El estilo de una BO (lo usan la sugerencia, la web y la base, que tiene una copia en SQL).</summary>
public static class BuildOrderStyle
{
    /// <summary>
    /// El estilo de una BO por sus pasos, que es su apertura: la última unidad militar que entrena antes del clic en
    /// Castillos (arqueros, exploradores, hombres de armas), o la última que entrena si antes no hay ninguna; si no
    /// entrena ninguna y clica Castillos, "fast_castle". null si no se entiende. Desde el 2026-10-31 "Scouts → CA" de
    /// los hunos sigue en Castillos con arqueros a caballo y sigue siendo de exploradores.
    /// </summary>
    public static string? Of(BuildOrder bo)
    {
        var castle = bo.Steps.ToList().FindIndex(s => s is { Do: StepAction.Research, What: "castle_age" });
        var military = bo.Steps.Take(castle < 0 ? bo.Steps.Count : castle).LastOrDefault(IsMilitary)?.What
                       ?? bo.Steps.LastOrDefault(IsMilitary)?.What;
        return military switch
        {
            "archer" or "skirmisher" => "archers",
            "scout_cavalry" or "eagle_scout" => "scouts", // las águilas son los exploradores de las civs mesoamericanas
            "militia" or "spearman" => "maa",
            null when bo.Steps.Any(s => s is { Do: StepAction.Research, What: "castle_age" }) => "fast_castle",
            _ => null,
        };
    }

    private static bool IsMilitary(Step s) => s.Do == StepAction.Train && s.What is { } w && w != "villager";
}
