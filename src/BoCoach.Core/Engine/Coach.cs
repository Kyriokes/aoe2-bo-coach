using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.Engine;

/// <summary>Un paso ya pagado que se está haciendo, y cuándo estima el Planner que termina.</summary>
public sealed record RunningStep(StepProgress Progress, double? EndsAt);

/// <summary>
/// El renglón de una línea (spec 2026-10-30): su paso, lo que le falta juntar y cuándo estima el Planner que se puede
/// hacer. <see cref="Step"/> null = la línea no tiene paso de la BO y va al fallback (GD, casa, tecnologías, postura).
/// </summary>
public sealed record LaneView(Lane Lane, StepProgress? Step, ResourceAmounts Missing, double? Eta);

/// <summary>Lo que muestra el overlay en un instante.</summary>
/// <param name="Missing">Lo que falta juntar para el paso actual (cero si ya alcanza).</param>
/// <param name="CurrentEta">Cuándo estima el Planner que se puede hacer el paso actual (reloj de partida).</param>
public sealed record CoachView(
    double Time,
    int Population,
    int PopulationCap,
    StepProgress? Current,
    StepProgress? Next,
    IReadOnlyList<StepProgress> Late,
    IReadOnlyList<RunningStep> Running,
    ResourceAmounts Missing,
    double? CurrentEta,
    IReadOnlyList<TargetDeviation> Deviations,
    Suggestion? Suggestion,
    int DoneSteps,
    int TotalSteps)
{
    public bool Finished => DoneSteps == TotalSteps;

    /// <summary>Aviso reciente del log que no es un paso ("farm_exhausted", "under_attack"), o null.</summary>
    public string? Alert { get; init; }

    /// <summary>
    /// Segundos que el centro urbano lleva sin sacar aldeanos de más de lo que tarda uno, o null si está produciendo
    /// (o investigando, o la BO ya no pide aldeanos).
    /// </summary>
    public double? TownCenterIdle { get; init; }

    /// <summary>
    /// Si el paso actual es entrenar aldeanos para una fuente: cuántos hay ahí y cuántos tiene que haber al terminar el
    /// paso, contando los que ya estaban (como el contador del HUD). "6 aldeanos a ovejas" arranca en 3/9.
    /// </summary>
    public (int Current, int Target)? SourceVillagers { get; init; }

    /// <summary>
    /// La guía dinámica (GD): cómo repartir los aldeanos que hay ahora (total del HUD) para volver a la BO por el camino
    /// más corto (<see cref="GuideDistribution"/>), o null si todavía no se leyó el total.
    /// </summary>
    public ResourceAmounts? Distribution { get; init; }

    /// <summary>El reparto de la BO en este punto, llevado al total de aldeanos de ahora (sin adaptar a los recursos).</summary>
    public ResourceAmounts? BoDistribution { get; init; }

    /// <summary>
    /// Cómo quiere la BO a los aldeanos cuando se termine el paso actual (si es mixto, todas sus metas), contando los
    /// iniciales, como el HUD. Es lo que muestra el overlay: la GD dice dónde ponerlos ahora y esto, a dónde se va.
    /// null si la BO terminó.
    /// </summary>
    public ResourceAmounts? StepTarget { get; init; }

    /// <summary>
    /// En un paso largo, cómo quiere la BO a los aldeanos que hay ahora (<see cref="BoDistribution"/>), para mostrarlo
    /// junto a <see cref="StepTarget"/>: mientras sube de edad, "8 en oro al terminar" no dice qué hacer con los 23 que
    /// ya están (pedido del usuario: partir los pasos largos en "ahora" y "al terminar"). null si el paso es corto o si
    /// los dos coinciden.
    /// </summary>
    public ResourceAmounts? NowTarget { get; init; }

    /// <summary>
    /// Aldeanos que hay ahora en cada recurso (estimados por la economía hasta que se lean del HUD), para comparar con
    /// <see cref="Distribution"/>, o null si todavía no hay estimación.
    /// </summary>
    public ResourceAmounts? Actual { get; init; }

    /// <summary>True si <see cref="Actual"/> salió del HUD (con el mod) y no de la estimación de la economía.</summary>
    public bool ActualFromHud { get; init; }

    /// <summary>Aldeanos en comida que mantienen los centros urbanos produciendo (0 si ya se llegó al máximo).</summary>
    public int FoodFloor { get; init; }

    /// <summary>Si el paso actual es un paso mixto: todas sus metas (ver <see cref="Tracker.CurrentGoals"/>).</summary>
    public IReadOnlyList<StepProgress> Goals { get; init; } = [];

    /// <summary>Recordatorio: quedan <see cref="Coach.HouseMargin"/> lugares de población o menos (y no se llegó a 200).</summary>
    public bool NeedsHouse { get; init; }

    /// <summary>Recordatorio: el centro urbano está parado y todavía no se llegó al máximo de aldeanos elegido.</summary>
    public bool TownCenterReminder { get; init; }

    /// <summary>
    /// Por qué se dio la BO por abandonada (<see cref="BuildOrderAbandonment"/>), o null si se sigue. Abandonada, la
    /// vista no tiene paso actual, siguiente ni atrasados: quedan los avisos y la GD contra lo que hay.
    /// </summary>
    public string? Abandoned { get; init; }

    /// <summary>Sin paso actual (la BO terminada, salvo granjas o producción) y no abandonada, la postura de cierre (D2); si no, null.</summary>
    public PostureView? Posture { get; init; }

    /// <summary>Señales recientes de la partida (D3: clic de edad del rival, ataque, aldeanos perdidos), la última al final.</summary>
    public IReadOnlyList<GameSignal> Signals { get; init; } = [];

    /// <summary>La rama de la BO en curso (D3), o null.</summary>
    public BranchView? Branch { get; init; }

    /// <summary>
    /// Con la BO terminada, pasado el aviso de <see cref="Coach.FinishedNoticeSeconds"/>: el overlay muestra solo lo
    /// que pide hacer algo (pedido del usuario, 2026-10-26).
    /// </summary>
    public bool Compact { get; init; }

    /// <summary>Con la BO terminada, las tecnologías que faltan (<see cref="TechReminders"/>).</summary>
    public IReadOnlyList<TechReminder> Techs { get; init; } = [];

    /// <summary>Un renglón por línea, en el orden de <see cref="Engine.Lanes.All"/>.</summary>
    public IReadOnlyList<LaneView> Lanes { get; init; } = [];

    /// <summary>
    /// Sin paso de aldeanos en la BO: a qué recurso mandar el próximo aldeano según la GD (el que más le falta contra lo
    /// que hay), o, si la GD no pide ninguno, el de más demanda sin cubrir por el stock. null si no hay a dónde.
    /// </summary>
    public Resource? VillagerGuide { get; init; }
}

/// <summary>
/// El loop del coach sin pantalla: cada snapshot actualiza el estado del jugador, el Tracker, el plan (cada
/// <see cref="ReplanEvery"/> segundos o cuando hay eventos nuevos) y la economía, y devuelve la vista del overlay.
/// Las correcciones manuales (atajos) recalculan la vista sin esperar al próximo snapshot.
/// <para>El log avisa cuando una investigación termina, no cuando se hace clic. El clic se reconoce porque el stock
/// baja de golpe justo el costo (Feudal: 500 de comida; telar: 50 de oro); si después sube lo mismo de golpe, se
/// canceló. Mientras tanto el paso queda "en curso" y el overlay pasa al siguiente.</para>
/// </summary>
public sealed class Coach
{
    private readonly GameData _data;
    private readonly Planner _planner;
    private readonly EconomyAdvisor _economy;
    private readonly EconomySettings _economySettings;
    private PlanResult? _plan;
    private double _plannedAt = double.NegativeInfinity;
    private Suggestion? _suggestion;
    private ResourceAmounts? _lastStock;
    private GameEvent? _alert;

    // Centro urbano: cuándo subió por última vez el total de aldeanos del HUD y cuándo se lo vio investigando.
    private int? _villagers;
    private double _lastVillagerAt;
    private double _townCenterBusyAt = double.NegativeInfinity;

    // GD: la que se muestra y la que espera sostenerse para reemplazarla.
    private ResourceAmounts? _guide;
    private readonly PostureCoach _posture;
    private PostureView? _postureView;
    private double? _postureSince;
    private readonly TechReminders _techs;
    private IReadOnlyList<QueueItem>? _queue;
    private ResourceAmounts? _guideCandidate;
    private double _guideCandidateSince;

    public Coach(BuildOrder bo, GameData data, EventCatalog? events = null, PlannerSettings? planner = null, EconomySettings? economy = null)
    {
        _data = data;
        _planner = new Planner(data, planner);
        _economySettings = economy ?? new EconomySettings();
        _economy = new EconomyAdvisor(data, planner, _economySettings);
        Tracker = new Tracker(bo, data, events);
        Tracker.Update(State);
        Abandonment = new BuildOrderAbandonment(Tracker.BuildOrder);
        _posture = new PostureCoach(Tracker.BuildOrder, data);
        _techs = new TechReminders(data, Tracker.BuildOrder.Civ, _posture.Definition.Queue);
        Signals = new GameSignals(data);
        _branches = new Branches(Tracker.BuildOrder, data, events);
        _report = new GameReportBuilder(Tracker.BuildOrder, _planner);
        View = BuildView();
    }

    public PlayerState State { get; } = new();
    public Tracker Tracker { get; }

    /// <summary>Si el jugador dejó la BO (D1).</summary>
    public BuildOrderAbandonment Abandonment { get; }

    /// <summary>Las señales de la partida (D3).</summary>
    public GameSignals Signals { get; }

    /// <summary>Cuántos segundos de partida se muestra una señal en el overlay.</summary>
    public double SignalSeconds { get; init; } = 10;

    private readonly HashSet<int> _ownClicksSeen = [];
    private readonly Branches _branches;
    private readonly GameReportBuilder _report;

    /// <summary>El informe de la partida hasta ahora (D5).</summary>
    public GameReport Report() => _report.Build();

    /// <summary>Cada cuántos segundos de partida se vuelve a simular el plan (si no hubo eventos).</summary>
    public double ReplanEvery { get; init; } = 5;

    /// <summary>Avisos del log que el overlay muestra (ver <see cref="CoachText.AlertText"/>).</summary>
    public static readonly IReadOnlySet<string> AlertEvents =
        new HashSet<string> { "farm_exhausted", "fish_trap_expired", "under_attack", "attacked_by_animals" };

    /// <summary>Cuántos segundos de partida se muestra un aviso.</summary>
    public double AlertSeconds { get; init; } = 8;

    /// <summary>Segundos de partida que el overlay muestra "BO terminada" antes de quedar solo con lo que hay que hacer.</summary>
    public double FinishedNoticeSeconds { get; init; } = 10;

    /// <summary>
    /// Margen sobre el tiempo de un aldeano antes de avisar que el centro urbano está parado: la cola no se ve, pero si
    /// pasan más de 25 s (más este margen) sin que suba el total del HUD, no estuvo produciendo todo el tiempo.
    /// </summary>
    public double TownCenterIdleGrace { get; init; } = 8;

    public CoachView View { get; private set; }

    /// <summary>El detalle de la economía del último snapshot (tasas, aldeanos estimados, demanda, déficit).</summary>
    public EconomyReport? Economy { get; private set; }

    public CoachView Update(GameSnapshot snapshot)
    {
        State.Apply(snapshot);
        _alert = snapshot.Events.LastOrDefault(e => AlertEvents.Contains(e.EventId)) ?? _alert;
        Tracker.Update(State);
        Abandonment.Update(State, Tracker.Steps);
        var clicked = DetectResearchClick(snapshot) | ResearchInQueue(snapshot);
        // El clic propio en una edad: su renglón del panel de puntajes cae a la vez (así no se toma por el del rival).
        foreach (var step in Tracker.InProgress.Where(s => s.Step.What is { } w && BuildOrderValidator.AgeOf(w) is not null && s.StartedAt is not null))
        {
            if (_ownClicksSeen.Add(step.Index))
                Signals.OwnAgeClick(step.StartedAt!.Value);
        }
        _branches.Trigger(Signals.Update(snapshot, State), State);
        _branches.Update(snapshot);
        TrackTownCenter(snapshot);
        if (clicked || State.LastAdded > 0 || snapshot.Time - _plannedAt >= ReplanEvery)
            Replan();
        _postureView = Abandonment.Abandoned ? null : PostureView();
        _postureSince = _postureView is null ? null : _postureSince ?? State.Time;
        Economy = _economy.Update(Tracker, _plan, PostureDemand());
        _suggestion = Economy.Suggestion;
        if (_postureView is not null)
            _techs.Update(State, FreshQueue, LivePlan.Start(Tracker).Age, State.VillagersOn ?? Actual(), Built, ResearchedByBuildOrder);
        UpdateGuide(snapshot.Time);
        View = BuildView();
        _report.Observe(snapshot, View, State, Tracker.Steps);
        return View;
    }

    /// <summary>
    /// Cuántos segundos tiene que sostenerse una GD distinta para reemplazar a la que se muestra (que no cambie a
    /// cada rato por una descarga). Si cambia el total de aldeanos, se actualiza al instante.
    /// </summary>
    public double GuideSettleSeconds { get; init; } = 12;

    /// <summary>Lugares de población libres desde los que se recuerda hacer una casa (lo que tarda en salir lo que hay en cola).</summary>
    public const int HouseMargin = 3;

    private void UpdateGuide(double now)
    {
        var foodFloor = Economy?.FoodFloor ?? 0;
        if (Abandonment.Abandoned)
        {
            (_guide, _guideCandidate) = (AbandonedGuide(foodFloor), null);
            return;
        }
        if (Distribution(foodFloor) is not { } bo)
        {
            _guide = null;
            return;
        }
        // Con los aldeanos por recurso del HUD, la GD parte de lo que hay y solo mueve si algo va a faltar: partiendo
        // del reparto de la BO, en la 20261018-001030 pedía pasar 8 de madera a comida con 500 de comida y 37 de madera.
        // Sin el HUD, parte del reparto de la BO. Lo que junta cada aldeano sale del modelo y no de la eficiencia
        // observada: con las ovejas bajaba a 0,35 y la GD pedía pasar 4 o 5 de madera a comida en plena Alta Edad Media.
        var start = State.VillagersOn is { } actual
            ? new ResourceAmounts(Math.Round(actual.Food), Math.Round(actual.Wood), Math.Round(actual.Stone), Math.Round(actual.Gold))
            : bo;
        var candidate = Economy is { } report
            ? GuideDistribution.Adjust(start, State.Stock, report.Demand, report.NominalRates, _economySettings.Horizon,
                foodFloor, _economySettings.ShortageThreshold, _economySettings.MaxMoves, StepTarget(Tracker.LaneStep(Lane.Villagers)))
            : start;
        if (_guide is not { } shown || Total(shown) != Total(candidate) || candidate == shown)
        {
            (_guide, _guideCandidate) = (candidate, null);
            return;
        }
        if (_guideCandidate != candidate)
            (_guideCandidate, _guideCandidateSince) = (candidate, now);
        else if (now - _guideCandidateSince >= GuideSettleSeconds)
            (_guide, _guideCandidate) = (candidate, null);
    }

    private static double Total(ResourceAmounts r) => r.Food + r.Wood + r.Gold + r.Stone;

    /// <summary>
    /// Con la BO abandonada, la GD es lo que hay (del HUD o estimado) con la comida que mantiene a los centros urbanos
    /// produciendo: lo que falta sale del recurso que más tiene.
    /// </summary>
    private ResourceAmounts? AbandonedGuide(int foodFloor)
    {
        if ((State.VillagersOn ?? (_villagers is null ? null : Actual())) is not { } actual)
            return null;
        var whole = Enum.GetValues<Resource>().ToDictionary(r => r, r => (int)Math.Round(actual[r]));
        var total = whole.Values.Sum();
        while (whole[Resource.Food] < Math.Min(foodFloor, total))
        {
            var donor = whole.Where(w => w.Key != Resource.Food).MaxBy(w => w.Value).Key;
            whole[donor]--;
            whole[Resource.Food]++;
        }
        return new ResourceAmounts(whole[Resource.Food], whole[Resource.Wood], whole[Resource.Stone], whole[Resource.Gold]);
    }

    /// <summary>
    /// Con los aldeanos por recurso del HUD, la sugerencia sale de comparar con la GD: del que más sobra al que más
    /// falta. Sin el HUD, la de la economía.
    /// </summary>
    private Suggestion? GuideSuggestion()
    {
        if (State.VillagersOn is not { } actual || _guide is not { } guide)
            return _suggestion;
        var resources = Enum.GetValues<Resource>();
        var from = resources.MaxBy(r => actual[r] - guide[r]);
        var to = resources.MaxBy(r => guide[r] - actual[r]);
        var count = (int)Math.Min(actual[from] - guide[from], guide[to] - actual[to]);
        return count >= 1 ? new Suggestion(from, to, count) : null;
    }

    /// <summary>
    /// Atajo "avanzar": da por hecho el paso actual (con una rama en curso, la cierra). Con la BO terminada, descarta
    /// la primera tecnología que falta ("Ya está": el coach arrancó con la partida empezada y no vio que se hizo).
    /// </summary>
    public CoachView Advance() => _branches.View() is not null ? Correct(() => _branches.Dismiss())
        : Finished ? Correct(() => _techs.Dismiss())
        : Correct(Tracker.Advance);

    /// <summary>La BO terminó (queda la postura de cierre) y no hay una rama en curso.</summary>
    private bool Finished => Tracker.Current is null && _postureView is not null && _branches.View() is null;

    /// <summary>
    /// Atajo "retroceder": deshace lo último que se completó o salteó. Con la BO abandonada, la retoma (lo ajeno y el
    /// atraso se cuentan desde ahora).
    /// </summary>
    public CoachView Back() => Abandonment.Abandoned
        ? Correct(() => Abandonment.Resume(State))
        : Correct(Tracker.Back);

    /// <summary>Atajo "saltear": marca el paso actual como salteado (con una rama en curso, la cierra).</summary>
    public CoachView Skip() => _branches.View() is not null ? Correct(() => _branches.Dismiss()) : Correct(Tracker.Skip);

    private CoachView Correct(Action action)
    {
        action();
        Replan();
        return View = BuildView();
    }

    /// <summary>
    /// Marca como en curso la primera investigación pendiente cuyo costo coincide con lo que bajó el stock, o
    /// desmarca una en curso si el stock subió justo su costo (se canceló).
    /// </summary>
    private bool DetectResearchClick(GameSnapshot snapshot)
    {
        var previous = _lastStock;
        _lastStock = snapshot.Stock;
        if (previous is not { } before)
            return false;
        var spent = before - snapshot.Stock;
        var civ = _data.Civ(Tracker.BuildOrder.Civ);
        var start = LivePlan.Start(Tracker);
        var age = start.Age;
        // La edad a la que se llegó: la de los pasos hechos o la que avisó el log (una BO sin el paso de la edad).
        var reached = start.Researched.Select(BuildOrderValidator.AgeOf).OfType<Age>().Append(age).Max();

        foreach (var step in Tracker.Steps.Where(s => s.Step.Do == StepAction.Research))
        {
            if (step.State is StepState.Done or StepState.Skipped || step.Step.What is not { } tech || !_data.TryGet(tech, out var entity))
                continue;
            if (step.State == StepState.InProgress && Matches(spent * -1, civ.Cost(entity, age)))
            {
                Tracker.ClearStarted(step.Index);
                return true;
            }
            if (step.State != StepState.InProgress && CanResearchNow(entity, reached) && Matches(spent, civ.Cost(entity, age)))
            {
                Tracker.MarkStarted(step.Index, snapshot.Time);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Si la investigación se puede hacer ahora: es de esta edad o de una anterior y su edificio está hecho (lo avisó el
    /// log o está hecho un paso de la BO que lo construye). Una baja del stock igual al costo de algo imposible es otra
    /// cosa: en la 20261025-230041, dos aldeanos encolados en la Edad Oscura (100 de comida) dejaron "en curso" la
    /// armadura acolchada toda la partida.
    /// </summary>
    private bool CanResearchNow(Entity tech, Age age) =>
        tech.Age <= age && (tech.MadeIn is not { } building || building == BuildOrderValidator.StartingBuilding || Built(building));

    /// <summary>Hay un edificio así: lo avisó el log o está hecho un paso de la BO que lo construye.</summary>
    private bool Built(string building) =>
        State.Count($"{building}_built") > 0
        || Tracker.Steps.Any(s => s.Step.Do == StepAction.Build && s.Step.What == building && s.State == StepState.Done);

    /// <summary>Un paso de la BO hizo esa investigación, la tiene en curso o se salteó.</summary>
    private bool ResearchedByBuildOrder(string tech) =>
        Tracker.Steps.Any(s => s.Step.Do == StepAction.Research && s.Step.What == tech && s.State is StepState.Done or StepState.InProgress or StepState.Skipped);

    /// <summary>
    /// Una investigación de la BO que aparece en la cola (avanzando o esperando) ya se pagó: queda en curso aunque la
    /// baja del stock no se haya visto. En la 20261018-132830 el clic en Castillos (20:26) no se vio en el stock y la
    /// cola lo mostraba. Tiene que estar en dos lecturas seguidas de la cola.
    /// </summary>
    private bool ResearchInQueue(GameSnapshot snapshot)
    {
        var previous = _lastQueueKeys;
        _lastQueueKeys = snapshot.Queue?.Select(i => i.Key).ToHashSet() ?? [];
        var started = false;
        foreach (var step in Tracker.Steps.Where(s => s.Step.Do == StepAction.Research && s.State is StepState.Pending or StepState.Ready or StepState.Late))
        {
            if (step.Step.What is { } tech && _lastQueueKeys.Contains(tech) && previous.Contains(tech))
            {
                Tracker.MarkStarted(step.Index, snapshot.Time);
                started = true;
            }
        }
        return started;
    }

    private HashSet<string> _lastQueueKeys = [];

    /// <summary>
    /// El cambio coincide con el costo en cada recurso que se paga (y hay alguno). Margen: un poco menos por errores
    /// de lectura y un poco más por lo recolectado entre dos capturas.
    /// </summary>
    internal static bool Matches(ResourceAmounts change, ResourceAmounts cost)
    {
        var paid = Enum.GetValues<Resource>().Where(r => cost[r] > 0).ToList();
        return paid.Count > 0 && paid.All(r => change[r] >= cost[r] * 0.85 && change[r] <= cost[r] + Math.Max(10, cost[r] * 0.1));
    }

    private void TrackTownCenter(GameSnapshot snapshot)
    {
        if (snapshot.Villagers is { } villagers)
        {
            if (_villagers is null || villagers > _villagers)
                _lastVillagerAt = snapshot.Time;
            _villagers = villagers;
        }
        if (Tracker.InProgress.Any(s => s.Step.What is { } what && _data.TryGet(what, out var e) && e.MadeIn == "town_center"))
            _townCenterBusyAt = snapshot.Time;
        // La cola cuenta recién cuando mostró algo: una caja corrida sobre el mapa la lee vacía toda la partida (en la
        // 20261025-230041 el centro urbano quedó "parado" los 29:53 con aldeanos saliendo sin parar).
        if (snapshot.Queue is { Count: > 0 })
            _queueTrusted = true;
        if (snapshot.Queue is { } queue && _queueTrusted)
        {
            _queue = queue;
            if (snapshot.Time - _queueSeenAt > QueueStaleSeconds)
                _queueFirstSeenAt = snapshot.Time;
            _queueSeenAt = snapshot.Time;
            if (queue.Any(item => IsTownCenterWork(item.Key)))
                _townCenterQueuedAt = snapshot.Time;
        }
    }

    /// <summary>
    /// Algo de la cola que tiene ocupado al centro urbano: aldeanos, sus tecnologías (telar, carretilla, edades) o
    /// lo que espera población (de eso avisa el recordatorio de la casa, no el del centro urbano).
    /// </summary>
    private bool IsTownCenterWork(string key) =>
        key is "villager" or "housed" || (_data.TryGet(key, out var entity) && entity.MadeIn == BuildOrderValidator.StartingBuilding);

    /// <summary>
    /// Con la cola de producción leída, el centro urbano está parado si hace <see cref="QueueIdleGrace"/> segundos
    /// que no hay nada suyo en la cola. Sin ella, se deduce de los aldeanos (y se quejaba mientras investigaba algo
    /// que la BO no tenía, como la carretilla).
    /// </summary>
    public double QueueIdleGrace { get; init; } = 5; // con 3, el cambio de un aldeano al telar o al clic de Feudal avisaba 1 s

    /// <summary>Segundos sin leer la cola a partir de los que se vuelve a deducir por los aldeanos.</summary>
    private const double QueueStaleSeconds = 5;

    private bool _queueTrusted;
    private double _queueSeenAt = double.NegativeInfinity;
    private double _queueFirstSeenAt = double.NegativeInfinity;
    private double _townCenterQueuedAt = double.NegativeInfinity;

    /// <summary>
    /// Cuánto lleva el centro urbano sin producir, contando desde el último aldeano (o desde que terminó de investigar)
    /// más lo que tarda uno. El centro urbano produce siempre hasta el máximo de aldeanos, salvo que la BO todavía
    /// pida aldeanos (entonces avisa aunque el máximo sea menor).
    /// </summary>
    private double? TownCenterIdle()
    {
        if (_villagers is not { } villagers || !_data.TryGet("villager", out var villager))
            return null;
        var pending = Tracker.Steps.Any(s => s.Step is { Do: StepAction.Train, What: "villager" } && s.State is not (StepState.Done or StepState.Skipped));
        if (!pending && villagers >= _economySettings.MaxVillagers)
            return null;
        if (State.Time - _queueSeenAt <= QueueStaleSeconds)
        {
            // Desde la última vez que la cola tuvo algo del centro urbano (o desde que se empezó a leer).
            var idleFor = State.Time - Math.Max(_townCenterQueuedAt, _queueFirstSeenAt);
            return idleFor >= QueueIdleGrace ? idleFor : null;
        }
        var civ = _data.Civ(Tracker.BuildOrder.Civ);
        var age = LivePlan.Start(Tracker).Age;
        var train = civ.Time(villager, age) / civ.WorkRate("town_center", age);
        var idle = State.Time - Math.Max(_lastVillagerAt, _townCenterBusyAt) - train;
        return idle >= TownCenterIdleGrace ? idle : null;
    }

    private (int Current, int Target)? SourceVillagers(StepProgress? current)
    {
        if (current?.Step is not { Do: StepAction.Train, What: "villager", Assign: { } source })
            return null;
        var target = current.Step.Count; // formato 2: el total en la fuente
        return (Math.Max(0, target - current.Remaining), target);
    }

    /// <summary>
    /// El reparto de la BO con lo hecho hasta ahora (aldeanos por recurso), llevado al total de aldeanos que muestra el
    /// HUD: si hay más o menos de lo que dice el plan, se reparte en la misma proporción (enteros que suman el total).
    /// La comida nunca queda debajo de lo que necesitan los centros urbanos para no parar: lo que falta sale del
    /// recurso que más tiene.
    /// </summary>
    private ResourceAmounts? Distribution(int foodFloor)
    {
        if (_villagers is not { } total || total <= 0)
            return null;
        var planned = new Dictionary<Resource, double>();
        foreach (var (source, count) in LivePlan.IntendedVillagers(Tracker))
            planned[source.Resource()] = planned.GetValueOrDefault(source.Resource()) + count;
        var sum = planned.Values.Sum();
        if (sum <= 0)
        {
            planned[Resource.Food] = 1;
            sum = 1;
        }

        var exact = Enum.GetValues<Resource>().ToDictionary(r => r, r => planned.GetValueOrDefault(r) * total / sum);
        var whole = exact.ToDictionary(e => e.Key, e => (int)Math.Floor(e.Value));
        foreach (var r in exact.OrderByDescending(e => e.Value - Math.Floor(e.Value)).Take(total - whole.Values.Sum()).Select(e => e.Key))
            whole[r]++;
        while (whole[Resource.Food] < Math.Min(foodFloor, total))
        {
            var donor = whole.Where(w => w.Key != Resource.Food).MaxBy(w => w.Value).Key;
            whole[donor]--;
            whole[Resource.Food]++;
        }
        return new ResourceAmounts(whole[Resource.Food], whole[Resource.Wood], whole[Resource.Stone], whole[Resource.Gold]);
    }

    private ResourceAmounts? StepTarget(StepProgress? current)
    {
        if (current is null)
            return null;
        // Si es parte de un paso mixto, hasta la última meta de aldeanos del grupo.
        var last = current.Step.Group is { } group
            ? Tracker.Steps.Where(s => s.Step.Group == group && Lanes.Of(s.Step) == Lane.Villagers).Max(s => s.Index)
            : current.Index;
        var perResource = new Dictionary<Resource, double>();
        foreach (var (source, count) in LivePlan.PlannedVillagers(Tracker.BuildOrder, last))
            perResource[source.Resource()] = perResource.GetValueOrDefault(source.Resource()) + count;
        return new ResourceAmounts(
            perResource.GetValueOrDefault(Resource.Food), perResource.GetValueOrDefault(Resource.Wood),
            perResource.GetValueOrDefault(Resource.Stone), perResource.GetValueOrDefault(Resource.Gold));
    }

    /// <summary>Aldeanos que le faltan a un paso de aldeanos para que valga la pena mostrar "ahora" (unos 75 s de centro urbano).</summary>
    public const int LongStepVillagers = 3;

    /// <summary>
    /// Paso largo: el centro urbano está investigando (edades, carretilla, telar: no salen aldeanos y el paso no
    /// avanza) o al paso le faltan <see cref="LongStepVillagers"/> aldeanos o más.
    /// </summary>
    private ResourceAmounts? NowTarget(StepProgress? current, ResourceAmounts? now, ResourceAmounts? end)
    {
        if (current is null || now is not { } n || end is not { } e || n == e)
            return null;
        var researching = Tracker.InProgress.Any(s => s.Step.What is { } what && _data.TryGet(what, out var entity)
            && entity.MadeIn == BuildOrderValidator.StartingBuilding);
        var remaining = SourceVillagers(current) is { } on ? on.Target - on.Current : current.Remaining;
        return researching || (LivePlan.IsVillagerTarget(current.Step) && remaining >= LongStepVillagers) ? n : null;
    }

    private ResourceAmounts? Actual() => Economy?.Villagers.PerResource is { } estimate
        ? new ResourceAmounts(Math.Round(estimate.Food), Math.Round(estimate.Wood), Math.Round(estimate.Stone), Math.Round(estimate.Gold))
        : null;

    private void Replan()
    {
        _plan = LivePlan.Replan(Tracker, _planner);
        _plannedAt = State.Time;
    }

    private CoachView BuildView()
    {
        if (Abandonment.Abandoned)
            return AbandonedView();
        var current = Tracker.Current;
        var villagers = Tracker.LaneStep(Lane.Villagers);
        var missing = current is null ? ResourceAmounts.Zero : MissingFor(current);

        var eta = current is null ? null : _plan?.For(current.Step.Id)?.Start;
        var deviations = _plan is null ? [] : LivePlan.Deviations(Tracker.BuildOrder, _plan).ToList();
        return new CoachView(
            State.Time,
            State.Population,
            State.PopulationCap,
            current,
            Tracker.Next,
            // En el gris (unidades ajenas a la par de las de la BO) lo militar no queda atrasado.
            Tracker.Late.Where(s => !(Abandonment.Gray && s.Step is { Do: StepAction.Train } train && train.What != "villager")).ToList(),
            Tracker.InProgress.Select(s => new RunningStep(s, _plan?.For(s.Step.Id)?.End))
                .Concat(Tracker.Producing.Select(s => new RunningStep(s, null)))
                .Concat(Tracker.Silent.Select(s => new RunningStep(s, null))).ToList(),
            missing,
            eta,
            deviations,
            GuideSuggestion(),
            Tracker.Steps.Count(s => s.State is StepState.Done or StepState.Skipped),
            Tracker.Steps.Count)
        {
            Alert = _alert is { } alert && State.Time - alert.Time < AlertSeconds ? alert.EventId : null,
            TownCenterIdle = TownCenterIdle(),
            SourceVillagers = SourceVillagers(current),
            Distribution = _guide ?? Distribution(Economy?.FoodFloor ?? 0),
            BoDistribution = Distribution(Economy?.FoodFloor ?? 0),
            StepTarget = StepTarget(villagers),
            NowTarget = NowTarget(villagers, Distribution(Economy?.FoodFloor ?? 0), StepTarget(villagers)),
            Actual = State.VillagersOn ?? (_villagers is null ? null : Actual()),
            ActualFromHud = State.VillagersOn is not null,
            FoodFloor = Economy?.FoodFloor ?? 0,
            Goals = Tracker.CurrentGoals,
            NeedsHouse = NeedsHouse(),
            TownCenterReminder = TownCenterIdle() is not null && _villagers < _economySettings.MaxVillagers,
            Posture = _postureView,
            Signals = RecentSignals(),
            Branch = _branches.View(),
            Compact = Finished && State.Time - _postureSince >= FinishedNoticeSeconds,
            Techs = Finished ? _techs.Shown : [],
            Lanes = Engine.Lanes.All.Select(LaneViewOf).ToList(),
            VillagerGuide = villagers is null ? VillagerGuide() : null,
        };
    }

    private LaneView LaneViewOf(Lane lane) => Tracker.LaneStep(lane) is { } step
        ? new LaneView(lane, step, MissingFor(step), _plan?.For(step.Step.Id)?.Start)
        : new LaneView(lane, null, ResourceAmounts.Zero, null);

    /// <summary>Lo que falta juntar para un paso: su costo con la civ en la edad del jugador menos el stock.</summary>
    private ResourceAmounts MissingFor(StepProgress step)
    {
        var missing = ResourceAmounts.Zero;
        if (step.Step.What is { } what && step.Step.Do != StepAction.Move && _data.TryGet(what, out var entity))
        {
            var cost = _data.Civ(Tracker.BuildOrder.Civ).Cost(entity, LivePlan.Start(Tracker).Age);
            foreach (var r in Enum.GetValues<Resource>())
                missing = missing.With(r, Math.Max(0, cost[r] - State.Stock[r]));
        }
        return missing;
    }

    private Resource? VillagerGuide()
    {
        var resources = Enum.GetValues<Resource>();
        if ((_guide ?? Distribution(Economy?.FoodFloor ?? 0)) is { } guide && (State.VillagersOn ?? Actual()) is { } actual)
        {
            var (best, gap) = resources.Select(r => (r, guide[r] - actual[r])).MaxBy(x => x.Item2);
            if (gap > 0)
                return best;
        }
        if (Economy is { } report)
        {
            var (best, gap) = resources.Select(r => (r, report.Demand[r] - State.Stock[r])).MaxBy(x => x.Item2);
            if (gap > 0)
                return best;
        }
        return null;
    }

    private IReadOnlyList<GameSignal> RecentSignals() => Signals.All.Where(s => State.Time - s.Time < SignalSeconds).ToList();

    /// <summary>
    /// La postura de cierre, cuando no queda paso actual (la cola, si se leyó hace poco). Pueden quedar abiertos lo que
    /// el juego no avisa (granjas) y la producción militar a medias: esos nunca se cierran solos.
    /// </summary>
    private PostureView? PostureView() => Tracker.Current is null
        ? _posture.View(State, FreshQueue, LivePlan.Start(Tracker).Age, _data.Civ(Tracker.BuildOrder.Civ))
        : null;

    /// <summary>La última cola leída, si se leyó hace poco (null si no).</summary>
    private IReadOnlyList<QueueItem>? FreshQueue => State.Time - _queueSeenAt <= QueueStaleSeconds ? _queue : null;

    /// <summary>
    /// D2: con la postura en pantalla, la economía (y con ella la GD) junta para producir sin parar sus unidades y
    /// para la próxima edad, en el mismo tiempo que usa para el próximo hito de la BO (2 horizontes).
    /// </summary>
    private ResourceAmounts? PostureDemand() => _postureView is { } view
        ? _posture.Demand(view, State, LivePlan.Start(Tracker).Age, _data.Civ(Tracker.BuildOrder.Civ), _economySettings.Horizon * 2,
            villagers: (State.Villagers ?? _villagers ?? 0) < _economySettings.MaxVillagers)
        : null;

    private bool NeedsHouse() => State.PopulationCap < BuildOrderValidator.MaxPopulation && State.PopulationCap - State.Population <= HouseMargin;

    /// <summary>
    /// La BO abandonada: sin pasos (ni actual, ni siguiente, ni atrasados, ni producción), solo lo que siempre vale:
    /// lo que se está investigando, los avisos, la casa, el centro urbano parado y la GD contra lo que hay.
    /// </summary>
    private CoachView AbandonedView() => new(
        State.Time,
        State.Population,
        State.PopulationCap,
        null,
        null,
        [],
        Tracker.InProgress.Select(s => new RunningStep(s, _plan?.For(s.Step.Id)?.End)).ToList(),
        ResourceAmounts.Zero,
        null,
        [],
        GuideSuggestion(),
        Tracker.Steps.Count(s => s.State is StepState.Done or StepState.Skipped),
        Tracker.Steps.Count)
    {
        Abandoned = Abandonment.Reason,
        Alert = _alert is { } alert && State.Time - alert.Time < AlertSeconds ? alert.EventId : null,
        TownCenterIdle = TownCenterIdle(),
        Distribution = _guide,
        Actual = State.VillagersOn ?? (_villagers is null ? null : Actual()),
        ActualFromHud = State.VillagersOn is not null,
        FoodFloor = Economy?.FoodFloor ?? 0,
        NeedsHouse = NeedsHouse(),
        TownCenterReminder = TownCenterIdle() is not null && _villagers < _economySettings.MaxVillagers,
        Signals = RecentSignals(),
        Branch = _branches.View(),
        Lanes = Engine.Lanes.All.Select(lane => new LaneView(lane, null, ResourceAmounts.Zero, null)).ToList(),
        VillagerGuide = VillagerGuide(),
    };
}