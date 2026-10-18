using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Economy;

public sealed record EconomySettings
{
    /// <summary>Horizonte H del déficit proyectado, en segundos.</summary>
    public double Horizon { get; init; } = 60;

    /// <summary>Ventana de las tasas observadas, en segundos.</summary>
    public double RateWindow { get; init; } = 20;

    /// <summary>Peso de lo observado contra lo que dice la BO al estimar aldeanos por recurso (0 a 1).</summary>
    public double ObservedWeight { get; init; } = 0.6;

    /// <summary>Déficit a partir del cual un recurso "va a faltar".</summary>
    public double ShortageThreshold { get; init; } = 25;

    /// <summary>Excedente (déficit negativo) a partir del cual un recurso "sobra".</summary>
    public double SurplusThreshold { get; init; } = 150;

    /// <summary>Máximo de aldeanos a mover en una sugerencia.</summary>
    public int MaxMoves { get; init; } = 5;

    /// <summary>Histéresis: el déficit tiene que sostenerse este tiempo antes de sugerir.</summary>
    public double SustainSeconds { get; init; } = 10;

    /// <summary>Histéresis: tiempo mínimo entre sugerencias.</summary>
    public double CooldownSeconds { get; init; } = 20;

    /// <summary>Si el objetivo del próximo hito está más cerca que esto (o ya pasó), se usa la estimación del Planner.</summary>
    public double MinSecondsToMilestone { get; init; } = 30;

    /// <summary>Cómo se elige la sugerencia (ver <see cref="VillagerAllocator"/>).</summary>
    public AllocatorKind Allocator { get; init; } = AllocatorKind.Exact;

    /// <summary>v2: peso de lo que sobra de más contra lo que falta (que pesa 1).</summary>
    public double SurplusWeight { get; init; } = 0.2;

    /// <summary>
    /// v2: segundos de trabajo que pierde un aldeano al cambiar de recurso (caminata, el viaje que vuelve sin carga).
    /// Con el peso de lo que sobra en 0,2 y H = 60 s, sacar gente de donde sobra para llevarla a donde no falta pierde
    /// un poco (12 s de lo que sobra contra 15 s de trabajo): solo conviene mover si algo va a faltar, y la cantidad es
    /// la que alcanza para cubrirlo.
    /// </summary>
    public double MoveSeconds { get; init; } = 15;

    /// <summary>v2: costo extra de mandar gente a un recurso donde no hay nadie (campamento nuevo, caminata larga).</summary>
    public double NewSiteCost { get; init; } = 40;

    /// <summary>v2: cuánto tiene que mejorar la economía para que valga la pena sugerir.</summary>
    public double MinGain { get; init; } = 15;

    /// <summary>
    /// Máximo de aldeanos que quiere el jugador (100 a 130 según la estrategia). Hasta llegar, los centros urbanos
    /// producen sin parar y la comida nunca baja de lo que eso necesita (<see cref="VillagerFloor"/>).
    /// </summary>
    public int MaxVillagers { get; init; } = 120;
}

/// <summary>Aldeanos por recurso estimados, y la eficiencia aprendida (1 = lo que asume el Planner).</summary>
public sealed record VillagerEstimate(ResourceAmounts PerResource, double Efficiency, int Total);

public sealed record Suggestion(Resource From, Resource To, int Count)
{
    public override string ToString() => $"Pasá {Count} de {Name(From)} a {Name(To)}";

    public static string Name(Resource r) => r switch
    {
        Resource.Food => "comida",
        Resource.Wood => "madera",
        Resource.Stone => "piedra",
        _ => "oro",
    };
}

/// <param name="Candidate">Lo que sugeriría el asignador ahora mismo, antes de la histéresis.</param>
/// <param name="Suggestion">Lo que se muestra en el overlay.</param>
public sealed record EconomyReport(
    ResourceAmounts ObservedRates,
    VillagerEstimate Villagers,
    ResourceAmounts Demand,
    ResourceAmounts Deficit,
    Suggestion? Candidate,
    Suggestion? Suggestion)
{
    /// <summary>Aldeanos en comida que hacen falta para que los centros urbanos no paren (0 si ya se llegó al máximo).</summary>
    public int FoodFloor { get; init; }

    /// <summary>Lo que junta por segundo un aldeano en cada recurso, con la eficiencia observada.</summary>
    public ResourceAmounts EffectiveRates { get; init; } = ResourceAmounts.Zero;

    /// <summary>
    /// Lo que junta por segundo un aldeano en cada recurso según el modelo, sin la eficiencia observada (que sale de
    /// descargas sueltas y en las ovejas baja hasta 0,35).
    /// </summary>
    public ResourceAmounts NominalRates { get; init; } = ResourceAmounts.Zero;
}

/// <summary>
/// Economía: tasas observadas, aldeanos estimados, demanda hasta el próximo hito de la BO, déficit en un horizonte H
/// y una sugerencia de reasignación (<see cref="VillagerAllocator"/>) con histéresis.
/// Llamar <see cref="Update"/> una vez por snapshot, después de actualizar el Tracker.
/// </summary>
public sealed class EconomyAdvisor
{
    private static readonly Dictionary<string, Age> AgeTechs = new()
    {
        ["feudal_age"] = Age.Feudal,
        ["castle_age"] = Age.Castle,
        ["imperial_age"] = Age.Imperial,
    };

    private readonly GameData _data;
    private readonly PlannerSettings _planner;
    private readonly EconomySettings _settings;
    private readonly RateObserver _observer;
    private readonly SuggestionGate _gate;
    private readonly GatherModel _gatherModel;

    // Civ de la BO, edad actual y tecnologías terminadas (las tasas y los costos dependen de eso); se fijan en cada Update.
    private CivBonuses _civ = CivBonuses.None;
    private Age _age;
    private IReadOnlyList<string> _researched = [];

    public EconomyAdvisor(GameData data, PlannerSettings? planner = null, EconomySettings? settings = null)
    {
        _data = data;
        _planner = planner ?? new PlannerSettings();
        _settings = settings ?? new EconomySettings();
        _observer = new RateObserver(data, _settings.RateWindow);
        _gate = new SuggestionGate(_settings.SustainSeconds, _settings.CooldownSeconds);
        _gatherModel = new GatherModel(data, _planner.Efficiency);
    }

    public EconomyReport Update(Tracker tracker, PlanResult? plan = null)
    {
        var state = tracker.State;
        _observer.Add(state);

        var start = LivePlan.Start(tracker);
        _civ = _data.Civ(tracker.BuildOrder.Civ);
        _age = start.Age;
        _researched = start.Researched;
        var perVillager = PerVillagerRates(start, state.Time);
        var villagers = EstimateVillagers(start, state, perVillager);
        var effective = Scale(perVillager, villagers.Efficiency);
        var projectedRates = Multiply(villagers.PerResource, effective);

        var demand = Demand(tracker, plan, state.Time);
        var foodFloor = 0;
        if (villagers.Total < _settings.MaxVillagers)
        {
            // La BO puede pedir más comida (subir de edad), pero nunca menos de la que mantiene los centros urbanos.
            var production = VillagerFloor.FoodPerSecond(_data, _civ, _age, VillagerFloor.TownCenters(tracker));
            demand = demand.With(Resource.Food, Math.Max(demand.Food, production));
            // Con lo que rinde un aldeano según el modelo, no con la eficiencia observada: esa sale de descargas
            // sueltas y baja mucho en las ovejas o mientras caminan, y el mínimo saltaba de 6 a 12 y de vuelta.
            foodFloor = Math.Min(villagers.Total, VillagerFloor.FoodVillagers(production, perVillager.Food));
        }
        var h = _settings.Horizon;
        var deficit = demand * h - (state.Stock + projectedRates * h);

        var candidate = VillagerAllocator.Choose(deficit, villagers.PerResource, effective, _settings);
        var suggestion = _gate.Update(state.Time, candidate);
        return new EconomyReport(_observer.Rates, villagers, demand, deficit, candidate, suggestion) { FoodFloor = foodFloor, EffectiveRates = effective, NominalRates = perVillager };
    }

    /// <summary>Recurso por segundo de un aldeano en cada recurso, según de qué fuentes saca la BO ese recurso.</summary>
    private ResourceAmounts PerVillagerRates(PlanStart start, double time)
    {
        var rates = ResourceAmounts.Zero;
        foreach (var resource in Enum.GetValues<Resource>())
        {
            var sources = start.Villagers.Where(v => v.Value > 0 && v.Key.Resource() == resource).ToList();
            double rate;
            if (sources.Count == 0)
            {
                rate = SourceRate(DefaultSource(resource, time));
            }
            else
            {
                rate = sources.Sum(s => s.Value * SourceRate(s.Key)) / sources.Sum(s => s.Value);
            }
            rates = rates.With(resource, rate);
        }
        return rates;
    }

    private double SourceRate(GatherSource source) => _gatherModel.Rate(source, _civ, _age, _researched);

    private static GatherSource DefaultSource(Resource resource, double time) => resource switch
    {
        Resource.Food => time < 600 ? GatherSource.Sheep : GatherSource.Farm,
        Resource.Wood => GatherSource.Wood,
        Resource.Gold => GatherSource.Gold,
        _ => GatherSource.Stone,
    };

    private VillagerEstimate EstimateVillagers(PlanStart start, PlayerState state, ResourceAmounts perVillager)
    {
        var military = state.Events.Count(e => EventConventions.IsUnitCreated(e.EventId) && e.EventId != EventConventions.VillagerCreated);
        var total = Math.Max(1, state.Villagers ?? state.Population - 1 - military); // -1: el explorador inicial

        // Según la BO, escalado al total real.
        var planned = ResourceAmounts.Zero;
        foreach (var (source, count) in start.Villagers)
            planned = planned.With(source.Resource(), planned[source.Resource()] + count);
        var plannedSum = Sum(planned);
        planned = plannedSum > 0 ? planned * (total / plannedSum) : ResourceAmounts.Of(Resource.Food, total);

        // Según lo observado: aldeanos "a eficiencia 1".
        var raw = ResourceAmounts.Zero;
        var observed = _observer.Rates;
        foreach (var r in Enum.GetValues<Resource>())
            raw = raw.With(r, perVillager[r] > 0 ? observed[r] / perVillager[r] : 0);
        var rawSum = Sum(raw);
        var enoughRates = _observer.Coverage >= _settings.RateWindow * 0.75 && rawSum > 0;

        // Con el mod, el HUD dice cuántos hay en cada recurso: no hace falta estimarlo. Las tasas solo dan la
        // eficiencia (cuánto rinde cada uno contra lo que asume el Planner).
        if (state.VillagersOn is { } seen && Sum(seen) > 0)
        {
            var seenEfficiency = enoughRates ? Math.Clamp(rawSum / Sum(seen), 0.3, 1.5) : 1;
            return new VillagerEstimate(seen, seenEfficiency, total);
        }

        if (_observer.Coverage < _settings.RateWindow * 0.75)
            return new VillagerEstimate(planned, 1, total);
        if (rawSum <= 0)
            return new VillagerEstimate(planned, 1, total);

        var efficiency = Math.Clamp(rawSum / total, 0.3, 1.5);
        var fromObserved = raw * (1 / efficiency);
        var w = _settings.ObservedWeight;
        var blended = fromObserved * w + planned * (1 - w);
        return new VillagerEstimate(blended * (total / Sum(blended)), efficiency, total);
    }

    /// <summary>
    /// Costo de lo que falta hasta el próximo hito (subir de edad), dividido el tiempo que queda para él. Lo que ya
    /// se pagó (una edad en curso) no cuenta: el próximo hito pasa a ser el siguiente.
    /// </summary>
    private ResourceAmounts Demand(Tracker tracker, PlanResult? plan, double now)
    {
        var items = LivePlan.Items(tracker).Where(i => i.PaidAt is null).ToList();
        var milestone = items
            .Select((item, i) => (item, i))
            .FirstOrDefault(x => x.item.Step.Do == StepAction.Research && x.item.Step.What is { } w && AgeTechs.ContainsKey(w));
        var upTo = milestone.item is null ? items.Count : milestone.i + 1;

        var cost = ResourceAmounts.Zero;
        foreach (var item in items.Take(upTo))
        {
            if (item.Step.What is { } what && _data.TryGet(what, out var entity))
                cost += _civ.Cost(entity, _age) * item.Remaining;
        }

        var seconds = _settings.Horizon * 2;
        if (milestone.item?.Step.What is { } tech)
        {
            var target = tech switch
            {
                "feudal_age" => tracker.BuildOrder.Targets?.FeudalClickTime,
                "castle_age" => tracker.BuildOrder.Targets?.CastleClickTime,
                _ => null,
            };
            if (target is { } goal && goal - now >= _settings.MinSecondsToMilestone)
                seconds = goal - now;
            else if (plan?.ClickTime(tech) is { } click && click - now >= _settings.MinSecondsToMilestone)
                seconds = click - now;
        }
        return cost * (1 / seconds);
    }

    private static double Sum(ResourceAmounts a) => a.Food + a.Wood + a.Stone + a.Gold;

    private static ResourceAmounts Scale(ResourceAmounts a, double k) => a * k;

    private static ResourceAmounts Multiply(ResourceAmounts a, ResourceAmounts b) =>
        new(a.Food * b.Food, a.Wood * b.Wood, a.Stone * b.Stone, a.Gold * b.Gold);
}

/// <summary>
/// Histéresis: una sugerencia se muestra recién cuando el mismo par de recursos se sostuvo
/// <c>sustain</c> segundos y pasaron <c>cooldown</c> segundos desde la anterior. Una a la vez.
/// Una vez visible:
/// <list type="bullet">
/// <item>se muestra al menos <c>minShow</c> segundos, aunque el déficit se cruce un momento con el umbral;</item>
/// <item>la cantidad cambia solo si el número nuevo se sostiene <see cref="CountSettleSeconds"/> segundos: el
/// asignador la recalcula en cada captura y sin esto saltaría de 3 a 2 a 5. Si el jugador hace caso, el déficit
/// se resuelve y la sugerencia desaparece igual.</item>
/// </list>
/// </summary>
public sealed class SuggestionGate(double sustainSeconds, double cooldownSeconds, double minShowSeconds = 8)
{
    public const double CountSettleSeconds = 6;

    private (Resource From, Resource To)? _pending;
    private double _pendingSince;
    private double _lastShown = double.NegativeInfinity;
    private double? _differentSince;
    private Suggestion? _active;

    public Suggestion? Update(double now, Suggestion? candidate)
    {
        if (_active is not null && candidate is null && now - _lastShown < minShowSeconds)
            return _active;

        if (candidate is null)
        {
            _pending = null;
            _active = null;
            return null;
        }

        var pair = (candidate.From, candidate.To);
        if (_active is not null && (_active.From, _active.To) == pair)
            return _active = Adjust(now, candidate);

        if (_pending != pair)
        {
            _pending = pair;
            _pendingSince = now;
            _active = null;
            return null;
        }

        if (now - _pendingSince >= sustainSeconds && now - _lastShown >= cooldownSeconds)
        {
            _lastShown = now;
            _differentSince = null;
            return _active = candidate;
        }
        return null;
    }

    private Suggestion Adjust(double now, Suggestion candidate)
    {
        if (candidate.Count == _active!.Count)
        {
            _differentSince = null;
            return _active;
        }
        _differentSince ??= now;
        if (now - _differentSince < CountSettleSeconds)
            return _active;
        _differentSince = null;
        return candidate;
    }
}
