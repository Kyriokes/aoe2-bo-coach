using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Las edades (spec, sección 3, regla 3): el objetivo activo es el primero de edad sin clic. Camino a él hace clic en
/// cada edad en orden; para cada una se asegura los 2 edificios que pide (los más baratos que falten), junta el costo
/// con prioridad (<see cref="GoalState.Planned"/>) y hace clic apenas alcanza, o a la población del objetivo.
/// </summary>
internal sealed class AgeRule : IGoalRule
{
    private static readonly string[] AgeTechs = ["feudal_age", "castle_age", "imperial_age"];

    /// <summary>Lo que tarda en salir el aldeano que está en producción: lo que se junta antes de que el centro urbano quede libre.</summary>
    public const double ClickLeadSeconds = 25;

    /// <summary>
    /// Cuánto antes de tener el costo se piden los edificios que faltan para la edad: antes, los que salen solos
    /// (campamento, molino) suelen alcanzar, y pedirlos de entrada le saca la madera al resto.
    /// </summary>
    public const double RequirementLeadSeconds = 90;

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        var goal = state.Plan.Goals.FirstOrDefault(g => g.Age is { } age && AgeTechs.Contains(age) && !Clicked(view, state, age));
        if (goal is null)
            return [];
        var tech = AgeTechs.Take(Array.IndexOf(AgeTechs, goal.Age) + 1).First(t => !Clicked(view, state, t));
        var entity = state.Data.Get(tech);
        var cost = state.Civ.Cost(entity, view.Age);
        state.Planned += cost;

        var steps = new List<Step>();
        var soon = SecondsToAfford(view, cost) is { } toAfford && toAfford <= RequirementLeadSeconds;
        if ((soon || goal.AtPop is not null) && BuildOrderValidator.AgeUpProgress(tech, Keys(view, state, pending: true)) < 2
            && Cheapest(view, state, tech) is { } building && !state.Affordable(view, state.Cost(view, building)))
        {
            state.Upcoming += state.Cost(view, building); // sale cuando alcanza
        }
        else if ((soon || goal.AtPop is not null) && BuildOrderValidator.AgeUpProgress(tech, Keys(view, state, pending: true)) < 2
            && Cheapest(view, state, tech) is { } affordable)
        {
            building = affordable;
            var from = view.Villagers.GetValueOrDefault(GatherSource.Wood) > 0
                ? GatherSource.Wood
                : view.Villagers.Where(v => v.Value > 0).OrderByDescending(v => v.Value).Select(v => (GatherSource?)v.Key).FirstOrDefault();
            var requirement = BuildOrderValidator.AgeUpRequirement(tech)!.Value.Requires;
            steps.Add(state.Add(
                new Step { Id = state.NewId(), Do = StepAction.Build, What = building, From = from, Assign = from ?? GatherSource.Wood },
                $"Para {StepText.Name(tech)} hacen falta 2 edificios de {AgeName(requirement)}: {StepText.Name(building)}"));
        }

        var atPop = tech == goal.Age ? goal.AtPop : null;
        if (atPop is { } pop)
        {
            if (BuildOrderValidator.AgeUpProgress(tech, Keys(view, state, pending: true)) >= 2)
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Research, What = tech, AtPop = pop },
                    $"Clic en {StepText.Name(tech)} a {pop} de población, como pide el objetivo"));
            return steps;
        }

        var ready = BuildOrderValidator.AgeUpProgress(tech, Keys(view, state, pending: false)) >= 2;
        if (ready && Affordable(view, cost, ClickLeadSeconds))
        {
            steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Research, What = tech },
                $"Clic en {StepText.Name(tech)}: alcanza el costo"));
            return steps;
        }

        var toAffordSeconds = SecondsToAfford(view, cost) ?? 0;
        if (ready && !double.IsInfinity(toAffordSeconds))
        {
            // Que el clic no quede en la cola detrás de un tramo largo de aldeanos.
            state.VillagersBeforeClick = Math.Clamp((int)Math.Ceiling(toAffordSeconds / state.VillagerTime(view.Age)), 1, VillagerRule.MaxTramo);
        }
        // Que la consulten cuando alcance, aunque no nazca nadie (ya se llegó a los aldeanos que pide la BO).
        state.WakeMeAt(view.Time + (double.IsInfinity(toAffordSeconds) || !ready ? 30 : Math.Max(5, toAffordSeconds - ClickLeadSeconds)));
        steps.AddRange(Rebalance(view, state, tech, cost));
        return steps;
    }

    /// <summary>Lo más que se espera juntar un recurso de la edad con los aldeanos de ahora antes de mover leñadores.</summary>
    public const double RebalanceSeconds = 120;

    /// <summary>
    /// Si ya no salen aldeanos (se llegó a 'until'), los nuevos no pueden ir a lo que falta: si un recurso que pide la
    /// edad (su costo y sus 2 edificios) no se junta en <see cref="RebalanceSeconds"/>, pasa aldeanos desde lo que sobra
    /// (la comida salvaje si la comida ya alcanza, y como mucho la mitad de los leñadores; nunca los granjeros).
    /// </summary>
    private static List<Step> Rebalance(PlanView view, GoalState state, string tech, ResourceAmounts cost)
    {
        var steps = new List<Step>();
        var villagers = view.Villagers.Values.Sum() + view.IdleVillagers + view.Builders
                        + view.Steps.Where(s => s.Step is { Do: StepAction.Train, What: "villager" }).Sum(s => s.Remaining + s.Unfinished);
        // Un reacomodo cada tanto: los "mover" se cumplen al instante y la consulta se repite en el mismo segundo.
        if (villagers < state.Plan.Villagers.Until || view.Time - state.RebalancedAt < RebalanceGapSeconds)
            return steps;

        var need = cost;
        var keys = Keys(view, state, pending: true);
        for (var k = BuildOrderValidator.AgeUpProgress(tech, keys); k < 2 && Cheapest(view, state, tech, keys) is { } building; k++)
        {
            need += state.Cost(view, building);
            keys.Add(building);
        }
        var income = Income(view);
        foreach (var (resource, target) in new[] { (Resource.Wood, GatherSource.Wood), (Resource.Gold, GatherSource.Gold), (Resource.Stone, GatherSource.Stone) })
        {
            var missing = need[resource] - view.Stock[resource] - income[resource] * RebalanceSeconds;
            var rate = view.Rates.GetValueOrDefault(target);
            if (missing <= 0 || rate <= 0)
                continue;
            var wanted = (int)Math.Ceiling(missing / (rate * RebalanceSeconds));
            foreach (var donor in new[] { GatherSource.Berries, GatherSource.Hunt, GatherSource.Sheep, GatherSource.Wood, GatherSource.Gold, GatherSource.Stone })
            {
                if (donor == target || wanted <= 0)
                    continue;
                var surplus = view.Stock[donor.Resource()] + income[donor.Resource()] * RebalanceSeconds - need[donor.Resource()];
                if (surplus <= 0)
                    continue;
                var on = view.Villagers.GetValueOrDefault(donor);
                var moved = Math.Min(wanted, donor == GatherSource.Wood ? on / 2 : on);
                if (moved <= 0)
                    continue;
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Move, Count = moved, From = donor, Assign = target },
                    $"A {StepText.Source(target)}: faltan {Math.Ceiling(need[resource] - view.Stock[resource]):0} para {StepText.Name(tech)} y ya no salen aldeanos"));
                wanted -= moved;
            }
        }
        if (steps.Count > 0)
            state.RebalancedAt = view.Time;
        return steps;
    }

    /// <summary>Lo que se espera entre un reacomodo y el siguiente.</summary>
    public const double RebalanceGapSeconds = 30;

    private static bool Clicked(PlanView view, GoalState state, string tech) =>
        view.Researched.Contains(tech)
        || view.Steps.Any(s => s.Step is { Do: StepAction.Research } && s.Step.What == tech)
        || state.Added.Any(s => s.Do == StepAction.Research && s.What == tech);

    /// <summary>Los edificios hechos y en obra (y los pedidos, con <paramref name="pending"/>), con lo que cuenta como ellos.</summary>
    private static HashSet<string> Keys(PlanView view, GoalState state, bool pending)
    {
        var keys = view.Buildings.Where(b => b.Value > 0).Select(b => b.Key).ToHashSet();
        IEnumerable<string> more = view.UnderConstruction.Keys;
        if (pending)
            more = more.Concat(state.Pending(view).Where(s => s.Do == StepAction.Build && s.What is not null).Select(s => s.What!));
        foreach (var key in more)
        {
            keys.Add(key);
            if (state.Data.TryGet(key, out var entity))
                keys.UnionWith(entity.CountsAs);
        }
        return keys;
    }

    /// <summary>El edificio más barato de los que pide la edad que falta y la civ puede hacer (sin el muelle ni el castillo).</summary>
    private static string? Cheapest(PlanView view, GoalState state, string tech) =>
        Cheapest(view, state, tech, Keys(view, state, pending: true));

    private static string? Cheapest(PlanView view, GoalState state, string tech, HashSet<string> keys)
    {
        return BuildOrderValidator.AgeUpRequirement(tech)!.Value.Buildings
            .Where(b => b is not ("dock" or "castle") && !keys.Contains(b) && state.Data.CanMake(state.Civ.Key, b) && state.Data.TryGet(b, out _))
            .Select((b, i) => (b, i, cost: Total(state.Civ.Cost(state.Data.Get(b), view.Age))))
            .OrderBy(x => x.cost).ThenBy(x => x.i)
            .Select(x => x.b)
            .FirstOrDefault();
    }

    private static double Total(ResourceAmounts cost) => cost.Food + cost.Wood + cost.Gold + cost.Stone;

    /// <summary>Lo que juntan por segundo los aldeanos de ahora, por recurso.</summary>
    private static ResourceAmounts Income(PlanView view)
    {
        var income = ResourceAmounts.Zero;
        foreach (var (source, count) in view.Villagers)
            income = income.With(source.Resource(), income[source.Resource()] + count * view.Rates.GetValueOrDefault(source));
        return income;
    }

    private static bool Affordable(PlanView view, ResourceAmounts cost, double seconds) =>
        Enum.GetValues<Resource>().All(r => view.Stock[r] + Income(view)[r] * seconds >= cost[r]);

    /// <summary>Cuántos segundos faltan para tener el costo con lo que se junta ahora (sin fin si un recurso no lo junta nadie).</summary>
    private static double? SecondsToAfford(PlanView view, ResourceAmounts cost)
    {
        var income = Income(view);
        var seconds = 0.0;
        foreach (var r in Enum.GetValues<Resource>().Where(r => cost[r] > view.Stock[r]))
            seconds = Math.Max(seconds, income[r] > 0 ? (cost[r] - view.Stock[r]) / income[r] : double.PositiveInfinity);
        return seconds;
    }

    private static string AgeName(Age age) => age switch
    {
        Age.Dark => "la Edad Oscura",
        Age.Feudal => "la Edad Feudal",
        Age.Castle => "los Castillos",
        _ => "la Edad Imperial",
    };
}
