using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>Cómo quedó un objetivo: el clic (o lo que corresponda) y cuándo terminó, o por qué no se pudo.</summary>
/// <param name="Index">El lugar del objetivo en la lista.</param>
public sealed record GoalResult(int Index, double? Start, double? End, string? Problem);

/// <summary>Lo que sale del generador: la BO, el porqué de cada paso, cómo quedó cada objetivo y la simulación.</summary>
public sealed record AssistedResult(
    BuildOrder BuildOrder,
    IReadOnlyDictionary<string, string> Reasons,
    IReadOnlyList<GoalResult> Goals,
    IReadOnlyList<string> Problems,
    PlanResult Plan);

/// <summary>
/// D12: arma los pasos de una BO asistida desde sus objetivos (<see cref="BuildOrder.Assisted"/>). Simula con la comida
/// finita del mapa y una política (<see cref="GoalPolicy"/>) que agrega los pasos mientras tanto; la BO que sale,
/// simulada de nuevo con <see cref="Settings"/>, da los mismos tiempos.
/// </summary>
public sealed class AssistedGenerator(GameData data, Maps maps)
{
    /// <summary>La versión del generador: si cambia, la misma BO puede salir con otros pasos.</summary>
    public const int Version = 2;

    /// <summary>Cómo simula el modo asistido: con la comida del mapa de la BO.</summary>
    public static PlannerSettings Settings(GameData data, Maps maps, BuildOrder bo) =>
        new() { Food = FoodSupply.For(data, maps.Get(bo.Map)) };

    public AssistedResult Generate(BuildOrder bo)
    {
        var plan = bo.Assisted ?? throw new ArgumentException("La BO no es asistida: no tiene 'assisted'.", nameof(bo));
        return SearchClickPops(bo, plan);
    }

    private static readonly string[] AgeTechs = ["feudal_age", "castle_age", "imperial_age"];

    /// <summary>
    /// El número mágico de aldeanos (pedido del usuario: "tiene que haber un número para llegar lo antes posible, siendo
    /// que cada aldeano multiplica la producción"): si el último objetivo de la lista es una edad sin "a N de
    /// población", la población de clic que lo hace llegar antes. Cada aldeano cuesta comida y tiempo del centro urbano y
    /// rinde después: con pocos falta economía, con muchos el clic espera. Se prueban poblaciones de a 3 desde la que da
    /// sin parar hacia abajo (con más, el clic siempre llegaba más tarde) hasta dos seguidas que no mejoran, y después las
    /// vecinas de la mejor; ante un empate gana la de más aldeanos. Los objetivos de arriba no pueden llegar más tarde.
    /// </summary>
    private AssistedResult SearchClickPops(BuildOrder bo, AssistedPlan plan)
    {
        var pops = new Dictionary<string, int>();
        var current = Run(bo, plan, pops, maxTime: null);
        var start = PlanStart.For(data.Civ(bo.Civ), data).Population;
        var claimed = new HashSet<string>();
        for (var i = 0; i < plan.Goals.Count; i++)
        {
            if (plan.Goals[i] is not { Age: { } age } goal || !AgeTechs.Contains(age))
                continue;
            var techs = AgeTechs.Take(Array.IndexOf(AgeTechs, age) + 1).Where(t => !claimed.Contains(t)).ToList();
            claimed.UnionWith(techs);
            // Solo el último objetivo: cortar los aldeanos de una edad con objetivos después le sacaba la economía a lo que
            // sigue (Feudal sola llegaba antes con 8 de población).
            if (goal.AtPop is not null || current.Goals[i].End is null || i != plan.Goals.Count - 1)
                continue;
            // La edad del objetivo; la de paso (Castillos camino a Imperial) solo si en la del objetivo casi no hubo qué probar
            // (su clic ya sale pocos aldeanos después del anterior): buscar siempre en las dos duplicaba el tiempo.
            var ownTrials = 0;
            foreach (var tech in Enumerable.Reverse(techs))
            {
                if (tech != age && ownTrials > 2)
                    break;
                if (current.Plan.Steps.FirstOrDefault(s => s.Step.Do == StepAction.Research && s.Step.What == tech)?.Start is not { } click)
                    continue;
                var greedy = PopulationAt(current.Plan, click);
                var previous = AgeTechs.Take(Array.IndexOf(AgeTechs, tech))
                    .Select(t => current.Plan.Steps.FirstOrDefault(s => s.Step.Do == StepAction.Research && s.Step.What == t)?.Start)
                    .Where(t => t is not null).Select(t => PopulationAt(current.Plan, t!.Value)).DefaultIfEmpty(start).Max();
                var bestEnd = current.Goals[i].End!.Value;
                int? bestPop = null;
                var tried = new Dictionary<int, double>();
                double End(int pop)
                {
                    if (tried.TryGetValue(pop, out var known))
                        return known;
                    var trial = Run(bo, plan, new Dictionary<string, int>(pops) { [tech] = pop }, maxTime: bestEnd + 1);
                    var above = Enumerable.Range(0, i).All(j => (trial.Goals[j].End ?? double.MaxValue) <= (current.Goals[j].End ?? double.MaxValue));
                    return tried[pop] = above ? trial.Goals[i].End ?? double.MaxValue : double.MaxValue;
                }
                bool Try(int pop)
                {
                    if (pop <= previous || pop > BuildOrderValidator.MaxPopulation)
                        return false;
                    var end = End(pop);
                    if (end < bestEnd || end == bestEnd && bestPop is { } b && pop > b)
                    {
                        bestEnd = end;
                        bestPop = pop;
                        return true;
                    }
                    return false;
                }
                // Bajando de a 3 desde la de sin parar; corta con dos seguidas que no mejoran (de ahí para abajo no cambia
                // o empeora: a la hora no le alcanza la economía).
                for (int pop = greedy, misses = 0; pop > previous && misses < 2; pop -= 3)
                    misses = Try(pop) ? 0 : misses + 1;
                if (bestPop is { } best)
                {
                    foreach (var near in new[] { best + 2, best + 1, best - 1, best - 2 })
                        Try(near);
                }
                if (tech == age)
                    ownTrials = tried.Count;
                if (bestPop is { } found && bestEnd < current.Goals[i].End!.Value)
                {
                    pops[tech] = found;
                    current = Run(bo, plan, pops, maxTime: null);
                }
            }
        }
        return current;
    }

    /// <summary>La población a esa hora (la última muestra de antes).</summary>
    private static int PopulationAt(PlanResult plan, double time) =>
        plan.Timeline.LastOrDefault(t => t.Time <= time)?.Population ?? plan.Timeline[0].Population;

    private AssistedResult Run(BuildOrder bo, AssistedPlan plan, IReadOnlyDictionary<string, int> pops, double? maxTime)
    {
        var state = new GoalState(data, data.Civ(bo.Civ), plan) { ClickPops = pops };
        var settings = Settings(data, maps, bo);
        if (maxTime is { } limit)
            settings = settings with { MaxTime = Math.Min(settings.MaxTime, limit) };
        var result = new Planner(data, settings).Simulate(bo with { Steps = [] }, new GoalPolicy(state));

        var goals = new List<GoalResult>();
        var problems = new List<string>();
        if (state.Farm is null)
            problems.Add("La civ no tiene granjas en los datos del juego (los kitanos usan pastos): cuando se acaba la comida salvaje, los aldeanos van a la madera y la comida no sigue.");
        for (var i = 0; i < plan.Goals.Count; i++)
        {
            var goal = plan.Goals[i];
            var own = goal.Age is { } age
                ? result.Steps.Where(s => s.Step.Do == StepAction.Research && s.Step.What == age).Take(1).ToList()
                : result.Steps.Where(s => state.GoalSteps.GetValueOrDefault(i)?.Contains(s.Step.Id) == true).ToList();
            double? start = own.Count > 0 ? own.Min(s => s.Start ?? double.MaxValue) : null;
            double? end = own.Count > 0 && own.All(s => s.End is not null) ? own.Max(s => s.End!.Value) : null;
            var problem = state.Skipped.TryGetValue(i, out var skipped) ? skipped
                : end is null ? $"No llega antes de {GameClock.Format(settings.MaxTime)}."
                : null;
            goals.Add(new GoalResult(i, start == double.MaxValue ? null : start, end, problem));
            if (problem is not null)
                problems.Add($"{Describe(goal)}: {problem}");
        }

        var generated = bo with
        {
            Steps = result.Steps.Select(s => s.Step with { After = [.. s.Step.After] }).ToList(),
            Posture = Posture(plan, state, result),
        };
        LivePlan.CountsToTotals(generated);
        return new AssistedResult(generated, state.Reasons, goals, problems, result);
    }

    /// <summary>
    /// La postura de cierre que sale de los objetivos (spec, sección 2): aldeanos en cola si no se llegó a 'until', la
    /// unidad del último objetivo de unidades que se puede, y la edad siguiente a la más alta que piden.
    /// </summary>
    private static ClosingPosture? Posture(AssistedPlan plan, GoalState state, PlanResult result)
    {
        var queue = new List<string>();
        if (result.Timeline[^1].Villagers < plan.Villagers.Until)
            queue.Add("villager");
        if (plan.Goals.Select((g, i) => (g, i)).LastOrDefault(x => x.g.Unit is not null && !state.Skipped.ContainsKey(x.i)).g?.Unit is { } unit)
            queue.Add(unit);
        var highest = plan.Goals.Where(g => g.Age is not null).Select(g => BuildOrderValidator.AgeOf(g.Age!)).Max();
        var nextAge = highest switch
        {
            Age.Feudal => "castle_age",
            Age.Castle => "imperial_age",
            _ => null,
        };
        return queue.Count > 0 || nextAge is not null ? new ClosingPosture { Queue = queue, NextAge = nextAge } : null;
    }

    private static string Describe(AssistedGoal goal) =>
        goal.Age is { } age ? StepText.Name(age)
        : goal.Unit is { } unit ? $"{goal.Count} {StepText.Name(unit, goal.Count ?? 1)}"
        : goal.TownCenters is { } n ? $"{n} centros urbanos"
        : goal.Tech is { } tech ? StepText.Name(tech)
        : "Objetivo";
}
