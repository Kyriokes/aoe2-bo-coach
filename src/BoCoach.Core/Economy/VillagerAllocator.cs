using BoCoach.Core.Data;

namespace BoCoach.Core.Economy;

/// <summary>Cómo se elige la sugerencia de aldeanos.</summary>
public enum AllocatorKind
{
    /// <summary>v1: de a 1 aldeano, del recurso que más sobra al que más falta, mientras mejore.</summary>
    Greedy,

    /// <summary>v2: prueba todas las sugerencias posibles y se queda con la que deja mejor la economía.</summary>
    Exact,
}

/// <summary>
/// Elige qué sugerir ("Pasá k de A a B") a partir del déficit proyectado en el horizonte H, los aldeanos estimados en
/// cada recurso y lo que junta un aldeano por segundo en cada uno. Mover k aldeanos de A a B cambia el déficit de A en
/// <c>+k · tasa_A · H</c> y el de B en <c>-k · tasa_B · H</c>.
/// </summary>
public static class VillagerAllocator
{
    public static Suggestion? Choose(ResourceAmounts deficit, ResourceAmounts villagers, ResourceAmounts perVillager, EconomySettings settings) =>
        settings.Allocator == AllocatorKind.Greedy
            ? Greedy(deficit, villagers, perVillager, settings)
            : Exact(deficit, villagers, perVillager, settings);

    /// <summary>
    /// v2: la sugerencia que más baja el costo de la economía (<see cref="Cost"/>), menos lo que cuesta mover a la
    /// gente: lo que cada uno deja de juntar en <see cref="EconomySettings.MoveSeconds"/>, y
    /// <see cref="EconomySettings.NewSiteCost"/> si van a un recurso donde no hay nadie (campamento nuevo o caminata
    /// larga). Como el overlay muestra un solo par de
    /// recursos a la vez, se prueban todos: 12 pares por hasta <see cref="EconomySettings.MaxMoves"/> aldeanos. Es el
    /// óptimo exacto entre lo que se puede sugerir, sin necesidad de un solver.
    /// <para>Igual que en v1, el recurso que cede gente tiene que seguir sobrando (déficit hasta la mitad de
    /// <see cref="EconomySettings.SurplusThreshold"/>): las tasas son estimadas y no conviene dejarlo justo.</para>
    /// </summary>
    public static Suggestion? Exact(ResourceAmounts deficit, ResourceAmounts villagers, ResourceAmounts perVillager, EconomySettings settings)
    {
        var h = settings.Horizon;
        var current = Cost(deficit, settings);
        Suggestion? best = null;
        var bestGain = settings.MinGain;

        foreach (var from in Enum.GetValues<Resource>())
        {
            var available = Math.Min(settings.MaxMoves, (int)Math.Floor(villagers[from] + 1e-9));
            foreach (var to in Enum.GetValues<Resource>().Where(r => r != from))
            {
                var newSite = villagers[to] < 0.5 ? settings.NewSiteCost : 0;
                var moveCost = settings.MoveSeconds * perVillager[to];
                for (var k = 1; k <= available; k++)
                {
                    var donor = deficit[from] + k * perVillager[from] * h;
                    if (donor > -settings.SurplusThreshold / 2)
                        break; // el que cede quedaría justo
                    var after = deficit.With(from, donor).With(to, deficit[to] - k * perVillager[to] * h);
                    var gain = current - Cost(after, settings) - k * moveCost - newSite;
                    if (gain > bestGain)
                        (best, bestGain) = (new Suggestion(from, to, k), gain);
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Costo de cómo queda la economía en el horizonte: lo que va a faltar (por encima de
    /// <see cref="EconomySettings.ShortageThreshold"/>) y, con menos peso, lo que sobra de más (por encima de
    /// <see cref="EconomySettings.SurplusThreshold"/>). Que algo sobre no alcanza para mover gente (el peso por aldeano
    /// es menor que el costo de moverlo); sirve para elegir de dónde sacarla cuando algo falta.
    /// </summary>
    public static double Cost(ResourceAmounts deficit, EconomySettings settings) =>
        Enum.GetValues<Resource>().Sum(r =>
            Math.Max(0, deficit[r] - settings.ShortageThreshold)
            + settings.SurplusWeight * Math.Max(0, -deficit[r] - settings.SurplusThreshold));

    /// <summary>v1 (greedy): mueve de a 1 aldeano del recurso que más sobra al que más falta, mientras mejore.</summary>
    public static Suggestion? Greedy(ResourceAmounts deficit, ResourceAmounts villagers, ResourceAmounts perVillager, EconomySettings settings)
    {
        var d = deficit;
        var h = settings.Horizon;
        Resource? from = null, to = null;
        var moved = 0;

        while (moved < settings.MaxMoves)
        {
            var needy = Enum.GetValues<Resource>().Where(r => d[r] > settings.ShortageThreshold).OrderByDescending(r => d[r]).ToList();
            var donors = Enum.GetValues<Resource>()
                .Where(r => d[r] < -settings.SurplusThreshold && villagers[r] - moved * (r == from ? 1 : 0) >= 1)
                .OrderBy(r => d[r])
                .ToList();
            if (needy.Count == 0 || donors.Count == 0)
                break;

            var target = needy[0];
            var donor = donors[0];
            if (from is not null && (donor != from || target != to))
                break; // una sola sugerencia (un par de recursos) a la vez
            if (d[donor] + perVillager[donor] * h > -settings.SurplusThreshold / 2)
                break; // el donante pasaría a quedar justo

            from = donor;
            to = target;
            d = d.With(donor, d[donor] + perVillager[donor] * h).With(target, d[target] - perVillager[target] * h);
            moved++;
        }

        return from is { } f && to is { } t ? new Suggestion(f, t, moved) : null;
    }
}
