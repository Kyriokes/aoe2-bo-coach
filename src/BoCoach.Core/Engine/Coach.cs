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
public sealed record LaneView(Lane Lane, StepProgress? Step, ResourceAmounts Missing, double? Eta)
{
    /// <summary>Pasos de la línea que quedaron atrás (<see cref="Tracker.LaneBehind"/>): se muestran aparte.</summary>
    public IReadOnlyList<StepProgress> Behind { get; init; } = [];
}

/// <summary>Lo que muestra el overlay en un instante.</summary>
/// <param name="Missing">Lo que falta juntar para el paso actual (cero si ya alcanza).</param>
/// <param name="CurrentEta">Cuándo estima el Planner que se puede hacer el paso actual (reloj de partida).</param>
public sealed record CoachView(
    double Time,
    int Population,
    int PopulationCap,
    StepProgress? Current,
    StepProgress? Next,
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

    /// <summary>Recordatorio: quedan <see cref="HouseMargin"/> lugares de población o menos (y no se llegó a 200).</summary>
    public bool NeedsHouse { get; init; }

    /// <summary>
    /// Con cuántos lugares libres se avisa la casa (<see cref="HousePacing"/>): 3 con el centro urbano solo, más con
    /// varias galerías y establos produciendo.
    /// </summary>
    public int HouseMargin { get; init; } = Coach.HouseMargin;

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
    /// que hay); si la GD no pide ninguno, el de más demanda sin cubrir por el stock, el que más se gasta o el que más
    /// aldeanos tiene en la GD. null solo sin ninguna lectura.
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
        Abandonment = new BuildOrderAbandonment(Tracker.BuildOrder, data);
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

    /// <summary>
    /// El clic de edad del rival se muestra más (pedido del usuario del 2026-10-31: "tiene que ser más largo, titilar
    /// entre el color del texto y el del jugador y ser mega molesto").
    /// </summary>
    public double RivalClickSeconds { get; init; } = 30;

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
        // Con el reloj corregido, el tiempo sin avanzar se cuenta de nuevo: el salto de la hora mala a la buena no es
        // tiempo de partida, y daba la BO por abandonada hasta que avanzara un paso.
        if (snapshot.ClockCorrected)
            Abandonment.Resume(State);
        Abandonment.Update(State, Tracker.Steps, !Tracker.HasOpenSteps, Tracker.Current is { } current ? MissingFor(current).Total : 0);
        var clicked = DetectResearchClick(snapshot) | ResearchInQueue(snapshot) | ExpireClicks(snapshot) | ReviveAgeClicks();
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
    public CoachView Advance(Lane? lane = null) => _branches.View() is not null ? Correct(() => _branches.Dismiss())
        : Finished ? Correct(() => _techs.Dismiss())
        : Correct(() => Tracker.Advance(lane));

    /// <summary>La BO terminó (queda la postura de cierre) y no hay una rama en curso.</summary>
    private bool Finished => !Tracker.HasOpenSteps && _postureView is not null && _branches.View() is null;

    /// <summary>
    /// Atajo "retroceder": deshace lo último que se completó o salteó. Con la BO abandonada, la retoma (lo ajeno y el
    /// atraso se cuentan desde ahora).
    /// </summary>
    public CoachView Back() => Abandonment.Abandoned
        ? Correct(() => Abandonment.Resume(State))
        : Correct(() =>
        {
            Tracker.Back();
            Abandonment.Rewound();
        });

    /// <summary>Atajo "saltear": marca el paso actual como salteado (con una rama en curso, la cierra).</summary>
    public CoachView Skip(Lane? lane = null) => _branches.View() is not null ? Correct(() => _branches.Dismiss()) : Correct(() => Tracker.Skip(lane));

    private CoachView Correct(Action action)
    {
        action();
        Replan();
        return View = BuildView();
    }

    /// <summary>
    /// Marca como en curso la primera investigación pendiente cuyo costo coincide con lo que bajó el stock, o
    /// desmarca una en curso si el stock subió justo su costo (se canceló).
    /// <para>Si la cola de esa captura la sigue mostrando, lo que volvió es otra cosa: al cancelarla de verdad, el ícono
    /// se va en el mismo cuadro en que vuelve el stock. En la revisión del 2026-11-05, sacar 9 aldeanos de la cola para
    /// que arrancara la Feudal devolvía 450 de comida, desmarcaba la Feudal en espera y el coach pedía el clic de nuevo.</para>
    /// <para>Una edad a la que ya se llegó (como en <see cref="ExpireClicks"/>) tampoco se cancela: sin la cola ni el aviso
    /// del log, comprar 500 de comida en el mercado en plena Feudal la desmarcaba y volvía "Hacé clic en Edad Feudal".</para>
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
            if (step.State == StepState.InProgress && Matches(spent * -1, civ.Cost(entity, age)) && snapshot.Queue?.Any(i => i.Key == tech) != true
                && !(BuildOrderValidator.AgeOf(tech) is { } stepAge && stepAge <= Tracker.Age))
            {
                _canceledByStock[step.Index] = step.StartedAt!.Value;
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

    /// <summary>Hay un edificio así: lo avisó el log (y "Atrás" no lo descartó) o está hecho un paso de la BO que lo construye.</summary>
    private bool Built(string building) => Tracker.Built(building);

    /// <summary>Un paso de la BO hizo esa investigación, la tiene en curso o se salteó.</summary>
    private bool ResearchedByBuildOrder(string tech) =>
        Tracker.Steps.Any(s => s.Step.Do == StepAction.Research && s.Step.What == tech && s.State is StepState.Done or StepState.InProgress or StepState.Skipped);

    /// <summary>
    /// Una investigación de la BO que la creencia del estado tiene en curso (la vio en la cola de producción en dos
    /// lecturas seguidas) ya se pagó: queda en curso aunque la baja del stock no se haya visto. En la 20261018-132830 el
    /// clic en Castillos (20:26) no se vio en el stock y la cola lo mostraba. La que termina sin que el log lo avise llega
    /// como evento sintético "&lt;tech&gt;_researched" y el Tracker la confirma por el camino de siempre.
    /// <para>Solo al aparecer en la cola: si no, la cancelación y "Atrás" se deshacían en la captura siguiente. Y si se
    /// va de la cola sin terminar, lo que solo la cola había dicho se deshace: era un ícono mal leído o se canceló. Lo
    /// que también se vio en el stock lo decide <see cref="ExpireClicks"/>.</para>
    /// <para>Lo que el stock dio por cancelado mientras la creencia lo tiene en curso vuelve a estar en curso, desde su
    /// clic, si la cola lo muestra en dos lecturas seguidas: en la captura de la devolución, el ícono en espera pudo salir "?".</para>
    /// </summary>
    private bool ResearchInQueue(GameSnapshot snapshot)
    {
        var previousRaw = _rawQueue;
        _rawQueue = snapshot.Queue?.Select(i => i.Key).ToHashSet() ?? [];
        if (snapshot.Belief is not { } belief)
            return false;
        var changed = false;
        var inQueue = belief.Research.Where(r => r.Value == ResearchStatus.InProgress).Select(r => r.Key).ToHashSet();
        foreach (var step in Tracker.Steps.Where(s => s.Step is { Do: StepAction.Research, What: not null }))
        {
            var tech = step.Step.What!;
            if (inQueue.Contains(tech) && !_researchInQueue.Contains(tech) && step.State is StepState.Pending or StepState.Ready)
            {
                Tracker.MarkStarted(step.Index, snapshot.Time);
                _startedByQueue.Add(step.Index);
                changed = true;
            }
            else if (!inQueue.Contains(tech) && _researchInQueue.Contains(tech) && _startedByQueue.Remove(step.Index)
                && step.State == StepState.InProgress && !belief.Done(tech))
            {
                Tracker.ClearStarted(step.Index);
                changed = true;
            }
            else if (_canceledByStock.TryGetValue(step.Index, out var clicked) && inQueue.Contains(tech)
                && step.State is StepState.Pending or StepState.Ready && _rawQueue.Contains(tech) && previousRaw.Contains(tech))
            {
                Tracker.MarkStarted(step.Index, clicked);
                changed = true;
            }
        }
        // Lo que ya no está pendiente, o que la creencia dejó de tener en curso, sigue por el camino de siempre.
        foreach (var index in _canceledByStock.Keys.ToList())
        {
            if (Tracker.Steps[index] is not { State: StepState.Pending or StepState.Ready, Step.What: { } tech } || !inQueue.Contains(tech))
                _canceledByStock.Remove(index);
        }
        _researchInQueue = inQueue;
        return changed;
    }

    private HashSet<string> _researchInQueue = [];
    private readonly HashSet<int> _startedByQueue = [];

    /// <summary>Las investigaciones que el stock dio por canceladas, con la hora de su clic (ver <see cref="ResearchInQueue"/>).</summary>
    private readonly Dictionary<int, double> _canceledByStock = [];

    /// <summary>Lo que mostró la cola en la última captura, tal cual se leyó (vacío si no se leyó).</summary>
    private HashSet<string> _rawQueue = [];

    /// <summary>
    /// Un clic visto en el stock que ni el log ni la cola confirman vence, igual que en <see cref="TechReminders"/>: dos
    /// aldeanos encolados son 100 de comida, lo mismo que la armadura acolchada, y un arquero (45 de oro) pasa por el telar.
    /// Sin esto quedaba en curso hasta el final y el coach no la pedía más. Mientras la cola la muestre, sigue. El clic de
    /// una edad a la que ya se llegó (el log no leyó el aviso, pero hay algo de esa edad) tampoco vence: no se puede volver a pedir.
    /// <para>El de una edad que vence sin que la cola se haya leído desde el clic se guarda (<see cref="ReviveAgeClicks"/>):
    /// sin ella nada lo desmiente. En la revisión del 2026-11-05, la Feudal sin cola ni aviso del log vencía antes de que
    /// llegara algo de Feudal, y después nada cerraba el paso: "Hacé clic en Edad Feudal" toda la partida.</para>
    /// </summary>
    private bool ExpireClicks(GameSnapshot snapshot)
    {
        var civ = _data.Civ(Tracker.BuildOrder.Civ);
        var age = Tracker.Age;
        var expired = false;
        foreach (var step in Tracker.InProgress.ToList())
        {
            if (step.Step.What is not { } tech || !_data.TryGet(tech, out var entity) || step.StartedAt is not { } clicked
                || snapshot.Belief?.InProgress(tech) == true || snapshot.Belief?.Done(tech) == true
                || BuildOrderValidator.AgeOf(tech) <= age)
                continue;
            if (snapshot.Time >= clicked + TechReminders.Pause(civ, entity, age))
            {
                if (BuildOrderValidator.AgeOf(tech) is not null && !QueueReadSince(clicked))
                    _expiredAgeClicks[step.Index] = clicked;
                Tracker.ClearStarted(step.Index);
                expired = true;
            }
        }
        return expired;
    }

    /// <summary>La cola se está leyendo y se leyó sin cortes desde ese momento: lo que se investiga desde entonces se vio en ella.</summary>
    private bool QueueReadSince(double time) => FreshQueue is not null && _queueFirstSeenAt <= time;

    /// <summary>
    /// El clic de una edad que venció sin la cola vuelve a estar en curso, con su hora, cuando algo más dice que se llegó
    /// a esa edad (una galería es de Feudal). En curso y no hecho: la edad sale de cualquier evento leído, y uno mal
    /// leído no puede cerrar el paso. Lo descarta que el paso deje de estar pendiente (otro clic, hecho, salteado).
    /// </summary>
    private bool ReviveAgeClicks()
    {
        var revived = false;
        foreach (var (index, clicked) in _expiredAgeClicks.ToList())
        {
            var step = Tracker.Steps[index];
            if (step.State is not (StepState.Pending or StepState.Ready))
            {
                _expiredAgeClicks.Remove(index);
            }
            else if (BuildOrderValidator.AgeOf(step.Step.What!) <= Tracker.Age)
            {
                _expiredAgeClicks.Remove(index);
                Tracker.MarkStarted(index, clicked);
                revived = true;
            }
        }
        return revived;
    }

    /// <summary>Los clics de edad que vencieron sin la cola, con su hora (ver <see cref="ReviveAgeClicks"/>).</summary>
    private readonly Dictionary<int, double> _expiredAgeClicks = [];

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
            // Solo una suba sobre lo más alto visto: una lectura que baja y vuelve (15, 14, 15) no es un aldeano nuevo.
            if (villagers > _mostVillagers && _mostVillagers > 0 && QueueMissedVillager(snapshot.Time))
                QueueMissesVillagers = true;
            _mostVillagers = Math.Max(_mostVillagers, villagers);
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
            if (queue.Any(item => item.Key == QueueItem.Unknown))
                _queueUnknownAt = snapshot.Time;
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
    /// La última vez que la cola mostró un ícono sin reconocer: puede ser del centro urbano, así que mientras esté no se
    /// mira la cola para decir que está parado (partida 20261104-142003, jugador naranja: 5:57 de aviso falso).
    /// </summary>
    private double _queueUnknownAt = double.NegativeInfinity;

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
        if (State.Time - _queueSeenAt <= QueueStaleSeconds && _queueUnknownAt < _queueSeenAt && !QueueMissesVillagers)
        {
            // Desde la última vez que la cola tuvo algo del centro urbano o algo sin reconocer (o desde que se empezó a leer).
            var idleFor = State.Time - Math.Max(Math.Max(_townCenterQueuedAt, _queueUnknownAt), _queueFirstSeenAt);
            return idleFor >= QueueIdleGrace ? idleFor : null;
        }
        // Lo del centro urbano que se vio en la cola también cuenta: con un "?" al lado, sigue estando ocupado.
        var idle = Math.Min(State.Time - Math.Max(_lastVillagerAt, _townCenterBusyAt) - VillagerTrainTime(villager), State.Time - _townCenterQueuedAt);
        return idle >= TownCenterIdleGrace ? idle : null;
    }

    private double VillagerTrainTime(Entity villager)
    {
        var civ = _data.Civ(Tracker.BuildOrder.Civ);
        var age = LivePlan.Start(Tracker).Age;
        return civ.Time(villager, age) / civ.WorkRate("town_center", age);
    }

    /// <summary>
    /// La cola no ve los aldeanos de este jugador: salió uno sin que, mientras se entrenaba, la cola (leída todo ese
    /// tiempo) mostrara nada del centro urbano. Desde ahí el centro urbano parado se deduce por los aldeanos. Partida del
    /// 2026-11-07 (cumanos, jugador amarillo): en un ciclo de cada dos la cola leía vacía o mandoble y arquero a caballo,
    /// y el aviso salía de 5 a 24 s con el centro urbano sacando un aldeano cada 25 s. Con uno alcanza: pidiendo dos se
    /// daba cuenta a las 6:38 en vez de a la 1:40, con tres avisos falsos más. Lo que podría engañarlo (un monje que
    /// convierte un aldeano con el centro urbano parado) solo lo pasa a deducir por los aldeanos, como sin cola.
    /// </summary>
    private bool QueueMissesVillagers { get; set; }

    /// <summary>Lo más alto que se leyó el total de aldeanos.</summary>
    private int _mostVillagers;

    private bool QueueMissedVillager(double now)
    {
        if (!_queueTrusted || !_data.TryGet("villager", out var villager))
            return false;
        // Desde el aldeano anterior (su ícono se lee hasta la captura en que sale, de ahí el segundo de más), y con todo
        // un entrenamiento en el medio: con dos centros urbanos salen más seguido y no se puede decir cuál se vio.
        var previous = _lastVillagerAt;
        return now - previous >= MissedVillagerShare * VillagerTrainTime(villager)
            && _queueFirstSeenAt <= previous && now - _queueSeenAt <= QueueStaleSeconds
            && _townCenterQueuedAt <= previous + 1;
    }

    /// <summary>Parte del tiempo de un aldeano que tiene que haber pasado desde el anterior para decir que la cola no lo vio.</summary>
    private const double MissedVillagerShare = 0.8;

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
            HouseMargin = CurrentHouseMargin(),
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

    private LaneView LaneViewOf(Lane lane) => (Tracker.LaneStep(lane) is { } step
        ? new LaneView(lane, step, MissingFor(step), _plan?.For(step.Step.Id)?.Start)
        : new LaneView(lane, null, ResourceAmounts.Zero, null))
        // D1: en el gris (unidades ajenas a la par de las de la BO) sigue la BO, pero lo militar no queda atrasado.
        with { Behind = lane == Lane.Produce && Abandonment.Gray ? [] : Tracker.LaneBehind(lane) };

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
            // No falta nada: el que más se gasta. El renglón de Aldeanos no puede quedar sin decir a dónde (pedido del
            // usuario: "estoy haciendo el 26, el 27, y no sé a qué recursos mandarlos").
            var (most, rate) = resources.Select(r => (r, report.Demand[r])).MaxBy(x => x.Item2);
            if (rate > 0)
                return most;
        }
        return (_guide ?? Distribution(Economy?.FoodFloor ?? 0)) is { } split ? resources.MaxBy(r => split[r]) : null;
    }

    private IReadOnlyList<GameSignal> RecentSignals() =>
        Signals.All.Where(s => State.Time - s.Time < (s.IsRivalClick ? RivalClickSeconds : SignalSeconds)).ToList();

    /// <summary>
    /// La postura de cierre, cuando no queda paso actual (la cola, si se leyó hace poco). Pueden quedar abiertos lo que
    /// el juego no avisa (granjas) y la producción militar a medias: esos nunca se cierran solos.
    /// </summary>
    private PostureView? PostureView() => !Tracker.HasOpenSteps
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

    private bool NeedsHouse() => State.PopulationCap < BuildOrderValidator.MaxPopulation && State.PopulationCap - State.Population <= CurrentHouseMargin();

    /// <summary>
    /// Casas por ritmo de producción (spec 2026-11-02, escalón 1): el margen sale de lo que produce la cola real (si se
    /// leyó hace poco; si no, el centro urbano solo) y de lo que se tarda en reaccionar, nunca menos que 3.
    /// </summary>
    private int CurrentHouseMargin()
    {
        var civ = _data.Civ(Tracker.BuildOrder.Civ);
        var age = LivePlan.Start(Tracker).Age;
        var queue = State.Time - _queueSeenAt <= QueueStaleSeconds ? _queue : null;
        return HousePacing.Margin(HousePacing.ConsumptionRate(queue, _data, civ, age), HousePacing.ReactionSeconds(_data, civ, age));
    }

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
        HouseMargin = CurrentHouseMargin(),
        TownCenterReminder = TownCenterIdle() is not null && _villagers < _economySettings.MaxVillagers,
        Signals = RecentSignals(),
        Branch = _branches.View(),
        Lanes = Engine.Lanes.All.Select(lane => new LaneView(lane, null, ResourceAmounts.Zero, null)).ToList(),
        VillagerGuide = VillagerGuide(),
    };
}