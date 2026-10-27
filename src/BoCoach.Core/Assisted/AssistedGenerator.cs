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
    public const int Version = 1;

    /// <summary>Cómo simula el modo asistido: con la comida del mapa de la BO.</summary>
    public static PlannerSettings Settings(GameData data, Maps maps, BuildOrder bo) =>
        new() { Food = FoodSupply.For(data, maps.Get(bo.Map)) };

    public AssistedResult Generate(BuildOrder bo)
    {
        var plan = bo.Assisted ?? throw new ArgumentException("La BO no es asistida: no tiene 'assisted'.", nameof(bo));
        var state = new GoalState(data, data.Civ(bo.Civ), plan);
        var settings = Settings(data, maps, bo);
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
