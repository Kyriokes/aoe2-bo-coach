using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Lo que se leyó de la pantalla en una captura, ya suavizado. null = no se pudo leer (región sin calibrar,
/// número cortado, pantalla de carga).
/// </summary>
/// <param name="NewEvents">Eventos del log que aparecieron en esta captura (ids de data/events.json).</param>
/// <param name="VillagersOn">Aldeanos en cada recurso según el HUD, o null si no se leyeron los cuatro.</param>
public sealed record ScreenReading(
    double? Clock,
    double? Food,
    double? Wood,
    double? Gold,
    double? Stone,
    int? Villagers,
    int? Population,
    int? PopulationCap,
    IReadOnlyList<string> NewEvents,
    ResourceAmounts? VillagersOn = null);

/// <summary>
/// Arma los <see cref="GameSnapshot"/> del motor a partir de las lecturas de pantalla:
/// <list type="bullet">
/// <item>Sin reloj no hay snapshot (pantalla de carga, menú): los eventos de esa captura se guardan para el siguiente.</item>
/// <item>Un recurso que no se leyó conserva su último valor (al principio, el stock inicial).</item>
/// <item>Si no se ve la población "N/M", se estima: aldeanos + unidades militares vistas en el log + el explorador
/// inicial, y el tope con lo que suman los edificios construidos (casas, centros urbanos). El log junta mensajes
/// repetidos, así que el tope estimado nunca queda por debajo de la población.</item>
/// <item>Si se ve, el tope delata las casas que el log no avisó (una segunda "House Built" mientras la primera sigue
/// siendo el último renglón no cambia nada en el log): una suba que ningún edificio avisado explica es una casa
/// terminada. Si el aviso llega después, no se cuenta de nuevo.</item>
/// <item>Con el "N/M" y el total de aldeanos, las unidades militares son la diferencia. Si suben más de lo que avisó
/// el log (dos galerías sacan un arquero cada una y el log muestra un solo renglón), las de más son del tipo del último
/// aviso de unidad. Mismo cuidado que con las casas: un aviso que llega después no es otra unidad.</item>
/// </list>
/// </summary>
public sealed class SnapshotBuilder(GameData data)
{
    private const int StartingCap = 5;
    private const int MaxPopulation = BuildOrders.BuildOrderValidator.MaxPopulation;
    private const string House = "house";
    private const string HouseBuilt = "house_built";

    /// <summary>Segundos de partida que un edificio avisado espera a que el tope suba antes de olvidarlo.</summary>
    private const double CreditSeconds = 20;

    private readonly List<string> _pendingEvents = [];
    /// <summary>Si el tope no se leyó en este tiempo (segundos de partida), su próxima lectura es un punto de partida nuevo.</summary>
    private const double CapGapSeconds = 5;

    private readonly List<(double Time, int Pop)> _capCredits = [];
    private readonly List<double> _inferredHouses = [];
    private (int Cap, double Time)? _lastReadCap;
    private double _lastClock;
    private ResourceAmounts _stock = PlanStart.Standard.Stock;
    private int _military;
    private int _builtCap = StartingCap;
    private int _population = PlanStart.Standard.Population;
    private int _cap = PlanStart.Standard.PopulationCap;
    private int? _villagers;

    /// <summary>
    /// Segundos que las unidades militares tienen que quedarse en un valor nuevo para tomarlo como firme. El total de
    /// aldeanos y el "N/M" (que cerca del tope el juego muestra una captura sí y otra no) cambian en capturas
    /// distintas: al salir un aldeano, la diferencia baja o sube por unos segundos sin que haya pasado nada (en la
    /// 20261018-001030, 5 s). Una baja (unidades que mueren) se espera más, porque contarla de más haría contar dos
    /// veces la unidad siguiente.
    /// </summary>
    private const double RiseSeconds = 3;
    private const double DropSeconds = 10;

    /// <summary>Unidades de más en una sola suba para creerla (si no, es una lectura mala).</summary>
    private const int MaxUnitsAtOnce = 6;

    private int? _steadyMilitary;
    private (int Military, double Since)? _militaryRun;
    private double _lastMilitaryRead = double.NegativeInfinity;
    private readonly List<double> _unitCredits = [];
    private readonly List<double> _unexplainedUnits = [];
    private readonly List<(string Id, double Time)> _inferredUnits = [];
    private string? _lastUnit;

    /// <summary>Edificios terminados según el log (y los deducidos), y cuándo salió cada unidad de cada tipo.</summary>
    private readonly Dictionary<string, int> _buildings = new() { [BuildOrders.BuildOrderValidator.StartingBuilding] = 1 };
    private readonly Dictionary<string, List<double>> _unitTimes = [];

    /// <summary>True si la última población salió de la estimación y no de la pantalla.</summary>
    public bool PopulationEstimated { get; private set; } = true;

    public GameSnapshot? Build(ScreenReading reading)
    {
        var now = reading.Clock ?? _lastClock;
        foreach (var id in reading.NewEvents)
        {
            // Una casa que el tope ya había delatado: el aviso del log llegó tarde y no es otra casa.
            _inferredHouses.RemoveAll(t => now - t > CreditSeconds);
            if (id == HouseBuilt && _inferredHouses.Count > 0)
            {
                _inferredHouses.RemoveAt(0);
                continue;
            }
            if (EventConventions.IsUnitCreated(id) && id != EventConventions.VillagerCreated)
            {
                LoggedUnit(id, now);
                continue;
            }
            _pendingEvents.Add(id);
            if (EventConventions.Parse(id) is { Action: BuildOrders.StepAction.Build } built)
            {
                _buildings[built.What] = _buildings.GetValueOrDefault(built.What) + 1;
                if (data.TryGet(built.What, out var entity) && entity.PopBonus > 0)
                {
                    _builtCap += entity.PopBonus;
                    _capCredits.Add((now, entity.PopBonus));
                }
            }
        }
        if (reading.PopulationCap is { } readCap)
            InferHousesFromCap(readCap, now);
        if (reading.Clock is { } c)
            _lastClock = c;

        _stock = new ResourceAmounts(
            reading.Food ?? _stock.Food,
            reading.Wood ?? _stock.Wood,
            reading.Stone ?? _stock.Stone,
            reading.Gold ?? _stock.Gold);
        // Más aldeanos que el tope de población del juego es una lectura mala: se descarta.
        var readVillagers = reading.Villagers is { } read && read <= MaxPopulation ? read : (int?)null;
        _villagers = readVillagers ?? _villagers;

        if (reading.Population is { } population && reading.PopulationCap is { } cap)
        {
            (_population, _cap) = (population, cap);
            PopulationEstimated = false;
            if (readVillagers is { } v && population >= v)
                InferUnitsFromPopulation(population - v, now);
        }
        else if (_villagers is { } villagers)
        {
            _population = villagers + _military + 1;
            _cap = Math.Min(MaxPopulation, Math.Max(_builtCap, _population));
            PopulationEstimated = true;
        }

        if (reading.Clock is not { } clock)
            return null;
        var events = _pendingEvents.Select(id => new GameEvent(id, clock)).ToList();
        _pendingEvents.Clear();
        return new GameSnapshot(clock, _stock, _population, _cap, events) { Villagers = readVillagers, VillagersOn = reading.VillagersOn };
    }

    /// <summary>Un "X Created" del log que no es un aldeano.</summary>
    private void LoggedUnit(string id, double now)
    {
        PruneUnits(now);
        _lastUnit = id;
        // Una unidad que la población ya había delatado: el aviso llegó tarde.
        var inferred = _inferredUnits.FindIndex(u => u.Id == id);
        if (inferred >= 0)
        {
            _inferredUnits.RemoveAt(inferred);
            return;
        }
        AddUnit(id, now);
        if (_unexplainedUnits.Count == 0)
        {
            _unitCredits.Add(now);
            return;
        }
        // La población subió antes de saber qué era: este aviso dice el tipo de todas (salieron juntas).
        _unexplainedUnits.RemoveAt(0);
        foreach (var _ in _unexplainedUnits)
            AddUnit(id, now);
        _unexplainedUnits.Clear();
    }

    private void InferUnitsFromPopulation(int military, double now)
    {
        PruneUnits(now);
        // Después de un rato sin leer, el log pudo avisar esas unidades: se arranca de nuevo.
        if (now - _lastMilitaryRead > CapGapSeconds)
            (_steadyMilitary, _militaryRun) = (military, null);
        _lastMilitaryRead = now;
        if (_steadyMilitary is not { } steady || military == steady)
        {
            _militaryRun = null;
            return;
        }
        if (_militaryRun is not { } run || run.Military != military)
            _militaryRun = run = (military, now);
        if (now - run.Since < (military > steady ? RiseSeconds : DropSeconds))
            return;

        var rise = military - steady;
        (_steadyMilitary, _militaryRun) = (military, null);
        if (rise <= 0 || rise > MaxUnitsAtOnce)
            return;
        // Primero, lo que explican los avisos del log.
        var explained = Math.Min(rise, _unitCredits.Count);
        _unitCredits.RemoveRange(0, explained);
        for (var i = explained; i < rise; i++)
        {
            if (_lastUnit is { } id)
            {
                AddUnit(id, now);
                _inferredUnits.Add((id, now));
            }
            else
            {
                _unexplainedUnits.Add(now);
            }
        }
    }

    private void AddUnit(string id, double now)
    {
        _pendingEvents.Add(id);
        _military++;
        BuildingsFromProduction(id, now);
    }

    /// <summary>
    /// Parte del tiempo de entrenamiento dentro de la que dos unidades iguales tienen que salir de edificios distintos
    /// (ni con las bonificaciones de civ un edificio saca una unidad en la mitad de su tiempo).
    /// </summary>
    private const double SameBuildingWindow = 0.5;

    /// <summary>
    /// Si salen más unidades iguales juntas de las que pueden sacar los edificios avisados por el log, hay más
    /// edificios: el log no agrega renglón si dos terminan a la vez (dos galerías de tiro con arco) y el paso de la BO
    /// quedaba trabado.
    /// </summary>
    private void BuildingsFromProduction(string unitEvent, double now)
    {
        if (EventConventions.Parse(unitEvent) is not { Action: BuildOrders.StepAction.Train } unit
            || !data.TryGet(unit.What, out var entity) || entity.MadeIn is not { } building || entity.Time <= 0)
            return;
        if (!_unitTimes.TryGetValue(unit.What, out var times))
            _unitTimes[unit.What] = times = [];
        times.RemoveAll(t => now - t > entity.Time * SameBuildingWindow);
        times.Add(now);
        var known = _buildings.GetValueOrDefault(building);
        for (; known < times.Count; known++)
            _pendingEvents.Add(EventConventions.EventFor(BuildOrders.StepAction.Build, building)!);
        _buildings[building] = known;
    }

    private void PruneUnits(double now)
    {
        _unitCredits.RemoveAll(t => now - t > CreditSeconds);
        _unexplainedUnits.RemoveAll(t => now - t > CreditSeconds);
        _inferredUnits.RemoveAll(u => now - u.Time > CreditSeconds);
    }

    private void InferHousesFromCap(int cap, double now)
    {
        _capCredits.RemoveAll(c => now - c.Time > CreditSeconds);
        if (_lastReadCap is { } last && now - last.Time <= CapGapSeconds && cap > last.Cap)
        {
            var rise = cap - last.Cap;
            // Primero, lo que explican los edificios que el log avisó.
            while (rise > 0 && _capCredits.Count > 0)
            {
                var used = Math.Min(rise, _capCredits[0].Pop);
                rise -= used;
                if (used == _capCredits[0].Pop)
                    _capCredits.RemoveAt(0);
                else
                    _capCredits[0] = (_capCredits[0].Time, _capCredits[0].Pop - used);
            }
            var housePop = data.TryGet(House, out var house) && house.PopBonus > 0 ? house.PopBonus : 5;
            for (; rise >= housePop; rise -= housePop)
            {
                _pendingEvents.Add(HouseBuilt);
                _inferredHouses.Add(now);
                _builtCap += housePop;
            }
        }
        _lastReadCap = (cap, now);
    }
}
