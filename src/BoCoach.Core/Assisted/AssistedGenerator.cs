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
        for (var i = 0; i < plan.Goals.Count; i++)
        {
            var goal = plan.Goals[i];
            var click = goal.Age is { } age ? result.Steps.FirstOrDefault(s => s.Step.Do == StepAction.Research && s.Step.What == age) : null;
            var problem = goal.Age is null ? "Este objetivo todavía no se puede generar."
                : click?.End is null ? $"No llega antes de {GameClock.Format(settings.MaxTime)}."
                : null;
            goals.Add(new GoalResult(i, click?.Start, click?.End, problem));
            if (problem is not null)
                problems.Add($"{Describe(goal)}: {problem}");
        }

        var generated = bo with
        {
            Steps = result.Steps.Select(s => s.Step with { After = [.. s.Step.After] }).ToList(),
            Posture = Posture(plan, result),
        };
        LivePlan.CountsToTotals(generated);
        return new AssistedResult(generated, state.Reasons, goals, problems, result);
    }

    /// <summary>
    /// La postura de cierre que sale de los objetivos: aldeanos en cola si no se llegó a 'until', y la edad siguiente a
    /// la última de la lista. Las unidades de la postura llegan con los objetivos de unidades (sub-paso d).
    /// </summary>
    private static ClosingPosture? Posture(AssistedPlan plan, PlanResult result)
    {
        var queue = result.Timeline[^1].Villagers < plan.Villagers.Until ? new[] { "villager" } : [];
        var nextAge = plan.Goals.LastOrDefault(g => g.Age is not null)?.Age switch
        {
            "feudal_age" => "castle_age",
            "castle_age" => "imperial_age",
            _ => null,
        };
        return queue.Length > 0 || nextAge is not null ? new ClosingPosture { Queue = queue, NextAge = nextAge } : null;
    }

    private static string Describe(AssistedGoal goal) =>
        goal.Age is { } age ? StepText.Name(age)
        : goal.Unit is { } unit ? $"{goal.Count} {StepText.Name(unit, goal.Count ?? 1)}"
        : goal.TownCenters is { } n ? $"{n} centros urbanos"
        : goal.Tech is { } tech ? StepText.Name(tech)
        : "Objetivo";
}
