using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Casas por ritmo de producción (spec 2026-11-02, escalón 1). Avisar con 3 lugares libres sirve con un solo centro
/// urbano y llega tarde con varias galerías: el margen sale de cuánta población por segundo consume lo que está
/// produciendo en la cola real y de cuánto se tarda en reaccionar (la casa, caminar y el jugador).
/// </summary>
public static class HousePacing
{
    /// <summary>El margen de siempre: nunca se avisa más tarde que con 3 lugares libres.</summary>
    public const int MinMargin = 3;

    /// <summary>Lo que camina el aldeano hasta donde va la casa, en segundos (aproximado).</summary>
    public const double WalkSeconds = 5;

    /// <summary>Lo que tarda el jugador en ver el aviso y mandar la casa (a calibrar con el registro de partida).</summary>
    public const double PlayerSeconds = 10;

    /// <summary>
    /// Población por segundo que consume lo que está produciendo: por cada ícono de la cola que no espera, la población
    /// de la unidad sobre su tiempo de entrenamiento con la civ y la velocidad del edificio. Las tecnologías y las
    /// edades no consumen. Sin cola (null) se cuenta solo el centro urbano sacando aldeanos.
    /// </summary>
    public static double ConsumptionRate(IReadOnlyList<QueueItem>? queue, GameData data, CivBonuses civ, Age age)
    {
        if (queue is null)
            return UnitRate("villager", data, civ, age);
        double rate = 0;
        foreach (var item in queue)
        {
            if (!item.Waiting)
                rate += UnitRate(item.Key, data, civ, age);
        }
        return rate;
    }

    private static double UnitRate(string key, GameData data, CivBonuses civ, Age age)
    {
        if (!data.TryGet(key, out var entity) || entity.Kind != EntityKind.Unit || entity.Pop <= 0)
            return 0;
        var seconds = civ.Time(entity, age) / civ.WorkRate(entity.MadeIn, age);
        return seconds > 0 ? entity.Pop / seconds : 0;
    }

    /// <summary>Lo que pasa desde el aviso hasta que la casa da lugar: construirla con la civ, caminar y el jugador.</summary>
    public static double ReactionSeconds(GameData data, CivBonuses civ, Age age) =>
        (data.TryGet("house", out var house) ? civ.Time(house, age) : 25) + WalkSeconds + PlayerSeconds;

    /// <summary>Lugares libres con los que avisar: el mayor entre <see cref="MinMargin"/> y ritmo × reacción, para arriba.</summary>
    public static int Margin(double consumptionRate, double reactionSeconds) =>
        Math.Max(MinMargin, (int)Math.Ceiling(consumptionRate * reactionSeconds - 1e-9));
}
