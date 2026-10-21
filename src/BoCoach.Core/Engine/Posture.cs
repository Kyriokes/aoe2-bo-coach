using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Un edificio de la postura que no está produciendo, o que todavía no se construyó.</summary>
/// <param name="Building">Clave del edificio ("stable").</param>
/// <param name="Unit">La unidad de la postura que se hace ahí ("scout_cavalry").</param>
/// <param name="Count">Cuántos de esos edificios están parados (0 si no hay ninguno construido).</param>
public sealed record PostureGap(string Building, string Unit, int Count)
{
    public bool Missing => Count == 0;
}

/// <summary>Lo que muestra el overlay de la postura de cierre (D2) con la BO terminada.</summary>
/// <param name="Queue">Las unidades siempre en cola.</param>
/// <param name="Derived">La BO no trae postura: se dedujo de sus pasos.</param>
/// <param name="Gaps">Edificios de la postura parados o que faltan.</param>
/// <param name="NextAge">La edad a la que se apunta ("castle_age"), o null si no hay (o ya se llegó).</param>
/// <param name="NextAgeMissing">Lo que falta para la edad guardando para la producción (cero si alcanza).</param>
/// <param name="NextAgeBuildingsMissing">Edificios de la edad actual que faltan para poder subir (pide 2).</param>
/// <param name="NextAgeInProgress">La edad ya se está investigando (está en la cola).</param>
public sealed record PostureView(
    IReadOnlyList<string> Queue,
    bool Derived,
    IReadOnlyList<PostureGap> Gaps,
    string? NextAge,
    ResourceAmounts NextAgeMissing,
    int NextAgeBuildingsMissing,
    bool NextAgeInProgress)
{
    /// <summary>Lo que cuesta la próxima edad más un lote de cada unidad de la postura por edificio (sin restar el stock).</summary>
    public ResourceAmounts NextAgeNeed { get; init; }

    /// <summary>Se puede hacer clic en la edad ya.</summary>
    public bool NextAgeReady => NextAge is not null && !NextAgeInProgress && NextAgeBuildingsMissing == 0
        && NextAgeMissing == ResourceAmounts.Zero;
}

/// <summary>
/// La postura de cierre de una BO (D2, diseño acordado con el usuario el 2026-10-20): al terminar los pasos, el
/// coach pide tener siempre en cola las unidades de la postura (en cada edificio que las produce) y junta lo que
/// sobra para la próxima edad. Las BO sin postura la deducen de sus pasos.
/// </summary>
public static class Posture
{
    private static readonly string[] Ages = ["feudal_age", "castle_age", "imperial_age"];

    /// <summary>La postura de la BO, o la deducida: aldeanos más la última unidad militar que entrena la BO, y la edad siguiente a la última que clica.</summary>
    public static (ClosingPosture Posture, bool Derived) Effective(BuildOrder bo)
    {
        if (bo.Posture is { } written)
            return (written, false);
        var queue = new List<string> { "villager" };
        if (bo.Steps.LastOrDefault(s => s.Do == StepAction.Train && s.What is { } w && w != "villager") is { What: { } unit })
            queue.Add(unit);
        var lastAge = bo.Steps.Where(s => s.Do == StepAction.Research && s.What is { } w && Ages.Contains(w)).Select(s => Array.IndexOf(Ages, s.What)).DefaultIfEmpty(-1).Max();
        return (new ClosingPosture { Queue = queue, NextAge = lastAge + 1 < Ages.Length ? Ages[lastAge + 1] : null }, true);
    }
}

/// <summary>Lleva la postura en vivo: los edificios parados (con margen) y la próxima edad.</summary>
public sealed class PostureCoach(BuildOrder bo, GameData data)
{
    /// <summary>Segundos sin nada en cola para dar por parado un edificio (lo mismo que el centro urbano).</summary>
    public double IdleGrace { get; init; } = 5;

    private readonly (ClosingPosture Posture, bool Derived) _posture = Posture.Effective(bo);
    private readonly Dictionary<string, double> _idleSince = [];

    public ClosingPosture Definition => _posture.Posture;

    /// <summary>La vista de la postura ahora. <paramref name="queue"/> es la última cola leída (null si no se lee).</summary>
    public PostureView View(PlayerState state, IReadOnlyList<QueueItem>? queue, Age age, CivBonuses civ)
    {
        var posture = _posture.Posture;
        var gaps = new List<PostureGap>();
        var reserve = ResourceAmounts.Zero;
        foreach (var unit in posture.Queue)
        {
            if (!data.TryGet(unit, out var entity) || entity.MadeIn is not { } building)
                continue;
            var buildings = Built(state, building);
            reserve += civ.Cost(entity, age) * Math.Max(1, buildings);
            // El centro urbano parado ya lo avisa el recordatorio del centro urbano.
            if (building == BuildOrderValidator.StartingBuilding)
                continue;
            if (buildings == 0)
            {
                gaps.Add(new PostureGap(building, unit, 0));
                continue;
            }
            if (queue is null)
                continue;
            var working = queue.Count(i => !i.Waiting && data.TryGet(i.Key, out var e) && e.MadeIn == building);
            if (working >= buildings)
            {
                _idleSince.Remove(building);
                continue;
            }
            var since = _idleSince.TryGetValue(building, out var s) ? s : _idleSince[building] = state.Time;
            if (state.Time - since >= IdleGrace)
                gaps.Add(new PostureGap(building, unit, buildings - working));
        }

        var next = posture.NextAge;
        if (next is not null && (state.Count($"{next}_researched") > 0 || BuildOrderValidator.AgeOf(next) is { } target && target <= age))
            next = null;
        var missing = ResourceAmounts.Zero;
        var needed = ResourceAmounts.Zero;
        var buildingsMissing = 0;
        if (next is not null && data.TryGet(next, out var ageTech))
        {
            var need = needed = civ.Cost(ageTech, age) + reserve;
            foreach (var r in Enum.GetValues<Resource>())
                missing = missing.With(r, Math.Max(0, need[r] - state.Stock[r]));
            if (BuildOrderValidator.AgeUpRequirement(next) is { } requirement)
                buildingsMissing = Math.Max(0, 2 - requirement.Buildings.Sum(b => Built(state, b)));
        }
        return new PostureView(posture.Queue, _posture.Derived, gaps, next, missing, buildingsMissing,
            next is not null && queue?.Any(i => i.Key == next) == true) { NextAgeNeed = needed };
    }

    /// <summary>
    /// Lo que pide la postura por segundo, para la economía y la GD: cada unidad de la postura producida sin parar en
    /// cada edificio que la hace (con la velocidad de producción de la civ), más juntar en
    /// <paramref name="savingSeconds"/> la próxima edad con un lote de cada unidad (<see cref="PostureView.NextAgeNeed"/>).
    /// Va sin restar el stock, como los pasos de la BO: la economía lo resta al medir el déficit (con lo que falta, el
    /// stock se restaba dos veces y la GD de la partida de scouts del 2026-10-15 nunca mandaba aldeanos al oro para
    /// Castillos). Con la edad en curso no se junta para ella, ni para una que no sea la siguiente a la actual: en la
    /// 20261018-132830, con Castillos investigándose, juntar para Imperial pedía pasar 5 de madera a comida.
    /// </summary>
    /// <param name="villagers">False con el tope de aldeanos alcanzado: el centro urbano deja de sacarlos.</param>
    public ResourceAmounts Demand(PostureView view, PlayerState state, Age age, CivBonuses civ, double savingSeconds, bool villagers)
    {
        var demand = ResourceAmounts.Zero;
        foreach (var unit in _posture.Posture.Queue)
        {
            if (unit == "villager" && !villagers || !data.TryGet(unit, out var entity) || entity.MadeIn is not { } building)
                continue;
            var train = civ.Time(entity, age) / civ.WorkRate(building, age);
            if (train > 0)
                demand += civ.Cost(entity, age) * (Built(state, building) / train);
        }
        if (view.NextAge is { } next && !view.NextAgeInProgress && savingSeconds > 0 && BuildOrderValidator.AgeOf(next) == age + 1)
            demand += view.NextAgeNeed * (1 / savingSeconds);
        return demand;
    }

    /// <summary>Cuántos de ese edificio hay según el log (el centro urbano, uno más: el inicial).</summary>
    private static int Built(PlayerState state, string building) =>
        state.Count($"{building}_built") + (building == BuildOrderValidator.StartingBuilding ? 1 : 0);
}
