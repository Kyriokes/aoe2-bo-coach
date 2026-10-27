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
        var result = new Planner(data, Settings(data, maps, bo)).Simulate(bo with { Steps = [] }, new GoalPolicy(state));

        var generated = bo with { Steps = result.Steps.Select(s => s.Step with { After = [.. s.Step.After] }).ToList() };
        LivePlan.CountsToTotals(generated);
        return new AssistedResult(generated, state.Reasons, [], [], result);
    }
}
