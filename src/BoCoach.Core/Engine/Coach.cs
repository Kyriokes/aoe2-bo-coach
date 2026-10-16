using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Economy;

namespace BoCoach.Core.Engine;

/// <summary>Un paso ya pagado que se está haciendo, y cuándo estima el Planner que termina.</summary>
public sealed record RunningStep(StepProgress Progress, double? EndsAt);

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
    /// Cómo repartir los aldeanos que hay ahora (total del HUD) según la BO en este punto, o null si todavía no se leyó
    /// el total.
    /// </summary>
    public ResourceAmounts? Distribution { get; init; }
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
    private PlanResult? _plan;
    private double _plannedAt = double.NegativeInfinity;
    private Suggestion? _suggestion;
    private ResourceAmounts? _lastStock;
    private GameEvent? _alert;

    // Centro urbano: cuándo subió por última vez el total de aldeanos del HUD y cuándo se lo vio investigando.
    private int? _villagers;
    private double _lastVillagerAt;
    private double _townCenterBusyAt = double.NegativeInfinity;

    public Coach(BuildOrder bo, GameData data, EventCatalog? events = null, PlannerSettings? planner = null, EconomySettings? economy = null)
    {
        _data = data;
        _planner = new Planner(data, planner);
        _economy = new EconomyAdvisor(data, planner, economy);
        Tracker = new Tracker(bo, data, events);
        Tracker.Update(State);
        View = BuildView();
    }

    public PlayerState State { get; } = new();
    public Tracker Tracker { get; }

    /// <summary>Cada cuántos segundos de partida se vuelve a simular el plan (si no hubo eventos).</summary>
    public double ReplanEvery { get; init; } = 5;

    /// <summary>Avisos del log que el overlay muestra (ver <see cref="CoachText.AlertText"/>).</summary>
    public static readonly IReadOnlySet<string> AlertEvents =
        new HashSet<string> { "farm_exhausted", "fish_trap_expired", "under_attack", "attacked_by_animals" };

    /// <summary>Cuántos segundos de partida se muestra un aviso.</summary>
    public double AlertSeconds { get; init; } = 8;

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
        var clicked = DetectResearchClick(snapshot);
        TrackTownCenter(snapshot);
        if (clicked || State.LastAdded > 0 || snapshot.Time - _plannedAt >= ReplanEvery)
            Replan();
        Economy = _economy.Update(Tracker, _plan);
        _suggestion = Economy.Suggestion;
        return View = BuildView();
    }

    /// <summary>Atajo "avanzar": da por hecho el paso actual.</summary>
    public CoachView Advance() => Correct(Tracker.Advance);

    /// <summary>Atajo "retroceder": deshace lo último que se completó o salteó.</summary>
    public CoachView Back() => Correct(Tracker.Back);

    /// <summary>Atajo "saltear": marca el paso actual como salteado.</summary>
    public CoachView Skip() => Correct(Tracker.Skip);

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
        var age = LivePlan.Start(Tracker).Age;

        foreach (var step in Tracker.Steps.Where(s => s.Step.Do == StepAction.Research))
        {
            if (step.State is StepState.Done or StepState.Skipped || step.Step.What is not { } tech || !_data.TryGet(tech, out var entity))
                continue;
            if (step.State == StepState.InProgress && Matches(spent * -1, civ.Cost(entity, age)))
            {
                Tracker.ClearStarted(step.Index);
                return true;
            }
            if (step.State != StepState.InProgress && Matches(spent, civ.Cost(entity, age)))
            {
                Tracker.MarkStarted(step.Index, snapshot.Time);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// El cambio coincide con el costo en cada recurso que se paga (y hay alguno). Margen: un poco menos por errores
    /// de lectura y un poco más por lo recolectado entre dos capturas.
    /// </summary>
    private static bool Matches(ResourceAmounts change, ResourceAmounts cost)
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
    }

    /// <summary>
    /// Cuánto lleva el centro urbano sin producir, contando desde el último aldeano (o desde que terminó de investigar)
    /// más lo que tarda uno. Solo si la BO todavía pide aldeanos.
    /// </summary>
    private double? TownCenterIdle()
    {
        if (_villagers is null || !_data.TryGet("villager", out var villager)
            || !Tracker.Steps.Any(s => s.Step is { Do: StepAction.Train, What: "villager" } && s.State is not (StepState.Done or StepState.Skipped)))
            return null;
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
    /// </summary>
    private ResourceAmounts? Distribution()
    {
        if (_villagers is not { } total || total <= 0)
            return null;
        var planned = new Dictionary<Resource, double>();
        foreach (var (source, count) in LivePlan.Start(Tracker).Villagers)
            planned[source.Resource()] = planned.GetValueOrDefault(source.Resource()) + count;
        var sum = planned.Values.Sum();
        if (sum <= 0)
            return null;

        var exact = Enum.GetValues<Resource>().ToDictionary(r => r, r => planned.GetValueOrDefault(r) * total / sum);
        var whole = exact.ToDictionary(e => e.Key, e => (int)Math.Floor(e.Value));
        foreach (var r in exact.OrderByDescending(e => e.Value - Math.Floor(e.Value)).Take(total - whole.Values.Sum()).Select(e => e.Key))
            whole[r]++;
        return new ResourceAmounts(whole[Resource.Food], whole[Resource.Wood], whole[Resource.Stone], whole[Resource.Gold]);
    }

    private void Replan()
    {
        _plan = LivePlan.Replan(Tracker, _planner);
        _plannedAt = State.Time;
    }

    private CoachView BuildView()
    {
        var current = Tracker.Current;
        var missing = ResourceAmounts.Zero;
        if (current?.Step.What is { } what && current.Step.Do != StepAction.Move && _data.TryGet(what, out var entity))
        {
            var cost = _data.Civ(Tracker.BuildOrder.Civ).Cost(entity, LivePlan.Start(Tracker).Age);
            foreach (var r in Enum.GetValues<Resource>())
                missing = missing.With(r, Math.Max(0, cost[r] - State.Stock[r]));
        }

        var eta = current is null ? null : _plan?.For(current.Step.Id)?.Start;
        var deviations = _plan is null ? [] : LivePlan.Deviations(Tracker.BuildOrder, _plan).ToList();
        return new CoachView(
            State.Time,
            State.Population,
            State.PopulationCap,
            current,
            Tracker.Next,
            Tracker.Late.ToList(),
            Tracker.InProgress.Select(s => new RunningStep(s, _plan?.For(s.Step.Id)?.End)).ToList(),
            missing,
            eta,
            deviations,
            _suggestion,
            Tracker.Steps.Count(s => s.State is StepState.Done or StepState.Skipped),
            Tracker.Steps.Count)
        {
            Alert = _alert is { } alert && State.Time - alert.Time < AlertSeconds ? alert.EventId : null,
            TownCenterIdle = TownCenterIdle(),
            SourceVillagers = SourceVillagers(current),
            Distribution = Distribution(),
        };
    }
}
