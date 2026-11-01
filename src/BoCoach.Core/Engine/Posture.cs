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

    /// <summary>
    /// La postura de la BO, o la deducida: aldeanos más la producción que la BO deja armada, y la edad siguiente a la
    /// última que clica. La producción es la última unidad militar que entrena la BO más la unidad de cada edificio de
    /// otro tipo que construye después (<see cref="DefaultUnit"/>): en la partida 20261031-215314 ("Scouts → CA" de los
    /// hunos: exploradores y después 3 galerías) pedía solo exploradores hasta el final. Se suman y no se reemplazan
    /// porque un edificio del final puede ser solo el segundo de Feudal para Castillos (la galería de "Romans
    /// Infantry"): en vivo, <see cref="PostureCoach"/> deja de pedir el que el jugador no usa.
    /// </summary>
    public static (ClosingPosture Posture, bool Derived) Effective(BuildOrder bo, GameData data)
    {
        if (bo.Posture is { } written)
            return (written, false);
        var queue = new List<string> { "villager" };
        var lastAge = bo.Steps.Where(s => s.Do == StepAction.Research && s.What is { } w && Ages.Contains(w)).Select(s => Array.IndexOf(Ages, s.What)).DefaultIfEmpty(-1).Max();
        var finalAge = lastAge < 0 ? Age.Dark : BuildOrderValidator.AgeOf(Ages[lastAge]) ?? Age.Dark;
        var lastUnit = bo.Steps.Select((s, i) => (Step: s, Index: i))
            .LastOrDefault(x => x.Step.Do == StepAction.Train && x.Step.What is { } w && w != "villager");
        var unitBuilding = lastUnit.Step?.What is { } u && data.TryGet(u, out var unitEntity) ? unitEntity.MadeIn : null;
        var later = bo.Steps.Skip(lastUnit.Step is null ? 0 : lastUnit.Index + 1)
            .Where(s => s.Do == StepAction.Build && s.What is { } b && b != unitBuilding && b != BuildOrderValidator.StartingBuilding)
            .Select(s => DefaultUnit(data, bo.Civ, s.What!, finalAge))
            .OfType<string>().Distinct().ToList();
        if (lastUnit.Step is not null)
            queue.AddRange(later.Prepend(lastUnit.Step.What!).Distinct());
        return (new ClosingPosture { Queue = queue, NextAge = lastAge + 1 < Ages.Length ? Ages[lastAge + 1] : null }, true);
    }

    /// <summary>
    /// La unidad de la postura para un edificio que la BO construye sin entrenar nada ahí: la primera de ese edificio
    /// que la civ puede hacer, de la edad más vieja hasta <paramref name="age"/> (galería: arqueros; establo:
    /// exploradores). null si no produce unidades militares (herrería, mercado) o si es el monasterio: los monjes no se
    /// sacan sin parar (la de los francos hace caballeros y al final mercado y monasterio). En vivo manda lo que el
    /// jugador saca ahí (<see cref="PostureCoach"/>).
    /// </summary>
    public static string? DefaultUnit(GameData data, string? civ, string building, Age age) => data.Entities
        .Where(e => e.Value.Kind == EntityKind.Unit && e.Value.MadeIn == building && e.Value.Age <= age
                    && !NonMilitary.Contains(e.Key) && data.CanMake(civ, e.Key))
        .OrderBy(e => e.Value.Age).Select(e => e.Key).FirstOrDefault();

    /// <summary>Las unidades que no cuentan como producción de la postura (las que no abandonan la BO, y los monjes).</summary>
    private static readonly HashSet<string> NonMilitary = ["villager", "fishing_ship", "trade_cart", "transport_ship", "monk"];
}

/// <summary>Lleva la postura en vivo: los edificios parados (con margen) y la próxima edad.</summary>
public sealed class PostureCoach(BuildOrder bo, GameData data)
{
    /// <summary>Segundos sin nada en cola para dar por parado un edificio (lo mismo que el centro urbano).</summary>
    public double IdleGrace { get; init; } = 5;

    private readonly (ClosingPosture Posture, bool Derived) _posture = Posture.Effective(bo, data);
    private readonly Dictionary<string, double> _idleSince = [];
    private readonly Dictionary<string, string> _lastMade = [];
    private double? _start;

    public ClosingPosture Definition => _posture.Posture;

    /// <summary>La vista de la postura ahora. <paramref name="queue"/> es la última cola leída (null si no se lee).</summary>
    public PostureView View(PlayerState state, IReadOnlyList<QueueItem>? queue, Age age, CivBonuses civ)
    {
        var posture = _posture.Posture;
        var units = Live(state, queue);
        var gaps = new List<PostureGap>();
        var reserve = ResourceAmounts.Zero;
        foreach (var unit in units)
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
            {
                // El castillo cuenta doble para Imperial: solo ya alcanza.
                var built = requirement.Buildings.Sum(b => Built(state, b))
                            + (BuildOrderValidator.AgeUpAloneBuilding(next) is { } alone && Built(state, alone) > 0 ? 1 : 0);
                buildingsMissing = Math.Max(0, 2 - built);
            }
        }
        return new PostureView(units, _posture.Derived, gaps, next, missing, buildingsMissing,
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
        foreach (var unit in view.Queue)
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

    /// <summary>Unidades que otro edificio de la postura tiene que sacar para dar por dejado a uno (ver <see cref="Live"/>).</summary>
    public int SwitchUnits { get; init; } = 3;

    /// <summary>
    /// Las unidades de la postura como las juega el jugador. En cada edificio, la última unidad que salió de ahí según
    /// la cola o el log (con <see cref="UnitLines"/>, la base de su mejora): la BO deducida pide arqueros en la galería
    /// y el jugador saca arqueros a caballo. Y un edificio queda afuera si otro de la postura sacó
    /// <see cref="SwitchUnits"/> unidades desde que este sacó la última (o desde que se construyó, o desde que empezó
    /// la postura si no hay ninguno): los hunos de "Scouts → CA" dejan el establo por las galerías, y los romanos de
    /// "Romans Infantry" no usan la galería que hicieron para Castillos. Si nadie produce, se piden todos.
    /// </summary>
    private List<string> Live(PlayerState state, IReadOnlyList<QueueItem>? queue)
    {
        _start ??= state.Time;
        var units = new List<string>();
        var military = new List<(string Unit, string Building)>();
        foreach (var unit in _posture.Posture.Queue)
        {
            if (unit == "villager" || !data.TryGet(unit, out var entity) || entity.MadeIn is not { } building)
            {
                units.Add(unit);
                continue;
            }
            if (queue?.LastOrDefault(i => !i.Waiting && IsUnitOf(i.Key, building)) is { } item)
                _lastMade[building] = UnitLines.Base(item.Key);
            else if (state.Events.LastOrDefault(e => IsCreatedIn(e, building)) is { } created)
                _lastMade[building] = UnitLines.Base(Made(created.EventId));
            military.Add((_lastMade.GetValueOrDefault(building, unit), building));
        }
        foreach (var (unit, building) in military)
        {
            var since = Math.Max(
                state.Events.Where(e => IsCreatedIn(e, building) || e.EventId == $"{building}_built").Select(e => e.Time).DefaultIfEmpty(double.MinValue).Max(),
                state.Events.Any(e => e.EventId == $"{building}_built") || building == BuildOrderValidator.StartingBuilding ? double.MinValue : _start.Value);
            var left = military.Any(o => o.Building != building
                                         && state.Events.Count(e => e.Time > since && IsCreatedIn(e, o.Building)) >= SwitchUnits);
            if (!left && !units.Contains(unit))
                units.Add(unit);
        }
        return units;
    }

    private static string Made(string eventId) => eventId[..^"_created".Length];

    private bool IsCreatedIn(GameEvent e, string building) => EventConventions.IsUnitCreated(e.EventId) && IsUnitOf(Made(e.EventId), building);

    private bool IsUnitOf(string key, string building) =>
        data.TryGet(UnitLines.Base(key), out var e) && e.Kind == EntityKind.Unit && e.MadeIn == building;

    /// <summary>Cuántos de ese edificio hay según el log (el centro urbano, uno más: el inicial).</summary>
    private static int Built(PlayerState state, string building) =>
        state.Count($"{building}_built") + (building == BuildOrderValidator.StartingBuilding ? 1 : 0);
}
