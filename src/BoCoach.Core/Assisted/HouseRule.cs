using BoCoach.Core.BuildOrders;
using BoCoach.Core.Data;
using BoCoach.Core.Engine;

namespace BoCoach.Core.Assisted;

/// <summary>
/// Las casas: cuando la población, más lo que está en producción, más lo que falta encolar llega al tope menos un
/// margen, una casa con un aldeano de la madera que vuelve a la madera.
/// </summary>
internal sealed class HouseRule : IGoalRule
{
    /// <summary>Los lugares de margen con un centro urbano (spec, sección 3).</summary>
    public const int Margin = 3;

    public IReadOnlyList<Step> Next(PlanView view, GoalState state)
    {
        if (!state.MakesHouses || view.PopulationCap >= BuildOrderValidator.MaxPopulation)
            return [];
        var house = CivBuildings.For(state.Data, state.Civ, "house")!;
        if (state.Building(view, house))
            return [];
        var projected = view.Population + view.InProduction + Queued(view, state);
        if (projected < view.PopulationCap - Margin)
            return [];
        var cost = state.Cost(view, house);
        if (!state.Affordable(view, cost))
        {
            state.Upcoming += cost; // que el reparto junte la madera; la casa sale cuando alcanza
            return [];
        }

        // Desde la madera; sin leñadores, desde donde haya más gente (o los sin tarea).
        var from = state.Builder(view);
        return [state.Add(
            new Step { Id = state.NewId(), Do = StepAction.Build, What = house, From = from, Assign = from ?? GatherSource.Sheep },
            $"Casa: con lo que está en cola la población llega a {projected} de {view.PopulationCap}")];
    }

    /// <summary>La población de lo que falta encolar: lo de la foto y lo agregado en esta consulta.</summary>
    private static int Queued(PlanView view, GoalState state)
    {
        int Pop(Step step) => step.What is { } what && state.Data.TryGet(what, out var e) ? e.Pop : 0;
        return view.Steps.Where(s => s.Step.Do == StepAction.Train && s.End is null).Sum(s => s.Remaining * Pop(s.Step))
               + state.Added.Where(s => s.Do == StepAction.Train).Sum(s => s.Count * Pop(s));
    }
}
