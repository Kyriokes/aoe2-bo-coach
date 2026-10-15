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
    Suggestion? Suggestion);

/// <summary>
/// Economía v1: tasas observadas, aldeanos estimados, demanda hasta el próximo hito de la BO, déficit en un
/// horizonte H y una sugerencia de reasignación (greedy, de a 1 aldeano) con histéresis.
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

    public EconomyAdvisor(GameData data, PlannerSettings? planner = null, EconomySettings? settings = null)
    {
        _data = data;
        _planner = planner ?? new PlannerSettings();
        _settings = settings ?? new EconomySettings();
        _observer = new RateObserver(data, _settings.RateWindow);
        _gate = new SuggestionGate(_settings.SustainSeconds, _settings.CooldownSeconds);
    }

    public EconomyReport Update(Tracker tracker, PlanResult? plan = null)
    {
        var state = tracker.State;
        _observer.Add(state);

        var start = LivePlan.Start(tracker);
        var perVillager = PerVillagerRates(start, state.Time);
        var villagers = EstimateVillagers(start, state, perVillager);
        var effective = Scale(perVillager, villagers.Efficiency);
        var projectedRates = Multiply(villagers.PerResource, effective);

        var demand = Demand(tracker, plan, state.Time);
        var h = _settings.Horizon;
        var deficit = demand * h - (state.Stock + projectedRates * h);

        var candidate = Allocate(deficit, villagers.PerResource, effective);
        var suggestion = _gate.Update(state.Time, candidate);
        return new EconomyReport(_observer.Rates, villagers, demand, deficit, candidate, suggestion);
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

    private double SourceRate(GatherSource source) =>
        _data.GatherRates[source] * _planner.Efficiency.GetValueOrDefault(source, 1);

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
        var total = Math.Max(1, state.Population - 1 - military); // -1: el explorador inicial

        // Según la BO, escalado al total real.
        var planned = ResourceAmounts.Zero;
        foreach (var (source, count) in start.Villagers)
            planned = planned.With(source.Resource(), planned[source.Resource()] + count);
        var plannedSum = Sum(planned);
        planned = plannedSum > 0 ? planned * (total / plannedSum) : ResourceAmounts.Of(Resource.Food, total);

        if (_observer.Coverage < _settings.RateWindow * 0.75)
            return new VillagerEstimate(planned, 1, total);

        // Según lo observado: aldeanos "a eficiencia 1", y la eficiencia que hace que sumen el total.
        var raw = ResourceAmounts.Zero;
        var observed = _observer.Rates;
        foreach (var r in Enum.GetValues<Resource>())
            raw = raw.With(r, perVillager[r] > 0 ? observed[r] / perVillager[r] : 0);
        var rawSum = Sum(raw);
        if (rawSum <= 0)
            return new VillagerEstimate(planned, 1, total);

        var efficiency = Math.Clamp(rawSum / total, 0.3, 1.5);
        var fromObserved = raw * (1 / efficiency);
        var w = _settings.ObservedWeight;
        var blended = fromObserved * w + planned * (1 - w);
        return new VillagerEstimate(blended * (total / Sum(blended)), efficiency, total);
    }

    /// <summary>Costo de lo que falta hasta el próximo hito (subir de edad), dividido el tiempo que queda para él.</summary>
    private ResourceAmounts Demand(Tracker tracker, PlanResult? plan, double now)
    {
        var items = LivePlan.Items(tracker);
        var milestone = items
            .Select((item, i) => (item, i))
            .FirstOrDefault(x => x.item.Step.Do == StepAction.Research && x.item.Step.What is { } w && AgeTechs.ContainsKey(w));
        var upTo = milestone.item is null ? items.Count : milestone.i + 1;

        var cost = ResourceAmounts.Zero;
        foreach (var item in items.Take(upTo))
        {
            if (item.Step.What is { } what && _data.TryGet(what, out var entity))
                cost += entity.Cost * item.Remaining;
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

    /// <summary>Greedy: mueve de a 1 aldeano del recurso que más sobra al que más falta, mientras mejore.</summary>
    private Suggestion? Allocate(ResourceAmounts deficit, ResourceAmounts villagers, ResourceAmounts perVillager)
    {
        var d = deficit;
        var h = _settings.Horizon;
        Resource? from = null, to = null;
        var moved = 0;

        while (moved < _settings.MaxMoves)
        {
            var needy = Enum.GetValues<Resource>().Where(r => d[r] > _settings.ShortageThreshold).OrderByDescending(r => d[r]).ToList();
            var donors = Enum.GetValues<Resource>()
                .Where(r => d[r] < -_settings.SurplusThreshold && villagers[r] - moved * (r == from ? 1 : 0) >= 1)
                .OrderBy(r => d[r])
                .ToList();
            if (needy.Count == 0 || donors.Count == 0)
                break;

            var target = needy[0];
            var donor = donors[0];
            if (from is not null && (donor != from || target != to))
                break; // una sola sugerencia (un par de recursos) a la vez
            if (d[donor] + perVillager[donor] * h > -_settings.SurplusThreshold / 2)
                break; // el donante pasaría a quedar justo

            from = donor;
            to = target;
            d = d.With(donor, d[donor] + perVillager[donor] * h).With(target, d[target] - perVillager[target] * h);
            moved++;
        }

        return from is { } f && to is { } t ? new Suggestion(f, t, moved) : null;
    }

    private static double Sum(ResourceAmounts a) => a.Food + a.Wood + a.Stone + a.Gold;

    private static ResourceAmounts Scale(ResourceAmounts a, double k) => a * k;

    private static ResourceAmounts Multiply(ResourceAmounts a, ResourceAmounts b) =>
        new(a.Food * b.Food, a.Wood * b.Wood, a.Stone * b.Stone, a.Gold * b.Gold);
}

/// <summary>
/// Histéresis: una sugerencia se muestra recién cuando el mismo par de recursos se sostuvo
/// <c>sustain</c> segundos y pasaron <c>cooldown</c> segundos desde la anterior. Una a la vez.
/// </summary>
public sealed class SuggestionGate(double sustainSeconds, double cooldownSeconds)
{
    private (Resource From, Resource To)? _pending;
    private double _pendingSince;
    private double _lastShown = double.NegativeInfinity;
    private Suggestion? _active;

    public Suggestion? Update(double now, Suggestion? candidate)
    {
        if (candidate is null)
        {
            _pending = null;
            _active = null;
            return null;
        }

        var pair = (candidate.From, candidate.To);
        if (_active is not null && (_active.From, _active.To) == pair)
            return _active = candidate; // sigue vigente; puede cambiar la cantidad

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
            return _active = candidate;
        }
        return null;
    }
}
