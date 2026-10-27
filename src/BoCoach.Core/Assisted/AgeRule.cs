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

    /// <summary>
    /// Cuánto antes de tener el costo se piden los edificios que faltan para la edad: antes, los que salen solos
    /// (campamento, molino) suelen alcanzar, y pedirlos de entrada le saca la madera al resto.
    /// </summary>
    public const double RequirementLeadSeconds = 90;

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        // Solo si el objetivo que se trabaja ahora es una edad (las intermedias que hagan falta se clican en orden).
        if (state.ActiveGoal(view) is not { } active || state.Plan.Goals[active] is not { Age: { } goalAge } goal || !AgeTechs.Contains(goalAge))
            return [];
        var tech = AgeTechs.Take(Array.IndexOf(AgeTechs, goal.Age) + 1).First(t => !Clicked(view, state, t));
        var entity = state.Data.Get(tech);
        var cost = state.Civ.Cost(entity, view.Age);
        state.Planned += cost;
        state.Ahead += AheadOf(view, state, tech, active);

        var steps = new List<Step>();
        var atPop = tech == goal.Age ? goal.AtPop : null;
        // Los 2 edificios se piden recién en la edad que los tiene (los de Feudal, en Feudal): pedidos mientras se sube,
        // esperan la edad y frenan el "mover" de la comida que se acaba.
        var soon = view.Age >= BuildOrderValidator.AgeUpRequirement(tech)!.Value.Requires && (atPop is { } target
            ? Math.Max(0, target - view.Population) * state.VillagerInterval(view) <= RequirementLeadSeconds
            : SecondsToAfford(view, cost) is { } toAfford && toAfford <= RequirementLeadSeconds);
        if (soon && Progress(state, tech, Keys(view, state, pending: true)) < 2 && Cheapest(view, state, tech) is { } building)
        {
            // Primero lo que pide antes (el cuartel de la galería); si ya está en obra, se espera a que termine.
            var first = state.ToBuild(view, building);
            if (first is null)
            {
                state.WakeMeAt(view.Time + 5);
            }
            else if (!state.Affordable(view, state.Cost(view, first)))
            {
                state.Upcoming += state.Cost(view, first); // sale cuando alcanza
            }
            else
            {
                var from = state.Builder(view);
                var requirement = BuildOrderValidator.AgeUpRequirement(tech)!.Value.Requires;
                steps.Add(state.Add(
                    new Step { Id = state.NewId(), Do = StepAction.Build, What = first, From = from, Assign = from ?? GatherSource.Wood, Builders = BuildersFor(view, state, first, cost) },
                    first != building
                        ? $"Para {StepText.Name(tech)} hacen falta 2 edificios de {AgeName(requirement)}: {StepText.Name(building)}, que pide antes {StepText.Name(first)}"
                        : $"Para {StepText.Name(tech)} hacen falta 2 edificios de {AgeName(requirement)}: {StepText.Name(building)}"));
            }
        }

        if (atPop is { } pop)
        {
            // El clic se agrega cuando se llega a la población y alcanza el costo: agregado antes, esperaría en la cola y
            // frenaría lo de detrás (las casas, sin las que no se llega nunca, o el "mover" de la comida que se acaba).
            // Hasta la población los tramos no la pasan; después el centro urbano espera el costo, como en las guías.
            state.VillagersBeforeClick = Math.Max(0, pop - view.Population - view.InProduction - state.QueuedVillagers(view));
            if (view.Population < pop)
                return steps;
            var buildings = Progress(state, tech, Finished(view, state)) >= 2;
            if (buildings && Affordable(view, cost, 0) && LoomFirst(view, state, tech) is { } loomAtPop)
            {
                steps.Add(loomAtPop);
            }
            else if (buildings && Affordable(view, cost, 0))
            {
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Research, What = tech, AtPop = pop },
                    $"Clic en {StepText.Name(tech)} a {pop} de población, como pide el objetivo"));
            }
            else
            {
                // El centro urbano espera el costo o un edificio: no nace nadie, así que la regla pide que la despierten,
                // y aunque no se haya llegado a 'until' pasa gente a lo que falta.
                var wait = buildings ? SecondsToAfford(view, cost) : null;
                state.WakeMeAt(view.Time + (wait is { } w && !double.IsInfinity(w) ? Math.Max(1, Math.Ceiling(w)) : 15));
                steps.AddRange(Rebalance(view, state, tech, cost, waiting: true));
            }
            return steps;
        }

        // Con los 2 edificios terminados: empezar a construirlos no habilita el clic (el Planner lo espera, como el juego).
        var ready = Progress(state, tech, Finished(view, state)) >= 2;
        // El clic va con el centro urbano libre y el costo juntado: agregado antes, esperaría en la cola (detrás de los
        // aldeanos o del costo) y frenaría lo que se agrega detrás, como el "mover" de la comida que se acaba (y si la
        // comida no llegaba al costo, la BO quedaba trabada para siempre). Para que el centro urbano no pare, el último
        // tramo antes del clic se corta para terminar cuando se junta el costo.
        var queueWait = state.TownCenterWait(view);
        if (ready && state.TownCenterFree(view) && Affordable(view, cost, 0) && LoomFirst(view, state, tech) is { } loom)
        {
            steps.Add(loom);
            return steps;
        }
        if (ready && state.TownCenterFree(view) && Affordable(view, cost, 0))
        {
            steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Research, What = tech },
                $"Clic en {StepText.Name(tech)}: alcanza el costo"));
            return steps;
        }

        var toAffordSeconds = SecondsToAfford(view, cost) ?? 0;
        if (!ready && (soon || Progress(state, tech, Keys(view, state, pending: true)) >= 2))
        {
            // Los edificios que faltan ya se están por pedir: el clic sale cuando se junta su madera y se terminan, y el
            // tramo de aldeanos de ahora se corta igual (si no, el clic esperaba un tramo entero al centro urbano).
            toAffordSeconds = ReadySeconds(view, state, tech, cost);
        }
        if ((ready || soon || Progress(state, tech, Keys(view, state, pending: true)) >= 2) && !double.IsInfinity(toAffordSeconds))
        {
            // Los aldeanos que entran antes del clic, después de los que ya están en la cola, sin atrasarlo.
            var villager = state.Civ.Cost(state.Data.Get("villager"), view.Age);
            state.VillagersBeforeClick = VillagersBefore(view, state, n =>
                ready ? SecondsToAfford(view, cost + villager * n) ?? 0 : ReadySeconds(view, state, tech, cost + villager * n), queueWait);
        }
        // Que la consulten cuando alcance, aunque no nazca nadie (ya se llegó a los aldeanos que pide la BO).
        state.WakeMeAt(view.Time + (double.IsInfinity(toAffordSeconds) || !ready ? 30 : Math.Max(1, Math.Ceiling(Math.Max(toAffordSeconds, queueWait)))));
        steps.AddRange(Rebalance(view, state, tech, cost, waiting: false));
        return steps;
    }

    /// <summary>
    /// Cuántos aldeanos construyen un edificio de la edad: los justos para que termine cuando se junta el costo del clic
    /// (cada uno de más suma un tercio del ritmo del primero: tiempo × 3 / (n + 2)), de 1 a <see cref="MaxBuilders"/>.
    /// Con uno solo, el clic de un castillo rápido esperaba al mercado.
    /// </summary>
    private static int? BuildersFor(PlanView view, GoalState state, string building, ResourceAmounts cost)
    {
        var seconds = state.Civ.Time(state.Data.Get(building), view.Age) / state.Civ.BuildRate(view.Age);
        var afford = SecondsToAfford(view, cost) ?? 0;
        if (double.IsInfinity(afford) || seconds <= afford)
            return null;
        var builders = (int)Math.Ceiling(3 * seconds / Math.Max(1, afford) - 2);
        return builders <= 1 ? null : Math.Min(MaxBuilders, builders);
    }

    /// <summary>Lo más que se pone a construir un edificio de la edad.</summary>
    public const int MaxBuilders = 4;

    /// <summary>
    /// Cuántos aldeanos entran antes del clic: se suman mientras terminen antes de que se pueda hacer clic, con su comida
    /// sumada al costo (<paramref name="readySeconds"/> con n aldeanos): cada uno lo atrasa. Las guías de castillo rápido
    /// cortan los aldeanos así; antes entraban los que salían mientras se juntaba el costo, y el clic esperaba su comida.
    /// </summary>
    private static int VillagersBefore(PlanView view, GoalState state, Func<int, double> readySeconds, double queueWait)
    {
        var max = VillagerRule.MaxTramo * state.TownCenters(view);
        var interval = state.VillagerInterval(view);
        var n = 0;
        while (n < max && queueWait + (n + 1) * interval <= readySeconds(n + 1))
            n++;
        return n;
    }

    /// <summary>
    /// Cuántos segundos faltan para poder hacer clic en <paramref name="tech"/> cuando todavía faltan sus edificios: juntar
    /// el costo y la madera de los que faltan, y construirlos.
    /// </summary>
    private static double ReadySeconds(PlanView view, GoalState state, string tech, ResourceAmounts cost)
    {
        var keys = Keys(view, state, pending: true);
        var total = cost;
        var build = 0.0;
        for (var k = Progress(state, tech, keys); k < 2 && Cheapest(view, state, tech, keys) is { } building; k++)
        {
            total += state.CostWithRequirements(view, building);
            build = Math.Max(build, state.Civ.Time(state.Data.Get(building), view.Age));
            keys.Add(building);
        }
        // Lo que les falta a los de la edad que están en obra (la duración con sus constructores, desde que empezaron).
        var requirement = BuildOrderValidator.AgeUpRequirement(tech)!.Value.Buildings.Append(BuildOrderValidator.AgeUpAloneBuilding(tech) ?? "");
        foreach (var underway in view.Steps.Where(s => s is { Start: { } started, End: null, Step: { Do: StepAction.Build, What: { } what } } && requirement.Contains(what)))
        {
            var entity = state.Data.Get(underway.Step.What!);
            var seconds = state.Civ.Time(entity, view.Age) * 3.0 / ((underway.Step.Builders ?? 1) + 2) / state.Civ.BuildRate(view.Age);
            build = Math.Max(build, underway.Start!.Value + seconds - view.Time);
        }
        return Math.Max(SecondsToAfford(view, total) ?? 0, build);
    }

    /// <summary>
    /// Lo que va a pedir la edad siguiente a <paramref name="tech"/> si el objetivo activo llega a ella: la madera de sus 2
    /// edificios más baratos, su oro y su piedra. La comida no: la juntan los que ya están en ella mientras se investiga.
    /// </summary>
    private static ResourceAmounts AheadOf(PlanView view, GoalState state, string tech, int active)
    {
        var at = Array.IndexOf(AgeTechs, tech);
        if (at + 1 >= AgeTechs.Length)
            return ResourceAmounts.Zero;
        var next = AgeTechs[at + 1];
        // Solo camino a la edad del objetivo activo: preparar la de un objetivo de más abajo atrasaba a este hasta 42 s
        // (spec: agregar un objetivo al final no atrasa a los de arriba).
        if (state.Plan.Goals[active].Age is not { } goalAge || Array.IndexOf(AgeTechs, goalAge) <= at)
            return ResourceAmounts.Zero;
        var cost = state.Civ.Cost(state.Data.Get(next), view.Age);
        var ahead = new ResourceAmounts(0, 0, cost.Stone, cost.Gold);
        var keys = Keys(view, state, pending: true);
        for (var k = Progress(state, next, keys); k < 2 && Cheapest(view, state, next, keys) is { } building; k++)
        {
            ahead += state.CostWithRequirements(view, building);
            keys.Add(building);
        }
        return ahead;
    }

    /// <summary>
    /// Con las mejoras eco, el telar va antes del clic de Feudal (spec, sección 3, regla 6): cuando el clic está por salir
    /// y el telar no está hecho ni pedido, sale el telar y el clic en la consulta siguiente (la regla pide que la
    /// despierten cuando termina). Null si no corresponde.
    /// </summary>
    private static Step? LoomFirst(PlanView view, GoalState state, string tech)
    {
        const string Loom = "loom";
        if (tech != "feudal_age" || !state.Plan.EcoUpgrades || !state.Data.TryGet(Loom, out var loom) || !state.Data.CanMake(state.Civ.Key, Loom)
            || view.Researched.Contains(Loom) || state.Pending(view).Any(s => s.Do == StepAction.Research && s.What == Loom)
            || state.Civ.Cost(loom, view.Age) == ResourceAmounts.Zero || !state.CanResearchNow(view, Loom))
            return null;
        state.WakeMeAt(view.Time + Math.Ceiling(state.Civ.Time(loom, view.Age) / state.Civ.WorkRate(BuildOrderValidator.StartingBuilding, view.Age)));
        return state.Add(new Step { Id = state.NewId(), Do = StepAction.Research, What = Loom }, "Telar antes de subir a Feudal");
    }

    /// <summary>Lo más que se espera juntar un recurso de la edad con los aldeanos de ahora antes de mover leñadores.</summary>
    public const double RebalanceSeconds = 120;

    /// <summary>
    /// Si ya no salen aldeanos (se llegó a 'until', o el centro urbano espera el clic a N de población:
    /// <paramref name="waiting"/>), los nuevos no pueden ir a lo que falta: si un recurso que pide la
    /// edad (su costo y sus 2 edificios) no se junta en <see cref="RebalanceSeconds"/>, pasa aldeanos desde lo que sobra
    /// (la comida salvaje si la comida ya alcanza, y como mucho la mitad de los leñadores; nunca los granjeros). Solo
    /// mueve a los libres (<see cref="GoalState.Free"/>) y lo que el donante puede dar sin quedar corto, y no pide otro
    /// reacomodo mientras un "mover" espera en la cola: un "mover" que no se puede hacer frena toda la BO.
    /// </summary>
    private static List<Step> Rebalance(PlanView view, GoalState state, string tech, ResourceAmounts cost, bool waiting)
    {
        var steps = new List<Step>();
        var villagers = view.Villagers.Values.Sum() + view.IdleVillagers + view.Builders
                        + view.Steps.Where(s => s.Step is { Do: StepAction.Train, What: "villager" }).Sum(s => s.Remaining + s.Unfinished);
        // Un reacomodo cada tanto: los "mover" se cumplen al instante y la consulta se repite en el mismo segundo.
        if (!waiting && villagers < state.Plan.Villagers.Until || view.Time - state.RebalancedAt < RebalanceGapSeconds
            || view.Steps.Any(s => s.End is null && s.Step.Do == StepAction.Move))
            return steps;

        var need = cost;
        var keys = Keys(view, state, pending: true);
        for (var k = Progress(state, tech, keys); k < 2 && Cheapest(view, state, tech, keys) is { } building; k++)
        {
            need += state.CostWithRequirements(view, building);
            keys.Add(building);
        }
        var gathered = Gathered(view, RebalanceSeconds);
        var fromWood = view.Villagers.GetValueOrDefault(GatherSource.Wood) / 2;
        foreach (var (resource, target) in new[] { (Resource.Wood, GatherSource.Wood), (Resource.Gold, GatherSource.Gold), (Resource.Stone, GatherSource.Stone) })
        {
            var missing = need[resource] - view.Stock[resource] - gathered[resource];
            var rate = view.Rates.GetValueOrDefault(target);
            if (missing <= 0 || rate <= 0)
                continue;
            var wanted = (int)Math.Ceiling(missing / (rate * RebalanceSeconds));
            foreach (var donor in new[] { GatherSource.Berries, GatherSource.Hunt, GatherSource.Sheep, GatherSource.Wood, GatherSource.Gold, GatherSource.Stone })
            {
                var donorRate = view.Rates.GetValueOrDefault(donor);
                var surplus = view.Stock[donor.Resource()] + gathered[donor.Resource()] - need[donor.Resource()];
                if (donor == target || wanted <= 0 || surplus <= 0 || donorRate <= 0)
                    continue;
                var moved = Math.Min(Math.Min(wanted, state.Free(view, donor)), (int)Math.Floor(surplus / (donorRate * RebalanceSeconds)));
                if (donor == GatherSource.Wood)
                    moved = Math.Min(moved, fromWood);
                if (moved <= 0)
                    continue;
                steps.Add(state.Add(new Step { Id = state.NewId(), Do = StepAction.Move, Count = moved, From = donor, Assign = target },
                    $"A {StepText.Source(target)}: faltan {Math.Ceiling(need[resource] - view.Stock[resource]):0} para {StepText.Name(tech)} y ya no salen aldeanos"));
                wanted -= moved;
                if (donor == GatherSource.Wood)
                    fromWood -= moved;
                gathered = gathered.With(donor.Resource(), gathered[donor.Resource()] - moved * donorRate * RebalanceSeconds)
                    .With(resource, gathered[resource] + moved * rate * RebalanceSeconds);
            }
        }
        if (steps.Count > 0)
            state.RebalancedAt = view.Time;
        return steps;
    }

    /// <summary>
    /// Lo que juntan en <paramref name="seconds"/> los aldeanos de ahora, por recurso: la comida salvaje, como mucho lo
    /// que queda de ella (si no, una fuente a punto de acabarse parecía sobrar).
    /// </summary>
    internal static ResourceAmounts Gathered(PlanView view, double seconds)
    {
        var gathered = ResourceAmounts.Zero;
        var wild = 0.0;
        foreach (var (source, count) in view.Villagers)
        {
            var amount = count * view.Rates.GetValueOrDefault(source) * seconds;
            if (source is GatherSource.Sheep or GatherSource.Hunt or GatherSource.Berries)
                wild += amount;
            else
                gathered = gathered.With(source.Resource(), gathered[source.Resource()] + amount);
        }
        if (view.Food is { } food)
            wild = Math.Min(wild, new[] { GatherSource.Sheep, GatherSource.Hunt, GatherSource.Berries }.Sum(s => Math.Max(0, food.Remaining.GetValueOrDefault(s))));
        return gathered.With(Resource.Food, gathered.Food + wild);
    }

    /// <summary>Lo que se espera entre un reacomodo y el siguiente.</summary>
    public const double RebalanceGapSeconds = 30;

    private static bool Clicked(PlanView view, GoalState state, string tech) => state.Clicked(view, tech);

    /// <summary>Cuántos de los 2 edificios de la edad hay entre <paramref name="keys"/>; los jemeres suben sin ninguno.</summary>
    private static int Progress(GoalState state, string tech, IEnumerable<string> keys) =>
        state.Civ.NoAgeBuildings ? 2 : BuildOrderValidator.AgeUpProgress(tech, keys);

    /// <summary>Los edificios terminados, con lo que cuenta como ellos.</summary>
    private static HashSet<string> Finished(PlanView view, GoalState state)
    {
        var keys = view.Buildings.Where(b => b.Value > 0).Select(b => b.Key).ToHashSet();
        foreach (var key in keys.ToList())
        {
            if (state.Data.TryGet(key, out var entity))
                keys.UnionWith(entity.CountsAs);
        }
        return keys;
    }

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

    /// <summary>
    /// El edificio que falta para la edad (sin el muelle ni el castillo) que la civ puede hacer: primero los que piden los
    /// objetivos (el establo de los exploradores para Castillos), después el más barato.
    /// </summary>
    private static string? Cheapest(PlanView view, GoalState state, string tech) =>
        Cheapest(view, state, tech, Keys(view, state, pending: true));

    private static string? Cheapest(PlanView view, GoalState state, string tech, HashSet<string> keys)
    {
        return BuildOrderValidator.AgeUpRequirement(tech)!.Value.Buildings
            .Where(b => b is not ("dock" or "castle") && !keys.Contains(b) && state.Data.CanMake(state.Civ.Key, b) && state.Data.TryGet(b, out _))
            .Select((b, i) => (b, i, cost: Total(state.CostWithRequirements(view, b))))
            .OrderBy(x => state.WantedBuildings.Contains(x.b) ? 0 : 1).ThenBy(x => x.cost).ThenBy(x => x.i)
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

    internal static bool Affordable(PlanView view, ResourceAmounts cost, double seconds) =>
        Enum.GetValues<Resource>().All(r => view.Stock[r] + Gathered(view, seconds)[r] >= cost[r]);

    /// <summary>Cuántos segundos faltan para tener el costo con lo que se junta ahora (sin fin si un recurso no lo junta nadie).</summary>
    internal static double? SecondsToAfford(PlanView view, ResourceAmounts cost)
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
