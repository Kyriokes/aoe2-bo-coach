using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;

namespace BoCoach.Core.Engine;

/// <summary>Desde dónde simula el Planner: el inicio de la partida (editor) o el estado actual (en vivo).</summary>
/// <param name="IdleVillagers">Aldeanos sin tarea (al inicio, los 3 que arrancan).</param>
/// <param name="Buildings">Edificios terminados, por clave de entidad.</param>
public sealed record PlanStart(
    double Time,
    ResourceAmounts Stock,
    int Population,
    int PopulationCap,
    Age Age,
    IReadOnlyDictionary<GatherSource, int> Villagers,
    int IdleVillagers,
    IReadOnlyDictionary<string, int> Buildings)
{
    /// <summary>Tecnologías ya terminadas (claves de game_data), en orden: las mejoras económicas cambian las tasas.</summary>
    public IReadOnlyList<string> Researched { get; init; } = [];

    /// <summary>Inicio estándar de AoE2:DE: 3 aldeanos, explorador, centro urbano y 200/200/200/100.</summary>
    public static PlanStart Standard { get; } = new(
        Time: 0,
        Stock: new ResourceAmounts(Food: 200, Wood: 200, Stone: 200, Gold: 100),
        Population: 4,
        PopulationCap: 5,
        Age: Age.Dark,
        Villagers: new Dictionary<GatherSource, int>(),
        IdleVillagers: 3,
        Buildings: new Dictionary<string, int> { [BuildOrderValidator.StartingBuilding] = 1 });

    /// <summary>
    /// El inicio estándar con las bonificaciones de la civ: recursos y aldeanos de más, la población que da su centro
    /// urbano (chinos: 15) y sin tope de casas si no las necesita (hunos).
    /// </summary>
    public static PlanStart For(CivBonuses civ, GameData data)
    {
        if (civ.IsNone)
            return Standard;
        var extra = civ.Start.ExtraVillagers;
        var cap = civ.Start.NoHouses
            ? BuildOrderValidator.MaxPopulation
            : data.TryGet(BuildOrderValidator.StartingBuilding, out var tc) ? civ.PopBonus(tc, Age.Dark) : Standard.PopulationCap;
        // Los aldeanos de más (chinos, mayas) van a las ovejas, que es lo que se hace al arrancar; los 3 comunes
        // quedan sin tarea porque la BO les dice qué hacer (casas, por ejemplo).
        return Standard with
        {
            Stock = Standard.Stock + civ.Start.Resources,
            Population = Standard.Population + extra,
            Villagers = extra > 0 ? new Dictionary<GatherSource, int> { [GatherSource.Sheep] = extra } : Standard.Villagers,
            PopulationCap = cap,
        };
    }
}

public sealed record PlannerSettings
{
    /// <summary>
    /// Fracción de la tasa del .dat que se logra en la práctica (caminatas, carga, etc.). La de granja es la
    /// más baja porque el .dat dice 0,53 y en partida se ve cerca de 0,32. En vivo se reemplaza por lo observado.
    /// </summary>
    public static readonly IReadOnlyDictionary<GatherSource, double> DefaultEfficiency = new Dictionary<GatherSource, double>
    {
        [GatherSource.Sheep] = 0.95,
        [GatherSource.Hunt] = 0.95,
        [GatherSource.Berries] = 0.85,
        [GatherSource.Fish] = 0.8,
        [GatherSource.Farm] = 0.6,
        [GatherSource.Wood] = 0.8,
        [GatherSource.Gold] = 0.85,
        [GatherSource.Stone] = 0.85,
    };

    public IReadOnlyDictionary<GatherSource, double> Efficiency { get; init; } = DefaultEfficiency;

    /// <summary>Hasta qué segundo de partida simular como máximo.</summary>
    public double MaxTime { get; init; } = 3600;

    /// <summary>Cada cuántos segundos guardar una muestra para la línea de tiempo.</summary>
    public double SampleEvery { get; init; } = 10;

    /// <summary>
    /// D12: la comida que hay en el mapa (ovejas, caza, bayas y granjas se agotan). Null = infinita, como en el editor
    /// manual, el optimizador y el coach; la usa el modo asistido.
    /// </summary>
    public FoodSupply? Food { get; init; }
}

/// <summary>Trabajo que falta de un paso. <paramref name="Remaining"/> sale del Tracker (o es el total en el editor).</summary>
/// <param name="PaidAt">Si una unidad del paso ya se pagó y se está haciendo (una investigación en curso), desde cuándo.</param>
public sealed record PlanItem(Step Step, int Remaining, double? PaidAt = null);

public enum BlockReason
{
    /// <summary>Falta población: hay que hacer casas.</summary>
    Population,

    /// <summary>Faltan recursos para pagarlo.</summary>
    Resources,

    /// <summary>No existe el edificio donde se produce.</summary>
    Producer,

    /// <summary>No hay aldeanos para construir o mover.</summary>
    Villagers,

    /// <summary>Todavía no se llegó a la edad necesaria.</summary>
    Age,
}

/// <summary>Un intervalo en que el paso no pudo avanzar.</summary>
public sealed record PlanBlock(string StepId, BlockReason Reason, double From, double To, Resource? Missing = null)
{
    public double Duration => To - From;

    /// <summary>
    /// Lo que falta ya viene en camino: la edad se está investigando, el edificio que lo produce se está construyendo
    /// o el aldeano que lo construye está en la cola. Es una espera, no un trabón.
    /// </summary>
    public bool Expected { get; init; }
}

/// <summary>Por qué unos aldeanos no juntan nada (solo con la comida finita, D12).</summary>
public enum IdleReason
{
    /// <summary>La fuente se acabó (ovejas, caza o bayas).</summary>
    Exhausted,

    /// <summary>La granja se vació y no hay madera para resembrarla.</summary>
    Reseed,

    /// <summary>
    /// Granjeros sin granja que esperan la suya: queda un paso de granjas que la hace desde la granja y todavía no la
    /// empezó (falta madera o le toca después).
    /// </summary>
    WaitingFarm,
}

/// <summary>Un intervalo en que aldeanos de una fuente no juntaron nada (D12: "6 aldeanos parados: se acabaron las ovejas").</summary>
public sealed record PlanIdle(GatherSource Source, IdleReason Reason, double From, double To, int Villagers)
{
    public double Duration => To - From;
}

/// <param name="Start">Cuándo empieza (primer aldeano en cola, primer cimiento, click de la tech).</param>
/// <param name="End">Cuándo termina el último trabajo del paso; null si no termina dentro del horizonte.</param>
/// <param name="Paid">El paso ya estaba pagado al empezar la simulación: <paramref name="Start"/> es real, no estimado.</param>
public sealed record StepEstimate(Step Step, double? Start, double? End, bool Paid = false);

public sealed record PlanSample(double Time, ResourceAmounts Stock, int Population, int PopulationCap, int Villagers);

public enum PlanJobKind
{
    /// <summary>Un edificio que produce una unidad o investiga una tecnología (de a una cosa por edificio).</summary>
    Produce,

    /// <summary>Aldeanos construyendo un edificio.</summary>
    Build,
}

/// <summary>Un trabajo de la simulación: qué se hizo, para qué paso, en qué edificio y cuándo (D8: una línea por edificio).</summary>
/// <param name="Building">El edificio que produce o el que se construye (<see cref="PlanBuilding.Id"/>); null si no se sabe.</param>
public sealed record PlanJob(string StepId, PlanJobKind Kind, string What, string? Building, double Start, double End);

/// <summary>Un edificio de la partida: los del arranque ("town_center#1") y cada uno que construye la BO ("s4#1").</summary>
/// <param name="StepId">El paso que lo construye; null si estaba al arrancar.</param>
/// <param name="Built">Cuándo se terminó; null si estaba al arrancar.</param>
public sealed record PlanBuilding(string Id, string Key, string? StepId, double? Built);

public sealed record PlanResult(
    IReadOnlyList<StepEstimate> Steps,
    IReadOnlyList<PlanBlock> Blocks,
    IReadOnlyList<PlanSample> Timeline)
{
    /// <summary>Cada unidad, tecnología y construcción con su edificio, en el orden en que empezó.</summary>
    public IReadOnlyList<PlanJob> Jobs { get; init; } = [];

    /// <summary>Los edificios: los del arranque y los que se terminaron, en orden.</summary>
    public IReadOnlyList<PlanBuilding> Buildings { get; init; } = [];

    /// <summary>Los aldeanos que se quedaron sin juntar nada; siempre vacío con la comida infinita.</summary>
    public IReadOnlyList<PlanIdle> Idle { get; init; } = [];

    public bool Completed => Steps.All(s => s.End is not null);

    public StepEstimate? For(string stepId) => Steps.FirstOrDefault(s => s.Step.Id == stepId);

    /// <summary>Segundo estimado del click en una tecnología (por ejemplo "feudal_age").</summary>
    public double? ClickTime(string what) =>
        Steps.FirstOrDefault(s => s.Step.Do == StepAction.Research && s.Step.What == what)?.Start;
}

/// <summary>
/// Simula la BO hacia adelante de a 1 segundo: recolección, cola de cada edificio, constructores y población.
/// Los pasos arrancan en orden (uno no arranca antes que el anterior), pero se superponen: mientras el centro
/// urbano entrena aldeanos, otros aldeanos construyen.
/// </summary>
public sealed class Planner(GameData data, PlannerSettings? settings = null)
{
    private const double Tick = 1;
    private readonly PlannerSettings _settings = settings ?? new PlannerSettings();

    /// <summary>Simula una BO completa desde el inicio estándar de su civ (vista previa del editor).</summary>
    public PlanResult Simulate(BuildOrder bo) => Simulate(bo, null);

    /// <summary>
    /// D12: simula la BO y le pregunta a la política qué agregar mientras tanto (el modo asistido). El resultado trae
    /// los pasos de la BO y los agregados, en orden, con las condiciones que les puso el Planner. Con política, todos
    /// los pasos de aldeanos van en unidades (aldeanos nuevos, como los que agrega la política): los de la BO se pasan
    /// de totales a unidades, así la BO generada sale entera con <see cref="LivePlan.CountsToTotals"/>.
    /// </summary>
    public PlanResult Simulate(BuildOrder bo, IStepPolicy? policy)
    {
        var civ = CivOf(bo);
        var units = LivePlan.Units(bo);
        var steps = policy is null
            ? bo.Steps
            : bo.Steps.Select((s, i) => LivePlan.IsVillagerTarget(s) ? s with { Count = units[i] } : s).ToList();
        return Simulate(steps.Select((s, i) => new PlanItem(s, units[i])).ToList(),
            PlanStart.For(civ, data), civ, policy);
    }

    /// <param name="civ">Bonificaciones de la civ (por defecto, ninguna).</param>
    /// <param name="policy">D12: la política que agrega pasos mientras se simula (ver <see cref="IStepPolicy"/>).</param>
    public PlanResult Simulate(IReadOnlyList<PlanItem> items, PlanStart start, CivBonuses? civ = null, IStepPolicy? policy = null) =>
        new Simulation(data, _settings, items, start, civ ?? CivBonuses.None, policy).Run();

    /// <summary>Las bonificaciones de la civ de la BO ("any" = ninguna).</summary>
    public CivBonuses CivOf(BuildOrder bo) => data.Civ(bo.Civ);

    private sealed class Simulation
    {
        private readonly GameData _data;
        private readonly PlannerSettings _settings;
        private readonly List<ItemSim> _items;
        private readonly Dictionary<GatherSource, int> _villagers;
        private readonly Dictionary<string, int> _buildings;
        private readonly Dictionary<string, Queue<ItemSim>> _queues = [];
        private readonly Dictionary<string, int> _busyProducers = [];
        private readonly List<(double At, ItemSim Item, Action Effect)> _running = [];
        private readonly List<PlanBlock> _blocks = [];
        private readonly List<PlanSample> _timeline = [];
        private readonly HashSet<string> _finishedSteps = [];
        // D8: qué edificio hace cada cosa. Cada clave (y lo que cuenta como ella) tiene sus edificios en el orden en
        // que se terminaron, con hasta cuándo están ocupados; cada trabajo toma el primero libre.
        private readonly Dictionary<string, List<Instance>> _instances = [];
        private readonly List<PlanJob> _jobs = [];
        private readonly List<PlanBuilding> _buildingList = [];
        private readonly List<PlanIdle> _idleList = [];

        /// <summary>Lo que queda de cada fuente de comida; null con la comida infinita.</summary>
        private readonly FoodStocks? _food;

        /// <summary>D12: la política que agrega pasos; null sin modo asistido.</summary>
        private readonly IStepPolicy? _policy;

        /// <summary>Los ids de los pasos, para que la política no repita uno.</summary>
        private readonly HashSet<string> _ids;

        /// <summary>Unidades con población que nacieron en este segundo.</summary>
        private int _births;

        /// <summary>
        /// Desde cuándo, al final de la lista de pasos, la BO no tiene pasos por arrancar (<c>inOrder</c>) y desde cuándo
        /// llegó al último (<c>reached</c>); null si no. Un paso agregado más tarde que eso se fija (ver <see cref="Pin"/>).
        /// </summary>
        private double? _inOrderSince;

        private double? _reachedSince;

        private double _time;
        private ResourceAmounts _stock;

        /// <summary>Lo que en este segundo quedó apartado para pasos que esperan recursos (ver <see cref="Pays"/>).</summary>
        private ResourceAmounts _claimed;

        private int _population;
        private int _reserved;
        private int _cap;
        private int _idle;
        private int _builders;
        private Age _age;
        private readonly CivBonuses _civ;
        private readonly GatherModel _gatherModel;
        private readonly List<string> _researched;
        private Dictionary<GatherSource, double> _rates = [];

        public Simulation(GameData data, PlannerSettings settings, IReadOnlyList<PlanItem> items, PlanStart start, CivBonuses civ, IStepPolicy? policy)
        {
            _data = data;
            _settings = settings;
            _civ = civ;
            _policy = policy;
            _ids = items.Select(i => i.Step.Id).ToHashSet();
            _time = start.Time;
            _stock = start.Stock;
            _population = start.Population;
            _cap = start.PopulationCap;
            _age = start.Age;
            _idle = start.IdleVillagers;
            _villagers = Enum.GetValues<GatherSource>().ToDictionary(s => s, s => start.Villagers.GetValueOrDefault(s));
            _buildings = new Dictionary<string, int>(start.Buildings);
            foreach (var (key, count) in start.Buildings)
            {
                for (var k = 1; k <= count; k++)
                {
                    var id = $"{key}#{k}";
                    _buildingList.Add(new PlanBuilding(id, key, null, null));
                    AddInstance(key, id);
                }
            }
            _gatherModel = new GatherModel(data, settings.Efficiency);
            _food = settings.Food is { } supply ? new FoodStocks(supply) : null;
            _researched = [.. start.Researched];
            RefreshRates();
            _items = items.Select((item, i) => new ItemSim(item.Step, i, item.Remaining, Lookup(item.Step)) { PaidAt = item.PaidAt }).ToList();
            foreach (var item in _items.Where(i => i.Remaining == 0))
                Finish(item);
            foreach (var item in _items.Where(i => i.PaidAt is not null && i.Remaining > 0 && i.Entity is not null && i.Step.Do is StepAction.Train or StepAction.Research))
                StartPaid(item);
        }

        private void AddInstance(string key, string id) =>
            (_instances.TryGetValue(key, out var list) ? list : _instances[key] = []).Add(new Instance(id) { FreeAt = _time });

        /// <summary>El primer edificio de esa clave libre ahora, que queda ocupado hasta <paramref name="until"/>.</summary>
        private string? TakeInstance(string producer, double until)
        {
            if (!_instances.TryGetValue(producer, out var list))
                return null;
            var free = list.FirstOrDefault(i => i.FreeAt <= _time + 1e-9);
            if (free is null)
                return null;
            free.FreeAt = until;
            return free.Id;
        }

        /// <summary>Lo ya pagado no se vuelve a pagar: ocupa su edificio hasta que termina.</summary>
        private void StartPaid(ItemSim item)
        {
            var entity = item.Entity!;
            var producer = entity.MadeIn ?? "";
            _busyProducers[producer] = _busyProducers.GetValueOrDefault(producer) + 1;
            var end = Math.Max(_time, item.PaidAt!.Value + ProductionTime(entity, producer));
            _jobs.Add(new PlanJob(item.Step.Id, PlanJobKind.Produce, entity.Key, TakeInstance(producer, end), item.PaidAt!.Value, end));
            _reserved += entity.Pop;
            item.Remaining--;
            item.Unfinished++;
            item.Started = true;
            item.Start = item.PaidAt;
            var queue = Queue(producer);
            for (var k = 0; k < item.Remaining; k++)
                queue.Enqueue(item);
            _running.Add((Math.Max(_time, item.PaidAt!.Value + ProductionTime(entity, producer)), item, Completion(item, entity, producer)));
        }

        private Entity? Lookup(Step step) =>
            step.What is not null && _data.TryGet(step.What, out var entity) ? entity : null;

        public PlanResult Run()
        {
            var nextSample = _time;
            // D12: si la política pidió que la despierten, la simulación sigue después de que termina todo; si hasta el
            // final no agrega nada más, el resultado es el del momento en que terminó (el de la BO generada).
            PlanResult? whenDone = null;
            var whenDoneItems = 0;
            while (true)
            {
                _births = 0;
                CompleteRunning();
                ReseedFarms();
                // Los pasos arrancan en orden: cada uno cuando el anterior arrancó. Los aldeanos son la excepción: el
                // centro urbano no para porque un paso de antes espere (un molino sin madera, un campamento sin
                // constructor); alcanza con que la BO haya llegado hasta ahí. Lo que no es aldeanos sigue esperando
                // a todo lo de antes.
                var inOrder = true;
                var reached = true;
                _claimed = ResourceAmounts.Zero;
                for (var i = 0; i < _items.Count; i++)
                    AdvanceInOrder(_items[i], ref inOrder, ref reached);
                if (_policy is not null)
                    AskPolicy(ref inOrder, ref reached);

                if (_time >= nextSample)
                {
                    Sample();
                    nextSample += _settings.SampleEvery;
                }
                var done = _items.All(i => i.End is not null);
                var waking = _policy?.WakeAt is { } wake && wake > _time;
                // Lo agregado después (al despertar) cuenta aunque termine en el mismo segundo: un "mover" es instantáneo.
                if (!done || _items.Count != whenDoneItems)
                    whenDone = null;
                if (done && waking && whenDone is null)
                {
                    whenDone = Result(final: true);
                    whenDoneItems = _items.Count;
                }
                if (done && !waking || _time >= _settings.MaxTime)
                    break;

                Gather();
                _time += Tick;
            }
            return whenDone ?? Result(final: true);
        }

        /// <summary>El resultado hasta ahora, con una última muestra de este segundo (copias: la simulación puede seguir).</summary>
        private PlanResult Result(bool final)
        {
            var timeline = new List<PlanSample>(_timeline);
            if (final)
                timeline.Add(new PlanSample(_time, _stock, _population, _cap, _villagers.Values.Sum() + _idle + _builders));
            return new PlanResult(
                _items.Select(i => new StepEstimate(i.Step, i.Start, i.End, i.PaidAt is not null)).ToList(),
                [.. _blocks],
                timeline)
            {
                Jobs = [.. _jobs],
                Buildings = [.. _buildingList],
                Idle = [.. _idleList],
            };
        }

        private void AdvanceInOrder(ItemSim item, ref bool inOrder, ref bool reached)
        {
            var villagers = IsVillagers(item.Step);
            Advance(item, villagers ? reached : inOrder);
            inOrder = villagers ? inOrder && Releases(item) : Releases(item);
            reached = item.Reached || item.Started || item.End is not null;
        }

        /// <summary>Cuántas veces se le puede preguntar a la política en un mismo segundo.</summary>
        private const int MaxAsksPerTick = 100;

        /// <summary>
        /// D12: los puntos de decisión. Se le pregunta a la política si nació alguien, si la BO se acaba de quedar sin
        /// pasos por arrancar o si todo terminó. Lo que agrega se avanza en este mismo segundo, como si hubiera estado
        /// en la BO desde el principio; si con eso la BO vuelve a quedarse sin pasos, se le pregunta otra vez.
        /// </summary>
        private void AskPolicy(ref bool inOrder, ref bool reached)
        {
            var ranOut = inOrder && _inOrderSince is null;
            _inOrderSince = inOrder ? _inOrderSince ?? _time : null;
            _reachedSince = reached ? _reachedSince ?? _time : null;
            var woke = _policy!.WakeAt is { } wake && _time >= wake;
            if (_births == 0 && !ranOut && !woke && _items.Any(i => i.End is null))
                return;

            for (var asks = 0; ; asks++)
            {
                if (asks == MaxAsksPerTick)
                    throw new InvalidOperationException(
                        $"La política agregó pasos sin parar a los {GameClock.Format(_time)}: {MaxAsksPerTick} veces en el mismo segundo.");
                var steps = _policy!.Next(View(inOrder));
                if (steps.Count == 0)
                    return;
                foreach (var given in steps)
                {
                    var step = Pin(given with { After = [.. given.After] }, IsVillagers(given) ? _reachedSince : _inOrderSince);
                    if (!_ids.Add(step.Id))
                        throw new InvalidOperationException($"La política agregó un paso con un id que ya existe: '{step.Id}'.");
                    if (step.Do is StepAction.Train or StepAction.Build && step.Count < 1)
                        throw new InvalidOperationException($"La política agregó un paso con una cantidad menor a 1: '{step.Id}'.");
                    var item = new ItemSim(step, _items.Count, step.Do is StepAction.Train or StepAction.Build ? step.Count : 1, Lookup(step));
                    _items.Add(item);
                    if (item.Remaining == 0)
                        Finish(item);
                    AdvanceInOrder(item, ref inOrder, ref reached);
                    _inOrderSince = inOrder ? _time : null;
                    _reachedSince = reached ? _time : null;
                }
                if (!inOrder)
                    return;
            }
        }

        /// <summary>
        /// Un paso agregado en este segundo que, en una simulación nueva de la BO, quedaría alcanzado desde antes
        /// (<paramref name="allowedSince"/>) se fija para que arranque recién ahora: con la población si en este segundo
        /// nació alguien (llegó recién ahora a ese número, como en las guías) y con la hora si no.
        /// </summary>
        private Step Pin(Step step, double? allowedSince)
        {
            // Si sus propias condiciones (after, a N de población, a los m:ss) todavía no se cumplen, en una simulación
            // nueva tampoco podía arrancar antes: no hace falta fijarlo.
            if (allowedSince is not { } since || since >= _time || !ConditionsMet(step))
                return step;
            if (_births > 0)
                step.AtPop = Math.Max(step.AtPop ?? 0, _population);
            else
                step.AtTime = Math.Max(step.AtTime ?? 0, (int)Math.Ceiling(_time));
            return step;
        }

        /// <summary>La foto para la política: copias, así no puede tocar la simulación.</summary>
        private PlanView View(bool ranOut) => new()
        {
            Time = _time,
            Stock = _stock,
            Population = _population,
            InProduction = _reserved,
            PopulationCap = _cap,
            Age = _age,
            Villagers = new Dictionary<GatherSource, int>(_villagers),
            IdleVillagers = _idle,
            Builders = _builders,
            Buildings = new Dictionary<string, int>(_buildings),
            UnderConstruction = _items.Where(i => i.Step.Do == StepAction.Build && i.Unfinished > 0 && i.Entity is not null)
                .GroupBy(i => i.Entity!.Key)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Unfinished)),
            Researched = [.. _researched],
            Rates = new Dictionary<GatherSource, double>(_rates),
            Queued = _queues.Where(q => q.Value.Count > 0).ToDictionary(q => q.Key, q => q.Value.Count),
            Busy = _busyProducers.Where(b => b.Value > 0).ToDictionary(b => b.Key, b => b.Value),
            Steps = _items.Select(i => new PlanViewStep(i.Step with { After = [.. i.Step.After] }, i.Remaining, i.Reached, i.Started, i.Start, i.End, i.Unfinished)).ToList(),
            Birth = _births > 0,
            RanOut = ranOut,
            Food = _food?.Snapshot(source => _civ.FoodYield(source, _age), WaitingFarmers()),
        };

        /// <summary>
        /// Si el paso ya deja arrancar al siguiente. Un clic de edad cuenta recién cuando se paga: lo que la BO pone
        /// después (pasar aldeanos a la madera mientras se investiga) no se hace antes del clic. Lo demás cuenta cuando
        /// entra en la cola o empieza.
        /// </summary>
        private static bool Releases(ItemSim item) =>
            item.End is not null
            || (item.Step.Do == StepAction.Research && AgeReachedBy(item.Step.What ?? "") is not null ? item.Start is not null : item.Started);

        private void Gather()
        {
            var waiting = _food is null ? 0 : WaitingFarmers();
            _food?.SyncFarms(_villagers[GatherSource.Farm] - waiting, FarmFood());
            foreach (var (source, count) in _villagers)
            {
                if (count == 0)
                    continue;
                var resource = source.Resource();
                var working = source == GatherSource.Farm ? count - waiting : count;
                var gathered = _rates[source] * working * Tick;
                if (_food is not null && FoodStocks.Counts(source))
                {
                    if (source == GatherSource.Farm && waiting > 0)
                        RecordIdle(source, IdleReason.WaitingFarm, waiting);
                    var idle = _food.Idle(source, working);
                    if (idle > 0)
                        RecordIdle(source, source == GatherSource.Farm ? IdleReason.Reseed : IdleReason.Exhausted, idle);
                    gathered = _food.Gather(source, working, gathered, _civ.FoodYield(source, _age));
                }
                _stock = _stock.With(resource, _stock[resource] + gathered);
            }
        }

        /// <summary>
        /// Los granjeros sin granja que esperan la suya: los que sobran sobre las granjas hechas, hasta las que quedan por
        /// empezar en pasos de granjas que se hacen desde la granja (así las hace un granjero que todavía no tiene la
        /// suya). Solo cuentan los pasos a los que la BO ya llegó: el que llega a la granja sin un paso así trae su granja,
        /// y una simulación nueva de una BO generada por la política (D12 b) da lo mismo que la de la política.
        /// </summary>
        private int WaitingFarmers()
        {
            var pending = _items
                .Where(i => i.End is null && (i.Reached || i.Started) && i.Step is { Do: StepAction.Build, What: "farm", From: GatherSource.Farm })
                .Sum(i => i.Remaining);
            return Math.Min(pending, Math.Max(0, _villagers[GatherSource.Farm] - _buildings.GetValueOrDefault("farm")));
        }

        /// <summary>
        /// Las granjas vacías se resiembran solas con la madera de una granja (el costo de la civ), antes que cualquier
        /// paso, como la cola de resiembra del molino. Solo con la comida finita.
        /// </summary>
        private void ReseedFarms()
        {
            if (_food is null)
                return;
            var cost = _civ.Cost(_data.Get("farm"), _age).Wood;
            var reseeded = _food.Reseed(_stock.Wood, cost, FarmFood());
            if (reseeded > 0)
                _stock = _stock.With(Resource.Wood, _stock.Wood - cost * reseeded);
        }

        /// <summary>
        /// La comida de una granja nueva o resembrada: la base más lo que suman las mejoras ya investigadas (por el
        /// multiplicador de la civ: sicilianos ×2,25), más lo que suma la civ a cada granja (folwark polaco: 10 %).
        /// </summary>
        private double FarmFood() =>
            ((_settings.Food?.FarmFood ?? _data.FoodPerObject[GatherSource.Farm])
             + _researched.Sum(t => _data.TryGet(t, out var tech) ? tech.FarmFood : 0) * _civ.FarmUpgradeFood(_age))
            * (1 + _civ.FarmExtra(_age));

        /// <summary>Registra aldeanos que no juntan nada este segundo; si ya estaban así, estira el intervalo (como <see cref="Block"/>).</summary>
        private void RecordIdle(GatherSource source, IdleReason reason, int villagers)
        {
            var i = _idleList.FindLastIndex(r => r.Source == source && r.Reason == reason);
            if (i >= 0 && _idleList[i].Villagers == villagers && _idleList[i].To >= _time)
            {
                _idleList[i] = _idleList[i] with { To = _time + Tick };
                return;
            }
            _idleList.Add(new PlanIdle(source, reason, _time, _time + Tick, villagers));
        }

        /// <summary>Tasas por aldeano con la edad y las tecnologías de ahora (cambian cuando termina una).</summary>
        private void RefreshRates() => _rates = _gatherModel.Rates(_civ, _age, _researched);

        private void CompleteRunning()
        {
            foreach (var job in _running.Where(j => j.At <= _time).OrderBy(j => j.At).ToList())
            {
                _running.Remove(job);
                job.Effect();
                job.Item.Unfinished--;
                if (job.Item.Unfinished == 0 && job.Item.Remaining == 0)
                    Finish(job.Item);
            }
        }

        private void Finish(ItemSim item)
        {
            item.Started = true;
            item.End ??= _time;
            item.Start ??= _time;
            _finishedSteps.Add(item.Step.Id);
        }

        private static bool IsVillagers(Step step) => step.Do == StepAction.Train && step.What == "villager";

        /// <param name="allowed">Si la BO ya llegó hasta este paso (ver <see cref="Run"/>).</param>
        private void Advance(ItemSim item, bool allowed)
        {
            if (item.End is not null)
                return;

            if (!item.Started)
            {
                if (!item.Reached)
                {
                    if (!allowed || !ConditionsMet(item.Step))
                        return;
                    item.Reached = true;
                }
                if (item.Step.Do is StepAction.Train or StepAction.Research)
                {
                    // Entra en la cola del edificio; se paga cuando el edificio lo empieza a producir.
                    var queue = Queue(item.Entity?.MadeIn);
                    for (var k = 0; k < item.Remaining; k++)
                        queue.Enqueue(item);
                    item.Started = true;
                }
            }

            switch (item.Step.Do)
            {
                case StepAction.Train or StepAction.Research:
                    Produce(item);
                    break;
                case StepAction.Build:
                    Build(item);
                    break;
                case StepAction.Move:
                    Move(item);
                    break;
            }
        }

        private bool ConditionsMet(Step step) =>
            step.After.All(_finishedSteps.Contains)
            && (step.AtPop is not { } pop || _population >= pop)
            && (step.AtTime is not { } t || _time >= t);

        private Queue<ItemSim> Queue(string? producer)
        {
            var key = producer ?? "";
            return _queues.TryGetValue(key, out var queue) ? queue : _queues[key] = new Queue<ItemSim>();
        }

        private void Produce(ItemSim item)
        {
            if (item.Entity is not { } entity)
                return;
            var producer = entity.MadeIn ?? "";
            var queue = Queue(producer);
            while (queue.TryPeek(out var head) && head == item)
            {
                if (_buildings.GetValueOrDefault(producer) == 0)
                {
                    Block(item, BlockReason.Producer, expected: BuildingComing(producer));
                    return;
                }
                if (_busyProducers.GetValueOrDefault(producer) >= _buildings[producer])
                    return; // el edificio está ocupado con el trabajo anterior: no es un bloqueo del paso
                if (entity.Age > _age)
                {
                    Block(item, BlockReason.Age, expected: AgeComing(entity.Age));
                    return;
                }
                if (entity.Pop > 0 && _population + _reserved + entity.Pop > _cap)
                {
                    Block(item, BlockReason.Population);
                    return;
                }
                var cost = _civ.Cost(entity, _age);
                if (!Pays(item, cost))
                    return;

                queue.Dequeue();
                _stock -= cost;
                _reserved += entity.Pop;
                _busyProducers[producer] = _busyProducers.GetValueOrDefault(producer) + 1;
                item.Remaining--;
                item.Unfinished++;
                item.Start ??= _time;
                var end = _time + ProductionTime(entity, producer);
                _jobs.Add(new PlanJob(item.Step.Id, PlanJobKind.Produce, entity.Key, TakeInstance(producer, end), _time, end));
                _running.Add((end, item, Completion(item, entity, producer)));
            }
        }

        /// <summary>Tiempo de entrenar o investigar con las bonificaciones de la civ (incluida la velocidad del edificio).</summary>
        private double ProductionTime(Entity entity, string producer) =>
            _civ.Time(entity, _age) / _civ.WorkRate(producer, _age);

        /// <summary>Lo que pasa cuando el edificio termina de producir una unidad o una tecnología.</summary>
        private Action Completion(ItemSim item, Entity entity, string producer) => () =>
        {
            _busyProducers[producer]--;
            _reserved -= entity.Pop;
            _population += entity.Pop;
            if (entity.Pop > 0)
                _births++;
            if (entity.Kind == EntityKind.Tech)
            {
                _researched.Add(entity.Key);
                if (AgeReachedBy(entity.Key) is { } reached)
                {
                    _age = reached;
                    _stock += _civ.AgeResources(reached); // dravidianos, etíopes
                }
                RefreshRates();
            }
            else if (entity.Key == "villager")
                AddVillagers(item.Step.Assign, 1);
        };

        private void Build(ItemSim item)
        {
            if (item.Entity is not { } entity || item.Remaining == 0)
                return;
            if (entity.Age > _age)
            {
                Block(item, BlockReason.Age, expected: AgeComing(entity.Age));
                return;
            }

            var parallel = entity.Key == "farm";
            var team = parallel ? 1 : item.Step.Builders ?? 1;
            while (item.Remaining > 0)
            {
                if (!parallel && item.Unfinished > 0)
                    return; // los mismos constructores hacen un edificio después del otro

                // Primero quién lo construye: sin aldeanos en 'from' lo que frena es esperarlos, no los recursos.
                if ((parallel || item.HeldBuilders == 0) && !HasBuilders(item.Step, entity))
                {
                    Block(item, BlockReason.Villagers, expected: VillagersComing(item.Step.From));
                    return;
                }
                var cost = _civ.Cost(entity, _age);
                if (!Pays(item, cost))
                    return;

                int builders;
                if (!parallel && item.HeldBuilders > 0)
                {
                    builders = item.HeldBuilders;
                }
                else
                {
                    builders = TakeVillagers(item.Step.From, team);
                    if (builders == 0)
                    {
                        Block(item, BlockReason.Villagers, expected: VillagersComing(item.Step.From));
                        return;
                    }
                    _builders += builders;
                    if (!parallel)
                        item.HeldBuilders = builders;
                }

                _stock -= cost;
                item.Remaining--;
                item.Unfinished++;
                item.Start ??= _time;
                item.Started = true;
                var buildTime = _civ.Time(entity, _age) * 3.0 / (builders + 2) / _civ.BuildRate(_age);
                var last = item.Remaining == 0;
                var started = _time;
                var finished = _time + buildTime;
                _running.Add((finished, item, () =>
                {
                    _buildings[entity.Key] = _buildings.GetValueOrDefault(entity.Key) + 1;
                    foreach (var counted in entity.CountsAs)
                        _buildings[counted] = _buildings.GetValueOrDefault(counted) + 1;
                    // D8: el edificio nuevo ("s4#1", "s4#2"...) y la construcción como trabajo.
                    var id = $"{item.Step.Id}#{++item.Built}";
                    _buildingList.Add(new PlanBuilding(id, entity.Key, item.Step.Id, finished));
                    _jobs.Add(new PlanJob(item.Step.Id, PlanJobKind.Build, entity.Key, id, started, finished));
                    AddInstance(entity.Key, id);
                    foreach (var counted in entity.CountsAs)
                        AddInstance(counted, id);
                    _cap = Math.Min(BuildOrderValidator.MaxPopulation, _cap + _civ.PopBonus(entity, _age));
                    if (parallel || last)
                    {
                        _builders -= builders;
                        AddVillagers(item.Step.Assign ?? item.Step.From, builders);
                        item.HeldBuilders = 0;
                    }
                }));

                if (!parallel)
                    return;
            }
        }

        private void Move(ItemSim item)
        {
            if (item.Step.Assign is not { } to)
                return;
            // Sin 'from', los que no tienen tarea (los 3 iniciales de una civ sin casas).
            var available = item.Step.From is { } from ? _villagers[from] : _idle;
            if (available == 0)
            {
                Block(item, BlockReason.Villagers, expected: VillagersComing(item.Step.From));
                return;
            }
            var moved = TakeVillagers(item.Step.From, Math.Min(item.Step.Count, available));
            _villagers[to] += moved;
            item.Started = true;
            item.Remaining = 0;
            Finish(item);
        }

        /// <summary>
        /// Si hay alguien para <see cref="TakeVillagers"/>: en 'from', o sin tarea, o en algún recurso. Una granja hecha
        /// por granjeros la hace uno que todavía no tiene la suya: los que ya trabajan una no hacen otra.
        /// </summary>
        private bool HasBuilders(Step step, Entity entity) => step.From switch
        {
            GatherSource.Farm when entity.Key == "farm" => _villagers[GatherSource.Farm] > _buildings.GetValueOrDefault("farm"),
            { } source => _villagers[source] > 0,
            null => _idle > 0 || _villagers.Values.Any(v => v > 0),
        };

        /// <summary>Saca aldeanos para construir: de 'from' si se indica; si no, los que no tienen tarea y después el recurso con más gente.</summary>
        private int TakeVillagers(GatherSource? from, int wanted)
        {
            if (from is { } source)
            {
                var taken = Math.Min(wanted, _villagers[source]);
                _villagers[source] -= taken;
                return taken;
            }

            var fromIdle = Math.Min(wanted, _idle);
            _idle -= fromIdle;
            var rest = wanted - fromIdle;
            if (rest > 0 && fromIdle == 0)
            {
                var busiest = _villagers.MaxBy(v => v.Value);
                var taken = Math.Min(rest, busiest.Value);
                _villagers[busiest.Key] -= taken;
                return taken;
            }
            return fromIdle;
        }

        private void AddVillagers(GatherSource? source, int count)
        {
            if (source is { } s)
                _villagers[s] += count;
            else
                _idle += count;
        }

        private static Age? AgeReachedBy(string tech) => tech switch
        {
            "feudal_age" => Age.Feudal,
            "castle_age" => Age.Castle,
            "imperial_age" => Age.Imperial,
            _ => null,
        };

        /// <summary>
        /// Si alcanza para pagar el paso. Lo que va antes en la BO tiene prioridad: si un paso anterior espera un
        /// recurso, ese recurso queda apartado para él en este segundo (si no, las granjas de 60 se comían de a una la
        /// madera que juntaba la collera de 75). Los aldeanos no esperan a nadie: el centro urbano no para por un
        /// edificio o una tecnología.
        /// </summary>
        private bool Pays(ItemSim item, ResourceAmounts cost)
        {
            var available = IsVillagers(item.Step) ? _stock : _stock - _claimed;
            // Solo cuenta lo que el paso usa: lo apartado puede dejar en negativo un recurso que no paga.
            var missing = Enum.GetValues<Resource>().Where(r => cost[r] > 0 && available[r] < cost[r]).ToList();
            if (missing.Count == 0)
                return true;
            Block(item, BlockReason.Resources, missing[0]);
            foreach (var r in missing)
                _claimed = _claimed.With(r, _claimed[r] + cost[r]);
            return false;
        }

        /// <summary>Registra que el paso está trabado este segundo; si ya lo estaba por lo mismo, estira el intervalo.</summary>
        /// <param name="expected">Lo que falta ya viene en camino (ver <see cref="PlanBlock.Expected"/>).</param>
        private void Block(ItemSim item, BlockReason reason, Resource? missing = null, bool expected = false)
        {
            var i = _blocks.FindLastIndex(b => b.StepId == item.Step.Id);
            if (i >= 0 && _blocks[i].Reason == reason && _blocks[i].Missing == missing && _blocks[i].Expected == expected && _blocks[i].To >= _time)
            {
                _blocks[i] = _blocks[i] with { To = _time + Tick };
                return;
            }
            _blocks.Add(new PlanBlock(item.Step.Id, reason, _time, _time + Tick, missing) { Expected = expected });
        }

        /// <summary>Ya se pagó (y no terminó) el clic de la edad que hace falta.</summary>
        private bool AgeComing(Age needed) =>
            _items.Any(i => i.Unfinished > 0 && i.Step.Do == StepAction.Research && AgeReachedBy(i.Step.What ?? "") >= needed);

        /// <summary>Se está construyendo un edificio de esa clave (o uno que cuenta como ella).</summary>
        private bool BuildingComing(string key) =>
            _items.Any(i => i.Unfinished > 0 && i.Step.Do == StepAction.Build && i.Entity is { } e && (e.Key == key || e.CountsAs.Contains(key)));

        /// <summary>Viene un aldeano a esa fuente: en la cola del centro urbano, o constructores que vuelven ahí.</summary>
        private bool VillagersComing(GatherSource? to) => _items.Any(i => IsVillagers(i.Step)
            ? i.Started && (i.Remaining > 0 || i.Unfinished > 0) && (to is null || i.Step.Assign == to)
            : i.Step.Do == StepAction.Build && i.Unfinished > 0 && to is not null && (i.Step.Assign ?? i.Step.From) == to);

        private void Sample() => _timeline.Add(new PlanSample(
            _time, _stock, _population, _cap, _villagers.Values.Sum() + _idle + _builders));
    }

    /// <summary>Un edificio que produce, con hasta cuándo está ocupado.</summary>
    private sealed class Instance(string id)
    {
        public string Id { get; } = id;
        public double FreeAt { get; set; }
    }

    private sealed class ItemSim(Step step, int index, int remaining, Entity? entity)
    {
        /// <summary>Cuántos edificios de este paso ya se terminaron (para numerarlos).</summary>
        public int Built { get; set; }

        public Step Step { get; } = step;
        public int Index { get; } = index;
        public Entity? Entity { get; } = entity;
        public int Remaining { get; set; } = remaining;
        public int Unfinished { get; set; }
        public int HeldBuilders { get; set; }
        public bool Started { get; set; }

        /// <summary>La BO llegó hasta este paso y se cumplen sus condiciones, aunque todavía no pueda empezar.</summary>
        public bool Reached { get; set; }

        public double? Start { get; set; }
        public double? End { get; set; }
        public double? PaidAt { get; init; }
    }
}
