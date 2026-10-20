using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>
/// Lo que se leyó de la pantalla en una captura, ya suavizado. null = no se pudo leer (región sin calibrar,
/// número cortado, pantalla de carga).
/// </summary>
/// <param name="NewEvents">Eventos del log que aparecieron en esta captura (ids de data/events.json).</param>
/// <param name="VillagersOn">Aldeanos en cada recurso según el HUD, o null si no se leyeron los cuatro.</param>
/// <param name="Queue">La cola de producción, o null si no se lee.</param>
/// <param name="Scores">El panel de puntajes, si se leyó en esta captura, o null.</param>
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
    ResourceAmounts? VillagersOn = null,
    IReadOnlyList<QueueItem>? Queue = null,
    IReadOnlyList<PlayerScore>? Scores = null);

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
/// <item>El tiempo cuenta desde que el jugador empieza a jugar, no desde el 00:00 del reloj: en la 20261017-163738 se
/// quedó 36 minutos quieto con los 3 aldeanos iniciales y todos los objetivos de la BO quedaban atrasados (ver
/// <see cref="StartOffset"/>).</item>
/// </list>
/// </summary>
/// <param name="civ">La civ con que se juega: su arranque (recursos y aldeanos iniciales) y la población que dan sus casas. null = valores base.</param>
public sealed class SnapshotBuilder(GameData data, CivBonuses? civ = null)
{
    private readonly CivBonuses _civ = civ ?? CivBonuses.None;
    private readonly PlanStart _start = PlanStart.For(civ ?? CivBonuses.None, data);

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
    private ResourceAmounts _stock = PlanStart.For(civ ?? CivBonuses.None, data).Stock;
    private int _military;
    private int _builtCap = PlanStart.For(civ ?? CivBonuses.None, data).PopulationCap;
    private int _population = PlanStart.For(civ ?? CivBonuses.None, data).Population;
    private int _cap = PlanStart.For(civ ?? CivBonuses.None, data).PopulationCap;
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

    /// <summary>
    /// Segundos del reloj del juego que pasaron antes de que el jugador empezara a jugar: el tiempo de los snapshots
    /// (y de sus eventos) es el reloj menos esto. Se fija en la primera captura con acción (un aldeano más que los
    /// iniciales, el stock distinto del inicial, un evento del log o algo en la cola de producción). Si antes se lo
    /// vio quieto, el inicio es esa captura; si la primera captura ya tiene acción (el coach arrancó a mitad de
    /// partida), se descuentan los aldeanos entrenados y lo demás se toma por idle solo si es casi todo el reloj.
    /// Menos de <see cref="IdleThreshold"/> no cuenta: en una partida normal se actúa en los primeros segundos.
    /// </summary>
    public double StartOffset { get; private set; }

    public const double IdleThreshold = 60;

    /// <summary>A mitad de partida, parte del reloj que tiene que quedar sin explicar por los aldeanos para tomarla por idle.</summary>
    private const double MostlyIdle = 0.75;

    private bool _startKnown;
    private bool _sawIdle;

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
                if (data.TryGet(built.What, out var entity) && _civ.PopBonus(entity, Age.Dark) is > 0 and var pop)
                {
                    _builtCap += pop;
                    _capCredits.Add((now, pop));
                }
                // El Settlement de los incas vale por campamento maderero, minero, molino y casa: cada uno avisa como
                // construido (confirma su paso de la BO y cuenta como productor). La población ya la dio el propio edificio.
                foreach (var counted in entity?.CountsAs ?? [])
                {
                    _buildings[counted] = _buildings.GetValueOrDefault(counted) + 1;
                    _pendingEvents.Add(EventConventions.EventFor(BuildOrders.StepAction.Build, counted)!);
                }
            }
        }
        if (reading.PopulationCap is { } readCap)
            InferHousesFromCap(readCap, now);
        if (reading.Queue is { } queue)
            BuildingsFromQueue(queue);
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
        var time = GameTime(clock, reading);
        var events = _pendingEvents.Select(id => new GameEvent(id, time)).ToList();
        _pendingEvents.Clear();
        return new GameSnapshot(time, _stock, _population, _cap, events) { Villagers = readVillagers, VillagersOn = reading.VillagersOn, Queue = reading.Queue, Scores = reading.Scores };
    }

    /// <summary>El reloj llevado al tiempo de juego (ver <see cref="StartOffset"/>); 0 mientras el jugador no haya hecho nada.</summary>
    private double GameTime(double clock, ScreenReading reading)
    {
        if (!_startKnown)
        {
            var villagers = _villagers ?? _start.IdleVillagers;
            var acted = villagers > _start.IdleVillagers
                || _stock != _start.Stock
                || _pendingEvents.Count > 0
                || reading.Queue is { Count: > 0 };
            if (!acted)
            {
                _sawIdle = true;
                return 0;
            }
            _startKnown = true;
            if (_sawIdle)
            {
                StartOffset = clock >= IdleThreshold ? clock : 0;
            }
            else
            {
                var trained = Math.Max(0, villagers - _start.IdleVillagers);
                var estimate = clock - trained * VillagerTime();
                StartOffset = estimate >= IdleThreshold && estimate >= clock * MostlyIdle ? estimate : 0;
            }
        }
        return Math.Max(0, clock - StartOffset);
    }

    private double VillagerTime() => data.TryGet("villager", out var villager) && villager.Time > 0 ? villager.Time : 25;

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

    /// <summary>
    /// La cola de producción tiene un ícono por edificio trabajando (más los que esperan, teñidos): dos arqueros que
    /// avanzan a la vez son dos galerías. En la 20261018-132830 se ven a las 12:14, cuando el log había avisado una
    /// sola (las dos terminaron juntas) y el coach seguía pidiendo la segunda hasta que salieron los arqueros, a las
    /// 12:53. Por las dudas se toma el máximo por clave (no se suman claves distintas) y la cuenta tiene que repetirse
    /// en dos lecturas seguidas.
    /// </summary>
    private void BuildingsFromQueue(IReadOnlyList<QueueItem> queue)
    {
        var counts = new Dictionary<string, int>();
        foreach (var group in queue.Where(i => !i.Waiting).GroupBy(i => i.Key))
        {
            if (data.TryGet(group.Key, out var entity) && entity.MadeIn is { } building)
                counts[building] = Math.Max(counts.GetValueOrDefault(building), group.Count());
        }
        var previous = _lastQueueBuildings;
        _lastQueueBuildings = counts;
        foreach (var (building, count) in counts)
        {
            if (previous.GetValueOrDefault(building) < count)
                continue;
            var known = _buildings.GetValueOrDefault(building);
            for (; known < count; known++)
                _pendingEvents.Add(EventConventions.EventFor(BuildOrders.StepAction.Build, building)!);
            _buildings[building] = known;
        }
    }

    private Dictionary<string, int> _lastQueueBuildings = [];

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
            // Con la civ: las casas de los incas dan 10 (sin esto, cada casa se contaba como dos).
            var housePop = data.TryGet(House, out var house) && _civ.PopBonus(house, Age.Dark) > 0 ? _civ.PopBonus(house, Age.Dark) : 5;
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
